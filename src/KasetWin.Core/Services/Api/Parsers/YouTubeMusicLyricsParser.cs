using System.Text;
using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Plain-text lyrics as YouTube Music itself serves them: the description body plus the
/// attribution footer YouTube returns alongside it (e.g. <c>"Source: Musixmatch"</c>).
/// </summary>
/// <param name="Text">The lyric body, newline separated. Never empty when a value is produced.</param>
/// <param name="Attribution">
/// YouTube's own footer line (the licensor credit), or <c>null</c> when the response carried none.
/// </param>
public sealed record YouTubeMusicLyrics(string Text, string? Attribution);

/// <summary>
/// Pure parsers for the YouTube Music (InnerTube) lyrics surface, which is a two-step lookup:
/// <list type="number">
///   <item><c>next</c> for the <c>videoId</c> returns the watch-next tabs; the "Lyrics" tab carries
///   a <c>browseEndpoint.browseId</c> (an <c>MPLYt…</c> id).</item>
///   <item><c>browse</c> on that id returns a <c>musicDescriptionShelfRenderer</c> whose
///   <c>description</c> holds the plain lyric text and whose <c>footer</c> holds the source credit.</item>
/// </list>
/// These lyrics are <b>plain text</b> — YouTube Music does not expose line timings here — so they
/// only ever feed the plain (fallback) lyric path.
/// </summary>
/// <remarks>
/// Every method is <c>static</c>, deterministic, and resilient: a reshuffled, partial, or entirely
/// unrelated response yields <c>null</c> rather than an exception (AGENTS "resilient parsers" rule).
/// Tabs are located by recursive renderer search so an extra wrapper renderer cannot break the
/// lookup (see <see cref="ResponseTreeSearch"/>).
/// </remarks>
public static class YouTubeMusicLyricsParser
{
    /// <summary>Browse-id prefix YouTube Music uses for the lyrics surface.</summary>
    public const string LyricsBrowseIdPrefix = "MPLYt";

    /// <summary>
    /// Localized spellings of the "Lyrics" tab title. YouTube localizes tab titles to the account
    /// language, so the id prefix is the primary signal and these are the fallback.
    /// </summary>
    private static readonly string[] LyricsTabTitles =
    {
        "lyrics", "lirik", "letra", "letras", "paroles", "liedtext", "songtext",
        "testo", "テキスト", "歌詞", "가사", "lirikan", "текст песни",
    };

    /// <summary>
    /// Returns the <c>browseId</c> of the "Lyrics" tab in a <c>next</c> (watch-next) response, or
    /// <c>null</c> when the track has no lyrics tab (or the tab is present but unselectable).
    /// </summary>
    /// <param name="nextResponse">The parsed <c>next</c> response root. <c>null</c> yields <c>null</c>.</param>
    public static string? FindLyricsBrowseId(JsonNode? nextResponse)
    {
        if (nextResponse is null)
        {
            return null;
        }

        string? titleMatch = null;

        foreach (var tab in ResponseTreeSearch.FindAll(nextResponse, "tabRenderer"))
        {
            if (tab is not JsonObject tabObj)
            {
                continue;
            }

            // An unselectable tab is YouTube's way of saying "no lyrics for this track".
            if (tabObj["unselectable"] is JsonValue uv && uv.TryGetValue<bool>(out var unselectable) && unselectable)
            {
                continue;
            }

            var browseId = FindBrowseId(tabObj);
            if (string.IsNullOrEmpty(browseId))
            {
                continue;
            }

            // Primary signal: the id prefix, which is language independent.
            if (browseId.StartsWith(LyricsBrowseIdPrefix, StringComparison.Ordinal))
            {
                return browseId;
            }

            // Fallback: a title that reads as "Lyrics" in one of the languages we know.
            if (titleMatch is null && IsLyricsTitle(ReadTabTitle(tabObj)))
            {
                titleMatch = browseId;
            }
        }

        return titleMatch;
    }

    /// <summary>
    /// Extracts the plain lyric text (and YouTube's own attribution footer) from a lyrics
    /// <c>browse</c> response, or <c>null</c> when the response carries no usable lyric text.
    /// </summary>
    /// <param name="browseResponse">The parsed <c>browse</c> response root.</param>
    public static YouTubeMusicLyrics? ParseLyrics(JsonNode? browseResponse)
    {
        if (browseResponse is null)
        {
            return null;
        }

        var shelf = ResponseTreeSearch.FindFirst(browseResponse, "musicDescriptionShelfRenderer");
        if (shelf is null)
        {
            return null;
        }

        var text = ReadText(shelf, "description");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var footer = ReadText(shelf, "footer");
        return new YouTubeMusicLyrics(
            Normalize(text),
            string.IsNullOrWhiteSpace(footer) ? null : footer.Trim());
    }

    // ── internals ───────────────────────────────────────────────────────────────────────

    /// <summary>The first <c>browseEndpoint.browseId</c> anywhere under <paramref name="tab"/>.</summary>
    private static string? FindBrowseId(JsonNode tab)
    {
        var endpoint = ResponseTreeSearch.FindFirst(tab, "browseEndpoint");
        return endpoint is JsonObject e && e["browseId"] is JsonValue v && v.TryGetValue<string>(out var id)
            ? id
            : null;
    }

    /// <summary>Tab titles come as a bare string or as a runs/simpleText text object.</summary>
    private static string? ReadTabTitle(JsonObject tab)
    {
        if (tab["title"] is JsonValue tv && tv.TryGetValue<string>(out var plain))
        {
            return plain;
        }

        return ReadText(tab, "title");
    }

    private static bool IsLyricsTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title)
        && Array.Exists(
            LyricsTabTitles,
            known => title.Trim().Contains(known, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads <c>node[key]</c> as InnerTube text: <c>simpleText</c>, or every <c>runs[].text</c>
    /// concatenated in order (the lyric body arrives as a single run, but newer responses split it).
    /// </summary>
    private static string? ReadText(JsonNode? node, string key)
    {
        if (node is not JsonObject obj || obj[key] is not JsonObject textNode)
        {
            return null;
        }

        if (textNode["simpleText"] is JsonValue sv && sv.TryGetValue<string>(out var simple))
        {
            return simple;
        }

        if (textNode["runs"] is not JsonArray runs)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var run in runs)
        {
            if (run is JsonObject r && r["text"] is JsonValue rv && rv.TryGetValue<string>(out var text))
            {
                builder.Append(text);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Normalizes line endings and trims trailing blank lines without reflowing the text.</summary>
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\n', ' ', '\t');
}
