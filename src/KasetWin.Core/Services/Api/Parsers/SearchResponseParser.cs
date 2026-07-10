using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for the InnerTube <c>search</c> response. Mirrors the macOS
/// <c>SearchResponseParser</c>: the highlighted Top Result is taken from
/// <c>musicCardShelfRenderer</c>, and the regular results
/// (<c>musicResponsiveListItemRenderer</c> rows inside <c>musicShelfRenderer</c>) are
/// classified into per-type groups (songs / albums / artists / playlists / podcasts) by
/// <c>pageType</c> and browseId prefix (Req 12.1, 12.4).
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c>, side-effect free and deterministic, so the parser satisfies
/// the idempotency / stable-identity guarantee (Property 23) and the classification guarantee
/// (Property 24). Container reshuffling (tabbed vs filtered search, extra wrappers) is tolerated
/// by locating the shelf renderers recursively via <see cref="ResponseTreeSearch"/>.
/// </para>
/// <para>
/// Type classification reuses the shared <see cref="BrowseIdClassifier"/> (task 5.3) for the
/// browseId-prefix table and layers the renderer <c>pageType</c> on top (mirroring
/// <see cref="HomeResponseParser"/>), so the prefix logic is not duplicated. This type lives in
/// <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.
/// </para>
/// </remarks>
public static class SearchResponseParser
{
    private const string PageTypeAlbum = "MUSIC_PAGE_TYPE_ALBUM";
    private const string PageTypeArtist = "MUSIC_PAGE_TYPE_ARTIST";
    private const string PageTypeUserChannel = "MUSIC_PAGE_TYPE_USER_CHANNEL";
    private const string PageTypeLibraryArtist = "MUSIC_PAGE_TYPE_LIBRARY_ARTIST";
    private const string PageTypePlaylist = "MUSIC_PAGE_TYPE_PLAYLIST";
    private const string PageTypePodcastShow = "MUSIC_PAGE_TYPE_PODCAST_SHOW";
    private const string PageTypePodcastShowDetail = "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE";

    /// <summary>
    /// Parses a search response into grouped results.
    /// </summary>
    /// <param name="root">The decoded <c>search</c> response tree.</param>
    /// <returns>
    /// A <see cref="SearchResponse"/> with the optional <see cref="SearchResponse.TopResult"/>
    /// and per-type groups. A well-formed response with no matches yields an empty response.
    /// </returns>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or lacks the expected <c>contents</c>
    /// results container (corrupted input, Req 20.3).
    /// </exception>
    public static SearchResponse Parse(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Search response is not a JSON object.");
        }

