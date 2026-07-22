using System.Globalization;
using System.Text.Json.Nodes;
using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Lyrics;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit + property tests for the YouTube Music lyrics source: the pure
/// <see cref="YouTubeMusicLyricsParser"/> (next → Lyrics tab browseId → browse → description shelf)
/// and the <see cref="YouTubeMusicLyricsProvider"/> that maps it onto the lyrics pipeline.
/// No network access: the InnerTube payloads are literal fixtures and the client is faked.
/// </summary>
public class YouTubeMusicLyricsTests
{
    private static readonly LyricsSearchInfo Info = new(
        Title: "Test Song",
        Artist: "Test Artist",
        Album: "Test Album",
        Duration: TimeSpan.FromSeconds(200),
        VideoId: "vid123");

    // A trimmed watch-next response shaped like the real one: tabs under the tabbed-results
    // renderer, of which the second is the Lyrics tab (MPLYt… browseId).
    private const string NextWithLyricsTab = """
    {
      "contents": {
        "singleColumnMusicWatchNextResultsRenderer": {
          "tabbedRenderer": {
            "watchNextTabbedResultsRenderer": {
              "tabs": [
                { "tabRenderer": { "title": "Up next", "selected": true } },
                { "tabRenderer": {
                    "title": "Lyrics",
                    "endpoint": { "browseEndpoint": { "browseId": "MPLYt_test_lyrics_id" } }
                } },
                { "tabRenderer": {
                    "title": "Related",
                    "endpoint": { "browseEndpoint": { "browseId": "MPTRt_test_related_id" } }
                } }
              ]
            }
          }
        }
      }
    }
    """;

    private const string BrowseWithLyrics = """
    {
      "contents": { "sectionListRenderer": { "contents": [
        { "musicDescriptionShelfRenderer": {
            "description": { "runs": [ { "text": "line one\nline two\nline three" } ] },
            "footer": { "runs": [ { "text": "Source: Musixmatch" } ] }
        } }
      ] } }
    }
    """;

    // ── Parser: browseId discovery ──────────────────────────────────────────────────────

    [Fact]
    public void FindLyricsBrowseId_reads_the_lyrics_tab_id()
    {
        Assert.Equal(
            "MPLYt_test_lyrics_id",
            YouTubeMusicLyricsParser.FindLyricsBrowseId(JsonNode.Parse(NextWithLyricsTab)));
    }

    [Fact]
    public void FindLyricsBrowseId_matches_a_localized_title_when_the_id_prefix_is_absent()
    {
        // YouTube localizes the tab title; an id that does not carry the MPLYt prefix must still
        // be found through the (Indonesian) title.
        var next = JsonNode.Parse("""
        { "tabs": [
          { "tabRenderer": { "title": "Berikutnya" } },
          { "tabRenderer": { "title": "Lirik", "endpoint": { "browseEndpoint": { "browseId": "XYZ_lyrics" } } } }
        ] }
        """);

        Assert.Equal("XYZ_lyrics", YouTubeMusicLyricsParser.FindLyricsBrowseId(next));
    }

    [Fact]
    public void FindLyricsBrowseId_skips_an_unselectable_lyrics_tab()
    {
        // "No lyrics for this track" arrives as a present-but-unselectable tab.
        var next = JsonNode.Parse("""
        { "tabs": [
          { "tabRenderer": {
              "title": "Lyrics",
              "unselectable": true,
              "endpoint": { "browseEndpoint": { "browseId": "MPLYt_nope" } }
          } }
        ] }
        """);

        Assert.Null(YouTubeMusicLyricsParser.FindLyricsBrowseId(next));
    }

    [Fact]
    public void FindLyricsBrowseId_returns_null_for_unrelated_or_null_input()
    {
        Assert.Null(YouTubeMusicLyricsParser.FindLyricsBrowseId(null));
        Assert.Null(YouTubeMusicLyricsParser.FindLyricsBrowseId(JsonNode.Parse("{}")));
        Assert.Null(YouTubeMusicLyricsParser.FindLyricsBrowseId(JsonNode.Parse("""{"tabs":[{"tabRenderer":{"title":"Up next"}}]}""")));
    }

    // ── Parser: lyric text ──────────────────────────────────────────────────────────────

    [Fact]
    public void ParseLyrics_reads_the_description_and_the_source_footer()
    {
        var lyrics = YouTubeMusicLyricsParser.ParseLyrics(JsonNode.Parse(BrowseWithLyrics));

        Assert.NotNull(lyrics);
        Assert.Equal("line one\nline two\nline three", lyrics!.Text);
        Assert.Equal("Source: Musixmatch", lyrics.Attribution);
    }

