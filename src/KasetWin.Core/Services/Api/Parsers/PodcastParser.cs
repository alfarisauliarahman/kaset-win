using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for the YouTube Music Podcasts discovery surface
/// (<c>FEmusic_podcasts</c>, advanced phase Req 27). Ported from the macOS <c>PodcastParser</c>; it
/// reuses the section-based shape of <see cref="HomeResponseParser"/> but produces podcast-specific
/// items (shows + episodes with playback progress).
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c> and deterministic — no I/O, clocks, or randomness — so identity is
/// stable across refreshes (Req 16.1). Traversal is resilient to container reshuffles via
/// <see cref="ResponseTreeSearch"/> (tabs / <c>singleColumnBrowseResultsRenderer</c> wrappers).
/// </para>
/// <para>
/// Region availability (Req 27.1/27.2) is decided by the client from the HTTP status (404 →
/// unavailable), not by this parser. A structurally valid response that yields no sections returns
/// an empty list rather than throwing, so an empty-but-available region is represented faithfully.
/// </para>
/// </remarks>
public static class PodcastParser
{
    /// <summary>Browse-id prefix identifying a podcast show.</summary>
    private const string ShowPrefix = "MPSPP";

    /// <summary>Percentage at/above which an episode is considered fully played.</summary>
    private const int PlayedThresholdPercent = 95;

    /// <summary>
    /// Parses a raw JSON string into the podcast discovery sections. Invalid JSON is reported as
    /// <see cref="KasetErrorKind.ParseError"/>.
    /// </summary>
    public static IReadOnlyList<PodcastSection> ParseDiscovery(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Podcasts response is not valid JSON.", ex);
        }

