using KasetWin.Core.Models;
using KasetWin.Core.Services.Lyrics;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Example-based unit tests for <see cref="SyncedLyricsNavigator"/> (Task 6.2, Req 17.2).
/// The named monotonicity <i>property</i> (Property 22) is implemented separately.
/// </summary>
public class SyncedLyricsNavigatorTests
{
    private static SyncedLyrics Sample() => new(
        new[]
        {
            new SyncedLyricLine { TimeInMs = 0, Text = "a" },
            new SyncedLyricLine { TimeInMs = 1_000, Text = "b" },
            new SyncedLyricLine { TimeInMs = 2_500, Text = "c" },
        },
        "test");

    [Fact]
    public void CurrentLineIndex_returns_minus_one_before_first_line()
    {
        var lyrics = new SyncedLyrics(
            new[] { new SyncedLyricLine { TimeInMs = 500, Text = "x" } },
            "test");

        Assert.Equal(-1, SyncedLyricsNavigator.CurrentLineIndex(lyrics, 0));
    }

    [Fact]
    public void CurrentLineIndex_returns_minus_one_for_empty()
    {
        var empty = new SyncedLyrics(Array.Empty<SyncedLyricLine>(), "test");
        Assert.Equal(-1, SyncedLyricsNavigator.CurrentLineIndex(empty, 10_000));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(999, 0)]
    [InlineData(1_000, 1)]
    [InlineData(2_499, 1)]
    [InlineData(2_500, 2)]
    [InlineData(10_000, 2)]
    public void CurrentLineIndex_picks_last_line_at_or_before_position(long positionMs, int expected)
    {
        Assert.Equal(expected, SyncedLyricsNavigator.CurrentLineIndex(Sample(), positionMs));
    }

    [Fact]
    public void CurrentLineIndex_is_non_decreasing_as_position_advances()
    {
        var lyrics = Sample();
        var previous = -1;
        for (long t = 0; t <= 3_000; t += 100)
        {
            var index = SyncedLyricsNavigator.CurrentLineIndex(lyrics, t);
            Assert.True(index >= previous, $"index decreased at t={t}");
            previous = index;
        }
    }

    [Fact]
    public void CurrentLine_returns_active_line_text()
    {
        Assert.Equal("b", SyncedLyricsNavigator.CurrentLine(Sample(), 1_500)?.Text);
        Assert.Null(SyncedLyricsNavigator.CurrentLine(Sample(), -1));
    }
}
