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
/// Episodes are intentionally left empty in this slice (advanced phase, ADR-0018). Related
/// artists, featured playlists and videos are parsed from their carousels. On malformed input the
/// parser throws <see cref="KasetError"/> with <see cref="KasetErrorKind.ParseError"/> rather than
/// crashing or leaking another exception type (Property 34). This type lives in
/// <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.
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
            // The id YTM expects for subscription/subscribe lives on the subscribe button renderer
            // (Bug 4); it can differ from the browse id used to load the page. Sending the browse id
            // instead is what produced the HTTP 400, so surface this id for the mutation.
            var subscribeChannelId = Str(subscribeButton, "channelId");
            var subscriberText = ExtractSubscriberText(subscribeButton);
            var monthlyListenersText = ExtractMonthlyListenersText(header);
            var (radioPlaylistId, radioVideoId) = ExtractStartRadio(header);

            var topSongs = new List<Song>();
            var albums = new List<Album>();
            var singles = new List<Album>();
            var videos = new List<Song>();
            var featured = new List<Playlist>();
            var related = new List<Artist>();
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
                    var section = ParseCarousel(carousel, artist, title);
                    if (section.IsEmpty)
                    {
                        continue;
                    }

                    var more = ExtractMoreBrowseId(carousel);

                    // Attribute the shelf's content to the right rail. A single carousel is
                    // homogeneous in practice, so the first non-empty bucket wins for the "See all".
                    if (section.Singles.Count > 0)
                    {
                        singles.AddRange(section.Singles);
                        if (more is not null)
                        {
                            seeAll = seeAll with { SinglesBrowseId = more };
                        }
                    }

                    if (section.Albums.Count > 0)
                    {
                        albums.AddRange(section.Albums);
                        if (more is not null)
                        {
                            seeAll = seeAll with { AlbumsBrowseId = more };
                        }
                    }

                    if (section.Videos.Count > 0)
                    {
                        videos.AddRange(section.Videos);
                        if (more is not null)
                        {
                            seeAll = seeAll with { VideosBrowseId = more };
                        }
                    }

                    if (section.Playlists.Count > 0)
                    {
                        featured.AddRange(section.Playlists);
                        if (more is not null)
                        {
                            seeAll = seeAll with { FeaturedBrowseId = more };
                        }
                    }

                    if (section.Artists.Count > 0)
                    {
                        related.AddRange(section.Artists);
                        if (more is not null)
                        {
                            seeAll = seeAll with { RelatedBrowseId = more };
                        }
                    }
                }
            }

            return new ArtistDetail
            {
                Artist = artist,
                Description = description,
                HeaderImageUrl = artist.ThumbnailUrl,
                SubscriberText = subscriberText,
                MonthlyListenersText = monthlyListenersText,
                RadioPlaylistId = radioPlaylistId,
                RadioVideoId = radioVideoId,
                TopSongs = topSongs,
                Albums = albums,
                SinglesAndEps = singles,
                Videos = videos,
                FeaturedPlaylists = featured,
                RelatedArtists = related,
                Episodes = Array.Empty<ArtistEpisode>(),
                IsSubscribed = isSubscribed,
                SubscribeChannelId = subscribeChannelId,
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

    /// <summary>
    /// Extracts the subscriber count line (e.g. <c>"1.2M subscribers"</c>) from the subscribe
    /// button, preferring the short form over the long form.
    /// </summary>
    private static string? ExtractSubscriberText(JsonNode? subscribeButton)
    {
        if (subscribeButton is null)
        {
            return null;
        }

        return ParsingHelpers.JoinRunTexts(subscribeButton, "shortSubscriberCountText")
            ?? ParsingHelpers.JoinRunTexts(subscribeButton, "subscriberCountText");
    }

    /// <summary>
    /// Extracts the monthly-listeners / monthly-audience line from the immersive header
    /// (<c>monthlyListenerCount.runs</c>), or <c>null</c>.
    /// </summary>
    private static string? ExtractMonthlyListenersText(JsonNode header)
    {
        var node = ResponseTreeSearch.FindFirst(header, "monthlyListenerCount");
        var joined = RunsTextDirect(node);
        return string.IsNullOrWhiteSpace(joined) ? null : joined!.Trim();
    }

    /// <summary>
    /// Reads the radio/mix seed from the header <c>startRadioButton</c>: a
    /// <c>watchPlaylistEndpoint.playlistId</c> (artist mix) and/or a
    /// <c>watchEndpoint.playlistId</c>/<c>videoId</c> (song radio fallback).
    /// </summary>
    private static (string? PlaylistId, string? VideoId) ExtractStartRadio(JsonNode header)
    {
        var startRadio = Prop(header, "startRadioButton");
        if (startRadio is null)
        {
            return (null, null);
        }

        var watchPlaylist = ResponseTreeSearch.FindFirst(startRadio, "watchPlaylistEndpoint");
        var playlistId = Str(watchPlaylist, "playlistId");
        if (playlistId is not null)
        {
            return (playlistId, null);
        }

        var watch = ResponseTreeSearch.FindFirst(startRadio, "watchEndpoint");
        return (Str(watch, "playlistId"), Str(watch, "videoId"));
    }

    private static string? RunsTextDirect(JsonNode? node)
    {
        var runs = Arr(Prop(node, "runs"));
        if (runs is null)
        {
            return null;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var run in runs)
        {
            if (Str(run, "text") is { } text)
            {
                sb.Append(text);
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
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
            Album = ParsingHelpers.ExtractAlbumFromFlexColumns(row),
            Duration = ExtractDurationFromColumns(row),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(row),
            IsExplicit = ParsingHelpers.ExtractIsExplicit(row),
        };
    }

    // MARK: - Carousels (musicCarouselShelfRenderer → musicTwoRowItemRenderer)

    /// <summary>
    /// The classified contents of a single carousel shelf. A real artist-page carousel is
    /// homogeneous (all albums, all videos, …); the buckets allow the rare mixed shelf and let the
    /// caller attribute the shelf's "See all" to the right rail.
    /// </summary>
    private readonly struct CarouselSection
    {
        public List<Album> Albums { get; init; }

        public List<Album> Singles { get; init; }

        public List<Song> Videos { get; init; }

        public List<Playlist> Playlists { get; init; }

        public List<Artist> Artists { get; init; }

        public bool IsEmpty =>
            Albums.Count == 0 && Singles.Count == 0 && Videos.Count == 0
            && Playlists.Count == 0 && Artists.Count == 0;
    }

    private static CarouselSection ParseCarousel(JsonNode carousel, Artist pageArtist, string? shelfTitle)
    {
        var section = new CarouselSection
        {
            Albums = new List<Album>(),
            Singles = new List<Album>(),
            Videos = new List<Song>(),
            Playlists = new List<Playlist>(),
            Artists = new List<Artist>(),
        };

        var contents = Arr(Prop(carousel, "contents"));
        if (contents is null)
        {
            return section;
        }

        var isSinglesShelf = IsSinglesShelf(shelfTitle);
        foreach (var itemData in contents)
        {
            if (Prop(itemData, "musicTwoRowItemRenderer") is not { } twoRow)
            {
                continue;
            }

            ClassifyCarouselItem(twoRow, pageArtist, isSinglesShelf, section);
        }

        return section;
    }

    /// <summary>
    /// Routes a single <c>musicTwoRowItemRenderer</c> into the matching bucket based on its
    /// endpoint shape (<c>watchEndpoint</c> → video) and, for browse targets, the
    /// <see cref="BrowseIdClassifier"/> kind (album/playlist/artist), using the shelf title as the
    /// album-vs-single tiebreaker. Mirrors the macOS <c>classifyCarouselItem</c>.
    /// </summary>
    private static void ClassifyCarouselItem(JsonNode twoRow, Artist pageArtist, bool isSinglesShelf, CarouselSection section)
    {
        // A video carousel item plays directly via watchEndpoint (no browseId).
        var watch = ResponseTreeSearch.FindFirst(twoRow, "watchEndpoint");
        var videoId = Str(watch, "videoId");

        var browseId = ParsingHelpers.ExtractBrowseId(twoRow)
            ?? Str(ResponseTreeSearch.FindFirst(twoRow, "browseEndpoint"), "browseId");

        if (browseId is null && videoId is not null)
        {
            var video = ParseVideoItem(twoRow, videoId, pageArtist);
            if (video is not null)
            {
                section.Videos.Add(video);
            }

            return;
        }

        if (browseId is null)
        {
            return;
        }

        switch (BrowseIdClassifier.Classify(browseId))
        {
            case BrowseIdKind.Album:
                var album = ParseAlbumItem(twoRow, browseId, pageArtist);
                if (album is null)
                {
                    break;
                }

                if (isSinglesShelf)
                {
                    section.Singles.Add(album);
                }
                else
                {
                    section.Albums.Add(album);
                }

                break;

            case BrowseIdKind.Playlist:
                var playlist = ParsePlaylistItem(twoRow, browseId, pageArtist);
                if (playlist is not null)
                {
                    section.Playlists.Add(playlist);
                }

                break;

            case BrowseIdKind.Artist:
                var related = ParseRelatedArtistItem(twoRow, browseId);
                if (related is not null)
                {
                    section.Artists.Add(related);
                }

                break;
        }
    }

    private static Album? ParseAlbumItem(JsonNode data, string browseId, Artist pageArtist)
    {
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

    private static Song? ParseVideoItem(JsonNode data, string videoId, Artist pageArtist)
    {
        var title = ParsingHelpers.ExtractText(data) ?? "Unknown";
        return new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = title,
            Artists = new[] { pageArtist },
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(data),
            HasVideo = true,
            IsExplicit = ParsingHelpers.ExtractIsExplicit(data),
        };
    }

    private static Playlist? ParsePlaylistItem(JsonNode data, string browseId, Artist pageArtist)
    {
        var title = ParsingHelpers.ExtractText(data);
        if (title is null)
        {
            return null;
        }

        return new Playlist
        {
            Id = browseId,
            Title = title,
            Author = pageArtist,
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(data),
        };
    }

    private static Artist? ParseRelatedArtistItem(JsonNode data, string browseId)
    {
        var name = ParsingHelpers.ExtractText(data);
        if (name is null)
        {
            return null;
        }

        return new Artist
        {
            Id = browseId,
            Name = name,
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(data),
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
