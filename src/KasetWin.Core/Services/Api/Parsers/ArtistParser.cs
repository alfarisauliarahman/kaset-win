using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for the <c>UC{channelId}</c> artist detail page
/// (Req 15.1). Ported from the macOS <c>ArtistParser</c>. Extracts the artist header
/// (<c>musicImmersiveHeaderRenderer</c>), the <b>Top songs</b> shelf
/// (<c>musicShelfRenderer</c>), the <b>Albums</b> and <b>Singles &amp; EPs</b> carousels
/// (<c>musicCarouselShelfRenderer</c> of <c>musicTwoRowItemRenderer</c>), the
/// subscription state (<c>subscribeButtonRenderer</c>) and the per-shelf "See all"
/// browse destinations (<c>moreContentButton</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c> and deterministic — no I/O, clocks, or randomness — so the
/// idempotency / stable-identity guarantee (Property 23) holds. Traversal is resilient to
/// container reshuffles (tabs, <c>singleColumnBrowseResultsRenderer</c>, …) via
/// <see cref="ResponseTreeSearch"/>, and album/single typing uses the <c>browseId</c> prefix
/// (<c>MPRE…</c>/<c>OLAK…</c>) via <see cref="BrowseIdClassifier"/>.
/// </para>
/// <para>
/// Episodes and related artists are intentionally left empty in this slice (advanced phase,
/// ADR-0018). On malformed input the parser throws <see cref="KasetError"/> with
/// <see cref="KasetErrorKind.ParseError"/> rather than crashing or leaking another exception
/// type (Property 34). This type lives in <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.
/// </para>
/// </remarks>
public static class ArtistParser
{
    private static readonly string[] HeaderRendererKeys =
    {
        "musicImmersiveHeaderRenderer",
        "musicHeaderRenderer",
        "musicVisualHeaderRenderer",
    };

