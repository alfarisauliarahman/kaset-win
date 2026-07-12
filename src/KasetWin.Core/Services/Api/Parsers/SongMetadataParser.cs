using System.Globalization;
using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for the YouTube Music <c>next</c> (and <c>player</c>) response
/// that backs "now playing" metadata: the base <see cref="Song"/> (videoId, title, artists,
/// album, duration, thumbnail) plus the playback-specific extras carried in a
/// <see cref="SongMetadata"/> — the <see cref="MusicVideoType"/>, live state, library
/// feedback tokens, the Lyrics tab browseId, and the radio continuation token (Req 9.1).
/// Mirrors the macOS <c>SongMetadataParser</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>next</c> shape exposes the song through a
/// <c>playlistPanelVideoRenderer</c> (optionally wrapped by
/// <c>playlistPanelVideoWrapperRenderer.primaryRenderer</c>); the <c>player</c> shape exposes it
/// through <c>videoDetails</c>. Both are handled, and the panel renderer is preferred when both
/// are present. Container reshuffling is tolerated by locating renderers recursively via
/// <see cref="ResponseTreeSearch"/>.
/// </para>
/// <para>
/// Every method is <c>static</c>, side-effect free and deterministic (no clocks, randomness,
/// culture-sensitive parsing, or mutation of the input tree), so the parser satisfies the
/// idempotency / stable-identity guarantee (Property 23) and <see cref="ParseMusicVideoType"/>
/// is a total, pure mapping (Property 37). This type lives in <c>KasetWin.Core</c> and has no
/// WinUI/WinRT dependency.
/// </para>
/// </remarks>
public static class SongMetadataParser
{
    private const string MusicVideoTypeOmv = "MUSIC_VIDEO_TYPE_OMV";
    private const string MusicVideoTypeAtv = "MUSIC_VIDEO_TYPE_ATV";
    private const string MusicVideoTypeUgc = "MUSIC_VIDEO_TYPE_UGC";
    private const string MusicVideoTypePodcastEpisode = "MUSIC_VIDEO_TYPE_PODCAST_EPISODE";

    /// <summary>Library-lyrics browse-id prefix surfaced by the Lyrics tab.</summary>
    private const string LyricsBrowseIdPrefix = "MPLYt";

    // Library add/remove icon types used by the toggle/service menu items.
    private static readonly string[] LibraryAddIcons = { "LIBRARY_ADD", "BOOKMARK_BORDER" };
    private static readonly string[] LibraryRemoveIcons = { "LIBRARY_REMOVE", "BOOKMARK" };

    private static readonly string[] ArtistSeparators = { " • ", " & ", ", ", " · ", "•", "&", "," };

    // MARK: - Public API