    [Fact]
    public void ParseLyrics_concatenates_multiple_runs_and_accepts_simpleText()
    {
        var multiRun = JsonNode.Parse("""
        { "musicDescriptionShelfRenderer": {
            "description": { "runs": [ { "text": "first " }, { "text": "second" } ] }
        } }
        """);
        var simple = JsonNode.Parse("""
        { "musicDescriptionShelfRenderer": {
            "description": { "simpleText": "plain body" },
            "footer": { "simpleText": "Source: LyricFind" }
        } }
        """);

        Assert.Equal("first second", YouTubeMusicLyricsParser.ParseLyrics(multiRun)!.Text);

        var parsedSimple = YouTubeMusicLyricsParser.ParseLyrics(simple);
        Assert.Equal("plain body", parsedSimple!.Text);
        Assert.Equal("Source: LyricFind", parsedSimple.Attribution);
    }

    [Fact]
    public void ParseLyrics_returns_null_when_there_is_no_usable_text()
    {
        Assert.Null(YouTubeMusicLyricsParser.ParseLyrics(null));
        Assert.Null(YouTubeMusicLyricsParser.ParseLyrics(JsonNode.Parse("{}")));
        Assert.Null(YouTubeMusicLyricsParser.ParseLyrics(JsonNode.Parse(
            """{ "musicDescriptionShelfRenderer": { "description": { "runs": [ { "text": "   " } ] } } }""")));
    }

    [Fact]
    public void ParseLyrics_normalizes_windows_line_endings()
    {
        var node = JsonNode.Parse("""
        { "musicDescriptionShelfRenderer": { "description": { "simpleText": "a\r\nb\r\n\n" } } }
        """);

        Assert.Equal("a\nb", YouTubeMusicLyricsParser.ParseLyrics(node)!.Text);
    }

    // ── Parser: timed lyrics (Android Music client) ─────────────────────────────────────

    // Trimmed verbatim from a live ANDROID_MUSIC browse of MPLYt_Y8THSupSgRO-1 (2026-07-22).
    // Note the cue times are decimal STRINGS, not numbers — that is the real wire shape.
    private const string AndroidTimedLyrics = """
    {
      "contents": { "elementRenderer": { "newElement": { "type": { "componentType": { "model": {
        "timedLyricsModel": { "lyricsData": {
          "timedLyricsData": [
            { "lyricLine": "♪", "cueRange": { "startTimeMilliseconds": "0", "endTimeMilliseconds": "11180", "metadata": { "id": "0" } } },
            { "lyricLine": "The club isn't the best place to find a lover", "cueRange": { "startTimeMilliseconds": "11180", "endTimeMilliseconds": "13300", "metadata": { "id": "1" } } },
            { "lyricLine": "So the bar is where I go, mm", "cueRange": { "startTimeMilliseconds": "13300", "endTimeMilliseconds": "15890", "metadata": { "id": "2" } } }
          ],
          "sourceMessage": "Source: Musixmatch"
        } }
      } } } } } }
    }
    """;

    [Fact]
    public void ParseTimedLyrics_reads_cue_ranges_and_the_source_message()
    {
        var lyrics = YouTubeMusicLyricsParser.ParseTimedLyrics(JsonNode.Parse(AndroidTimedLyrics));

        Assert.NotNull(lyrics);
        Assert.True(lyrics!.HasTimings);
        Assert.Equal("Source: Musixmatch", lyrics.Attribution);
        Assert.Null(lyrics.Text);

        var lines = lyrics.TimedLines!;
        Assert.Equal(3, lines.Count);
        Assert.Equal(0, lines[0].TimeInMs);
        Assert.Equal(11_180, lines[0].Duration);
        Assert.Equal(11_180, lines[1].TimeInMs);
        Assert.Equal(2_120, lines[1].Duration);
        Assert.Equal("The club isn't the best place to find a lover", lines[1].Text);
    }

    [Fact]
    public void ParseTimedLyrics_returns_null_for_a_desktop_client_response()
    {
        // The WEB_REMIX payload carries no timedLyricsModel — that is exactly the silent downgrade
        // a stale pinned Android clientVersion produces, and it must not look like a parse failure.
        Assert.Null(YouTubeMusicLyricsParser.ParseTimedLyrics(JsonNode.Parse(BrowseWithLyrics)));
        Assert.Null(YouTubeMusicLyricsParser.ParseTimedLyrics(null));
        Assert.Null(YouTubeMusicLyricsParser.ParseTimedLyrics(JsonNode.Parse("{}")));
    }

