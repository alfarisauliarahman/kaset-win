using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Lyrics as YouTube Music itself serves them, in whichever fidelity the response carried:
/// per-line timed lines (<see cref="TimedLines"/>, Android Music client) or a plain text body
/// (<see cref="Text"/>, desktop client), plus the attribution footer YouTube returns alongside
/// either of them (e.g. <c>"Source: Musixmatch"</c>).
/// </summary>
/// <param name="Text">The plain lyric body, newline separated, or <c>null</c>.</param>
/// <param name="TimedLines">Timed lines when the response carried cue ranges, else <c>null</c>.</param>
/// <param name="Attribution">
/// YouTube's own footer / <c>sourceMessage</c> (the licensor credit YouTube requires be shown), or
/// <c>null</c> when the response carried none.
/// </param>
public sealed record YouTubeMusicLyrics(
    string? Text,
    IReadOnlyList<SyncedLyricLine>? TimedLines,
    string? Attribution)
{
    /// <summary>Convenience ctor for the plain-text (desktop client) shape.</summary>
    public YouTubeMusicLyrics(string? text, string? attribution)
        : this(text, null, attribution)
    {
    }

    /// <summary>Whether the payload carries per-line timings (karaoke-capable).</summary>
    public bool HasTimings => TimedLines is { Count: > 0 };

    /// <summary>Whether the payload carries nothing usable at all.</summary>
    public bool IsEmpty => !HasTimings && string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Pure parsers for the YouTube Music (InnerTube) lyrics surface, which is a two-step lookup:
/// <list type="number">
///   <item><c>next</c> for the <c>videoId</c> returns the watch-next tabs; the "Lyrics" tab carries
///   a <c>browseEndpoint.browseId</c> (an <c>MPLYt…</c> id).</item>
///   <item><c>browse</c> on that id returns a <c>musicDescriptionShelfRenderer</c> whose
///   <c>description</c> holds the plain lyric text and whose <c>footer</c> holds the source credit.</item>
/// </list>
/// The fidelity of step 2 depends on which InnerTube client asks:
/// <list type="bullet">
///   <item><b>WEB_REMIX</b> (desktop) → <c>musicDescriptionShelfRenderer</c>, plain untimed text.</item>
///   <item><b>ANDROID_MUSIC</b> → <c>timedLyricsModel.lyricsData.timedLyricsData</c>, one entry per
///   line with a <c>cueRange</c> carrying <c>startTimeMilliseconds</c>/<c>endTimeMilliseconds</c>.</item>
/// </list>
/// Both shapes were captured from live responses on 2026-07-22 (browseId <c>MPLYt…</c>); the
/// parsers below are written against those captures, not against guesses.
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
    /// Parses a lyrics <c>browse</c> response at the best fidelity it carries: timed lines when
    /// present (Android Music client), otherwise the plain description shelf (desktop client).
    /// Returns <c>null</c> when the response carries neither — including YouTube's own
    /// "Lyrics not available" message payload.
    /// </summary>
    /// <param name="browseResponse">The parsed <c>browse</c> response root.</param>
    public static YouTubeMusicLyrics? Parse(JsonNode? browseResponse) =>
        ParseTimedLyrics(browseResponse) ?? ParseLyrics(browseResponse);

    /// <summary>
    /// Extracts lyrics from an <b>Android Music client</b> (<c>timedLyricsModel</c>) lyrics
    /// <c>browse</c> response at the fidelity it actually carries:
    /// <list type="bullet">
    ///   <item>at least one <c>cueRange</c> → timed lines (<see cref="YouTubeMusicLyrics.TimedLines"/>);</item>
    ///   <item>lines but no <c>cueRange</c> anywhere → the same lines as plain
    ///   <see cref="YouTubeMusicLyrics.Text"/>, because the track has no synced version;</item>
    ///   <item>no <c>timedLyricsModel</c> at all → <c>null</c> (a desktop-client response or a stale
    ///   pinned client version both look like this).</item>
    /// </list>
    /// </summary>
    /// <param name="browseResponse">The parsed <c>browse</c> response root.</param>
    public static YouTubeMusicLyrics? ParseTimedLyrics(JsonNode? browseResponse)
    {
        if (browseResponse is null)
        {
            return null;
        }

        // Live shape: contents.elementRenderer.newElement.type.componentType.model.timedLyricsModel
        //             .lyricsData.{ timedLyricsData[], sourceMessage }
        // Searched by key rather than by path so the element wrapper chain can be reshuffled.
        var lyricsData = ResponseTreeSearch.FindFirst(browseResponse, "timedLyricsModel") is { } model
            ? ResponseTreeSearch.FindFirst(model, "lyricsData")
            : null;

        if (lyricsData is null || ResponseTreeSearch.FindFirst(lyricsData, "timedLyricsData") is not JsonArray entries)
        {
            return null;
        }

        var lines = new List<SyncedLyricLine>(entries.Count);
        var body = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is not JsonObject e)
            {
                continue;
            }

            var text = ReadString(e, "lyricLine");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            body.Add(text.Trim());

            // Cue times arrive as decimal STRINGS ("11180"), not numbers. Verified live 2026-07-22:
            // cueRange = { startTimeMilliseconds, endTimeMilliseconds, metadata: { id } }.
            var cue = e["cueRange"] as JsonObject;
            var start = ReadMilliseconds(cue, "startTimeMilliseconds");
            if (start is null)
            {
                continue;
            }

            var end = ReadMilliseconds(cue, "endTimeMilliseconds");
            lines.Add(new SyncedLyricLine
            {
                TimeInMs = start.Value,
                Duration = end is { } e2 && e2 > start.Value ? e2 - start.Value : 0,
                Text = text.Trim(),
            });
        }

        if (body.Count == 0)
        {
            return null;
        }

        var attribution = ReadString(lyricsData as JsonObject, "sourceMessage");
        attribution = string.IsNullOrWhiteSpace(attribution) ? null : attribution.Trim();

        // A timed-SHAPED response is not necessarily a SYNCED one: verified live, a track can come
        // back with a full timedLyricsData array and not one cueRange in it (BiQIc7fG9pA, 2026-07-22).
        // Zero cues means the track has no synced version, and the honest result is plain text — a
        // "Synced" result built from nothing would render as lyrics that never advance.
        if (lines.Count == 0)
        {
            return new YouTubeMusicLyrics(string.Join('\n', body), attribution);
        }

        return new YouTubeMusicLyrics(
            Text: null,
            TimedLines: lines,
            Attribution: attribution);
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
            TimedLines: null,
            string.IsNullOrWhiteSpace(footer) ? null : footer.Trim());
    }

    /// <summary>Reads <c>node[key]</c> as a plain JSON string, or <c>null</c>.</summary>
    private static string? ReadString(JsonObject? node, string key) =>
        node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// Reads a cue timestamp. YouTube sends these as decimal strings; a numeric value is accepted
    /// too so a future shape change to real numbers does not silently drop every line.
    /// </summary>
    private static int? ReadMilliseconds(JsonObject? cue, string key)
    {
        if (cue?[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return (int)Math.Clamp(parsed, 0, int.MaxValue);
        }

        return value.TryGetValue<long>(out var number) ? (int)Math.Clamp(number, 0, int.MaxValue) : null;
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
