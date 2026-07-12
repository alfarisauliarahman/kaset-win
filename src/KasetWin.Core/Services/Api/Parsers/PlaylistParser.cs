using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for YouTube Music playlist/album detail responses, their
/// continuation pages, the add-to-playlist menu, and the create-playlist result. Mirrors the
/// macOS <c>PlaylistParser</c>: header metadata is read from the header renderers
/// (<c>musicDetailHeaderRenderer</c> / <c>musicResponsiveHeaderRenderer</c> and the editable /
/// immersive / visual variants), and tracks come from the
/// <c>musicPlaylistShelfRenderer</c> rows (<c>musicResponsiveListItemRenderer</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c>, side-effect free and deterministic, so the parser satisfies
/// the idempotency / stable-identity guarantee (Property 23) and the ownership/delete-affordance
/// guarantee (Property 27). The <c>musicPlaylistShelfRenderer</c> is treated as the
/// authoritative track source; "Suggestions" shelves are ignored so suggested tracks are never
/// counted as playlist tracks, and the playlist shelf's continuation token wins over
/// section-level suggestion tokens (Property 26, Req 8.4). Container reshuffling is tolerated
/// by locating renderers recursively via <see cref="ResponseTreeSearch"/>.
/// </para>
/// <para>This type lives in <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.</para>
/// </remarks>
public static class PlaylistParser
{
    private const string PageTypeAlbum = "MUSIC_PAGE_TYPE_ALBUM";

    private static readonly string[] HeaderContentKinds =
    {
        "album", "single", "ep", "playlist", "song", "uploads",
    };

    // Known renderer wrappers that carry an add-to-playlist option. Arbitrary parent
    // containers are NOT treated as options just because a playlistId appears somewhere
    // in their command tree.
    private static readonly string[] AddToPlaylistOptionRendererKeys =
    {
        "playlistAddToOptionRenderer",
        "addToPlaylistItemRenderer",
        "musicResponsiveListItemRenderer",
        "musicTwoRowItemRenderer",
    };

    // MARK: - Detail

