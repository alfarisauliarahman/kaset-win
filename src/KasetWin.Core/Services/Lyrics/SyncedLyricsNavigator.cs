using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// Pure, dependency-free helpers for mapping a playback position onto a
/// <see cref="SyncedLyrics"/> line (Req 17.2). Kept separate from <see cref="LyricsService"/> so
/// the highlighting logic can be exercised headless and via property-based tests (Property 22).
/// </summary>
/// <remarks>
/// <para>
/// <b>Monotonicity guarantee (Property 22).</b> <see cref="LrcParser"/> produces lines sorted
/// ascending (stable) by <see cref="SyncedLyricLine.TimeInMs"/>. Given that ordering,
/// <see cref="CurrentLineIndex(SyncedLyrics, long)"/> returns the index of the last line whose
/// <see cref="SyncedLyricLine.TimeInMs"/> is less than or equal to the position. Because that
/// boundary only moves forward as the position increases, the returned index never decreases for
/// a non-decreasing sequence of positions, and every line with a <c>TimeInMs</c> strictly greater
/// than the position lies at an index beyond the current one (i.e. is "upcoming").
/// </para>
/// </remarks>
public static class SyncedLyricsNavigator
{
    /// <summary>
    /// Returns the index of the <em>current</em> line for the given playback position: the last
    /// line whose <see cref="SyncedLyricLine.TimeInMs"/> is <c>&lt;= positionMs</c>.
    /// </summary>
    /// <param name="lyrics">The synced lyrics (assumed sorted ascending by time, as produced by
    /// <see cref="LrcParser.Parse(string)"/>).</param>
    /// <param name="positionMs">The current playback position in milliseconds.</param>
    /// <returns>
    /// The zero-based index of the active line, or <c>-1</c> when the position precedes the first
    /// line (or there are no lines). The result is monotonically non-decreasing in
    /// <paramref name="positionMs"/> (Property 22).
    /// </returns>
    public static int CurrentLineIndex(SyncedLyrics lyrics, long positionMs)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        var lines = lyrics.Lines;
        if (lines.Count == 0)
        {
            return -1;
        }

        // Binary search for the rightmost line with TimeInMs <= positionMs.
        var lo = 0;
        var hi = lines.Count - 1;
        var result = -1;

        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (lines[mid].TimeInMs <= positionMs)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the active <see cref="SyncedLyricLine"/> for the given position, or
    /// <see langword="null"/> when the position precedes the first line.
    /// </summary>
    public static SyncedLyricLine? CurrentLine(SyncedLyrics lyrics, long positionMs)
    {
        var index = CurrentLineIndex(lyrics, positionMs);
        return index < 0 ? null : lyrics.Lines[index];
    }
}
