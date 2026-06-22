using System.Globalization;
using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api.Parsers.YouTube;

/// <summary>
/// Pure, dependency-free extraction helpers shared by the regular-YouTube (video) parsers
/// (Req 32). Parallel to the music-side <see cref="ParsingHelpers"/> by design (ADR-0020) — the
/// YouTube renderer shapes (<c>videoRenderer</c>/<c>gridVideoRenderer</c>/<c>videoCardRenderer</c>/
/// <c>lockupViewModel</c>/<c>reelItemRenderer</c>) carry display-ready text and either
/// <c>{ runs: [...] }</c> or <c>{ simpleText }</c> text nodes, so these helpers resolve both.
/// </summary>
/// <remarks>
/// Every method is <c>static</c>, side-effect free and deterministic (no clocks/randomness, no
/// mutation of the input tree), so the parsers can satisfy the idempotency / stable-identity
/// guarantee. Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency.
/// </remarks>
public static class YouTubeParsingHelpers
{
    /// <summary>
    /// Resolves the display text of a YouTube text node, handling both the modern
    /// <c>{ "runs": [ { "text": … } ] }</c> shape (runs concatenated) and the legacy
    /// <c>{ "simpleText": … }</c> shape. Returns <c>null</c> when neither is present/non-empty.
    /// </summary>
    public static string? Text(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        if (obj.TryGetPropertyValue("simpleText", out var simple)
            && simple is JsonValue sv
            && sv.TryGetValue<string>(out var simpleText)
            && !string.IsNullOrEmpty(simpleText))
        {
            return simpleText;
        }

        if (obj.TryGetPropertyValue("runs", out var runsNode) && runsNode is JsonArray runs)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var run in runs)
            {
                if (run is JsonObject runObj
                    && runObj.TryGetPropertyValue("text", out var text)
                    && text is JsonValue tv
                    && tv.TryGetValue<string>(out var s))
                {
                    builder.Append(s);
                }
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        // Some "content" view-model nodes expose the string directly under "content".
        if (obj.TryGetPropertyValue("content", out var content)
            && content is JsonValue cv
            && cv.TryGetValue<string>(out var contentText)
            && !string.IsNullOrEmpty(contentText))
        {
            return contentText;
        }

        return null;
    }

    /// <summary>Reads a string property from <paramref name="node"/>, or <c>null</c>.</summary>
    public static string? GetString(JsonNode? node, string key)
    {
        if (node is JsonObject obj
            && obj.TryGetPropertyValue(key, out var value)
            && value is JsonValue jv
            && jv.TryGetValue<string>(out var s))
        {
            return s;
        }

        return null;
    }

    /// <summary>Reads an int property (int/long/double), or <c>null</c>.</summary>
    public static int? GetInt(JsonNode? node, string key)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) && value is JsonValue jv)
        {
            if (jv.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (jv.TryGetValue<long>(out var l))
            {
                return (int)l;
            }

            if (jv.TryGetValue<double>(out var d))
            {
                return (int)d;
            }

            if (jv.TryGetValue<string>(out var s)
                && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Returns the child object at <paramref name="key"/>, or <c>null</c>.</summary>
    public static JsonNode? Prop(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

    /// <summary>
    /// Returns the best (highest-resolution) thumbnail URL under the node, resolving the common
    /// nestings (<c>thumbnail.thumbnails</c> and view-model <c>image.sources</c>). Protocol-relative
    /// URLs are normalized to <c>https:</c>. <c>null</c> when none present.
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
            if (area >= bestArea)
            {
                bestArea = area;
                bestUrl = url;
            }
        }

        return ToUri(bestUrl);
    }

    /// <summary>Parses a URL string to an absolute <see cref="Uri"/>, normalizing protocol-relative URLs.</summary>
    public static Uri? ToUri(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var normalized = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// Whether the renderer represents live content (a <c>LIVE</c>/<c>DEFAULT_LIVE</c> badge or a
    /// thumbnail overlay style of <c>LIVE</c>). Best-effort across renderer generations.
    /// </summary>
    public static bool IsLive(JsonNode? node)
    {
        foreach (var badge in ResponseTreeSearch.FindAll(node, "metadataBadgeRenderer"))
        {
            var style = GetString(badge, "style");
            if (style is not null && style.Contains("LIVE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var overlay in ResponseTreeSearch.FindAll(node, "thumbnailOverlayTimeStatusRenderer"))
        {
            var style = GetString(overlay, "style");
            if (style is not null && style.Equals("LIVE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the resume-progress percent (0–100) from a renderer's
    /// <c>thumbnailOverlayResumePlaybackRenderer.percentDurationWatched</c>, or <c>null</c>.
    /// </summary>
    public static int? WatchedPercent(JsonNode? node)
    {
        var resume = ResponseTreeSearch.FindFirst(node, "thumbnailOverlayResumePlaybackRenderer");
        var percent = GetInt(resume, "percentDurationWatched");
        return percent is >= 0 and <= 100 ? percent : null;
    }

    private static JsonArray? FindThumbnailArray(JsonNode? node)
    {
        // renderer.thumbnail.thumbnails (videoRenderer family)
        if (Prop(Prop(node, "thumbnail"), "thumbnails") is JsonArray direct)
        {
            return direct;
        }

        // view-model image.sources (lockupViewModel / shortsLockupViewModel)
        var sources = ResponseTreeSearch.FindFirst(node, "sources");
        if (sources is JsonArray sourcesArray && LooksLikeThumbnailArray(sourcesArray))
        {
            return sourcesArray;
        }

        // Fallback: first nested "thumbnails" array anywhere in the node.
        var nested = ResponseTreeSearch.FindFirst(node, "thumbnails");
        return nested as JsonArray;
    }

    private static bool LooksLikeThumbnailArray(JsonArray array) =>
        array.Count > 0 && array[0] is JsonObject first && first.ContainsKey("url");
}
