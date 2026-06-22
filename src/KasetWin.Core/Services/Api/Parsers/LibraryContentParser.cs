using System.Text.Json;
using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for the InnerTube <c>FEmusic_library_landing</c> surface
/// (Req 13.1). The library landing exposes the user's saved playlists, followed artists,
/// saved albums, the Liked Music auto playlist (<c>VLLM</c>) and — minimally — subscribed
/// podcast shows, laid out as <c>musicTwoRowItemRenderer</c> tiles inside a
/// <c>gridRenderer</c> (or, depending on the layout YouTube returns, a
/// <c>musicShelfRenderer</c> / <c>musicCarouselShelfRenderer</c>) under a
/// <c>sectionListRenderer</c>. Mirrors the macOS <c>LibraryContentParser</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c>, side-effect free and deterministic — no I/O, clocks, or
/// randomness — so the parser satisfies the idempotency / stable-identity guarantee
/// (Property 23). Each tile is classified into the correct collection by renderer
/// <c>pageType</c> first and then by <c>browseId</c> prefix via
/// <see cref="BrowseIdClassifier"/> (Property 25 / Req 13.1):
/// <c>VL/PL/RDCLAK</c> (incl. <c>VLLM</c> Liked Music) → playlist, <c>MPRE/OLAK</c> → album,
/// <c>UC/MPLAUC</c> → artist, <c>MPSPP</c> → podcast (surfaced into the playlist collection
/// since <see cref="LibraryContent"/> has no dedicated podcast list), and a
/// <c>watchEndpoint</c> tile → song.
/// </para>
/// <para>
/// On malformed / scrambled input the parser throws <see cref="KasetError"/> with
/// <see cref="KasetErrorKind.ParseError"/> rather than crashing or leaking another exception
/// type (Property 34, Req 20.3). A structurally valid response that simply contains no items
/// returns an empty <see cref="LibraryContent"/>. This type lives in <c>KasetWin.Core</c> and
/// has no WinUI/WinRT dependency.
/// </para>
/// </remarks>
public static class LibraryContentParser
{
    private const string PageTypeAlbum = "MUSIC_PAGE_TYPE_ALBUM";
    private const string PageTypePlaylist = "MUSIC_PAGE_TYPE_PLAYLIST";
    private const string PageTypeArtist = "MUSIC_PAGE_TYPE_ARTIST";
    private const string PageTypeUserChannel = "MUSIC_PAGE_TYPE_USER_CHANNEL";
    private const string PageTypeLibraryArtist = "MUSIC_PAGE_TYPE_LIBRARY_ARTIST";
    private const string PageTypePodcastShow = "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE";

