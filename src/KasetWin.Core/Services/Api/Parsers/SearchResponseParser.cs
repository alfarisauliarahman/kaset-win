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

        // musicShelfRenderer holds the regular (non-top) results. Searching recursively keeps
        // us resilient to the tabbed vs filtered container layouts YouTube returns.
        foreach (var shelf in ResponseTreeSearch.FindAll(contents, "musicShelfRenderer"))
        {
            if (shelf is not JsonObject shelfObj
                || !shelfObj.TryGetPropertyValue("contents", out var shelfContents)
                || shelfContents is not JsonArray rows)
            {
                continue;
            }

            foreach (var row in rows)
            {
                var renderer = Prop(row, "musicResponsiveListItemRenderer");
                if (renderer is null)
                {
                    continue;
                }

                ClassifyListItem(renderer, songs, albums, artists, playlists, podcasts);
            }
        }

        return new SearchResponse
        {
            TopResult = topResult,
            Songs = songs,
            Albums = albums,
            Artists = artists,
            Playlists = playlists,
            Podcasts = podcasts,
        };
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
                ThumbnailUrl = thumbnail,
                IsExplicit = ParsingHelpers.ExtractIsExplicit(card),
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

    private static void ClassifyListItem(
        JsonNode renderer,
        List<Song> songs,
        List<Album> albums,
        List<Artist> artists,
        List<Playlist> playlists,
        List<Playlist> podcasts)
    {
        // Songs: a videoId in playlistItemData or in the title run's watchEndpoint.
        var videoId = GetString(Prop(renderer, "playlistItemData"), "videoId")
                      ?? GetString(Prop(FlexColumnFirstRun(renderer, 0), "watchEndpoint"), "videoId")
                      ?? GetString(Prop(Prop(renderer, "navigationEndpoint"), "watchEndpoint"), "videoId");

        var title = ExtractTitleFromFlexColumns(renderer);

        if (!string.IsNullOrEmpty(videoId))
        {
            songs.Add(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = title ?? "Unknown",
                Artists = ParsingHelpers.ExtractArtistsFromFlexColumns(renderer),
                ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(renderer),
                IsExplicit = ParsingHelpers.ExtractIsExplicit(renderer),
            });
            return;
        }

        // Other types: browseEndpoint either at the renderer root or on the title run.
        var browse = Prop(Prop(renderer, "navigationEndpoint"), "browseEndpoint")
                     ?? Prop(Prop(FlexColumnFirstRun(renderer, 0), "navigationEndpoint"), "browseEndpoint");
        var browseId = GetString(browse, "browseId");
        if (string.IsNullOrEmpty(browseId))
        {
            return;
        }

        var pageType = ExtractPageType(browse);
        var thumbnail = ParsingHelpers.BestThumbnailUrl(renderer);
        var subtitle = ParsingHelpers.JoinRunTexts(FlexColumn(renderer, 1), "text");
        var displayTitle = title ?? "Unknown";

        switch (ResolveBrowseKind(browseId, pageType))
        {
            case BrowseIdKind.Album:
                albums.Add(new Album { Id = browseId, Title = displayTitle, ThumbnailUrl = thumbnail });
                break;
            case BrowseIdKind.Artist:
                artists.Add(new Artist { Id = browseId, Name = displayTitle, ThumbnailUrl = thumbnail });
                break;
            case BrowseIdKind.Podcast:
                podcasts.Add(MakePlaylist(browseId, displayTitle, thumbnail, subtitle));
                break;
            case BrowseIdKind.Playlist:
                playlists.Add(MakePlaylist(browseId, displayTitle, thumbnail, subtitle));
                break;
        }
    }

    private static Playlist MakePlaylist(string browseId, string title, Uri? thumbnail, string? subtitle) =>
        new()
        {
            Id = browseId,
            Title = title,
            ThumbnailUrl = thumbnail,
            Author = string.IsNullOrEmpty(subtitle)
                ? null
                : new Artist { Id = ParsingHelpers.StableId("playlist-author", subtitle), Name = subtitle },
        };

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
