using System.Text.Json;
using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for the section-based InnerTube surfaces that share the
/// Home response shape: <c>FEmusic_home</c>, <c>FEmusic_explore</c>, <c>FEmusic_charts</c>,
/// <c>FEmusic_moods_and_genres</c>, and <c>FEmusic_new_releases</c>. Ported from the macOS
/// <c>HomeResponseParser</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c> and deterministic — no I/O, clocks, or randomness — so the
/// idempotency / stable-identity guarantee (Property 23) holds. Traversal is resilient to
/// container reshuffles via <see cref="ResponseTreeSearch"/>, and item typing uses
/// <see cref="BrowseIdClassifier"/> plus the renderer <c>pageType</c> (Property 25).
/// </para>
/// <para>
/// On malformed / scrambled input the parser throws <see cref="KasetError"/> with
/// <see cref="KasetErrorKind.ParseError"/> rather than crashing or leaking another exception
/// type (Property 34). A structurally valid response that simply yields no sections returns an
/// empty <see cref="HomeResponse"/>.
/// </para>
/// </remarks>
public static class HomeResponseParser
{
    /// <summary>
    /// Parses a raw JSON string into a <see cref="HomeResponse"/>. Invalid JSON is reported as
    /// <see cref="KasetErrorKind.ParseError"/>.
    /// </summary>
    public static HomeResponse Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Home response is not valid JSON.", ex);
        }

        return Parse(root);
    }

    /// <summary>
    /// Parses a section-based browse response (Home/Explore/Charts/Moods/NewReleases) into a
    /// <see cref="HomeResponse"/> with its sections and an optional continuation token.
    /// </summary>
    /// <param name="root">The parsed InnerTube response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is not a
    /// JSON object or contains no recognisable section-list structure (Property 34).
    /// </exception>
    public static HomeResponse Parse(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Home response root is missing or not a JSON object.");
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
                    "Home response contains no sectionListRenderer.");
            }

            var sections = ParseSections(Arr(Prop(sectionList, "contents")));
            var token = ExtractContinuationToken(sectionList);

            return new HomeResponse { Sections = sections, ContinuationToken = token };
        }
        catch (KasetError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Failed to parse Home response.", ex);
        }
    }

    /// <summary>
    /// Parses a continuation response (progressive Home/Explore loading) into the additional
    /// sections and the next continuation token.
    /// </summary>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is not a
    /// JSON object or carries no continuation contents (Property 34).
    /// </exception>
    public static HomeResponse ParseContinuation(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Home continuation root is missing or not a JSON object.");
        }

        try
        {
            var sections = new List<HomeSection>();
            JsonNode? tokenSource = null;

            var sectionListContinuation = ResponseTreeSearch.FindFirst(root, "sectionListContinuation");
            if (sectionListContinuation is not null)
            {
                sections.AddRange(ParseSections(Arr(Prop(sectionListContinuation, "contents"))));
                tokenSource = sectionListContinuation;
            }

            var shelfContinuation = ResponseTreeSearch.FindFirst(root, "musicShelfContinuation");
            if (shelfContinuation is not null)
            {
                var shelf = ParseMusicShelf(shelfContinuation);
                if (shelf is not null)
                {
                    sections.Add(shelf);
                }

                tokenSource ??= shelfContinuation;
            }

            if (sectionListContinuation is null && shelfContinuation is null)
            {
                throw new KasetError(
                    KasetErrorKind.ParseError,
                    "Home continuation contains no recognisable continuation contents.");
            }

            return new HomeResponse
            {
                Sections = sections,
                ContinuationToken = ExtractContinuationToken(tokenSource ?? root),
            };
        }
        catch (KasetError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Failed to parse Home continuation.", ex);
        }
    }

    // MARK: - Section parsing

    private static IReadOnlyList<HomeSection> ParseSections(JsonArray? sectionContents)
    {
        if (sectionContents is null)
        {
            return Array.Empty<HomeSection>();
        }

        var sections = new List<HomeSection>(sectionContents.Count);
        foreach (var sectionData in sectionContents)
        {
            var section = ParseHomeSection(sectionData);
            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    private static HomeSection? ParseHomeSection(JsonNode? data)
    {
        if (Prop(data, "musicCarouselShelfRenderer") is { } carousel)
        {
            return ParseShelf(carousel, ExtractCarouselTitle(carousel) ?? "Unknown Section", "contents");
        }

        if (Prop(data, "musicShelfRenderer") is { } shelf)
        {
            return ParseMusicShelf(shelf);
        }

        if (Prop(data, "musicCardShelfRenderer") is { } card)
        {
            var title = ParsingHelpers.ExtractText(Prop(card, "header") is { } h
                ? Prop(h, "musicCardShelfHeaderBasicRenderer")
                : null) ?? "Featured";
            return ParseShelf(card, title, "contents");
        }

        if (Prop(data, "musicImmersiveCarouselShelfRenderer") is { } immersive)
        {
            return ParseShelf(immersive, ExtractCarouselTitle(immersive) ?? "Featured", "contents");
        }

        if (Prop(data, "gridRenderer") is { } grid)
        {
            var title = ParsingHelpers.ExtractText(Prop(grid, "header") is { } h
                ? Prop(h, "gridHeaderRenderer")
                : null) ?? "Charts";
            return ParseShelf(grid, title, "items");
        }

        // itemSectionRenderer is a transparent wrapper around another renderer.
        if (Prop(data, "itemSectionRenderer") is { } itemSection
            && Arr(Prop(itemSection, "contents")) is { } itemContents)
        {
            foreach (var itemContent in itemContents)
            {
                var section = ParseHomeSection(itemContent);
                if (section is not null)
                {
                    return section;
                }
            }
        }

        return null;
    }

    private static HomeSection? ParseMusicShelf(JsonNode shelf) =>
        ParseShelf(shelf, ParsingHelpers.ExtractText(shelf) ?? "Unknown Section", "contents");

    private static HomeSection? ParseShelf(JsonNode shelf, string title, string itemsKey)
    {
        var contents = Arr(Prop(shelf, itemsKey));
        if (contents is null)
        {
            return null;
        }

        var items = new List<HomeSectionItem>(contents.Count);
        foreach (var itemData in contents)
        {
            var item = ParseHomeSectionItem(itemData);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        var firstItemId = items[0].Id;
        var stableId = ParsingHelpers.StableId(title, firstItemId);

        return new HomeSection
        {
            Id = stableId,
            Title = title,
            Items = items,
            IsChart = IsChartSection(title),
        };
    }

    // MARK: - Item parsing

    private static HomeSectionItem? ParseHomeSectionItem(JsonNode? data)
    {
        if (Prop(data, "musicTwoRowItemRenderer") is { } twoRow)
        {
            return ParseTwoRowItem(twoRow);
        }

        if (Prop(data, "musicResponsiveListItemRenderer") is { } responsive)
        {
            return ParseResponsiveListItem(responsive);
        }

        if (Prop(data, "musicNavigationButtonRenderer") is { } button)
        {
            return ParseNavigationButton(button);
        }

        return null;
    }

    private static HomeSectionItem? ParseTwoRowItem(JsonNode data)
    {
        var title = ParsingHelpers.ExtractText(data);
        if (title is null)
        {
            return null;
        }

        var thumbnail = ParsingHelpers.BestThumbnailUrl(data);
        var navigationEndpoint = Prop(data, "navigationEndpoint");
        if (navigationEndpoint is null)
        {
            return null;
        }

        // watchEndpoint → playable song/video.
        var videoId = Str(Prop(navigationEndpoint, "watchEndpoint"), "videoId");
        if (videoId is not null)
        {
            return new HomeSectionItem.SongItem(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = title,
                Artists = ParsingHelpers.ExtractArtists(data),
                ThumbnailUrl = thumbnail,
                VideoType = ExtractMusicVideoType(navigationEndpoint),
                IsExplicit = ParsingHelpers.ExtractIsExplicit(data),
            });
        }

        // browseEndpoint → album / playlist / artist.
        var browseEndpoint = Prop(navigationEndpoint, "browseEndpoint");
        var browseId = Str(browseEndpoint, "browseId");
        if (browseId is not null)
        {
            return CreateBrowseItem(
                browseId,
                ExtractPageType(browseEndpoint),
                title,
                thumbnail,
                ParsingHelpers.ExtractArtists(data),
                playlistAuthor: ParsingHelpers.JoinRunTexts(data));
        }

        return null;
    }

    private static HomeSectionItem? ParseResponsiveListItem(JsonNode data)
    {
        var videoId = ExtractResponsiveVideoId(data);
        if (videoId is null)
        {
            // Non-song row (e.g. an album/artist link in a vertical shelf).
            var browseEndpoint = Prop(Prop(data, "navigationEndpoint"), "browseEndpoint");
            var browseId = Str(browseEndpoint, "browseId");
            if (browseId is null)
            {
                return null;
            }

            var browseTitle = ExtractTitleFromFlexColumns(data) ?? "Unknown";
            return CreateBrowseItem(
                browseId,
                ExtractPageType(browseEndpoint),
                browseTitle,
                ParsingHelpers.BestThumbnailUrl(data),
                ParsingHelpers.ExtractArtistsFromFlexColumns(data),
                playlistAuthor: null);
        }

        var title = ExtractTitleFromFlexColumns(data) ?? "Unknown";
        return new HomeSectionItem.SongItem(new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = title,
            Artists = ParsingHelpers.ExtractArtistsFromFlexColumns(data),
            Duration = ExtractDurationFromFlexColumns(data),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(data),
            VideoType = ExtractMusicVideoType(Prop(data, "navigationEndpoint")),
            IsExplicit = ParsingHelpers.ExtractIsExplicit(data),
        });
    }

    private static HomeSectionItem.PlaylistItem? ParseNavigationButton(JsonNode data)
    {
        var title = ParsingHelpers.ExtractText(data, "buttonText");
        if (title is null)
        {
            return null;
        }

        var browseEndpoint = Prop(Prop(data, "clickCommand"), "browseEndpoint");
        var browseId = Str(browseEndpoint, "browseId");
        if (browseId is null)
        {
            return null;
        }

        // Mood/genre buttons can repeat a browseId across sections; the params token keeps the
        // identity unique so list virtualization does not collide.
        var paramsToken = Str(browseEndpoint, "params");
        var uniqueId = paramsToken is not null ? $"{browseId}_{paramsToken}" : browseId;

        var thumbnail = ParsingHelpers.BestThumbnailUrl(data);

        return new HomeSectionItem.PlaylistItem(new Playlist
        {
            Id = uniqueId,
            Title = title,
            ThumbnailUrl = thumbnail,
        });
    }

    // MARK: - Browse item typing

    private static HomeSectionItem? CreateBrowseItem(
        string browseId,
        string? pageType,
        string title,
        Uri? thumbnail,
        IReadOnlyList<Artist> artists,
        string? playlistAuthor)
    {
        var kind = ResolveBrowseKind(browseId, pageType);

        switch (kind)
        {
            case BrowseIdKind.Album:
                return new HomeSectionItem.AlbumItem(new Album
                {
                    Id = browseId,
                    Title = title,
                    Artists = artists,
                    ThumbnailUrl = thumbnail,
                });

            case BrowseIdKind.Artist:
                return new HomeSectionItem.ArtistItem(new Artist
                {
                    Id = browseId,
                    Name = title,
                    ThumbnailUrl = thumbnail,
                });

            // Podcast shows are browsable like playlists; surface them as a playlist item so
            // Home content is not dropped (there is no dedicated Home podcast item).
            case BrowseIdKind.Playlist:
            case BrowseIdKind.Podcast:
                return new HomeSectionItem.PlaylistItem(new Playlist
                {
                    Id = browseId,
                    Title = title,
                    Author = playlistAuthor is not null
                        ? new Artist { Id = ParsingHelpers.StableId("playlist-author", playlistAuthor), Name = playlistAuthor }
                        : null,
                    ThumbnailUrl = thumbnail,
                });

            default:
                return null;
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
            case "MUSIC_PAGE_TYPE_ALBUM":
                return BrowseIdKind.Album;
            case "MUSIC_PAGE_TYPE_PLAYLIST":
                return BrowseIdKind.Playlist;
            case "MUSIC_PAGE_TYPE_ARTIST":
            case "MUSIC_PAGE_TYPE_USER_CHANNEL":
            case "MUSIC_PAGE_TYPE_LIBRARY_ARTIST":
                return BrowseIdKind.Artist;
            case "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE":
                return BrowseIdKind.Podcast;
        }

        return BrowseIdClassifier.Classify(browseId);
    }

    // MARK: - Local extraction helpers

    private static string? ExtractCarouselTitle(JsonNode carousel)
    {
        var header = Prop(carousel, "header");
        if (header is null)
        {
            return null;
        }

        return ParsingHelpers.ExtractText(Prop(header, "musicCarouselShelfBasicHeaderRenderer"));
    }

    private static string? ExtractPageType(JsonNode? browseEndpoint) =>
        Str(
            Prop(
                Prop(browseEndpoint, "browseEndpointContextSupportedConfigs"),
                "browseEndpointContextMusicConfig"),
            "pageType");

    private static MusicVideoType? ExtractMusicVideoType(JsonNode? navigationEndpoint)
    {
        var config = Prop(
            Prop(Prop(navigationEndpoint, "watchEndpoint"), "watchEndpointMusicSupportedConfigs"),
            "watchEndpointMusicConfig");
        var raw = Str(config, "musicVideoType");
        return raw switch
        {
            "MUSIC_VIDEO_TYPE_OMV" => MusicVideoType.Omv,
            "MUSIC_VIDEO_TYPE_ATV" => MusicVideoType.Atv,
            "MUSIC_VIDEO_TYPE_UGC" => MusicVideoType.Ugc,
            "MUSIC_VIDEO_TYPE_PODCAST_EPISODE" => MusicVideoType.PodcastEpisode,
            null => null,
            _ => MusicVideoType.Unknown,
        };
    }

    private static string? ExtractResponsiveVideoId(JsonNode data)
    {
        // playlistItemData.videoId is the canonical source on responsive rows.
        var direct = Str(Prop(data, "playlistItemData"), "videoId");
        if (direct is not null)
        {
            return direct;
        }

        // Fallback: the play button's watch endpoint, found regardless of overlay nesting.
        var watch = ResponseTreeSearch.FindFirst(data, "watchEndpoint");
        return Str(watch, "videoId");
    }

    private static string? ExtractTitleFromFlexColumns(JsonNode data)
    {
        var flexColumns = Arr(Prop(data, "flexColumns"));
        if (flexColumns is null || flexColumns.Count == 0)
        {
            return null;
        }

        var renderer = Prop(flexColumns[0], "musicResponsiveListItemFlexColumnRenderer");
        return ParsingHelpers.ExtractText(renderer, "text");
    }

    private static TimeSpan? ExtractDurationFromFlexColumns(JsonNode data)
    {
        // Duration commonly lives in a fixed column; scan both fixed and flex columns for the
        // first run whose text parses as a colon-separated duration.
        foreach (var columnsKey in new[] { "fixedColumns", "flexColumns" })
        {
            var columns = Arr(Prop(data, columnsKey));
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

    private static string? ExtractContinuationToken(JsonNode? node)
    {
        // Legacy shape: continuations[].nextContinuationData.continuation.
        var continuations = Arr(Prop(node, "continuations"));
        if (continuations is { Count: > 0 })
        {
            var token = Str(Prop(continuations[0], "nextContinuationData"), "continuation");
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }
        }

        // Newer shape: continuationItemRenderer.continuationEndpoint.continuationCommand.token.
        var command = ResponseTreeSearch.FindFirst(node, "continuationCommand");
        var newToken = Str(command, "token");
        return string.IsNullOrEmpty(newToken) ? null : newToken;
    }

    private static bool IsChartSection(string title)
    {
        var lowered = title.ToLowerInvariant();
        return lowered.Contains("chart", StringComparison.Ordinal)
            || lowered.Contains("trending", StringComparison.Ordinal)
            || lowered.Contains("top songs", StringComparison.Ordinal)
            || lowered.Contains("top albums", StringComparison.Ordinal)
            || lowered.Contains("top artists", StringComparison.Ordinal)
            || lowered.Contains("top videos", StringComparison.Ordinal);
    }

    // MARK: - JsonNode accessors

    private static JsonNode? Prop(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

    private static JsonArray? Arr(JsonNode? node) => node as JsonArray;

    private static string? Str(JsonNode? node, string key) =>
        Prop(node, key) is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