    /// <summary>
    /// Parses a raw JSON string into an <see cref="ArtistDetail"/>. Invalid JSON is reported as
    /// <see cref="KasetErrorKind.ParseError"/>.
    /// </summary>
    public static ArtistDetail Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Artist response is not valid JSON.", ex);
        }

        return Parse(root);
    }

    /// <summary>
    /// Parses an artist detail browse response into an <see cref="ArtistDetail"/>.
    /// </summary>
    /// <param name="root">The parsed InnerTube response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is not a
    /// JSON object or carries no recognisable artist header (Property 34).
    /// </exception>
    public static ArtistDetail Parse(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Artist response root is missing or not a JSON object.");
        }

        try
        {
            var header = FindHeader(root);
            if (header is null)
            {
                throw new KasetError(
                    KasetErrorKind.ParseError,
                    "Artist response contains no recognisable artist header renderer.");
            }

            var subscribeButton = ResponseTreeSearch.FindFirst(header, "subscribeButtonRenderer");
            var artist = ParseArtist(header, subscribeButton);
            var description = ParsingHelpers.JoinRunTexts(header, "description");
            var isSubscribed = Bool(subscribeButton, "subscribed") ?? false;

            var topSongs = new List<Song>();
            var albums = new List<Album>();
            var singles = new List<Album>();
            var seeAll = new ArtistSeeAllDestinations();

            var sectionList = ResponseTreeSearch.FindFirst(root, "sectionListRenderer");
            foreach (var sectionData in Arr(Prop(sectionList, "contents")) ?? new JsonArray())
            {
                if (Prop(sectionData, "musicShelfRenderer") is { } songShelf)
                {
                    topSongs.AddRange(ParseTopSongs(songShelf));
                    var more = ExtractMoreBrowseId(songShelf);
                    if (more is not null)
                    {
                        seeAll = seeAll with { SongsBrowseId = more };
                    }
                }
                else if (Prop(sectionData, "musicCarouselShelfRenderer") is { } carousel)
                {
                    var title = ExtractCarouselTitle(carousel);
                    var isSingles = IsSinglesShelf(title);
                    var parsed = ParseAlbumCarousel(carousel, artist);
                    if (parsed.Count == 0)
                    {
                        continue;
                    }

                    var more = ExtractMoreBrowseId(carousel);
                    if (isSingles)
                    {
                        singles.AddRange(parsed);
                        if (more is not null)
                        {
                            seeAll = seeAll with { SinglesBrowseId = more };
                        }
                    }
                    else
                    {
                        albums.AddRange(parsed);
                        if (more is not null)
                        {
                            seeAll = seeAll with { AlbumsBrowseId = more };
                        }
                    }
                }
            }

            return new ArtistDetail
            {
                Artist = artist,
                Description = description,
                TopSongs = topSongs,
                Albums = albums,
                SinglesAndEps = singles,
                Episodes = Array.Empty<ArtistEpisode>(),
                IsSubscribed = isSubscribed,
                SeeAll = seeAll,
            };
        }
        catch (KasetError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Failed to parse Artist response.", ex);
        }
    }

    // MARK: - Header / artist identity

    private static JsonNode? FindHeader(JsonNode root)
    {
        foreach (var key in HeaderRendererKeys)
        {
            if (ResponseTreeSearch.FindFirst(root, key) is { } header)
            {
                return header;
            }
        }

        return null;
    }

    private static Artist ParseArtist(JsonNode header, JsonNode? subscribeButton)
    {
        var name = ParsingHelpers.ExtractText(header) ?? "Unknown Artist";
        var thumbnail = ParsingHelpers.BestThumbnailUrl(header);

        // The channelId (UC…) lives on the subscribe button; it is the navigable artist id.
        var channelId = Str(subscribeButton, "channelId");
        var id = ParsingHelpers.IsNavigableArtistId(channelId)
            ? channelId!
            : ParsingHelpers.StableId("artist", name);

        return new Artist { Id = id, Name = name, ThumbnailUrl = thumbnail };
    }

    // MARK: - Top songs (musicShelfRenderer)

    private static IReadOnlyList<Song> ParseTopSongs(JsonNode shelf)
    {
        var contents = Arr(Prop(shelf, "contents"));
        if (contents is null)
        {
            return Array.Empty<Song>();
        }

        var songs = new List<Song>(contents.Count);
        foreach (var itemData in contents)
        {
            if (Prop(itemData, "musicResponsiveListItemRenderer") is not { } row)
            {
                continue;
            }

            var song = ParseSongRow(row);
            if (song is not null)
            {
                songs.Add(song);
            }
        }

        return songs;
    }

    private static Song? ParseSongRow(JsonNode row)
    {
        var videoId = ExtractVideoId(row);
        if (videoId is null)
        {
            return null;
        }

        var title = ExtractTitleFromFlexColumns(row) ?? "Unknown";
        return new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = title,
            Artists = ParsingHelpers.ExtractArtistsFromFlexColumns(row),
            Duration = ExtractDurationFromColumns(row),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(row),
            IsExplicit = ParsingHelpers.ExtractIsExplicit(row),
        };
    }

    // MARK: - Albums / singles (musicCarouselShelfRenderer → musicTwoRowItemRenderer)

    private static IReadOnlyList<Album> ParseAlbumCarousel(JsonNode carousel, Artist pageArtist)
    {
        var contents = Arr(Prop(carousel, "contents"));
        if (contents is null)
        {
            return Array.Empty<Album>();
        }

        var albums = new List<Album>(contents.Count);
        foreach (var itemData in contents)
        {
            if (Prop(itemData, "musicTwoRowItemRenderer") is not { } twoRow)
            {
                continue;
            }

            var album = ParseAlbumItem(twoRow, pageArtist);
            if (album is not null)
            {
                albums.Add(album);
            }
        }

        return albums;
    }

    private static Album? ParseAlbumItem(JsonNode data, Artist pageArtist)
    {
        // The browse target may sit on the renderer's top-level navigationEndpoint or on the
        // title run (sanitized fixtures use the latter); search the item for either shape.
        var browseId = ParsingHelpers.ExtractBrowseId(data)
            ?? Str(ResponseTreeSearch.FindFirst(data, "browseEndpoint"), "browseId");

        // Only album / single browse ids (MPRE…/OLAK…) belong on the Albums / Singles rails.
        if (browseId is null || BrowseIdClassifier.Classify(browseId) != BrowseIdKind.Album)
        {
            return null;
        }

        var title = ParsingHelpers.ExtractText(data);
        if (title is null)
        {
            return null;
        }

        return new Album
        {
            Id = browseId,
            Title = title,
            Artists = new[] { pageArtist },
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(data),
            Year = ExtractYear(data),
        };
    }

    // MARK: - Shelf classification

    private static bool IsSinglesShelf(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return false;
        }

        var lowered = title.ToLowerInvariant();
        return lowered.Contains("single", StringComparison.Ordinal)
            || lowered.Contains("ep", StringComparison.Ordinal);
    }

    private static string? ExtractCarouselTitle(JsonNode carousel) =>
        ParsingHelpers.ExtractText(
            Prop(Prop(carousel, "header"), "musicCarouselShelfBasicHeaderRenderer"));

    /// <summary>
    /// Extracts the "See all" / "More" browse id for a shelf, when present. Covers both the
    /// carousel header <c>moreContentButton</c> and the <c>musicShelfRenderer</c>
    /// <c>bottomEndpoint</c> (used by the Top songs shelf).
    /// </summary>
    private static string? ExtractMoreBrowseId(JsonNode shelf)
    {
        var moreButton = ResponseTreeSearch.FindFirst(shelf, "moreContentButton");
        if (moreButton is not null)
        {
            // The navigationEndpoint may sit directly on the more button or under its
            // buttonRenderer; search the subtree for the browse target either way.
            var fromButton = Str(ResponseTreeSearch.FindFirst(moreButton, "browseEndpoint"), "browseId");
            if (fromButton is not null)
            {
                return fromButton;
            }
        }

        var bottom = Prop(shelf, "bottomEndpoint");
        return Str(Prop(bottom, "browseEndpoint"), "browseId");
    }

    // MARK: - Field extraction helpers

    private static string? ExtractVideoId(JsonNode row)
    {
        var direct = Str(Prop(row, "playlistItemData"), "videoId");
        if (direct is not null)
        {
            return direct;
        }

        var watch = ResponseTreeSearch.FindFirst(row, "watchEndpoint");
        return Str(watch, "videoId");
    }

    private static string? ExtractTitleFromFlexColumns(JsonNode row)
    {
        var flexColumns = Arr(Prop(row, "flexColumns"));
        if (flexColumns is null || flexColumns.Count == 0)
        {
            return null;
        }

        var renderer = Prop(flexColumns[0], "musicResponsiveListItemFlexColumnRenderer");
        return ParsingHelpers.ExtractText(renderer, "text");
    }

    private static TimeSpan? ExtractDurationFromColumns(JsonNode row)
    {
        foreach (var columnsKey in new[] { "fixedColumns", "flexColumns" })
        {
            var columns = Arr(Prop(row, columnsKey));
            if (columns is null)
            {
                continue;
            }

            foreach (var column in columns)
            {
                var renderer = Prop(column, "musicResponsiveListItemFixedColumnRenderer")
                    ?? Prop(column, "musicResponsiveListItemFlexColumnRenderer");
                foreach (var text in ParsingHelpers.ExtractRunTexts(renderer, "text"))
                {
                    var parsed = ParsingHelpers.ParseDuration(text);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }
            }
        }

        return null;
    }

    private static string? ExtractYear(JsonNode data)
    {
        // Subtitles arrive either as separate runs ("Album", "•", "2024") or as a single
        // joined run ("Album • 2024"), so scan for the first standalone 4-digit year token.
        foreach (var text in ParsingHelpers.ExtractRunTexts(data, "subtitle"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\b(\d{4})\b");
            if (match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1900 and <= 2100)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    // MARK: - JsonNode accessors

    private static JsonNode? Prop(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

    private static JsonArray? Arr(JsonNode? node) => node as JsonArray;

    private static string? Str(JsonNode? node, string key) =>
        Prop(node, key) is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static bool? Bool(JsonNode? node, string key) =>
        Prop(node, key) is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;
}
