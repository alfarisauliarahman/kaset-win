using KasetWin.Core.Models;
using KasetWin.Core.Services.Lyrics;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Example-based unit tests for <see cref="LrcParser"/> (Task 6.1, Req 17.5).
/// The named round-trip <i>property</i> test (Property 21) is implemented separately
/// in Task 6.3; these cover concrete behaviours and a representative round-trip example.
/// </summary>
public class LrcParserTests
{
    [Fact]
    public void Parse_extracts_timestamp_and_text()
    {
        var lyrics = LrcParser.Parse("[00:12.34] hello world");

        var line = Assert.Single(lyrics.Lines);
        Assert.Equal((0 * 60 + 12) * 1000 + 340, line.TimeInMs);
        Assert.Equal("hello world", line.Text);
    }

    [Fact]
    public void Parse_supports_no_fraction_and_millisecond_variants()
    {
        var lyrics = LrcParser.Parse("[01:02] a\n[01:02.5] b\n[01:02.567] c");

        Assert.Collection(
            lyrics.Lines,
            l => Assert.Equal(62_000, l.TimeInMs),   // [01:02]      -> 62.000s
            l => Assert.Equal(62_500, l.TimeInMs),   // [01:02.5]    -> 62.500s
            l => Assert.Equal(62_567, l.TimeInMs));  // [01:02.567]  -> 62.567s
    }

    [Fact]
    public void Parse_skips_metadata_and_blank_lines()
    {
        const string lrc = """
            [ar:Some Artist]
            [ti:Some Title]
            [al:Some Album]
            [by:Someone]

            [00:01.00] only real line
            """;

        var line = Assert.Single(LrcParser.Parse(lrc).Lines);
        Assert.Equal(1000, line.TimeInMs);
        Assert.Equal("only real line", line.Text);
    }

    [Fact]
    public void Parse_applies_offset_to_every_line()
    {
        // offset shifts times *earlier*; negative results clamp to 0.
        var lyrics = LrcParser.Parse("[offset:250]\n[00:01.00] a\n[00:00.10] b");

        Assert.Collection(
            lyrics.Lines,
            l => Assert.Equal(0, l.TimeInMs),     // 100 - 250 -> clamped to 0
            l => Assert.Equal(750, l.TimeInMs));  // 1000 - 250
    }

    [Fact]
    public void Parse_expands_multiple_timestamps_on_one_line()
    {
        var lyrics = LrcParser.Parse("[00:01.00][00:05.00] repeated");

        Assert.Collection(
            lyrics.Lines,
            l => { Assert.Equal(1000, l.TimeInMs); Assert.Equal("repeated", l.Text); },
            l => { Assert.Equal(5000, l.TimeInMs); Assert.Equal("repeated", l.Text); });
    }

    [Fact]
    public void Parse_sorts_lines_ascending_by_time()
    {
        var lyrics = LrcParser.Parse("[00:05.00] later\n[00:01.00] earlier");

        Assert.Collection(
            lyrics.Lines,
            l => Assert.Equal(1000, l.TimeInMs),
            l => Assert.Equal(5000, l.TimeInMs));
    }

    [Fact]
    public void Parse_empty_or_null_yields_empty_lyrics()
    {
        Assert.True(LrcParser.Parse(null).IsEmpty);
        Assert.True(LrcParser.Parse(string.Empty).IsEmpty);
        Assert.True(LrcParser.Parse("\n\n   \n").IsEmpty);
    }

    [Fact]
    public void Format_prints_millisecond_precision_timestamps()
    {
        var lyrics = new SyncedLyrics(
            new[] { new SyncedLyricLine { TimeInMs = 62_567, Text = "c" } },
            "test");

        Assert.Equal("[01:02.567] c", LrcParser.Format(lyrics));
    }

    [Fact]
    public void RoundTrip_preserves_times_and_text()
    {
        const string lrc = """
            [ar:Artist]
            [00:00.50] first
            [00:12.34] second
            [01:05.789] third
            [00:12.34] tie keeps order
            """;

        var first = LrcParser.Parse(lrc);
        var second = LrcParser.Parse(LrcParser.Format(first));

        Assert.Equal(first.Lines.Count, second.Lines.Count);
        for (var i = 0; i < first.Lines.Count; i++)
        {
            Assert.Equal(first.Lines[i].TimeInMs, second.Lines[i].TimeInMs);
            Assert.Equal(first.Lines[i].Text, second.Lines[i].Text);
        }
    }
}