    [Fact]
    public void ParseTimedLyrics_falls_back_to_plain_text_when_no_line_carries_a_cue()
    {
        // Verified live (BiQIc7fG9pA, 2026-07-22): the Android client answers a track that has no
        // synced version with a FULL timedLyricsData array and not one cueRange in it. That is a
        // plain lyric, not a synced one — a "Synced" result built from zero cues would render as
        // lyrics that never advance.
        var node = JsonNode.Parse("""
        { "timedLyricsModel": { "lyricsData": {
            "timedLyricsData": [ { "lyricLine": "first" }, { "lyricLine": "second" } ],
            "sourceMessage": "Source: Musixmatch"
        } } }
        """);

        var lyrics = YouTubeMusicLyricsParser.ParseTimedLyrics(node);

        Assert.NotNull(lyrics);
        Assert.False(lyrics!.HasTimings);
        Assert.False(lyrics.IsEmpty);
        Assert.Equal("first\nsecond", lyrics.Text);
        Assert.Equal("Source: Musixmatch", lyrics.Attribution);
    }

    [Fact]
    public void ParseTimedLyrics_accepts_cue_times_sent_as_json_numbers()
    {
        // The wire shape is decimal strings today. Accepting numbers too means a future shape
        // change costs us nothing rather than silently dropping every line.
        var node = JsonNode.Parse("""
        { "timedLyricsModel": { "lyricsData": { "timedLyricsData": [
            { "lyricLine": "numeric", "cueRange": { "startTimeMilliseconds": 1980, "endTimeMilliseconds": 3750 } }
        ] } } }
        """);

        var lines = YouTubeMusicLyricsParser.ParseTimedLyrics(node)!.TimedLines!;

        Assert.Equal(1_980, lines[0].TimeInMs);
        Assert.Equal(1_770, lines[0].Duration);
    }

    [Fact]
    public void ParseTimedLyrics_skips_entries_with_no_text_or_no_cue()
    {
        var node = JsonNode.Parse("""
        { "timedLyricsModel": { "lyricsData": { "timedLyricsData": [
            { "lyricLine": "", "cueRange": { "startTimeMilliseconds": "10" } },
            { "lyricLine": "no cue at all" },
            { "lyricLine": "kept", "cueRange": { "startTimeMilliseconds": "500", "endTimeMilliseconds": "900" } }
        ] } } }
        """);

        var lines = YouTubeMusicLyricsParser.ParseTimedLyrics(node)!.TimedLines!;

        Assert.Single(lines);
        Assert.Equal("kept", lines[0].Text);
        Assert.Equal(500, lines[0].TimeInMs);
    }

    [Fact]
    public void Parse_prefers_timed_over_plain_and_falls_back_to_plain()
    {
        Assert.True(YouTubeMusicLyricsParser.Parse(JsonNode.Parse(AndroidTimedLyrics))!.HasTimings);

        var plain = YouTubeMusicLyricsParser.Parse(JsonNode.Parse(BrowseWithLyrics));
        Assert.NotNull(plain);
        Assert.False(plain!.HasTimings);
        Assert.False(plain.IsEmpty);

        // YouTube's own "Lyrics not available" message payload is neither.
        Assert.Null(YouTubeMusicLyricsParser.Parse(JsonNode.Parse("""
        { "contents": { "messageRenderer": { "text": { "runs": [ { "text": "Lyrics not available" } ] } } } }
        """)));
    }

    // ── Provider ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provider_returns_plain_lyrics_labelled_with_the_provider_name()
    {
        var client = new FakeYTMusicClient
        {
            YouTubeMusicLyrics = _ => new YouTubeMusicLyrics("body", "Source: Musixmatch"),
        };
        var provider = new YouTubeMusicLyricsProvider(client);

        var result = await provider.SearchAsync(Info);

        var plain = Assert.IsType<LyricResult.Plain>(result);
        Assert.Equal(YouTubeMusicLyricsProvider.ProviderName, plain.Lyrics.Source);
        // YouTube's own attribution footer is preserved at the end of the text.
        Assert.Equal("body\n\nSource: Musixmatch", plain.Lyrics.Text);
    }

