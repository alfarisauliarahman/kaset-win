using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// Pure, dependency-free parser/printer for LRC (LyRiCs) payloads (Req 17.5).
/// </summary>
/// <remarks>
/// <para>
/// The surface is intentionally <c>static</c> and deterministic so it can be exercised
/// headless and via property-based tests (Property 21: round-trip). This type lives in
/// <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.
/// </para>
/// <para>
/// <b>Round-trip guarantee.</b> For any LRC payload, <see cref="Parse(string)"/> →
/// <see cref="Format(SyncedLyrics)"/> → <see cref="Parse(string)"/> yields a
/// <see cref="SyncedLyrics"/> equivalent to the first parse (same per-line
/// <see cref="SyncedLyricLine.TimeInMs"/> and <see cref="SyncedLyricLine.Text"/>).
/// This holds because:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="Format(SyncedLyrics)"/> always prints timestamps with full
///     <b>millisecond</b> precision (<c>[mm:ss.fff]</c>). Re-parsing therefore recovers the
///     exact <c>TimeInMs</c> of every line regardless of the original input precision
///     (<c>[mm:ss]</c>, <c>[mm:ss.xx]</c> or <c>[mm:ss.xxx]</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     Metadata tags (<c>[ar:]</c>, <c>[ti:]</c>, <c>[al:]</c>, <c>[by:]</c>) are dropped on
///     parse and are not re-emitted, so they cannot perturb the timed lines. An
///     <c>[offset:]</c> tag is folded into each line's time on the first parse and is not
///     re-emitted, so a second parse (which sees no offset) produces identical times.
///     </description>
///   </item>
///   <item>
///     <description>
///     Lines are ordered by <c>TimeInMs</c> with a <b>stable</b> sort, so lines that share a
///     timestamp keep their relative order across repeated parse/format cycles.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static partial class LrcParser
{
    /// <summary>Source label applied to a <see cref="SyncedLyrics"/> produced by <see cref="Parse(string)"/>.</summary>
    public const string SourceLabel = "LRC";

    // [mm:ss], [mm:ss.xx] or [mm:ss.xxx]. Minutes allow 1+ digits; the fraction is optional and
    // may be 1-3 digits (centiseconds or milliseconds).
    [GeneratedRegex(@"\[(\d+):([0-5]?\d)(?:\.(\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    // A line that is *only* a metadata tag such as [ar:Artist] / [offset:+250] (case-insensitive
    // key, no timed content). Used to recognise and skip pure-metadata lines.
    [GeneratedRegex(@"^\s*\[([A-Za-z]+):([^\]]*)\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MetadataLineRegex();

    /// <summary>
    /// Parses an LRC payload into a sorted <see cref="SyncedLyrics"/>.
    /// </summary>
    /// <param name="lrc">The raw LRC text. <see langword="null"/> is treated as empty.</param>
    /// <returns>
    /// A <see cref="SyncedLyrics"/> whose <see cref="SyncedLyrics.Lines"/> are sorted ascending by
    /// <see cref="SyncedLyricLine.TimeInMs"/> (stable for ties). Metadata-only lines are skipped,
    /// blank lines are ignored, an <c>[offset:]</c> tag shifts every time, and a single source
    /// line bearing multiple timestamps (e.g. <c>[00:01.00][00:05.00] text</c>) expands into one
    /// line per timestamp. Returns an empty result when no timed lines are present.
    /// </returns>
    public static SyncedLyrics Parse(string? lrc)
    {
        var lines = new List<SyncedLyricLine>();

        if (string.IsNullOrEmpty(lrc))
        {
            return new SyncedLyrics(lines, SourceLabel);
        }

        var offsetMs = 0;

        foreach (var rawLine in SplitLines(lrc))
        {
            // A pure metadata line carries no lyric text; capture [offset:] then skip it.
            var metaMatch = MetadataLineRegex().Match(rawLine);
            if (metaMatch.Success)
            {
                var key = metaMatch.Groups[1].Value;
                if (string.Equals(key, "offset", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(
                        metaMatch.Groups[2].Value.Trim(),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var parsedOffset))
                {
                    offsetMs = parsedOffset;
                }

                continue;
            }

            var timestamps = TimestampRegex().Matches(rawLine);
            if (timestamps.Count == 0)
            {
                // No timing on this line (blank line, free text, or non-offset metadata) → ignore.
                continue;
            }

            // The lyric text is whatever remains once every timestamp tag is removed.
            var text = TimestampRegex().Replace(rawLine, string.Empty).Trim();

            // A single source line may carry several timestamps → emit one line per timestamp.
            foreach (Match ts in timestamps)
            {
                var minutes = int.Parse(ts.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(ts.Groups[2].Value, CultureInfo.InvariantCulture);
                var fractionMs = ParseFractionToMs(ts.Groups[3].Value);

                var timeInMs = ((minutes * 60 + seconds) * 1000) + fractionMs - offsetMs;
                if (timeInMs < 0)
                {
                    timeInMs = 0;
                }

                lines.Add(new SyncedLyricLine { TimeInMs = timeInMs, Text = text });
            }
        }

        // Stable ascending sort so equal timestamps keep their relative order (round-trip safe).
        var sorted = lines.OrderBy(static l => l.TimeInMs).ToList();

        // Derive each line's duration as the gap to the next line (last line stays 0).
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            sorted[i] = sorted[i] with { Duration = sorted[i + 1].TimeInMs - sorted[i].TimeInMs };
        }

        return new SyncedLyrics(sorted, SourceLabel);
    }

    /// <summary>
    /// Prints a <see cref="SyncedLyrics"/> back to LRC text, one <c>[mm:ss.fff] text</c> line per
    /// entry, in the order supplied.
    /// </summary>
    /// <param name="lyrics">The lyrics to print.</param>
    /// <returns>
    /// The LRC representation. Timestamps use full millisecond precision so that
    /// <see cref="Parse(string)"/> recovers the exact <see cref="SyncedLyricLine.TimeInMs"/> of
    /// every line (round-trip guarantee, Property 21).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="lyrics"/> is <see langword="null"/>.</exception>
    public static string Format(SyncedLyrics lyrics)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        var builder = new StringBuilder();
        for (var i = 0; i < lyrics.Lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(FormatTimestamp(lyrics.Lines[i].TimeInMs))
                .Append(' ')
                .Append(lyrics.Lines[i].Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a millisecond offset as an LRC timestamp tag <c>[mm:ss.fff]</c> with full
    /// millisecond precision. Negative values are clamped to zero.
    /// </summary>
    private static string FormatTimestamp(int timeInMs)
    {
        if (timeInMs < 0)
        {
            timeInMs = 0;
        }

        var minutes = timeInMs / 60_000;
        var seconds = timeInMs % 60_000 / 1000;
        var milliseconds = timeInMs % 1000;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{minutes:D2}:{seconds:D2}.{milliseconds:D3}]");
    }

    /// <summary>
    /// Converts an LRC fractional-seconds capture into milliseconds: <c>"1" → 100</c>,
    /// <c>"12" → 120</c>, <c>"123" → 123</c>, <c>"" → 0</c>.
    /// </summary>
    private static int ParseFractionToMs(string fraction)
    {
        if (string.IsNullOrEmpty(fraction))
        {
            return 0;
        }

        // Right-pad to milliseconds (3 digits); the regex guarantees 1-3 digits so no truncation.
        Span<char> buffer = stackalloc char[3];
        for (var i = 0; i < 3; i++)
        {
            buffer[i] = i < fraction.Length ? fraction[i] : '0';
        }

        return int.Parse(buffer, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>Splits a payload on CR, LF, or CRLF without allocating empty-entry arrays per call.</summary>
    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\n' or '\r')
            {
                yield return text[start..i];

                // Treat CRLF as a single break.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        // Trailing segment after the last break (skipped when the text ended on a break).
        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