        return ParseDiscovery(root);
    }

    /// <summary>
    /// Parses the podcasts discovery response tree into a list of <see cref="PodcastSection"/>.
    /// Returns an empty list when the response carries no recognisable section structure (a valid,
    /// possibly empty, region) rather than throwing.
    /// </summary>
    /// <param name="root">The parsed InnerTube response tree.</param>
    public static IReadOnlyList<PodcastSection> ParseDiscovery(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            return Array.Empty<PodcastSection>();
        }

        // Resilient to the container renderers (tabs, singleColumnBrowseResultsRenderer, …) being
        // reshuffled around the section list.
        var sectionList = ResponseTreeSearch.FindFirst(root, "sectionListRenderer");
        var sectionContents = Arr(Prop(sectionList, "contents"));
        if (sectionContents is null)
        {
            return Array.Empty<PodcastSection>();
        }

        var sections = new List<PodcastSection>(sectionContents.Count);
        foreach (var sectionData in sectionContents)
        {
            var section = ParseSection(sectionData);
            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    /// <summary>
    /// Parses a continuation response (progressive podcast loading) into the additional sections.
    /// </summary>
    public static IReadOnlyList<PodcastSection> ParseContinuation(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            return Array.Empty<PodcastSection>();
        }

        var sections = new List<PodcastSection>();

        var sectionListContinuation = ResponseTreeSearch.FindFirst(root, "sectionListContinuation");
        if (Arr(Prop(sectionListContinuation, "contents")) is { } contents)
        {
            foreach (var sectionData in contents)
            {
                var section = ParseSection(sectionData);
                if (section is not null)
                {
                    sections.Add(section);
                }
            }
        }

        var carouselContinuation = ResponseTreeSearch.FindFirst(root, "musicCarouselShelfContinuation");
        if (carouselContinuation is not null)
        {
            var section = ParseShelf(carouselContinuation, "More", "contents");
            if (section is not null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    /// <summary>
    /// Parses an episode-list continuation page (podcast show / podcast playlist) into its episode
    /// items plus the next continuation token. Understands both the 2025
    /// <c>appendContinuationItemsAction.continuationItems</c> envelope and the legacy
    /// <c>musicPlaylistShelfContinuation</c> / <c>musicShelfContinuation</c> shapes.
    /// </summary>
    public static (IReadOnlyList<PodcastEpisode> Episodes, string? NextToken) ParseEpisodesContinuation(JsonNode? root)
    {
        JsonArray? rows = null;
        JsonNode? legacyShelf = null;

        if (ResponseTreeSearch.FindFirst(root, "appendContinuationItemsAction") is { } append)
        {
            rows = Arr(Prop(append, "continuationItems"));
        }

        if (rows is null)
        {
            legacyShelf = ResponseTreeSearch.FindFirst(root, "musicPlaylistShelfContinuation")
                ?? ResponseTreeSearch.FindFirst(root, "musicShelfContinuation");
            rows = Arr(Prop(legacyShelf, "contents"));
        }

        if (rows is null)
        {
            // 2025: continuation requests are often answered with a FULL page shape (root
            // "contents" + sectionListRenderer) instead of a continuation envelope — parse it
            // like an initial page and pull the next token the same way.
            var fullPage = new List<PodcastEpisode>();
            foreach (var section in ParseDiscovery(root))
            {
                foreach (var item in section.Items)
                {
                    if (item is PodcastSectionItem.EpisodeItem pageEp)
                    {
                        fullPage.Add(pageEp.Episode);
                    }
                }
            }

            return (fullPage, fullPage.Count > 0 ? ExtractEpisodesContinuationToken(root) : null);
        }

        var episodes = new List<PodcastEpisode>();
        string? nextToken = null;
        foreach (var row in rows)
        {
            if (ParseItem(row) is PodcastSectionItem.EpisodeItem ep)
            {
                episodes.Add(ep.Episode);
            }
            else if (Prop(row, "continuationItemRenderer") is { } contRow)
            {
                nextToken = Str(ResponseTreeSearch.FindFirst(contRow, "continuationCommand"), "token");
            }
        }

        // Legacy shelves carry the next token in shelf.continuations[0].nextContinuationData.
        if (nextToken is null && Arr(Prop(legacyShelf, "continuations")) is { Count: > 0 } continuations)
        {
            nextToken = Str(Prop(continuations[0], "nextContinuationData"), "continuation");
        }

        return (episodes, nextToken);
    }

    /// <summary>
    /// Extracts the first episode-list continuation token from an initial show/playlist page
    /// (a trailing <c>continuationItemRenderer</c> or legacy shelf <c>continuations</c>).
    /// </summary>
    public static string? ExtractEpisodesContinuationToken(JsonNode? root)
    {
        if (ResponseTreeSearch.FindFirst(root, "continuationItemRenderer") is { } contRow
            && Str(ResponseTreeSearch.FindFirst(contRow, "continuationCommand"), "token") is { } token)
        {
            return token;
        }

        var shelf = ResponseTreeSearch.FindFirst(root, "musicPlaylistShelfRenderer")
            ?? ResponseTreeSearch.FindFirst(root, "musicShelfRenderer");
        if (Arr(Prop(shelf, "continuations")) is { Count: > 0 } continuations)
        {
            return Str(Prop(continuations[0], "nextContinuationData"), "continuation");
        }

        return null;
    }

    // MARK: - Section parsing

    private static PodcastSection? ParseSection(JsonNode? data)
    {
        if (Prop(data, "musicCarouselShelfRenderer") is { } carousel)
        {
            return ParseShelf(carousel, ExtractCarouselTitle(carousel) ?? "Podcasts", "contents");
        }

        if (Prop(data, "musicShelfRenderer") is { } shelf)
        {
            return ParseShelf(shelf, ParsingHelpers.ExtractText(shelf) ?? "Podcasts", "contents");
        }

        // A podcast playlist page (VLPL...) lays its episodes out in a musicPlaylistShelfRenderer,
        // with the same multi-row episode items the discovery shelves use.
        if (Prop(data, "musicPlaylistShelfRenderer") is { } playlistShelf)
        {
            return ParseShelf(playlistShelf, "Episodes", "contents");
        }

        if (Prop(data, "gridRenderer") is { } grid)
        {
            var title = ParsingHelpers.ExtractText(Prop(grid, "header") is { } h
                ? Prop(h, "gridHeaderRenderer")
                : null) ?? "Podcasts";
            return ParseShelf(grid, title, "items");
        }

        // itemSectionRenderer is a transparent wrapper around another renderer.
        if (Prop(data, "itemSectionRenderer") is { } itemSection
            && Arr(Prop(itemSection, "contents")) is { } itemContents)
        {
            foreach (var itemContent in itemContents)
            {
                var section = ParseSection(itemContent);
                if (section is not null)
                {
                    return section;
                }
            }
        }

        return null;
    }

    private static PodcastSection? ParseShelf(JsonNode shelf, string title, string itemsKey)
    {
        var contents = Arr(Prop(shelf, itemsKey));
        if (contents is null)
        {
            return null;
        }

        var items = new List<PodcastSectionItem>(contents.Count);
        foreach (var itemData in contents)
        {
            var item = ParseItem(itemData);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        var stableId = ParsingHelpers.StableId(title, items[0].Id);
        return new PodcastSection { Id = stableId, Title = title, Items = items };
    }

    // MARK: - Item parsing

    private static PodcastSectionItem? ParseItem(JsonNode? data)
    {
        if (Prop(data, "musicTwoRowItemRenderer") is { } twoRow)
        {
            return ParseTwoRowItem(twoRow);
        }

        if (Prop(data, "musicMultiRowListItemRenderer") is { } multiRow)
        {
            return ParseMultiRowItem(multiRow);
        }

        if (Prop(data, "musicResponsiveListItemRenderer") is { } responsive)
        {
            return ParseResponsiveItem(responsive);
        }

        return null;
    }

    /// <summary>Two-row card: a podcast show (MPSPP browseId) or an episode (watchEndpoint).</summary>
    private static PodcastSectionItem? ParseTwoRowItem(JsonNode data)
    {
        var title = ParsingHelpers.ExtractText(data);
        if (title is null)
        {
            return null;
        }

        var thumbnail = ParsingHelpers.BestThumbnailUrl(data);
        var navigationEndpoint = Prop(data, "navigationEndpoint");

        var browseId = Str(Prop(navigationEndpoint, "browseEndpoint"), "browseId");
        if (browseId is not null && browseId.StartsWith(ShowPrefix, StringComparison.Ordinal))
        {
            return new PodcastSectionItem.ShowItem(new PodcastShow
            {
                Id = browseId,
                Title = title,
                Author = ParsingHelpers.JoinRunTexts(data),
                ThumbnailUrl = thumbnail,
            });
        }

        var videoId = Str(Prop(navigationEndpoint, "watchEndpoint"), "videoId");
        if (videoId is not null)
        {
            return new PodcastSectionItem.EpisodeItem(new PodcastEpisode(videoId, title, null, 0, false)
            {
                ThumbnailUrl = thumbnail,
                ShowTitle = ParsingHelpers.JoinRunTexts(data),
            });
        }

        return null;
    }

    /// <summary>Multi-row list item: a podcast episode carrying playback progress + played state.</summary>
    private static PodcastSectionItem? ParseMultiRowItem(JsonNode data)
    {
        var videoId = Str(Prop(data, "onTap") is { } onTap ? Prop(onTap, "watchEndpoint") : null, "videoId")
            ?? Str(ResponseTreeSearch.FindFirst(data, "watchEndpoint"), "videoId");
        if (videoId is null)
        {
            return null;
        }

        var title = ParsingHelpers.ExtractText(data) ?? "Episode";
        var thumbnail = ParsingHelpers.BestThumbnailUrl(data);
        var showTitle = ParsingHelpers.ExtractText(data, "subtitle");
        var showBrowseId = ExtractShowBrowseIdFromSubtitle(data);

        // Progress + duration live under playbackProgress.musicPlaybackProgressRenderer (NOT directly
        // on the row): playbackProgressPercentage (0-100) and durationText (total episode length).
        var progressRenderer = Prop(Prop(data, "playbackProgress"), "musicPlaybackProgressRenderer");
        double progress = 0;
        var played = false;
        var percent = Int(progressRenderer, "playbackProgressPercentage");
        if (percent is { } pct)
        {
            progress = Math.Clamp(pct / 100.0, 0.0, 1.0);
            played = pct >= PlayedThresholdPercent;
        }

        // A "Played" pill overrides the percentage.
        var playedText = ParsingHelpers.ExtractText(data, "playedText");
        if (playedText is not null && playedText.Equals("Played", StringComparison.OrdinalIgnoreCase))
        {
            played = true;
            progress = 1.0;
        }

        // durationText.runs come as ["​ • ", "50:46"] — join all runs (not just the first) and
        // strip the leading bullet/space, then parse. Falls back to a root-level durationText.
        TimeSpan? duration = null;
        string? cleanedDurationText = null;
        var durationText = ParsingHelpers.JoinRunTexts(progressRenderer, "durationText")
            ?? ParsingHelpers.JoinRunTexts(data, "durationText");
        if (!string.IsNullOrWhiteSpace(durationText))
        {
            cleanedDurationText = durationText.Trim().TrimStart('•', ' ', '·', '•').Trim();
            duration = ParseEpisodeDuration(cleanedDurationText);
        }

        return new PodcastSectionItem.EpisodeItem(new PodcastEpisode(videoId, title, duration, progress, played)
        {
            DurationText = cleanedDurationText,
            ThumbnailUrl = thumbnail,
            ShowTitle = showTitle,
            ShowBrowseId = showBrowseId,
            Description = CollapseWhitespace(ParsingHelpers.ExtractText(data, "description")),
            PublishedText = ParsingHelpers.ExtractText(data, "subtitle"),
            PlayedFeedback = ExtractPlayedFeedback(data),
        });
    }

    /// <summary>
    /// Extracts the "Mark as played"/"Mark as unplayed" feedback tokens from the row's menu. The
    /// item is a <c>toggleMenuServiceItemRenderer</c> whose default icon is <c>CHECK</c> (language
    /// independent); its default endpoint marks played, the toggled endpoint marks unplayed.
    /// </summary>
    private static FeedbackTokens? ExtractPlayedFeedback(JsonNode data)
    {
        foreach (var toggle in ResponseTreeSearch.FindAll(data, "toggleMenuServiceItemRenderer"))
        {
            if (!string.Equals(Str(Prop(toggle, "defaultIcon"), "iconType"), "CHECK", StringComparison.Ordinal))
            {
                continue;
            }

            var markPlayed = Str(
                ResponseTreeSearch.FindFirst(Prop(toggle, "defaultServiceEndpoint"), "feedbackEndpoint"), "feedbackToken");
            var markUnplayed = Str(
                ResponseTreeSearch.FindFirst(Prop(toggle, "toggledServiceEndpoint"), "feedbackEndpoint"), "feedbackToken");
            if (markPlayed is not null || markUnplayed is not null)
            {
                return new FeedbackTokens(markPlayed, markUnplayed);
            }
        }

        return null;
    }

    // Flattens newlines/runs of whitespace into single spaces so a multi-paragraph episode
    // description fills the 2-line clamp instead of breaking after the first short line.
    private static string? CollapseWhitespace(string? s) =>
        s is null ? null : System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>Responsive list item: fallback for episodes (videoId) or shows (MPSPP browseId).</summary>
    private static PodcastSectionItem? ParseResponsiveItem(JsonNode data)
    {
        var videoId = Str(Prop(data, "playlistItemData"), "videoId")
            ?? Str(ResponseTreeSearch.FindFirst(data, "watchEndpoint"), "videoId");
        var title = ExtractTitleFromFlexColumns(data) ?? "Episode";
        var thumbnail = ParsingHelpers.BestThumbnailUrl(data);

        if (videoId is not null)
        {
            return new PodcastSectionItem.EpisodeItem(new PodcastEpisode(videoId, title, null, 0, false)
            {
                ThumbnailUrl = thumbnail,
            });
        }

        var browseId = ParsingHelpers.ExtractBrowseId(data);
        if (browseId is not null && browseId.StartsWith(ShowPrefix, StringComparison.Ordinal))
        {
            return new PodcastSectionItem.ShowItem(new PodcastShow
            {
                Id = browseId,
                Title = title,
                ThumbnailUrl = thumbnail,
            });
        }

        return null;
    }

    // MARK: - Local extraction helpers

    private static string? ExtractCarouselTitle(JsonNode carousel)
    {
        var header = Prop(carousel, "header");
        return header is null
            ? null
            : ParsingHelpers.ExtractText(Prop(header, "musicCarouselShelfBasicHeaderRenderer"));
    }

    private static string? ExtractShowBrowseIdFromSubtitle(JsonNode data)
    {
        var runs = Arr(Prop(Prop(data, "subtitle"), "runs"));
        if (runs is null)
        {
            return null;
        }

        foreach (var run in runs)
        {
            var browseId = Str(Prop(Prop(run, "navigationEndpoint"), "browseEndpoint"), "browseId");
            if (browseId is not null && browseId.StartsWith(ShowPrefix, StringComparison.Ordinal))
            {
                return browseId;
            }
        }

        return null;
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

    /// <summary>
    /// Parses an episode duration string, accepting both the colon form (<c>mm:ss</c> /
    /// <c>h:mm:ss</c>) and the natural-language minutes form (<c>"36 min"</c>).
    /// </summary>
    private static TimeSpan? ParseEpisodeDuration(string text)
    {
        var colon = ParsingHelpers.ParseDuration(text);
        if (colon is not null)
        {
            return colon;
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith(" min", StringComparison.OrdinalIgnoreCase))
        {
            var numberPart = trimmed[..^4].Trim();
            if (int.TryParse(numberPart, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                return TimeSpan.FromMinutes(minutes);
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

    private static int? Int(JsonNode? node, string key)
    {
        if (Prop(node, key) is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<long>(out var l))
            {
                return (int)l;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (int)d;
            }
        }

        return null;
    }
}
