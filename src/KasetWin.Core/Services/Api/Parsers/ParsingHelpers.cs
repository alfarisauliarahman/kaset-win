using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free extraction helpers shared by every per-surface InnerTube parser
/// (tasks 5.3–5.9). The macOS <c>ParsingHelpers</c> counterpart, ported to operate on
/// <see cref="JsonNode"/> from <see cref="System.Text.Json.Nodes"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c>, side-effect free and deterministic: identical input
/// always yields identical output (no clocks, randomness, culture-sensitive parsing, or
/// mutation of the input tree). This is what lets the parsers satisfy the idempotency /
/// stable-identity guarantee (Property 23). Stable, content-derived ids are produced via
/// <see cref="StableId"/> so refreshes do not churn list identity (Req 16.1).
/// </para>
/// <para>This type lives in <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.</para>
/// </remarks>
public static class ParsingHelpers
{
    /// <summary>YouTube channel id prefix (navigable artist destination).</summary>
    public const string ChannelIdPrefix = "UC";

    /// <summary>Library artist browse-id prefix (navigable artist destination).</summary>
    public const string LibraryArtistBrowseIdPrefix = "MPLAUC";

    private static readonly string[] ArtistSeparators =
    {
        " • ", " & ", ", ", "•", "&", ",",
    };

    private static readonly HashSet<string> ContentTypeKeywords = new(StringComparer.Ordinal)
    {
        "Song", "Video", "Album", "Playlist", "Artist", "Episode", "Podcast",
    };

    // MARK: - Stable identity

    /// <summary>
    /// Generates a stable, deterministic id from content components. Mirrors the macOS
    /// helper (SHA-256 of <c>title|component0|component1…</c>, first 16 bytes as lowercase
    /// hex) so non-navigable items (e.g. plain-text artists) keep a stable identity across
    /// refreshes instead of churning (Req 16.1).
    /// </summary>
    public static string StableId(string title, params string[] components)
    {
        ArgumentNullException.ThrowIfNull(title);

        var combined = new StringBuilder(title);
        foreach (var component in components ?? Array.Empty<string>())
        {
            combined.Append('|').Append(component);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(combined.ToString()));
        return Convert.ToHexString(digest, 0, 16).ToLowerInvariant();
    }

    // MARK: - Navigable id detection

    /// <summary>
    /// Whether <paramref name="id"/> is a navigable artist id: a YouTube channel id
    /// (<c>UC…</c>) or a library artist browse id (<c>MPLAUC…</c>). Generated/content-hash
    /// ids are not navigable.
    /// </summary>
    public static bool IsNavigableArtistId(string? id) =>
        !string.IsNullOrEmpty(id) &&
        (id.StartsWith(ChannelIdPrefix, StringComparison.Ordinal) ||
         id.StartsWith(LibraryArtistBrowseIdPrefix, StringComparison.Ordinal));

    // MARK: - URLs

    /// <summary>
    /// Normalizes a URL string by adding an <c>https:</c> scheme to protocol-relative URLs
    /// (<c>//host/…</c>). Other strings are returned unchanged.
    /// </summary>
    public static string NormalizeUrl(string urlString)
    {
        ArgumentNullException.ThrowIfNull(urlString);
        return urlString.StartsWith("//", StringComparison.Ordinal) ? "https:" + urlString : urlString;
    }

    // MARK: - Text helpers

    /// <summary>
    /// Returns the text of the first run under <c>node[key].runs[0].text</c>, or <c>null</c>.
    /// </summary>
    public static string? ExtractText(JsonNode? node, string key = "title")
    {
        var runs = AsArray(Prop(Prop(node, key), "runs"));
        var first = runs is { Count: > 0 } ? runs[0] : null;
        return GetString(first, "text");
    }

    /// <summary>
    /// Returns each <c>text</c> value from a runs array (under <c>node[key].runs</c>), in order.
    /// </summary>
    public static IReadOnlyList<string> ExtractRunTexts(JsonNode? node, string key = "title")
    {
        var runs = AsArray(Prop(Prop(node, key), "runs"));
        if (runs is null)
        {
            return Array.Empty<string>();
        }

        var texts = new List<string>(runs.Count);
        foreach (var run in runs)
        {
            var text = GetString(run, "text");
            if (text is not null)
            {
                texts.Add(text);
            }
        }

        return texts;
    }

    /// <summary>
    /// Joins all run texts under <c>node[key].runs</c> into a single string, or <c>null</c>
    /// when empty (e.g. <c>"Playlist • YouTube Music • 145 songs"</c>).
    /// </summary>
    public static string? JoinRunTexts(JsonNode? node, string key = "subtitle")
    {
        var texts = ExtractRunTexts(node, key);
        if (texts.Count == 0)
        {
            return null;
        }

        var joined = string.Concat(texts);
        return joined.Length == 0 ? null : joined;
    }