    [Fact]
    public async Task Provider_stays_plain_when_the_payload_carries_no_timings()
    {
        // Text that merely LOOKS timed is still plain — only real cue ranges promote a result to
        // the synced tier.
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => new YouTubeMusicLyrics("[00:01.00] not really timed", null) };

        var result = await new YouTubeMusicLyricsProvider(client).SearchAsync(Info);

        Assert.IsNotType<LyricResult.Synced>(result);
        Assert.IsType<LyricResult.Plain>(result);
    }

    [Fact]
    public async Task Provider_returns_synced_lyrics_when_the_payload_is_timed()
    {
        var timed = new YouTubeMusicLyrics(
            Text: null,
            TimedLines: new[]
            {
                new SyncedLyricLine { TimeInMs = 11_180, Duration = 2_120, Text = "first line" },
                new SyncedLyricLine { TimeInMs = 13_300, Duration = 2_590, Text = "second line" },
            },
            Attribution: "Source: Musixmatch");
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => timed };

        var result = await new YouTubeMusicLyricsProvider(client).SearchAsync(Info);

        var synced = Assert.IsType<LyricResult.Synced>(result);
        Assert.Equal(YouTubeMusicLyricsProvider.ProviderName, synced.Lyrics.Source);

        // The licensor credit is kept as the last line, timed strictly after the final lyric so it
        // can never take the highlight from a line that is still being sung.
        Assert.Equal(3, synced.Lyrics.Lines.Count);
        Assert.Equal("Source: Musixmatch", synced.Lyrics.Lines[^1].Text);
        Assert.True(synced.Lyrics.Lines[^1].TimeInMs > synced.Lyrics.Lines[^2].TimeInMs);
    }

    [Fact]
    public async Task Provider_labels_synced_lyrics_even_when_the_payload_carries_no_attribution()
    {
        // The timed payload has no footer of its own; when the desktop browse that normally supplies
        // the licensor credit is unavailable too, the result must STILL carry a source, because
        // LyricsService.ActiveProvider and the panel's "Sumber: …" line both read it.
        var timed = new YouTubeMusicLyrics(
            Text: null,
            TimedLines: new[] { new SyncedLyricLine { TimeInMs = 0, Duration = 900, Text = "only line" } },
            Attribution: null);
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => timed };

        var synced = Assert.IsType<LyricResult.Synced>(await new YouTubeMusicLyricsProvider(client).SearchAsync(Info));

        Assert.Equal(YouTubeMusicLyricsProvider.ProviderName, synced.Lyrics.Source);
        Assert.Single(synced.Lyrics.Lines);
    }

    [Fact]
    public async Task Provider_maps_a_miss_and_a_transport_fault_to_Unavailable()
    {
        var miss = new FakeYTMusicClient { YouTubeMusicLyrics = _ => null };
        Assert.IsType<LyricResult.Unavailable>(await new YouTubeMusicLyricsProvider(miss).SearchAsync(Info));

        var faulty = new FakeYTMusicClient { YouTubeMusicLyrics = _ => throw new HttpRequestException("boom") };
        Assert.IsType<LyricResult.Unavailable>(await new YouTubeMusicLyricsProvider(faulty).SearchAsync(Info));
    }

    [Fact]
    public async Task Provider_skips_podcast_episodes_without_calling_the_api()
    {
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => new YouTubeMusicLyrics("body", null) };
        var podcast = Info with { IsPodcast = true };

        Assert.IsType<LyricResult.Unavailable>(await new YouTubeMusicLyricsProvider(client).SearchAsync(podcast));
        Assert.Equal(0, client.YouTubeMusicLyricsCalls);
    }

    [Fact]
    public async Task Provider_memoizes_per_video_so_the_panel_reopening_costs_no_requests()
    {
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => new YouTubeMusicLyrics("body", null) };
        var provider = new YouTubeMusicLyricsProvider(client);

        await provider.SearchAsync(Info);
        await provider.SearchAsync(Info);

        Assert.Equal(1, client.YouTubeMusicLyricsCalls);
    }

    // ── Service: the resolved lyrics always carry their source ──────────────────────────

    [Fact]
    public async Task LyricsService_exposes_the_provider_that_produced_the_lyrics()
    {
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => new YouTubeMusicLyrics("body", null) };
        var service = new LyricsService(new ILyricsProvider[] { new YouTubeMusicLyricsProvider(client) });

        await service.LoadForTrackAsync(Info);

        Assert.Equal(YouTubeMusicLyricsProvider.ProviderName, service.ActiveProvider);
        Assert.Equal(
            YouTubeMusicLyricsProvider.ProviderName,
            Assert.IsType<LyricResult.Plain>(service.CurrentLyrics).Lyrics.Source);
    }

    // Feature: kaset-winui3, Property 45: hasil lirik selalu membawa nama penyedianya
    // Validates: Requirements 17.1, 17.2
    [Fact]
    public void Property45_every_non_empty_lyric_result_carries_a_provider_source()
    {
        // For any provider name and any result a provider may hand back — including one that
        // forgot to label itself — the stamped result exposes a non-empty Source, so the UI's
        // "Sumber: …" line is never blank for lyrics that actually exist.
        Gen.Select(
                Gen.String[Gen.Char.AlphaNumeric, 1, 12],
                Gen.String[Gen.Char.AlphaNumeric, 0, 12],
                Gen.Int[0, 2])
            .Sample(
                t =>
                {
                    var (providerName, rawSource, kind) = t;
                    var source = rawSource.Length == 0 ? null : rawSource;

                    LyricResult input = kind switch
                    {
                        0 => new LyricResult.Plain(new PlainLyrics("text", source)),
                        1 => new LyricResult.Synced(new SyncedLyrics(
                            new[] { new SyncedLyricLine { TimeInMs = 0, Text = "text" } },
                            source ?? string.Empty)),
                        _ => new LyricResult.Unavailable(),
                    };

                    var stamped = LyricsService.StampSource(input, providerName);

                    var stampedSource = stamped switch
                    {
                        LyricResult.Plain p => p.Lyrics.Source,
                        LyricResult.Synced s => s.Lyrics.Source,
                        _ => null,
                    };

                    if (input is LyricResult.Unavailable)
                    {
                        Assert.Null(stampedSource);
                    }
                    else
                    {
                        Assert.False(string.IsNullOrWhiteSpace(stampedSource));

                        // An existing label is never overwritten by the service.
                        Assert.Equal(source ?? providerName, stampedSource);
                    }
                },
                iter: 100);
    }

    // Feature: kaset-winui3, Property 46: payload lirik ber-timing tidak pernah hilang isinya
    // Validates: Requirements 17.1, 17.2
    [Fact]
    public void Property46_a_timed_payload_never_loses_its_lines_whatever_the_cues_look_like()
    {
        // For ANY mix of cued and uncued lines the Android client may send, parsing it must:
        //  - never throw and never return null while there is at least one line of text;
        //  - claim timings if and only if at least one line actually carried a cueRange;
        //  - keep every cued line, with a non-negative start time.
        // This is the invariant that keeps a partially-synced or fully-unsynced track from
        // degrading to "no lyrics" instead of to plain text.
        Gen.Select(Gen.String[Gen.Char.AlphaNumeric, 1, 8], Gen.Bool, Gen.Int[0, 300_000])
            .List[1, 20]
            .Sample(
                entries =>
                {
                    var data = new JsonArray();
                    foreach (var (text, cued, startMs) in entries)
                    {
                        var entry = new JsonObject { ["lyricLine"] = text };
                        if (cued)
                        {
                            // Cue times go on the wire as decimal strings, so generate them as such.
                            entry["cueRange"] = new JsonObject
                            {
                                ["startTimeMilliseconds"] = startMs.ToString(CultureInfo.InvariantCulture),
                                ["endTimeMilliseconds"] = (startMs + 1_000).ToString(CultureInfo.InvariantCulture),
                            };
                        }

                        data.Add(entry);
                    }

                    var node = new JsonObject
                    {
                        ["timedLyricsModel"] = new JsonObject
                        {
                            ["lyricsData"] = new JsonObject { ["timedLyricsData"] = data },
                        },
                    };

                    var lyrics = YouTubeMusicLyricsParser.ParseTimedLyrics(node);

                    Assert.NotNull(lyrics);
                    Assert.False(lyrics!.IsEmpty);

                    var cuedCount = entries.Count(e => e.Item2);
                    Assert.Equal(cuedCount > 0, lyrics.HasTimings);

                    if (cuedCount > 0)
                    {
                        Assert.Equal(cuedCount, lyrics.TimedLines!.Count);
                        Assert.All(lyrics.TimedLines, line => Assert.True(line.TimeInMs >= 0));
                    }
                    else
                    {
                        Assert.False(string.IsNullOrWhiteSpace(lyrics.Text));
                    }
                },
                iter: 100);
    }
}