        if (!obj.TryGetPropertyValue("contents", out var contents) || contents is null)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Search response is missing the 'contents' results container.");
        }

        HomeSectionItem? topResult = null;
        var card = ResponseTreeSearch.FindFirst(contents, "musicCardShelfRenderer");
        if (card is not null)
        {
            topResult = ParseCardShelf(card);
        }

        var songs = new List<Song>();
        var albums = new List<Album>();
        var artists = new List<Artist>();
        var playlists = new List<Playlist>();
        var podcasts = new List<Playlist>();

        // 2025 universal search: the selected tab's sectionListRenderer interleaves
        // musicShelfRenderer sections with per-item itemSectionRenderer wrappers — together they
        // form ONE flat, relevance-ordered result list (exactly what the web shows). Walk the
        // section list in document order; consecutive single-item sections merge into the flat
        // relevance list, while titled shelves keep their own section.
        string? continuationToken = null;
        var sections = new List<SearchSection>();
        var flat = new List<HomeSectionItem>();

        void ParseRows(JsonArray rows, List<HomeSectionItem> into)
        {
            foreach (var row in rows)
            {
                if (Prop(row, "musicResponsiveListItemRenderer") is { } renderer
                    && ParseRowItem(renderer) is { } item)
                {
                    into.Add(item);
                    ClassifyItem(item, songs, albums, artists, playlists, podcasts);
                }
            }
        }

        var sectionList = ResponseTreeSearch.FindFirst(contents, "sectionListRenderer");
        if (Prop(sectionList, "contents") is JsonArray orderedSections)
        {
            foreach (var sectionNode in orderedSections)
            {
                if (Prop(sectionNode, "musicShelfRenderer") is JsonObject shelfObj)
                {
                    var title = ParsingHelpers.ExtractText(shelfObj);
                    if (Prop(shelfObj, "contents") is JsonArray shelfRows)
                    {
                        if (string.IsNullOrEmpty(title))
                        {
                            // An untitled shelf is part of the flat relevance list.
                            ParseRows(shelfRows, flat);
                        }
                        else
                        {
                            var shelfItems = new List<HomeSectionItem>();
                            ParseRows(shelfRows, shelfItems);
                            if (shelfItems.Count > 0)
                            {
                                sections.Add(new SearchSection(title, shelfItems));
                            }
                        }
                    }

                    // A filtered (single-type) search returns one pageable shelf; keep the first
                    // token found.
                    continuationToken ??= ExtractContinuationToken(shelfObj);
                }
                else if (Prop(sectionNode, "itemSectionRenderer") is { } itemSection
                    && Prop(itemSection, "contents") is JsonArray itemRows)
                {
                    ParseRows(itemRows, flat);
                }
            }
        }
        else
        {
            // Fallback (older/odd containers): find shelves anywhere in the tree.
            foreach (var shelf in ResponseTreeSearch.FindAll(contents, "musicShelfRenderer"))
            {
                if (shelf is JsonObject shelfObj && Prop(shelfObj, "contents") is JsonArray rows)
                {
                    ParseRows(rows, flat);
                    continuationToken ??= ExtractContinuationToken(shelfObj);
                }
            }
        }

        if (flat.Count > 0)
        {
            // The flat relevance list leads (it is what the web shows first).
            sections.Insert(0, new SearchSection("Hasil teratas", flat));
        }

        return new SearchResponse
        {
            TopResult = topResult,
            Sections = sections,
            Songs = songs,
            Albums = albums,
            Artists = artists,
            Playlists = playlists,
            Podcasts = podcasts,
            ContinuationToken = continuationToken,
        };
    }

    /// <summary>
    /// Parses a <b>search continuation</b> response (the next page of a filtered shelf) into the
    /// additional rows and the following continuation token. Mirrors <see cref="Parse"/> row
    /// classification but reads the <c>musicShelfContinuation</c> envelope. A response with no
    /// recognisable continuation contents yields an empty response (null token) — the natural end of
    /// the shelf, not an error.
    /// </summary>
    public static SearchResponse ParseContinuation(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Search continuation root is missing or not a JSON object.");
        }

        var songs = new List<Song>();
        var albums = new List<Album>();
        var artists = new List<Artist>();
        var playlists = new List<Playlist>();
        var podcasts = new List<Playlist>();

        var shelf = ResponseTreeSearch.FindFirst(root, "musicShelfContinuation");
        if (shelf is JsonObject shelfObj
            && shelfObj.TryGetPropertyValue("contents", out var shelfContents)
            && shelfContents is JsonArray rows)
        {
            foreach (var row in rows)
            {
                var renderer = Prop(row, "musicResponsiveListItemRenderer");
                if (renderer is not null && ParseRowItem(renderer) is { } item)
                {
                    ClassifyItem(item, songs, albums, artists, playlists, podcasts);
                }
            }
        }

        return new SearchResponse
        {
            Songs = songs,
            Albums = albums,
            Artists = artists,
            Playlists = playlists,
            Podcasts = podcasts,
            ContinuationToken = ExtractContinuationToken(shelf),
        };
    }

    /// <summary>Reads the next-page token from a shelf's <c>continuations</c> (legacy or command).</summary>
    private static string? ExtractContinuationToken(JsonNode? shelf)
    {
        if (Prop(shelf, "continuations") is JsonArray continuations && continuations.Count > 0)
        {
            var token = GetString(Prop(continuations[0], "nextContinuationData"), "continuation");
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }
        }

        var command = ResponseTreeSearch.FindFirst(shelf, "continuationCommand");
        var newToken = GetString(command, "token");
        return string.IsNullOrEmpty(newToken) ? null : newToken;
    }

    // MARK: - Top Result (musicCardShelfRenderer)

    private static HomeSectionItem? ParseCardShelf(JsonNode card)
    {
        var titleRun = FirstTitleRun(card);
        if (titleRun is null)
        {
            return null;
        }

        var title = GetString(titleRun, "text");
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var thumbnail = ParsingHelpers.BestThumbnailUrl(card);
        var subtitle = ParsingHelpers.JoinRunTexts(card, "subtitle");
        var nav = Prop(titleRun, "navigationEndpoint");

        // A song top result navigates via a watchEndpoint (videoId); everything else via browse.
        var videoId = GetString(Prop(nav, "watchEndpoint"), "videoId");
        if (!string.IsNullOrEmpty(videoId))
        {
            return new HomeSectionItem.SongItem(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = title,
                // Without artists the player bar shows a blank second line when a top result is
                // played (Bug #6). The card subtitle carries them ("Song • Artist • Album …").
                Artists = ExtractCardArtists(card, subtitle),
                ThumbnailUrl = thumbnail,
                IsExplicit = ParsingHelpers.ExtractIsExplicit(card),
                // The card subtitle leads with the kind ("Video …" / "Song …"); a video top result
                // gets a 16:9 cover in the UI, a song a square one.
                VideoType = LooksLikeVideoSubtitle(subtitle) ? MusicVideoType.Omv : null,
            });
        }

        var browse = Prop(nav, "browseEndpoint");
        var browseId = GetString(browse, "browseId");
        if (string.IsNullOrEmpty(browseId))
        {
            return null;
        }

        var pageType = ExtractPageType(browse);
        return CreateSectionItem(browseId, pageType, title, thumbnail, subtitle);
    }

    private static HomeSectionItem? CreateSectionItem(
        string browseId,
        string? pageType,
        string title,
        Uri? thumbnail,
        string? subtitle)
    {
        return ResolveBrowseKind(browseId, pageType) switch
        {
            BrowseIdKind.Album => new HomeSectionItem.AlbumItem(new Album
            {
                Id = browseId,
                Title = title,
                ThumbnailUrl = thumbnail,
            }),
            BrowseIdKind.Artist => new HomeSectionItem.ArtistItem(new Artist
            {
                Id = browseId,
                Name = title,
                ThumbnailUrl = thumbnail,
                SubtitleText = CleanArtistSubtitle(subtitle),
            }),
            BrowseIdKind.Podcast or BrowseIdKind.Playlist => new HomeSectionItem.PlaylistItem(new Playlist
            {
                Id = browseId,
                Title = title,
                ThumbnailUrl = thumbnail,
                Author = string.IsNullOrEmpty(subtitle)
                    ? null
                    : new Artist { Id = ParsingHelpers.StableId("playlist-author", subtitle), Name = subtitle },
            }),
            _ => null,
        };
    }

    // MARK: - Regular results (musicResponsiveListItemRenderer)

    /// <summary>
    /// Parses one result row into its section item (song / album / artist / playlist / podcast),
    /// or <see langword="null"/> when the row is not navigable. Single source of truth for both
    /// the verbatim shelf sections and the per-type grouped lists.
    /// </summary>
    private static HomeSectionItem? ParseRowItem(JsonNode renderer)
    {
        // Songs / episodes: a videoId in playlistItemData or in the title run's watchEndpoint.
        var videoId = GetString(Prop(renderer, "playlistItemData"), "videoId")
                      ?? GetString(Prop(FlexColumnFirstRun(renderer, 0), "watchEndpoint"), "videoId")
                      ?? GetString(Prop(Prop(renderer, "navigationEndpoint"), "watchEndpoint"), "videoId");

        var title = ExtractTitleFromFlexColumns(renderer);

        if (!string.IsNullOrEmpty(videoId))
        {
            return new HomeSectionItem.SongItem(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = title ?? "Unknown",
                Artists = ParsingHelpers.ExtractArtistsFromFlexColumns(renderer),
                Album = ParsingHelpers.ExtractAlbumFromFlexColumns(renderer),
                ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(renderer),
                IsExplicit = ParsingHelpers.ExtractIsExplicit(renderer),
            });
        }

        // Other types: browseEndpoint either at the renderer root or on the title run.
        var browse = Prop(Prop(renderer, "navigationEndpoint"), "browseEndpoint")
                     ?? Prop(Prop(FlexColumnFirstRun(renderer, 0), "navigationEndpoint"), "browseEndpoint");
        var browseId = GetString(browse, "browseId");
        if (string.IsNullOrEmpty(browseId))
        {
            return null;
        }

        return CreateSectionItem(
            browseId,
            ExtractPageType(browse),
            title ?? "Unknown",
            ParsingHelpers.BestThumbnailUrl(renderer),
            ParsingHelpers.JoinRunTexts(FlexColumn(renderer, 1), "text"));
    }

    /// <summary>Routes a parsed row item into the per-type grouped lists.</summary>
    private static void ClassifyItem(
        HomeSectionItem item,
        List<Song> songs,
        List<Album> albums,
        List<Artist> artists,
        List<Playlist> playlists,
        List<Playlist> podcasts)
    {
        switch (item)
        {
            case HomeSectionItem.SongItem s:
                songs.Add(s.Song);
                break;
            case HomeSectionItem.AlbumItem a:
                albums.Add(a.Album);
                break;
            case HomeSectionItem.ArtistItem ar:
                artists.Add(ar.Artist);
                break;
            case HomeSectionItem.PlaylistItem p:
                // A podcast show's browseId is always MPSPP-prefixed (same signal ResolveBrowseKind
                // uses); everything else is an ordinary playlist.
                (p.Pl.Id.StartsWith("MPSPP", StringComparison.Ordinal) ? podcasts : playlists).Add(p.Pl);
                break;
        }
    }

    // MARK: - Classification

    /// <summary>
    /// Resolves a result's navigable kind from its renderer <c>pageType</c> first (most
    /// reliable), falling back to the shared <see cref="BrowseIdClassifier"/> prefix table
    /// (<c>MPRE/OLAK</c>→album, <c>UC/MPLAUC</c>→artist, <c>MPSPP</c>→podcast,
    /// <c>VL/PL/RDCLAK</c>→playlist). Mirrors <see cref="HomeResponseParser"/> so Search and
    /// Home classify identically (Property 24/25).
    /// </summary>
    private static BrowseIdKind ResolveBrowseKind(string browseId, string? pageType)
    {
        switch (pageType)
        {
            case PageTypeAlbum:
                return BrowseIdKind.Album;
            case PageTypeArtist:
            case PageTypeUserChannel:
            case PageTypeLibraryArtist:
                return BrowseIdKind.Artist;
            case PageTypePodcastShow:
            case PageTypePodcastShowDetail:
                return BrowseIdKind.Podcast;
            case PageTypePlaylist:
                return BrowseIdKind.Playlist;
        }

        return BrowseIdClassifier.Classify(browseId);
    }

    // MARK: - Card artists

    /// <summary>
    /// Extracts the artists backing a Top-Result <c>musicCardShelfRenderer</c> song. Prefers the
    /// subtitle runs that carry an artist <c>browseEndpoint</c> (real channel ids, navigable); if the
    /// card exposes none it falls back to the plain artist segment of the joined subtitle
    /// ("Song • <artist> • Album …") so the player bar still shows a name (Bug #6).
    /// </summary>
    private static IReadOnlyList<Artist> ExtractCardArtists(JsonNode card, string? subtitle)
    {
        var artists = new List<Artist>();
        if (Prop(Prop(card, "subtitle"), "runs") is JsonArray runs)
        {
            foreach (var run in runs)
            {
                var browse = Prop(Prop(run, "navigationEndpoint"), "browseEndpoint");
                var browseId = GetString(browse, "browseId");
                if (string.IsNullOrEmpty(browseId)
                    || ResolveBrowseKind(browseId, ExtractPageType(browse)) != BrowseIdKind.Artist)
                {
                    continue;
                }

                var name = GetString(run, "text");
                if (!string.IsNullOrEmpty(name))
                {
                    artists.Add(new Artist { Id = browseId, Name = name });
                }
            }
        }

        if (artists.Count == 0 && !string.IsNullOrWhiteSpace(subtitle))
        {
            // "Song • Artist Name • Album …" → the second "•"-separated segment is the artist line.
            var segments = subtitle.Split('•', '·');
            if (segments.Length >= 2)
            {
                var name = segments[1].Trim();
                if (name.Length > 0)
                {
                    artists.Add(new Artist { Id = ParsingHelpers.StableId("artist", name), Name = name });
                }
            }
        }

        return artists;
    }

    // MARK: - Subtitle helpers

    /// <summary>
    /// True when a card/list subtitle leads with a video kind ("Video", "Music video",
    /// "Video musik", "Klip"), so the UI can render a 16:9 cover instead of a square one. The kind
    /// is always the first "•"-separated segment, so we only inspect that segment.
    /// </summary>
    private static bool LooksLikeVideoSubtitle(string? subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return false;
        }

        var first = subtitle.Split('•', '·')[0];
        return first.Contains("Video", StringComparison.OrdinalIgnoreCase)
            || first.Contains("Klip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the informative tail of an artist subtitle (subscriber / monthly-listener count)
    /// by stripping the leading kind label ("Artist" / "Artis") and its separator. Returns
    /// <see langword="null"/> when nothing informative remains.
    /// </summary>
    private static string? CleanArtistSubtitle(string? subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return null;
        }

        var s = subtitle.Trim();
        foreach (var prefix in ArtistKindPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..].TrimStart(' ', '•', '·', '-', '–').Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static readonly string[] ArtistKindPrefixes = ["Artist", "Artis"];

    // MARK: - JsonNode helpers

    private static string? ExtractPageType(JsonNode? browseEndpoint) =>
        GetString(
            Prop(Prop(browseEndpoint, "browseEndpointContextSupportedConfigs"), "browseEndpointContextMusicConfig"),
            "pageType");

    private static JsonNode? FirstTitleRun(JsonNode? node)
    {
        var runs = Prop(Prop(node, "title"), "runs") as JsonArray;
        return runs is { Count: > 0 } ? runs[0] : null;
    }

    private static string? ExtractTitleFromFlexColumns(JsonNode? renderer)
    {
        var run = FlexColumnFirstRun(renderer, 0);
        return GetString(run, "text");
    }

    private static JsonNode? FlexColumn(JsonNode? renderer, int index)
    {
        if (Prop(renderer, "flexColumns") is JsonArray columns && index >= 0 && index < columns.Count)
        {
            return Prop(columns[index], "musicResponsiveListItemFlexColumnRenderer");
        }

        return null;
    }

    private static JsonNode? FlexColumnFirstRun(JsonNode? renderer, int index)
    {
        var runs = Prop(Prop(FlexColumn(renderer, index), "text"), "runs") as JsonArray;
        return runs is { Count: > 0 } ? runs[0] : null;
    }

    private static JsonNode? Prop(JsonNode? node, string? key)
    {
        if (key is not null && node is JsonObject obj && obj.TryGetPropertyValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static string? GetString(JsonNode? node, string key)
    {
        var value = Prop(node, key);
        return value is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
    }
}