    /// <summary>
    /// Parses a <c>next</c> (or <c>player</c>) response into a <see cref="SongMetadata"/>:
    /// the base <see cref="Song"/> plus video type, live state, feedback tokens, lyrics
    /// browseId, and radio continuation token.
    /// </summary>
    /// <param name="root">The decoded <c>next</c>/<c>player</c> response tree.</param>
    /// <param name="videoId">The requested videoId; becomes the song identity (Req 16.1).</param>
    /// <returns>The parsed metadata. Optional fields are <see langword="null"/> when absent.</returns>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or carries neither a
    /// <c>playlistPanelVideoRenderer</c> nor a <c>videoDetails</c> object to build a song from
    /// (corrupted input, Req 20.3).
    /// </exception>
    public static SongMetadata Parse(JsonNode? root, string videoId)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Song metadata response is not a JSON object.");
        }

        var panelRenderer = ExtractPanelVideoRenderer(obj);
        var videoDetails = Prop(obj, "videoDetails") as JsonObject;

        if (panelRenderer is null && videoDetails is null)
        {
            throw new KasetError(
                KasetErrorKind.ParseError,
                $"Song metadata response for '{videoId}' has neither a playlistPanelVideoRenderer nor videoDetails.");
        }

        var menu = panelRenderer is not null ? ParseMenuData(panelRenderer) : MenuParseResult.Empty;
        var videoType = ResolveMusicVideoType(panelRenderer, videoDetails);

        var song = panelRenderer is not null
            ? BuildSongFromPanel(panelRenderer, videoId, videoType, menu)
            : BuildSongFromVideoDetails(videoDetails!, videoId, videoType, menu);

        return new SongMetadata
        {
            Song = song,
            VideoType = videoType,
            IsLive = DetectIsLive(obj, panelRenderer, videoDetails),
            FeedbackTokens = menu.FeedbackTokens,
            LyricsBrowseId = ExtractLyricsBrowseId(obj),
            RadioContinuationToken = ExtractRadioContinuationToken(obj),
        };
    }

    /// <summary>
    /// Maps a renderer/details node to its <see cref="MusicVideoType"/>. Reads the
    /// <c>next</c> location
    /// (<c>navigationEndpoint.watchEndpoint.watchEndpointMusicSupportedConfigs.watchEndpointMusicConfig.musicVideoType</c>)
    /// and the <c>player</c> location (a direct <c>musicVideoType</c> on the node, e.g.
    /// <c>videoDetails</c>). Recognized values map to <see cref="MusicVideoType.Omv"/> /
    /// <see cref="MusicVideoType.Atv"/> / <see cref="MusicVideoType.Ugc"/> /
    /// <see cref="MusicVideoType.PodcastEpisode"/>; anything else (including a missing value)
    /// maps to <see cref="MusicVideoType.Unknown"/> (Property 37).
    /// </summary>
    /// <param name="node">The renderer or <c>videoDetails</c> node. <c>null</c> yields
    /// <see cref="MusicVideoType.Unknown"/>.</param>
    public static MusicVideoType ParseMusicVideoType(JsonNode? node) =>
        MapMusicVideoType(ExtractMusicVideoTypeString(node));

    // MARK: - Music video type

    /// <summary>Maps a raw <c>musicVideoType</c> string to the enum; unknown/null → Unknown.</summary>
    private static MusicVideoType MapMusicVideoType(string? raw) => raw switch
    {
        MusicVideoTypeOmv => MusicVideoType.Omv,
        MusicVideoTypeAtv => MusicVideoType.Atv,
        MusicVideoTypeUgc => MusicVideoType.Ugc,
        MusicVideoTypePodcastEpisode => MusicVideoType.PodcastEpisode,
        _ => MusicVideoType.Unknown,
    };

    /// <summary>
    /// Extracts the raw <c>musicVideoType</c> string from a node, checking the <c>next</c>
    /// watch-endpoint path first, then a direct <c>musicVideoType</c> key (player shape).
    /// </summary>
    private static string? ExtractMusicVideoTypeString(JsonNode? node)
    {
        // next: navigationEndpoint.watchEndpoint.watchEndpointMusicSupportedConfigs
        //         .watchEndpointMusicConfig.musicVideoType
        var fromWatch = GetString(
            Prop(
                Prop(Prop(Prop(node, "navigationEndpoint"), "watchEndpoint"), "watchEndpointMusicSupportedConfigs"),
                "watchEndpointMusicConfig"),
            "musicVideoType");

        // player: videoDetails.musicVideoType (or the node itself carrying the key)
        return fromWatch ?? GetString(node, "musicVideoType");
    }

    private static MusicVideoType ResolveMusicVideoType(JsonNode? panelRenderer, JsonObject? videoDetails)
    {
        var fromPanel = ParseMusicVideoType(panelRenderer);
        if (fromPanel != MusicVideoType.Unknown)
        {
            return fromPanel;
        }

        return ParseMusicVideoType(videoDetails);
    }

    // MARK: - Panel renderer (next)

    /// <summary>
    /// Locates the <c>playlistPanelVideoRenderer</c> in a <c>next</c> response, handling both the
    /// direct shape and the <c>playlistPanelVideoWrapperRenderer.primaryRenderer</c> wrapper.
    /// Returns <c>null</c> when the response is not a <c>next</c> queue shape.
    /// </summary>
    private static JsonObject? ExtractPanelVideoRenderer(JsonObject root)
    {
        // Direct: first playlistPanelVideoRenderer anywhere in the tree (queue ordering preserved
        // by depth-first document-order search).
        if (ResponseTreeSearch.FindFirst(root, "playlistPanelVideoRenderer") is JsonObject direct)
        {
            return direct;
        }

        // Wrapped: playlistPanelVideoWrapperRenderer.primaryRenderer.playlistPanelVideoRenderer
        var wrapper = ResponseTreeSearch.FindFirst(root, "playlistPanelVideoWrapperRenderer");
        if (Prop(Prop(wrapper, "primaryRenderer"), "playlistPanelVideoRenderer") is JsonObject wrapped)
        {
            return wrapped;
        }

        return null;
    }

    private static Song BuildSongFromPanel(JsonObject renderer, string videoId, MusicVideoType videoType, MenuParseResult menu)
    {
        return new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = ParsingHelpers.ExtractText(renderer, "title") ?? "Unknown",
            Artists = ParseArtistsFromByline(renderer),
            // The panel's byline/menu carries the album browse target (MPREb…); surfacing it lets
            // "open the song's album" affordances work from watch-next metadata.
            Album = AlbumFromRenderer(renderer),
            Duration = ParsingHelpers.ParseDuration(ParsingHelpers.ExtractText(renderer, "lengthText")),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(renderer),
            VideoType = videoType,
            HasVideo = videoType.HasVideoContent(),
            LikeStatus = menu.LikeStatus,
            IsInLibrary = menu.IsInLibrary,
            FeedbackTokens = menu.FeedbackTokens,
            IsExplicit = ParsingHelpers.ExtractIsExplicit(renderer),
        };
    }

    /// <summary>The song's album (first <c>MPREb…</c> browse target under the renderer), if any.</summary>
    private static Album? AlbumFromRenderer(JsonObject renderer)
    {
        foreach (var endpoint in ResponseTreeSearch.FindAll(renderer, "browseEndpoint"))
        {
            var browseId = GetString(endpoint, "browseId");
            if (browseId is not null && browseId.StartsWith("MPREb", StringComparison.Ordinal))
            {
                return new Album { Id = browseId, Title = string.Empty };
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts artists from a panel renderer's <c>longBylineText.runs</c>. The byline is
    /// bullet-segmented (<c>Artist(s) • Album • Year</c>), so only the FIRST segment is artists —
    /// keeping every non-separator run leaked the album and year into the artist list. Runs whose
    /// browse id is an album (<c>MPRE…</c>) are skipped regardless of segment. Linked runs keep
    /// their <c>browseEndpoint.browseId</c>; plain-text runs get a deterministic
    /// <see cref="ParsingHelpers.StableId"/> so the artist line is never blank.
    /// </summary>
    private static IReadOnlyList<Artist> ParseArtistsFromByline(JsonNode? renderer)
    {
        var runs = AsArray(Prop(Prop(renderer, "longBylineText"), "runs"));
        if (runs is null)
        {
            return Array.Empty<Artist>();
        }

        var artists = new List<Artist>();
        foreach (var run in runs)
        {
            var text = GetString(run, "text");
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (text.Contains('•', StringComparison.Ordinal))
            {
                // First bullet ends the artist segment; the rest is album/year/views.
                break;
            }

            if (IsArtistSeparator(text))
            {
                continue;
            }

            var browseId = ParsingHelpers.ExtractBrowseId(run);
            if (browseId is not null && browseId.StartsWith("MPRE", StringComparison.Ordinal))
            {
                continue;
            }

            artists.Add(browseId is not null
                ? new Artist { Id = browseId, Name = text }
                : new Artist { Id = ParsingHelpers.StableId("artist", text), Name = text });
        }

        return artists;
    }

    // MARK: - Video details (player)

    private static Song BuildSongFromVideoDetails(JsonObject videoDetails, string videoId, MusicVideoType videoType, MenuParseResult menu)
    {
        return new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = GetString(videoDetails, "title") ?? "Unknown",
            Artists = ParseArtistFromVideoDetails(videoDetails),
            Album = null,
            Duration = ParseLengthSeconds(GetString(videoDetails, "lengthSeconds")),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(videoDetails),
            VideoType = videoType,
            HasVideo = videoType.HasVideoContent(),
            LikeStatus = menu.LikeStatus,
            IsInLibrary = menu.IsInLibrary,
            FeedbackTokens = menu.FeedbackTokens,
            IsExplicit = ParsingHelpers.ExtractIsExplicit(videoDetails),
        };
    }

    private static Artist[] ParseArtistFromVideoDetails(JsonObject videoDetails)
    {
        var author = GetString(videoDetails, "author")?.Trim();
        if (string.IsNullOrEmpty(author))
        {
            return Array.Empty<Artist>();
        }

        var channelId = GetString(videoDetails, "channelId");
        var id = ParsingHelpers.IsNavigableArtistId(channelId)
            ? channelId!
            : ParsingHelpers.StableId("artist", author);

        return new[] { new Artist { Id = id, Name = author } };
    }

    private static TimeSpan? ParseLengthSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    // MARK: - Live detection

    /// <summary>
    /// Whether the track is a live stream (disables seeking, Req 9). Detected via
    /// <c>videoDetails.isLiveContent</c>, a <c>playabilityStatus.liveStreamability</c> block,
    /// or a live badge/overlay icon on the panel renderer.
    /// </summary>
    private static bool DetectIsLive(JsonObject root, JsonNode? panelRenderer, JsonObject? videoDetails)
    {
        if (GetBool(videoDetails, "isLiveContent") == true)
        {
            return true;
        }

        if (Prop(Prop(root, "playabilityStatus"), "liveStreamability") is not null)
        {
            return true;
        }

        return panelRenderer is not null && HasLiveBadge(panelRenderer);
    }

    private static bool HasLiveBadge(JsonNode renderer)
    {
        foreach (var badge in ResponseTreeSearch.FindAll(renderer, "liveBadgeRenderer"))
        {
            if (badge is not null)
            {
                return true;
            }
        }

        // Some responses mark live via a thumbnail overlay icon type.
        foreach (var icon in ResponseTreeSearch.FindAll(renderer, "icon"))
        {
            if (GetString(icon, "iconType") == "LIVE")
            {
                return true;
            }
        }

        return false;
    }

    // MARK: - Lyrics tab

    /// <summary>
    /// Extracts the Lyrics tab browseId (<c>MPLYt…</c>) from the watch-next tabs. Scans every
    /// <c>tabRenderer</c> for a browseId carried under <c>endpoint</c>/<c>navigationEndpoint</c>
    /// (<c>browseEndpoint.browseId</c>), returning the first one with the <c>MPLYt</c> prefix.
    /// </summary>
    private static string? ExtractLyricsBrowseId(JsonObject root)
    {
        foreach (var tabRenderer in ResponseTreeSearch.FindAll(root, "tabRenderer"))
        {
            var browseId = BrowseIdFromTab(tabRenderer);
            if (browseId is not null && browseId.StartsWith(LyricsBrowseIdPrefix, StringComparison.Ordinal))
            {
                return browseId;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the "Related" tab browseId from the watch-next tabs: the first tab that carries a
    /// browseId other than the Lyrics tab (<c>MPLYt…</c>). The "Up next" tab carries no browseId, so
    /// this reliably lands on the Related tab. Returns <c>null</c> when no related tab is present.
    /// </summary>
    public static string? ExtractRelatedBrowseId(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            return null;
        }

        // The watch-next tabs are [Up next, Lyrics (MPLYt…), Related]. The Related tab is last, so
        // take the LAST tab that carries a non-lyrics browseId (robust even if Up next also has one).
        string? related = null;
        foreach (var tabRenderer in ResponseTreeSearch.FindAll(obj, "tabRenderer"))
        {
            var browseId = BrowseIdFromTab(tabRenderer);
            if (browseId is not null && !browseId.StartsWith(LyricsBrowseIdPrefix, StringComparison.Ordinal))
            {
                related = browseId;
            }
        }

        return related;
    }

    private static string? BrowseIdFromTab(JsonNode? tabRenderer)
    {
        foreach (var endpointKey in new[] { "endpoint", "navigationEndpoint" })
        {
            var browseId = GetString(Prop(Prop(tabRenderer, endpointKey), "browseEndpoint"), "browseId");
            if (browseId is not null)
            {
                return browseId;
            }
        }

        return null;
    }

    // MARK: - Radio continuation

    /// <summary>
    /// Extracts the radio/mix continuation token used to drive the infinite mix (Req 25). Reads
    /// <c>playlistPanelRenderer.continuations[0]</c>, preferring
    /// <c>nextRadioContinuationData.continuation</c> and falling back to
    /// <c>nextContinuationData.continuation</c>. Scoped to the panel renderer when present, else
    /// searched anywhere in the tree.
    /// </summary>
    private static string? ExtractRadioContinuationToken(JsonObject root)
    {
        var panel = ResponseTreeSearch.FindFirst(root, "playlistPanelRenderer") ?? (JsonNode)root;
        var continuations = AsArray(Prop(panel, "continuations"));
        if (continuations is not { Count: > 0 })
        {
            return null;
        }

        var first = continuations[0];
        return GetString(Prop(first, "nextRadioContinuationData"), "continuation")
               ?? GetString(Prop(first, "nextContinuationData"), "continuation");
    }

    // MARK: - Menu (feedback tokens / library / like status)

    private readonly struct MenuParseResult
    {
        public static readonly MenuParseResult Empty = new(null, false, null);

        public MenuParseResult(FeedbackTokens? feedbackTokens, bool isInLibrary, LikeStatus? likeStatus)
        {
            FeedbackTokens = feedbackTokens;
            IsInLibrary = isInLibrary;
            LikeStatus = likeStatus;
        }

        public FeedbackTokens? FeedbackTokens { get; }

        public bool IsInLibrary { get; }

        public LikeStatus? LikeStatus { get; }
    }

    private static MenuParseResult ParseMenuData(JsonNode renderer)
    {
        var menuRenderer = Prop(Prop(renderer, "menu"), "menuRenderer");
        var items = AsArray(Prop(menuRenderer, "items"));
        if (menuRenderer is null || items is null)
        {
            return MenuParseResult.Empty;
        }

        FeedbackTokens? feedbackTokens = null;
        var isInLibrary = false;

        foreach (var item in items)
        {
            if (ParseToggleMenuItem(item) is { } toggle)
            {
                feedbackTokens = toggle.Tokens;
                isInLibrary = isInLibrary || toggle.IsInLibrary;
                continue;
            }

            if (ParseMenuServiceItem(item) is { } service)
            {
                feedbackTokens = MergeTokens(feedbackTokens, service.Tokens);
                isInLibrary = isInLibrary || service.IsInLibrary;
            }
        }

        return new MenuParseResult(feedbackTokens, isInLibrary, ParseLikeStatus(menuRenderer));
    }

    private static (FeedbackTokens? Tokens, bool IsInLibrary)? ParseToggleMenuItem(JsonNode? item)
    {
        var toggle = Prop(item, "toggleMenuServiceItemRenderer");
        var iconType = GetString(Prop(toggle, "defaultIcon"), "iconType");
        if (toggle is null || iconType is null)
        {
            return null;
        }

        var defaultToken = FeedbackTokenFrom(toggle, "defaultServiceEndpoint");
        var toggledToken = FeedbackTokenFrom(toggle, "toggledServiceEndpoint");

        if (Array.IndexOf(LibraryAddIcons, iconType) >= 0)
        {
            return (new FeedbackTokens(defaultToken, toggledToken), false);
        }

        if (Array.IndexOf(LibraryRemoveIcons, iconType) >= 0)
        {
            return (new FeedbackTokens(toggledToken, defaultToken), true);
        }

        return null;
    }

    private static (FeedbackTokens? Tokens, bool IsInLibrary)? ParseMenuServiceItem(JsonNode? item)
    {
        var service = Prop(item, "menuServiceItemRenderer");
        var iconType = GetString(Prop(service, "icon"), "iconType");
        if (service is null || iconType is null)
        {
            return null;
        }

        var token = FeedbackTokenFrom(service, "serviceEndpoint");

        if (Array.IndexOf(LibraryAddIcons, iconType) >= 0)
        {
            return (new FeedbackTokens(token, null), false);
        }

        if (Array.IndexOf(LibraryRemoveIcons, iconType) >= 0)
        {
            return (new FeedbackTokens(null, token), true);
        }

        return null;
    }

    private static FeedbackTokens? MergeTokens(FeedbackTokens? existing, FeedbackTokens? incoming)
    {
        if (incoming is null)
        {
            return existing;
        }

        if (existing is null)
        {
            return incoming;
        }

        return new FeedbackTokens(existing.Add ?? incoming.Add, existing.Remove ?? incoming.Remove);
    }

    private static string? FeedbackTokenFrom(JsonNode? container, string endpointKey) =>
        GetString(Prop(Prop(container, endpointKey), "feedbackEndpoint"), "feedbackToken");

    private static LikeStatus? ParseLikeStatus(JsonNode? menuRenderer)
    {
        var buttons = AsArray(Prop(menuRenderer, "topLevelButtons"));
        if (buttons is null)
        {
            return null;
        }

        foreach (var button in buttons)
        {
            var status = GetString(Prop(button, "likeButtonRenderer"), "likeStatus");
            if (status is null)
            {
                continue;
            }

            return status switch
            {
                "LIKE" => LikeStatus.Like,
                "DISLIKE" => LikeStatus.Dislike,
                _ => LikeStatus.Indifferent,
            };
        }

        return null;
    }

    // MARK: - Helpers

    private static bool IsArtistSeparator(string text) => Array.IndexOf(ArtistSeparators, text) >= 0;

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

    private static bool? GetBool(JsonNode? node, string key)
    {
        if (Prop(node, key) is JsonValue value && value.TryGetValue<bool>(out var b))
        {
            return b;
        }

        return null;
    }
}
