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
    public async Task Provider_never_returns_synced_lyrics()
    {
        // The YouTube Music payload has no line timings, so this provider must stay in the plain
        // tier — that is what keeps it a fallback behind LRCLib/NetEase.
        var client = new FakeYTMusicClient { YouTubeMusicLyrics = _ => new YouTubeMusicLyrics("[00:01.00] not really timed", null) };

        var result = await new YouTubeMusicLyricsProvider(client).SearchAsync(Info);

        Assert.IsNotType<LyricResult.Synced>(result);
        Assert.IsType<LyricResult.Plain>(result);
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
}