    // MARK: - Thumbnails

    /// <summary>
    /// Returns every thumbnail URL for an item, in source order, resolving the common
    /// renderer nestings (<c>musicThumbnailRenderer</c>, <c>croppedSquareThumbnailRenderer</c>,
    /// <c>thumbnailRenderer</c>, <c>foregroundThumbnail</c>, or a direct <c>thumbnails</c> array).
    /// Protocol-relative URLs are normalized to <c>https:</c>.
    /// </summary>
    public static IReadOnlyList<Uri> ExtractThumbnails(JsonNode? node)
    {
        var thumbnails = FindThumbnailArray(node);
        if (thumbnails is null)
        {
            return Array.Empty<Uri>();
        }

        var result = new List<Uri>(thumbnails.Count);
        foreach (var thumb in thumbnails)
        {
            var url = GetString(thumb, "url");
            if (url is null)
            {
                continue;
            }

            if (Uri.TryCreate(NormalizeUrl(url), UriKind.Absolute, out var uri))
            {
                result.Add(uri);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the best (highest-resolution) thumbnail URL for an item, chosen by
    /// <c>width × height</c>, or <c>null</c> when none is present. Ties keep the earliest
    /// entry, so selection is deterministic.
    /// </summary>
    public static Uri? BestThumbnailUrl(JsonNode? node)
    {
        var thumbnails = FindThumbnailArray(node);
        if (thumbnails is null)
        {
            return null;
        }

        string? bestUrl = null;
        long bestArea = -1;
        foreach (var thumb in thumbnails)
        {
            var url = GetString(thumb, "url");
            if (url is null)
            {
                continue;
            }

            long area = (long)(GetInt(thumb, "width") ?? 0) * (GetInt(thumb, "height") ?? 0);
            if (area > bestArea)
            {
                bestArea = area;
                bestUrl = url;
            }
        }

        if (bestUrl is null)
        {
            return null;
        }

        return Uri.TryCreate(NormalizeUrl(bestUrl), UriKind.Absolute, out var uri) ? uri : null;
    }

    // MARK: - Duration

    /// <summary>
    /// Parses a <c>mm:ss</c> or <c>h:mm:ss</c> duration string into a <see cref="TimeSpan"/>,
    /// or <c>null</c> when the text is not a well-formed colon-separated duration.
    /// </summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(':');
        var values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return parts.Length switch
        {
            2 => TimeSpan.FromSeconds((values[0] * 60) + values[1]),
            3 => TimeSpan.FromSeconds((values[0] * 3600) + (values[1] * 60) + values[2]),
            _ => null,
        };
    }

    // MARK: - Explicit badge

    /// <summary>
    /// Whether the renderer is marked as explicit content. Inspects both the <c>badges</c>
    /// array (used by <c>musicResponsiveListItemRenderer</c>) and <c>subtitleBadges</c> (used
    /// by <c>musicTwoRowItemRenderer</c> / header renderers) for a
    /// <c>MUSIC_EXPLICIT_BADGE</c> inline badge.
    /// </summary>
    public static bool ExtractIsExplicit(JsonNode? node)
    {
        foreach (var key in new[] { "badges", "subtitleBadges" })
        {
            var badges = AsArray(Prop(node, key));
            if (badges is null)
            {
                continue;
            }

            foreach (var badge in badges)
            {
                var icon = Prop(Prop(badge, "musicInlineBadgeRenderer"), "icon");
                if (GetString(icon, "iconType") == "MUSIC_EXPLICIT_BADGE")
                {
                    return true;
                }
            }
        }

        return false;
    }

    // MARK: - Artists

    /// <summary>
    /// Extracts artists from a <c>subtitle.runs</c> array. Linked runs (with a
    /// <c>browseEndpoint.browseId</c>) keep that id; non-linked text runs are preserved with a
    /// deterministic <see cref="StableId"/> so the artist line is never blank. Separator runs
    /// (<c>•</c>, <c>&amp;</c>, <c>,</c>) are skipped.
    /// </summary>
    public static IReadOnlyList<Artist> ExtractArtists(JsonNode? node)
    {
        var runs = AsArray(Prop(Prop(node, "subtitle"), "runs"));
        if (runs is null)
        {
            return Array.Empty<Artist>();
        }

        var artists = new List<Artist>();
        foreach (var run in runs)
        {
            var text = GetString(run, "text");
            if (string.IsNullOrEmpty(text) || IsArtistSeparator(text) || LooksLikeCountText(text))
            {
                continue; // skip separators and view/play-count runs (e.g. "540K views")
            }

            var browseId = ExtractBrowseId(run);
            artists.Add(browseId is not null
                ? new Artist { Id = browseId, Name = text }
                : new Artist { Id = StableId("artist", text), Name = text });
        }

        return artists;
    }

    /// <summary>Joined text of the FIRST flex column (the row's title), or <c>null</c>.</summary>
    public static string? ExtractTitleFromFlexColumns(JsonNode? node) => FlexColumnText(node, 0);

    /// <summary>Joined text of the SECOND flex column (subtitle, e.g. "12 lagu"), or <c>null</c>.</summary>
    public static string? ExtractSecondFlexColumnText(JsonNode? node) => FlexColumnText(node, 1);

    private static string? FlexColumnText(JsonNode? node, int index)
    {
        var columns = AsArray(Prop(node, "flexColumns"));
        if (columns is null || columns.Count <= index)
        {
            return null;
        }

        var runs = AsArray(Prop(Prop(Prop(columns[index], "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs"));
        if (runs is null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var run in runs)
        {
            var text = GetString(run, "text");
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }

        var joined = string.Concat(parts).Trim();
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>
    /// Returns the view/play-count run from a two-row item's subtitle (e.g. "2,5 jt x ditonton"),
    /// or <c>null</c> when none is present. <see cref="ExtractArtists"/> deliberately filters these
    /// out of the artist list, so callers that want to DISPLAY the count read it via this instead.
    /// </summary>
    public static string? ExtractViewsFromSubtitle(JsonNode? node)
    {
        var runs = AsArray(Prop(Prop(node, "subtitle"), "runs"));
        if (runs is null)
        {
            return null;
        }

        foreach (var run in runs)
        {
            var text = GetString(run, "text");
            if (!string.IsNullOrEmpty(text) && !IsArtistSeparator(text) && LooksLikeCountText(text))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a subtitle run is a view/play/stream count (in English or Indonesian) rather than an
    /// artist name, so it can be excluded from the artist list. Linked artist runs never match.
    /// </summary>
    private static bool LooksLikeCountText(string text)
    {
        var lowered = text.ToLowerInvariant();
        return lowered.Contains("view", StringComparison.Ordinal)
            || lowered.Contains("ditonton", StringComparison.Ordinal)
            || lowered.Contains("play", StringComparison.Ordinal)
            || lowered.Contains("diputar", StringComparison.Ordinal)
            || lowered.Contains("stream", StringComparison.Ordinal)
            || lowered.Contains("pendengar", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts artists from the second flex column of a
    /// <c>musicResponsiveListItemRenderer</c>. Linked, navigable artist runs are returned
    /// first; if none are present, the first plain-text artist run (skipping separators and
    /// metadata such as durations, song counts, years, and view/play counts) is preserved
    /// with a deterministic <see cref="StableId"/>. Mirrors the macOS uploaded-songs path.
    /// </summary>
    public static IReadOnlyList<Artist> ExtractArtistsFromFlexColumns(JsonNode? node)
    {
        var flexColumns = AsArray(Prop(node, "flexColumns"));
        if (flexColumns is null || flexColumns.Count <= 1)
        {
            return Array.Empty<Artist>();
        }

        // flexColumns[1].musicResponsiveListItemFlexColumnRenderer.text.runs
        var renderer = Prop(flexColumns[1], "musicResponsiveListItemFlexColumnRenderer");
        var runs = AsArray(Prop(Prop(renderer, "text"), "runs"));
        if (runs is null)
        {
            return Array.Empty<Artist>();
        }

        var artists = new List<Artist>();
        foreach (var run in runs)
        {
            var name = GetString(run, "text")?.Trim();
            if (string.IsNullOrEmpty(name) || IsArtistSeparator(name))
            {
                continue;
            }

            var browseId = ExtractBrowseId(run);
            if (browseId is not null && IsNavigableArtistId(browseId))
            {
                artists.Add(new Artist { Id = browseId, Name = name });
            }
        }

        if (artists.Count > 0)
        {
            return artists;
        }

        // Fallback: uploaded songs expose plain-text artist metadata with no endpoint.
        foreach (var run in runs)
        {
            if (Prop(run, "navigationEndpoint") is not null)
            {
                continue;
            }

            var name = GetString(run, "text")?.Trim();
            if (string.IsNullOrEmpty(name) || IsArtistSeparator(name) || IsMetadataText(name))
            {
                continue;
            }

            return new[] { new Artist { Id = StableId("upload-artist", name), Name = name } };
        }

        return Array.Empty<Artist>();
    }

    /// <summary>
    /// Extracts <c>navigationEndpoint.browseEndpoint.browseId</c> from a node, or <c>null</c>.
    /// </summary>
    public static string? ExtractBrowseId(JsonNode? node) =>
        GetString(Prop(Prop(node, "navigationEndpoint"), "browseEndpoint"), "browseId");

    /// <summary>
    /// Extracts the album a song row belongs to from its flex columns (Bug 5). The album link sits
    /// on a flex-column run whose <c>navigationEndpoint.browseEndpoint</c> targets an album browseId
    /// (<c>MPRE…</c>/<c>OLAK…</c>, or a <c>MUSIC_PAGE_TYPE_ALBUM</c> pageType). Returns the
    /// <see cref="Album"/> (id + title) when present, or <c>null</c> when the row carries no album
    /// link — ids are never fabricated. Mirrors the macOS
    /// <c>ParsingHelpers.extractAlbumFromFlexColumns</c> and the playlist track-row parser.
    /// </summary>
    public static Album? ExtractAlbumFromFlexColumns(JsonNode? node)
    {
        var flexColumns = AsArray(Prop(node, "flexColumns"));
        if (flexColumns is null)
        {
            return null;
        }

        // The album link lives in a non-title column (typically the 2nd/3rd). Scan every flex column
        // and return the first run that navigates to an album browse target.
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

                if (pageType == "MUSIC_PAGE_TYPE_ALBUM"
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

    // MARK: - Metadata classification

    private static bool IsArtistSeparator(string text) =>
        Array.IndexOf(ArtistSeparators, text) >= 0;

    private static bool IsMetadataText(string text)
    {
        if (ContentTypeKeywords.Contains(text)
            || ParseDuration(text) is not null
            || IsNaturalLanguageDuration(text)
            || ExtractSongCount(text) is not null
            || IsStandaloneYear(text))
        {
            return true;
        }

        var lowercased = text.ToLowerInvariant();
        return lowercased.Contains(" views", StringComparison.Ordinal)
            || lowercased.Contains(" plays", StringComparison.Ordinal)
            || lowercased.Contains(" subscribers", StringComparison.Ordinal)
            || lowercased.Contains("episodes", StringComparison.Ordinal);
    }

    private static bool IsStandaloneYear(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 4
            && trimmed.All(char.IsDigit)
            && int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && year is >= 1900 and <= 2100;
    }

    private static bool IsNaturalLanguageDuration(string text)
    {
        var lowercased = text.ToLowerInvariant();
        string[] units = { "second", "seconds", "minute", "minutes", "hour", "hours" };
        if (!units.Any(u => lowercased.Contains(u, StringComparison.Ordinal)))
        {
            return false;
        }

        var unitChars = string.Concat(units);
        return lowercased.All(c =>
            char.IsDigit(c)
            || char.IsWhiteSpace(c)
            || c == ','
            || unitChars.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>
    /// Extracts a song/track count from subtitle text (e.g. <c>"Playlist • 145 songs" → 145</c>),
    /// or <c>null</c> when no count is present.
    /// </summary>
    public static int? ExtractSongCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"([\d,]+)\s+(?:songs?|tracks?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        var digits = match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
    }

    // MARK: - JsonNode accessors

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

    private static JsonArray? FindThumbnailArray(JsonNode? node)
    {
        // data.thumbnail.{musicThumbnailRenderer|croppedSquareThumbnailRenderer}.thumbnail.thumbnails
        var thumbnail = Prop(node, "thumbnail");
        if (thumbnail is not null)
        {
            var fromRenderer = ThumbnailsUnderRenderers(thumbnail);
            if (fromRenderer is not null)
            {
                return fromRenderer;
            }

            // data.thumbnail.thumbnails (direct)
            if (AsArray(Prop(thumbnail, "thumbnails")) is { } direct)
            {
                return direct;
            }
        }

        // data.thumbnailRenderer.{musicThumbnailRenderer|croppedSquareThumbnailRenderer}.thumbnail.thumbnails
        var thumbnailRenderer = Prop(node, "thumbnailRenderer");
        if (thumbnailRenderer is not null && ThumbnailsUnderRenderers(thumbnailRenderer) is { } fromTr)
        {
            return fromTr;
        }

        // data.foregroundThumbnail.musicThumbnailRenderer.thumbnail.thumbnails
        var foreground = Prop(node, "foregroundThumbnail");
        if (foreground is not null
            && AsArray(Prop(Prop(Prop(foreground, "musicThumbnailRenderer"), "thumbnail"), "thumbnails")) is { } fromFg)
        {
            return fromFg;
        }

        // data.thumbnails (direct, top level)
        return AsArray(Prop(node, "thumbnails"));
    }

    private static JsonArray? ThumbnailsUnderRenderers(JsonNode container)
    {
        foreach (var rendererKey in new[] { "musicThumbnailRenderer", "croppedSquareThumbnailRenderer" })
        {
            if (AsArray(Prop(Prop(Prop(container, rendererKey), "thumbnail"), "thumbnails")) is { } thumbnails)
            {
                return thumbnails;
            }
        }

        return null;
    }
}