    /// <summary>
    /// Parses a raw JSON string into a <see cref="LibraryContent"/>. Invalid JSON is reported
    /// as <see cref="KasetErrorKind.ParseError"/>.
    /// </summary>
    public static LibraryContent Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Library landing response is not valid JSON.", ex);
        }

        return Parse(root);
    }

    /// <summary>
    /// Parses a <c>FEmusic_library_landing</c> response tree into a <see cref="LibraryContent"/>
    /// with its playlists, albums, artists and songs.
    /// </summary>
    /// <param name="root">The decoded InnerTube response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or contains no recognisable section-list
    /// structure (Property 34, Req 20.3).
    /// </exception>
    public static LibraryContent Parse(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            throw new KasetError(
                KasetErrorKind.ParseError,
                "Library landing response root is missing or not a JSON object.");
        }

        try
        {
            // ResponseTreeSearch makes this resilient to the container renderers (tabs,
            // singleColumnBrowseResultsRenderer, …) being reshuffled around the section list.
            var sectionList = ResponseTreeSearch.FindFirst(root, "sectionListRenderer");
            if (sectionList is null)
            {
                throw new KasetError(
                    KasetErrorKind.ParseError,
                    "Library landing response contains no sectionListRenderer.");
            }

            var playlists = new List<Playlist>();
            var albums = new List<Album>();
            var artists = new List<Artist>();
            var songs = new List<Song>();

            // The landing surface uses musicTwoRowItemRenderer tiles (grids/carousels) and may
            // also use musicResponsiveListItemRenderer rows. Collecting both recursively keeps
            // the parser resilient to whether the items sit in a gridRenderer, a
            // musicShelfRenderer, or a musicCarouselShelfRenderer.
            foreach (var tile in ResponseTreeSearch.FindAll(sectionList, "musicTwoRowItemRenderer"))
            {
                ClassifyTwoRowItem(tile, playlists, albums, artists, songs);
            }

            foreach (var row in ResponseTreeSearch.FindAll(sectionList, "musicResponsiveListItemRenderer"))
            {
                ClassifyResponsiveListItem(row, playlists, albums, artists, songs);
            }

            return new LibraryContent
            {
                Playlists = playlists,
                Albums = albums,
                Artists = artists,
                Songs = songs,
            };
        }
        catch (KasetError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Failed to parse library landing response.", ex);
        }
    }

    // MARK: - Item classification

    private static void ClassifyTwoRowItem(
        JsonNode tile,
        List<Playlist> playlists,
        List<Album> albums,
        List<Artist> artists,
        List<Song> songs)
    {
        var title = ParsingHelpers.ExtractText(tile);
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        var thumbnail = ParsingHelpers.BestThumbnailUrl(tile);
        var navigationEndpoint = Prop(tile, "navigationEndpoint");
        if (navigationEndpoint is null)
        {
            return;
        }

        // watchEndpoint → a playable song/video tile.
        var videoId = Str(Prop(navigationEndpoint, "watchEndpoint"), "videoId");
        if (!string.IsNullOrEmpty(videoId))
        {
            songs.Add(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = title,
                Artists = ParsingHelpers.ExtractArtists(tile),
                ThumbnailUrl = thumbnail,
                IsExplicit = ParsingHelpers.ExtractIsExplicit(tile),
            });
            return;
        }

        var browseEndpoint = Prop(navigationEndpoint, "browseEndpoint");
        var browseId = Str(browseEndpoint, "browseId");
        if (string.IsNullOrEmpty(browseId))
        {
            return;
        }

        AddBrowseItem(
            browseId,
            ExtractPageType(browseEndpoint),
            title,
            thumbnail,
            ParsingHelpers.ExtractArtists(tile),
            ParsingHelpers.JoinRunTexts(tile),
            playlists,
            albums,
            artists);
    }

    private static void ClassifyResponsiveListItem(
        JsonNode row,
        List<Playlist> playlists,
        List<Album> albums,
        List<Artist> artists,
        List<Song> songs)
    {
        var title = ExtractTitleFromFlexColumns(row);

        // playlistItemData.videoId (or a watchEndpoint) → a playable song row.
        var videoId = Str(Prop(row, "playlistItemData"), "videoId")
            ?? Str(ResponseTreeSearch.FindFirst(row, "watchEndpoint"), "videoId");
        if (!string.IsNullOrEmpty(videoId))
        {
            songs.Add(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = title ?? "Unknown",
                Artists = ParsingHelpers.ExtractArtistsFromFlexColumns(row),
                ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(row),
                IsExplicit = ParsingHelpers.ExtractIsExplicit(row),
            });
            return;
        }

        var browseEndpoint = Prop(Prop(row, "navigationEndpoint"), "browseEndpoint");
        var browseId = Str(browseEndpoint, "browseId");
        if (string.IsNullOrEmpty(browseId))
        {
            return;
        }

        AddBrowseItem(
            browseId,
            ExtractPageType(browseEndpoint),
            title ?? "Unknown",
            ParsingHelpers.BestThumbnailUrl(row),
            ParsingHelpers.ExtractArtistsFromFlexColumns(row),
            playlistAuthor: null,
            playlists,
            albums,
            artists);
    }

    // MARK: - Browse item typing

    private static void AddBrowseItem(
        string browseId,
        string? pageType,
        string title,
        Uri? thumbnail,
        IReadOnlyList<Artist> itemArtists,
        string? playlistAuthor,
        List<Playlist> playlists,
        List<Album> albums,
        List<Artist> artists)
    {
        switch (ResolveBrowseKind(browseId, pageType))
        {
            case BrowseIdKind.Album:
                albums.Add(new Album
                {
                    Id = browseId,
                    Title = title,
                    Artists = itemArtists,
                    ThumbnailUrl = thumbnail,
                });
                break;

            case BrowseIdKind.Artist:
                artists.Add(new Artist
                {
                    Id = browseId,
                    Name = title,
                    ThumbnailUrl = thumbnail,
                });
                break;

            // Podcast shows have no dedicated collection on LibraryContent, so surface them
            // (minimally) as playlists alongside the Liked Music auto playlist (VLLM) and the
            // user's saved playlists, rather than dropping them.
            case BrowseIdKind.Playlist:
            case BrowseIdKind.Podcast:
                playlists.Add(new Playlist
                {
                    Id = browseId,
                    Title = title,
                    Author = playlistAuthor is not null
                        ? new Artist
                        {
                            Id = ParsingHelpers.StableId("playlist-author", playlistAuthor),
                            Name = playlistAuthor,
                        }
                        : null,
                    ThumbnailUrl = thumbnail,
                });
                break;

            default:
                // Unknown prefix with no usable pageType: drop rather than misclassify.
                break;
        }
    }

    /// <summary>
    /// Resolves the item kind from the renderer <c>pageType</c> first (most reliable) and falls
    /// back to the <c>browseId</c> prefix via <see cref="BrowseIdClassifier"/>.
    /// </summary>
    private static BrowseIdKind ResolveBrowseKind(string browseId, string? pageType)
    {
        switch (pageType)
        {
            case PageTypeAlbum:
                return BrowseIdKind.Album;
            case PageTypePlaylist:
                return BrowseIdKind.Playlist;
            case PageTypeArtist:
            case PageTypeUserChannel:
            case PageTypeLibraryArtist:
                return BrowseIdKind.Artist;
            case PageTypePodcastShow:
                return BrowseIdKind.Podcast;
        }

        return BrowseIdClassifier.Classify(browseId);
    }

    // MARK: - Local extraction helpers

    private static string? ExtractPageType(JsonNode? browseEndpoint) =>
        Str(
            Prop(
                Prop(browseEndpoint, "browseEndpointContextSupportedConfigs"),
                "browseEndpointContextMusicConfig"),
            "pageType");

    private static string? ExtractTitleFromFlexColumns(JsonNode? row)
    {
        var flexColumns = Arr(Prop(row, "flexColumns"));
        if (flexColumns is null || flexColumns.Count == 0)
        {
            return null;
        }

        var renderer = Prop(flexColumns[0], "musicResponsiveListItemFlexColumnRenderer");
        return ParsingHelpers.ExtractText(renderer, "text");
    }

    // MARK: - JsonNode accessors

    private static JsonNode? Prop(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

    private static JsonArray? Arr(JsonNode? node) => node as JsonArray;

    private static string? Str(JsonNode? node, string key) =>
        Prop(node, key) is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