    /// <summary>
    /// Parses a playlist (or album) detail browse response into a <see cref="PlaylistDetail"/>:
    /// metadata, the ordered track list, ownership flag, and the first-page continuation token.
    /// </summary>
    /// <param name="root">The decoded <c>browse</c> response tree.</param>
    /// <param name="playlistId">The browseId used to request the playlist (becomes the identity).</param>
    /// <returns>The parsed detail. A well-formed response with no tracks yields an empty track list.</returns>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or carries neither a <c>header</c> nor a
    /// <c>contents</c> container (corrupted input, Req 20.3).
    /// </exception>
    public static PlaylistDetail ParsePlaylistDetail(JsonNode? root, string playlistId)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Playlist response is not a JSON object.");
        }

        if (!obj.ContainsKey("header") && !obj.ContainsKey("contents"))
        {
            throw new KasetError(KasetErrorKind.ParseError, "Playlist response is missing both 'header' and 'contents'.");
        }

        var header = ParseHeader(obj);
        var tracks = ParseTracks(obj, header.ThumbnailUrl);
        var trackCount = Math.Max(header.TrackCount ?? 0, tracks.Count);

        // The header sometimes omits the explicit badge even when the album clearly is (e.g. it lives
        // only on the tracks); treat the album as explicit when any track is, as a robust fallback.
        var isExplicit = header.IsExplicit || tracks.Any(t => t.IsExplicit == true);

        var playlist = new Playlist
        {
            Id = playlistId,
            Title = header.Title,
            Author = header.Author,
            ThumbnailUrl = header.ThumbnailUrl,
            TrackCount = trackCount == 0 ? null : trackCount,
            ReleaseDateText = header.ReleaseDateText,
            ContentType = header.ContentType,
            Description = header.Description,
            IsOwnedByUser = PlaylistEditability.IsOwnedByUser(obj),
            IsExplicit = isExplicit,
        };

        return new PlaylistDetail
        {
            Playlist = playlist,
            Tracks = tracks,
            ContinuationToken = ExtractContinuationToken(obj),
            LikePlaylistId = ExtractLikeablePlaylistId(obj),
            // A podcast playlist lays its episodes out as musicMultiRowListItemRenderer rows, which
            // the track parser (musicResponsiveListItemRenderer only) yields nothing for. Flag it so
            // the caller can reroute to the podcast surface instead of showing an empty playlist.
            IsPodcastPlaylist = tracks.Count == 0
                && ResponseTreeSearch.FindFirst(obj, "musicMultiRowListItemRenderer") is not null,
        };
    }

    /// <summary>
    /// Finds the likeable playlist id for "add to library": an album's audio-playlist id
    /// (<c>OLAK…</c>) or a real <c>VL/PL</c> playlist id, searching the response tree. Returns
    /// <c>null</c> when none is present so the caller falls back to the requested browseId.
    /// </summary>
    private static string? ExtractLikeablePlaylistId(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var direct = NormalizeId(GetString(obj, "playlistId"));
                if (direct is not null
                    && (direct.StartsWith("OLAK", StringComparison.Ordinal)
                        || direct.StartsWith("VL", StringComparison.Ordinal)
                        || direct.StartsWith("PL", StringComparison.Ordinal)))
                {
                    return direct;
                }

                foreach (var (_, child) in obj)
                {
                    if (ExtractLikeablePlaylistId(child) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (ExtractLikeablePlaylistId(item) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses a single continuation page of playlist tracks (Req 8.4): the next batch of tracks
    /// plus the optional token for the page after it. Handles the legacy
    /// <c>continuationContents</c> shelf format and the 2025 <c>onResponseReceivedActions</c>
    /// (<c>appendContinuationItemsAction</c>) format.
    /// </summary>
    /// <param name="root">The decoded continuation response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/> or not a JSON object (Req 20.3).
    /// </exception>
    public static PlaylistContinuation ParsePlaylistContinuation(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Playlist continuation response is not a JSON object.");
        }

        // Legacy: continuationContents.{musicPlaylistShelfContinuation|musicShelfContinuation}
        foreach (var key in new[] { "musicPlaylistShelfContinuation", "musicShelfContinuation" })
        {
            var shelf = Prop(Prop(obj, "continuationContents"), key);
            if (shelf is null)
            {
                continue;
            }

            var rows = AsArray(Prop(shelf, "contents"));
            if (rows is null)
            {
                continue;
            }

            var legacyTracks = ParseTrackRows(rows, null);
            var legacyToken = TokenFromRenderer(shelf) ?? TokenFromContents(rows);
            return new PlaylistContinuation { Tracks = legacyTracks, ContinuationToken = legacyToken };
        }

        // 2025: onResponseReceivedActions[].appendContinuationItemsAction.continuationItems
        var actions = AsArray(Prop(obj, "onResponseReceivedActions"));
        if (actions is { Count: > 0 })
        {
            foreach (var action in actions)
            {
                var items = AsArray(Prop(Prop(action, "appendContinuationItemsAction"), "continuationItems"));
                if (items is null)
                {
                    continue;
                }

                return new PlaylistContinuation
                {
                    Tracks = ParseTrackRows(items, null),
                    ContinuationToken = TokenFromContents(items),
                };
            }
        }

        return new PlaylistContinuation();
    }

    // MARK: - Add to playlist menu

    /// <summary>
    /// Parses the <c>playlist/get_add_to_playlist</c> response into an
    /// <see cref="AddToPlaylistMenu"/>: the de-duplicated (by playlist id) list of playlists the
    /// track can be added to, plus <see cref="AddToPlaylistMenu.CanCreate"/> which is
    /// <see langword="true"/> only when a <c>createPlaylistEndpoint</c> affordance is present.
    /// Only known option renderers are read.
    /// </summary>
    /// <param name="root">The decoded menu response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/> or not a JSON object (Req 20.3).
    /// </exception>
    public static AddToPlaylistMenu ParseAddToPlaylistMenu(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Add-to-playlist response is not a JSON object.");
        }

        // Scope the search to the add-to-playlist renderer when present so unrelated playlist
        // ids elsewhere in the response are not picked up.
        var scope = ResponseTreeSearch.FindFirst(obj, "addToPlaylistRenderer") ?? obj;
        var canCreate = ResponseTreeSearch.ContainsKey(scope, "createPlaylistEndpoint");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var playlists = new List<Playlist>();
        CollectAddToPlaylistOptions(scope, playlists, seen);

        return new AddToPlaylistMenu { Playlists = playlists, CanCreate = canCreate };
    }

    /// <summary>
    /// Extracts the playlist id returned by <c>playlist/create</c>. YouTube Music has returned
    /// this in several shapes: prefer the explicit top-level <c>playlistId</c>, then known
    /// nested command/toast paths, then a recursive <c>VL/PL</c>-prefixed lookup.
    /// </summary>
    /// <param name="root">The decoded create response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or carries no playlist id at all (Req 20.3).
    /// </exception>
    public static string ParseCreatedPlaylistId(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Create-playlist response is not a JSON object.");
        }

        var topLevel = NormalizeId(GetString(obj, "playlistId"));
        if (topLevel is not null)
        {
            return topLevel;
        }

        var known = ExtractCreatedIdFromKnownPaths(obj);
        if (known is not null)
        {
            return known;
        }

        var recursive = ExtractPlaylistId(obj);
        if (recursive is not null)
        {
            return recursive;
        }

        throw new KasetError(KasetErrorKind.ParseError, "Create-playlist response did not contain a playlist id.");
    }

    // MARK: - Header parsing

    private sealed class HeaderData
    {
        public string Title { get; set; } = "Unknown Playlist";

        public Uri? ThumbnailUrl { get; set; }

        public Artist? Author { get; set; }

        public int? TrackCount { get; set; }

        public string? ReleaseDateText { get; set; }

        public string? ContentType { get; set; }

        public string? Description { get; set; }

        public Uri? AuthorThumbnailUrl { get; set; }

        public bool IsExplicit { get; set; }
    }

    private static HeaderData ParseHeader(JsonObject root)
    {
        var header = new HeaderData();

        // Order matters: the detail header (or its editable wrapper) is preferred, then the
        // responsive header, then the immersive / visual fallbacks. Each only fills gaps.
        ApplyHeaderRenderer(ResponseTreeSearch.FindFirst(root, "musicDetailHeaderRenderer"), header);
        ApplyEditableHeader(ResponseTreeSearch.FindFirst(root, "musicEditablePlaylistDetailHeaderRenderer"), header);
        ApplyHeaderRenderer(ResponseTreeSearch.FindFirst(root, "musicResponsiveHeaderRenderer"), header);
        ApplyHeaderRenderer(ResponseTreeSearch.FindFirst(root, "musicImmersiveHeaderRenderer"), header);
        ApplyHeaderRenderer(ResponseTreeSearch.FindFirst(root, "musicVisualHeaderRenderer"), header);
        ApplyDescription(root, header);

        // 2025 headers carry the owner's photo in an avatarStackViewModel → avatarViewModel with
        // image.sources (not the classic thumbnails array); fall back to that when nothing matched.
        header.AuthorThumbnailUrl ??= AvatarViewModelUrl(root);

        if (header.Author is not null && header.AuthorThumbnailUrl is not null && header.Author.ThumbnailUrl is null)
        {
            header.Author = header.Author with { ThumbnailUrl = header.AuthorThumbnailUrl };
        }

        return header;
    }

    /// <summary>First <c>avatarViewModel.image.sources[].url</c> in the response, or null.</summary>
    private static Uri? AvatarViewModelUrl(JsonObject root)
    {
        var avatar = ResponseTreeSearch.FindFirst(root, "avatarViewModel");
        var sources = AsArray(Prop(Prop(avatar, "image"), "sources"));
        if (sources is null)
        {
            return null;
        }

        foreach (var source in sources)
        {
            var url = GetString(source, "url");
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static void ApplyEditableHeader(JsonNode? renderer, HeaderData header)
    {
        // The editable header nests a musicDetailHeaderRenderer; reuse the standard path.
        ApplyHeaderRenderer(Prop(Prop(renderer, "header"), "musicDetailHeaderRenderer"), header);
    }

    private static void ApplyHeaderRenderer(JsonNode? renderer, HeaderData header)
    {
        if (renderer is null)
        {
            return;
        }

        if (header.Title == "Unknown Playlist")
        {
            var title = ParsingHelpers.ExtractText(renderer, "title");
            if (!string.IsNullOrEmpty(title))
            {
                header.Title = title;
            }
        }

        if (!header.IsExplicit)
        {
            header.IsExplicit = ParsingHelpers.ExtractIsExplicit(renderer);
        }

        header.ThumbnailUrl ??= ParsingHelpers.BestThumbnailUrl(renderer);
        header.AuthorThumbnailUrl ??= BestNestedThumbnailUrl(Prop(renderer, "straplineThumbnail"))
                                      ?? BestNestedThumbnailUrl(Prop(renderer, "facepile"));

        foreach (var key in new[] { "subtitle", "secondSubtitle", "straplineTextOne" })
        {
            var runs = AsArray(Prop(Prop(renderer, key), "runs"));
            if (runs is null)
            {
                continue;
            }

            header.Author ??= ExtractHeaderAuthor(runs);
            ApplyMetadata(runs, header);
        }
    }

    private static void ApplyDescription(JsonObject root, HeaderData header)
    {
        if (!string.IsNullOrWhiteSpace(header.Description))
        {
            return;
        }

        foreach (var key in new[] { "musicDescriptionShelfRenderer", "descriptionShelfRenderer" })
        {
            var renderer = ResponseTreeSearch.FindFirst(root, key);
            var description = ParsingHelpers.ExtractText(renderer, "description")
                              ?? ParsingHelpers.ExtractText(renderer, "descriptionText")
                              ?? ParsingHelpers.ExtractText(renderer, "text");
            if (!string.IsNullOrWhiteSpace(description))
            {
                header.Description = description.Trim();
                return;
            }
        }
    }

    private static Artist? ExtractHeaderAuthor(JsonArray runs)
    {
        // Prefer a linked, navigable artist run that is not a content-kind label.
        foreach (var run in runs)
        {
            var browseId = ParsingHelpers.ExtractBrowseId(run);
            var name = GetString(run, "text")?.Trim();
            if (browseId is not null
                && ParsingHelpers.IsNavigableArtistId(browseId)
                && !string.IsNullOrEmpty(name)
                && !IsHeaderContentKind(name))
            {
                return new Artist { Id = browseId, Name = name };
            }
        }

        // Otherwise the first plain-text author candidate (skip kinds/counts/durations).
        foreach (var run in runs)
        {
            var name = GetString(run, "text")?.Trim();
            if (IsHeaderAuthorCandidate(name))
            {
                return new Artist { Id = ParsingHelpers.StableId("playlist-author", name!), Name = name! };
            }
        }

        return null;
    }

    private static void ApplyMetadata(JsonArray runs, HeaderData header)
    {
        foreach (var run in runs)
        {
            var text = GetString(run, "text")?.Trim();
            if (string.IsNullOrEmpty(text) || IsArtistSeparatorText(text))
            {
                continue;
            }

            header.ContentType ??= ExtractContentType(text);
            header.ReleaseDateText ??= ExtractReleaseDateText(text);

            if (header.TrackCount is null)
            {
                var count = ParsingHelpers.ExtractSongCount(text);
                if (count is not null)
                {
                    header.TrackCount = count;
                }
            }
        }
    }

    private static string? ExtractContentType(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        if (Array.IndexOf(HeaderContentKinds, lower) < 0)
        {
            return null;
        }

        return lower switch
        {
            "ep" => "EP",
            "album" => "Album",
            "single" => "Single",
            "playlist" => "Playlist",
            "song" => "Song",
            "uploads" => "Uploads",
            _ => text,
        };
    }

    private static string? ExtractReleaseDateText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 4 && trimmed.All(char.IsDigit))
        {
            return trimmed;
        }

        if (!DateTime.TryParse(trimmed, out var date))
        {
            return null;
        }

        return $"{date.Day} {IndonesianMonthName(date.Month)} {date.Year}";
    }

    private static string IndonesianMonthName(int month) => month switch
    {
        1 => "Januari",
        2 => "Februari",
        3 => "Maret",
        4 => "April",
        5 => "Mei",
        6 => "Juni",
        7 => "Juli",
        8 => "Agustus",
        9 => "September",
        10 => "Oktober",
        11 => "November",
        12 => "Desember",
        _ => string.Empty,
    };

    private static bool IsHeaderAuthorCandidate(string? text)
    {
        if (string.IsNullOrEmpty(text) || text == "•" || IsArtistSeparatorText(text))
        {
            return false;
        }

        if (IsHeaderContentKind(text)
            || ParsingHelpers.ExtractSongCount(text) is not null
            || ParsingHelpers.ParseDuration(text) is not null)
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains(" views", StringComparison.Ordinal)
            || lower.Contains(" plays", StringComparison.Ordinal)
            || lower.Contains(" subscribers", StringComparison.Ordinal)
            || lower.Contains("monthly audience", StringComparison.Ordinal)
            || lower.Contains("episodes", StringComparison.Ordinal))
        {
            return false;
        }

        // Catch-all: every metadata form the header carries — durations ("3 menit, 33 detik" /
        // "17 minutes"), counts ("6 lagu" / "6 songs"), view/listener/subscriber counts, and years —
        // contains a digit, while a real author name does not. Rejecting any digit-bearing fallback
        // text handles every UI language at once (the app pins hl=id, so the server returns Indonesian
        // metadata that English-only checks missed, leaking the duration/count onto every track).
        // Linked artists (with a browseId) are picked by the first loop above, so a rare digit-bearing
        // artist name is unaffected unless it is completely unlinked.
        return !text.Any(char.IsDigit);
    }

    private static bool IsHeaderContentKind(string text) =>
        Array.IndexOf(HeaderContentKinds, text.Trim().ToLowerInvariant()) >= 0;

    private static bool IsArtistSeparatorText(string text) =>
        text is " • " or " & " or ", " or "•" or "&" or ",";

    // MARK: - Track parsing

    private static List<Song> ParseTracks(JsonObject root, Uri? fallbackThumb)
    {
        // The playlist shelf is authoritative: when present, its rows are the playlist tracks
        // and any sibling musicShelfRenderer holds Suggestions, which we ignore.
        var playlistShelf = ResponseTreeSearch.FindFirst(root, "musicPlaylistShelfRenderer");
        if (playlistShelf is not null && AsArray(Prop(playlistShelf, "contents")) is { } shelfRows)
        {
            var parsed = ParseTrackRows(shelfRows, fallbackThumb);
            // TEMP diag: a podcast playlist renders 0 tracks because its episode rows use a different
            // renderer than musicResponsiveListItemRenderer — log the row renderer keys to confirm.
            if (parsed.Count == 0 && shelfRows.Count > 0 && shelfRows[0] is JsonObject firstRow)
            {
                Diag.Write($"playlist rows=0 rowKeys=[{string.Join(",", firstRow.Select(kv => kv.Key))}] rowCount={shelfRows.Count}");
            }

            return parsed;
        }

        // Fallback: collect rows from non-suggestion musicShelfRenderer sections.
        var tracks = new List<Song>();
        foreach (var shelf in ResponseTreeSearch.FindAll(root, "musicShelfRenderer"))
        {
            if (IsSuggestedShelf(shelf) || AsArray(Prop(shelf, "contents")) is not { } rows)
            {
                continue;
            }

            tracks.AddRange(ParseTrackRows(rows, fallbackThumb));
        }

        return tracks;
    }

    private static List<Song> ParseTrackRows(JsonArray rows, Uri? fallbackThumb)
    {
        var tracks = new List<Song>(rows.Count);
        var position = 1;
        foreach (var row in rows)
        {
            var song = ParseTrackRow(row, fallbackThumb, position);
            if (song is not null)
            {
                tracks.Add(song);
                position++;
            }
        }

        return tracks;
    }

    private static Song? ParseTrackRow(JsonNode? row, Uri? fallbackThumb, int fallbackTrackNumber)
    {
        var renderer = Prop(row, "musicResponsiveListItemRenderer");
        if (renderer is null)
        {
            return null;
        }

        var videoId = ExtractVideoId(renderer);
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }

        var (rank, trend) = ChartIndexFromRow(renderer);
        return new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = TitleFromFlexColumns(renderer) ?? "Unknown",
            Artists = ParsingHelpers.ExtractArtistsFromFlexColumns(renderer),
            Album = AlbumFromFlexColumns(renderer),
            Duration = DurationFromRow(renderer),
            TrackNumber = TrackNumberFromRow(renderer) ?? fallbackTrackNumber,
            Rank = rank,
            Trend = trend,
            ListenerCountText = ListenerCountFromRow(renderer),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(renderer) ?? fallbackThumb,
            IsExplicit = ParsingHelpers.ExtractIsExplicit(renderer),
        };
    }

    /// <summary>
    /// Reads a chart playlist row's <c>customIndexColumn.musicCustomIndexColumnRenderer</c>: the
    /// rank number and the trend arrow (<c>icon.iconType</c>). Returns <c>(0, None)</c> for ordinary
    /// (non-chart) playlist rows that carry no custom index column.
    /// </summary>
    private static (int Rank, TrendDirection Trend) ChartIndexFromRow(JsonNode? renderer)
    {
        var custom = Prop(Prop(renderer, "customIndexColumn"), "musicCustomIndexColumnRenderer");
        if (custom is null)
        {
            return (0, TrendDirection.None);
        }

        var rank = 0;
        var rankText = ParsingHelpers.ExtractText(custom, "text");
        if (rankText is not null)
        {
            var digits = new string(rankText.Where(char.IsDigit).ToArray());
            _ = int.TryParse(digits, out rank);
        }

        var trend = GetString(Prop(custom, "icon"), "iconType") switch
        {
            "ARROW_DROP_UP" or "TRENDING_UP" or "ARROW_CHART_UP" => TrendDirection.Up,
            "ARROW_DROP_DOWN" or "TRENDING_DOWN" or "ARROW_CHART_DOWN" => TrendDirection.Down,
            "ARROW_CHART_NEUTRAL" => TrendDirection.Neutral,
            _ => TrendDirection.None,
        };

        return (rank, trend);
    }

    private static int? TrackNumberFromRow(JsonNode? renderer)
    {
        foreach (var text in RowTexts(renderer))
        {
            if (int.TryParse(text.Trim(), out var number) && number is > 0 and < 1000)
            {
                return number;
            }
        }

        return null;
    }

    private static string? ListenerCountFromRow(JsonNode? renderer)
    {
        foreach (var text in RowTexts(renderer))
        {
            var lower = text.ToLowerInvariant();
            if (lower.Contains(" listener", StringComparison.Ordinal)
                || lower.Contains(" play", StringComparison.Ordinal)
                || lower.Contains(" view", StringComparison.Ordinal))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string> RowTexts(JsonNode? renderer)
    {
        foreach (var columnKey in new[] { "fixedColumns", "flexColumns" })
        {
            var columns = AsArray(Prop(renderer, columnKey));
            if (columns is null)
            {
                continue;
            }

            foreach (var column in columns)
            {
                foreach (var rendererKey in new[]
                {
                    "musicResponsiveListItemFixedColumnRenderer",
                    "musicResponsiveListItemFlexColumnRenderer",
                })
                {
                    var runs = AsArray(Prop(Prop(Prop(column, rendererKey), "text"), "runs"));
                    if (runs is null)
                    {
                        continue;
                    }

                    foreach (var run in runs)
                    {
                        var text = GetString(run, "text");
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            yield return text;
                        }
                    }
                }
            }
        }
    }

    private static Uri? BestNestedThumbnailUrl(JsonNode? node)
    {
        var direct = ParsingHelpers.BestThumbnailUrl(node);
        if (direct is not null)
        {
            return direct;
        }

        foreach (var rendererKey in new[] { "musicThumbnailRenderer", "croppedSquareThumbnailRenderer" })
        {
            var thumbnails = AsArray(Prop(Prop(Prop(node, rendererKey), "thumbnail"), "thumbnails"));
            if (thumbnails is null)
            {
                continue;
            }

            Uri? best = null;
            var bestArea = -1;
            foreach (var thumbnail in thumbnails)
            {
                var url = GetString(thumbnail, "url");
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                var width = GetInt(thumbnail, "width") ?? 0;
                var height = GetInt(thumbnail, "height") ?? 0;
                var area = width * height;
                if (area > bestArea)
                {
                    best = uri;
                    bestArea = area;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private static string? ExtractVideoId(JsonNode? renderer) =>
        GetString(Prop(renderer, "playlistItemData"), "videoId")
        ?? GetString(Prop(FlexColumnFirstRun(renderer, 0), "watchEndpoint"), "videoId")
        ?? GetString(Prop(Prop(FlexColumnFirstRun(renderer, 0), "navigationEndpoint"), "watchEndpoint"), "videoId")
        ?? GetString(Prop(Prop(renderer, "navigationEndpoint"), "watchEndpoint"), "videoId");

    private static string? TitleFromFlexColumns(JsonNode? renderer) =>
        GetString(FlexColumnFirstRun(renderer, 0), "text");

    private static Album? AlbumFromFlexColumns(JsonNode? renderer)
    {
        var flexColumns = AsArray(Prop(renderer, "flexColumns"));
        if (flexColumns is null)
        {
            return null;
        }

        foreach (var column in flexColumns)
        {
            var runs = AsArray(Prop(Prop(Prop(column, "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs"));
            if (runs is null)
            {
                continue;
            }

            foreach (var run in runs)
            {
                var browse = Prop(Prop(run, "navigationEndpoint"), "browseEndpoint");
                var browseId = GetString(browse, "browseId");
                if (string.IsNullOrEmpty(browseId))
                {
                    continue;
                }

                var pageType = GetString(
                    Prop(Prop(browse, "browseEndpointContextSupportedConfigs"), "browseEndpointContextMusicConfig"),
                    "pageType");

                if (pageType == PageTypeAlbum
                    || browseId.StartsWith("MPRE", StringComparison.Ordinal)
                    || browseId.StartsWith("OLAK", StringComparison.Ordinal))
                {
                    var title = GetString(run, "text");
                    if (!string.IsNullOrEmpty(title))
                    {
                        return new Album { Id = browseId, Title = title };
                    }
                }
            }
        }

        return null;
    }

    private static TimeSpan? DurationFromRow(JsonNode? renderer)
    {
        // Preferred location: fixedColumns[].musicResponsiveListItemFixedColumnRenderer.text.runs
        var fixedColumns = AsArray(Prop(renderer, "fixedColumns"));
        if (fixedColumns is not null)
        {
            foreach (var column in fixedColumns)
            {
                var runs = AsArray(Prop(Prop(Prop(column, "musicResponsiveListItemFixedColumnRenderer"), "text"), "runs"));
                if (runs is { Count: > 0 } && ParsingHelpers.ParseDuration(GetString(runs[0], "text")) is { } fixedDuration)
                {
                    return fixedDuration;
                }
            }
        }

        // Fallback: any flex-column run whose text parses as a duration (mm:ss / h:mm:ss).
        var flexColumns = AsArray(Prop(renderer, "flexColumns"));
        if (flexColumns is not null)
        {
            foreach (var column in flexColumns)
            {
                var runs = AsArray(Prop(Prop(Prop(column, "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs"));
                if (runs is null)
                {
                    continue;
                }

                foreach (var run in runs)
                {
                    if (ParsingHelpers.ParseDuration(GetString(run, "text")) is { } flexDuration)
                    {
                        return flexDuration;
                    }
                }
            }
        }

        return null;
    }

    private static bool IsSuggestedShelf(JsonNode? shelf)
    {
        var title = ParsingHelpers.ExtractText(shelf, "title")?.Trim().ToLowerInvariant();
        return title is not null
            && (title == "suggestions" || title == "suggested" || title.Contains("suggestion", StringComparison.Ordinal));
    }

    // MARK: - Continuation token extraction

    private static string? ExtractContinuationToken(JsonObject root)
    {
        // The playlist shelf token wins so we page through tracks before any Suggestions.
        var playlistShelf = ResponseTreeSearch.FindFirst(root, "musicPlaylistShelfRenderer");
        if (playlistShelf is not null)
        {
            var token = TokenFromRenderer(playlistShelf)
                        ?? TokenFromContents(AsArray(Prop(playlistShelf, "contents")));
            if (token is not null)
            {
                return token;
            }
        }

        foreach (var shelf in ResponseTreeSearch.FindAll(root, "musicShelfRenderer"))
        {
            if (IsSuggestedShelf(shelf))
            {
                continue;
            }

            var token = TokenFromRenderer(shelf) ?? TokenFromContents(AsArray(Prop(shelf, "contents")));
            if (token is not null)
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>Legacy format: <c>renderer.continuations[0].nextContinuationData.continuation</c>.</summary>
    private static string? TokenFromRenderer(JsonNode? renderer)
    {
        var continuations = AsArray(Prop(renderer, "continuations"));
        if (continuations is not { Count: > 0 })
        {
            return null;
        }

        return GetString(Prop(continuations[0], "nextContinuationData"), "continuation");
    }

    /// <summary>2025 format: trailing <c>continuationItemRenderer.continuationEndpoint.continuationCommand.token</c>.</summary>
    private static string? TokenFromContents(JsonArray? contents)
    {
        if (contents is not { Count: > 0 })
        {
            return null;
        }

        var last = contents[contents.Count - 1];
        return GetString(
            Prop(Prop(Prop(last, "continuationItemRenderer"), "continuationEndpoint"), "continuationCommand"),
            "token");
    }

    // MARK: - Add to playlist option collection

    private static void CollectAddToPlaylistOptions(JsonNode? node, List<Playlist> playlists, HashSet<string> seen)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in AddToPlaylistOptionRendererKeys)
                {
                    if (obj.TryGetPropertyValue(key, out var renderer) && renderer is JsonObject)
                    {
                        var option = ParseAddToPlaylistOption(renderer);
                        if (option is not null && seen.Add(option.Id))
                        {
                            playlists.Add(option);
                        }
                    }
                }

                foreach (var (_, child) in obj)
                {
                    CollectAddToPlaylistOptions(child, playlists, seen);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    CollectAddToPlaylistOptions(item, playlists, seen);
                }

                break;
        }
    }

    private static Playlist? ParseAddToPlaylistOption(JsonNode renderer)
    {
        var playlistId = ExtractPlaylistId(renderer);
        if (playlistId is null)
        {
            return null;
        }

        var title = ParsingHelpers.ExtractText(renderer, "title")
                    ?? ParsingHelpers.ExtractText(renderer, "text")
                    ?? ParsingHelpers.ExtractText(renderer, "label")
                    ?? ParsingHelpers.ExtractText(renderer, "primaryText")
                    ?? FirstFlexColumnText(renderer)
                    ?? "Unknown Playlist";

        // "Create new playlist" rows can carry a playlist id in their command tree; skip them.
        if (title is "Create new playlist" or "New playlist")
        {
            return null;
        }

        return new Playlist
        {
            Id = playlistId,
            Title = title,
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(renderer),
        };
    }

    // MARK: - Created-playlist id extraction

    private static string? ExtractCreatedIdFromKnownPaths(JsonObject root)
    {
        var actions = AsArray(Prop(root, "actions"));
        if (actions is not null)
        {
            foreach (var action in actions)
            {
                // addToToastAction.item.notificationTextRenderer.navigationEndpoint.browseEndpoint.playlistId
                var toastNav = Prop(
                    Prop(Prop(Prop(action, "addToToastAction"), "item"), "notificationTextRenderer"),
                    "navigationEndpoint");
                var fromToast = NormalizeId(GetString(Prop(toastNav, "browseEndpoint"), "playlistId"));
                if (fromToast is not null)
                {
                    return fromToast;
                }
            }

            foreach (var action in actions)
            {
                var fromNav = NormalizeId(GetString(Prop(Prop(action, "navigationEndpoint"), "browseEndpoint"), "playlistId"));
                if (fromNav is not null)
                {
                    return fromNav;
                }
            }
        }

        var command = Prop(root, "command");
        if (command is not null)
        {
            return NormalizeId(GetString(Prop(command, "browseEndpoint"), "playlistId"))
                   ?? NormalizeId(GetString(command, "playlistId"));
        }

        return null;
    }

    /// <summary>
    /// Recursively finds the first <c>VL/PL</c>-prefixed <c>playlistId</c> in the tree.
    /// Used both for add-to-playlist options and as a last-resort created-id lookup.
    /// </summary>
    private static string? ExtractPlaylistId(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var direct = NormalizeId(GetString(obj, "playlistId"));
                if (direct is not null
                    && (direct.StartsWith("VL", StringComparison.Ordinal) || direct.StartsWith("PL", StringComparison.Ordinal)))
                {
                    return direct;
                }

                foreach (var (_, child) in obj)
                {
                    var found = ExtractPlaylistId(child);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            case JsonArray array:
                foreach (var item in array)
                {
                    var found = ExtractPlaylistId(item);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    // MARK: - JsonNode helpers

    private static string? NormalizeId(string? id)
    {
        var trimmed = id?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? FirstFlexColumnText(JsonNode? renderer) =>
        GetString(FlexColumnFirstRun(renderer, 0), "text");

    private static JsonNode? FlexColumnFirstRun(JsonNode? renderer, int index)
    {
        if (Prop(renderer, "flexColumns") is JsonArray columns && index >= 0 && index < columns.Count)
        {
            var runs = AsArray(Prop(Prop(Prop(columns[index], "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs"));
            return runs is { Count: > 0 } ? runs[0] : null;
        }

        return null;
    }

    private static JsonNode? Prop(JsonNode? node, string? key)
    {
        if (key is not null && node is JsonObject obj && obj.TryGetPropertyValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static JsonArray? AsArray(JsonNode? node) => node as JsonArray;

    private static string? GetString(JsonNode? node, string key)
    {
        var value = Prop(node, key);
        return value is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
    }

    private static int? GetInt(JsonNode? node, string key)
    {
        var value = Prop(node, key);
        if (value is JsonValue jv && jv.TryGetValue<int>(out var i))
        {
            return i;
        }

        return null;
    }
}
