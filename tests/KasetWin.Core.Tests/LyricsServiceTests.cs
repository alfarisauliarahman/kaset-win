using KasetWin.Core.Models;
using KasetWin.Core.Services.Lyrics;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LyricsService"/> coordination (Task 6.2, Req 17.2/17.3/17.4):
/// synced→plain priority, per-videoId caching, and stale-result protection.
/// </summary>
public class LyricsServiceTests
{
    private static LyricsSearchInfo Info(string videoId = "vid1") =>
        new("Title", "Artist", null, null, videoId);

    private static LyricResult Synced(string source = "p") =>
        new LyricResult.Synced(new SyncedLyrics(
            new[] { new SyncedLyricLine { TimeInMs = 0, Text = "x" } }, source));

    private static LyricResult Plain(string source = "p") =>
        new LyricResult.Plain(new PlainLyrics("text", source));

    [Fact]
    public async Task LoadForTrackAsync_prefers_synced_over_plain()
    {
        var plainProvider = new FakeProvider("plain", Plain("plain"));
        var syncedProvider = new FakeProvider("synced", Synced("synced"));
        var service = new LyricsService(new ILyricsProvider[] { plainProvider, syncedProvider });

        await service.LoadForTrackAsync(Info());

        var synced = Assert.IsType<LyricResult.Synced>(service.CurrentLyrics);
        Assert.Equal("synced", synced.Lyrics.Source);
        Assert.Equal("synced", service.ActiveProvider);
        Assert.False(service.IsLoading);
    }

    [Fact]
    public async Task LoadForTrackAsync_falls_back_to_plain_when_no_synced()
    {
        var service = new LyricsService(new ILyricsProvider[]
        {
            new FakeProvider("a", new LyricResult.Unavailable()),
            new FakeProvider("b", Plain("b")),
        });

        await service.LoadForTrackAsync(Info());

        Assert.IsType<LyricResult.Plain>(service.CurrentLyrics);
        Assert.Equal("b", service.ActiveProvider);
    }

    [Fact]
    public async Task LoadForTrackAsync_resolves_the_synced_tier_by_registration_priority()
    {
        // Two providers both return synced lyrics and the SECOND one answers first. Registration
        // order must still decide, otherwise "which provider produced this" is a coin toss decided
        // by network latency and the source line in the panel flaps between tracks.
        var preferred = new FakeProvider("first", Synced("first"), delayMs: 60);
        var faster = new FakeProvider("second", Synced("second"));
        var service = new LyricsService(new ILyricsProvider[] { preferred, faster });

        await service.LoadForTrackAsync(Info());

        Assert.Equal("first", service.ActiveProvider);
        Assert.Equal("first", Assert.IsType<LyricResult.Synced>(service.CurrentLyrics).Lyrics.Source);
    }

    [Fact]
    public async Task LoadForTrackAsync_does_not_wait_for_lower_priority_providers()
    {
        // The mirror image: when the HIGHEST-priority provider answers, the lookup returns without
        // waiting for the slower ones (a hit must not pay the slowest provider's latency).
        var fastest = new FakeProvider("first", Synced("first"));
        var slow = new FakeProvider("second", Synced("second"), delayMs: 5_000);
        var service = new LyricsService(new ILyricsProvider[] { fastest, slow });

        var started = DateTimeOffset.UtcNow;
        await service.LoadForTrackAsync(Info());

        Assert.Equal("first", service.ActiveProvider);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task LoadForTrackAsync_preferred_provider_wins_over_registration_order()
    {
        var service = new LyricsService(new ILyricsProvider[]
        {
            new FakeProvider("first", Synced("first")),
            new FakeProvider("second", Synced("second"), delayMs: 40),
        })
        {
            PreferredProvider = "second",
        };

        await service.LoadForTrackAsync(Info());

        Assert.Equal("second", service.ActiveProvider);
    }

    // ── The shipped registration order: YouTube Music, then LRCLib, then NetEase (ADR 0005) ─────

    [Fact]
    public async Task A_plain_result_from_the_first_provider_never_outranks_a_synced_one_behind_it()
    {
        // YouTube Music is registered first, but a track it has no SYNCED lyrics for (timed-shaped
        // payload with zero cueRange) must not demote a genuinely synced LRCLib result. Being first
        // only breaks ties INSIDE a tier; the tier itself still decides.
        var youtubeMusic = new FakeProvider("YouTube Music", Plain("YouTube Music"));
        var lrclib = new FakeProvider("LRCLib", Synced("LRCLib"), delayMs: 40);
        var service = new LyricsService(new ILyricsProvider[] { youtubeMusic, lrclib });

        await service.LoadForTrackAsync(Info());

        Assert.Equal("LRCLib", service.ActiveProvider);
        Assert.Equal("LRCLib", Assert.IsType<LyricResult.Synced>(service.CurrentLyrics).Lyrics.Source);
    }

    [Fact]
    public async Task An_empty_first_provider_falls_through_to_the_ones_behind_it()
    {
        // The load-bearing path of the reordering: when YouTube Music has nothing (no lyrics tab,
        // stale pinned client, transport fault), the chain must continue to LRCLib and NetEase
        // rather than short-circuit on the empty answer of the highest-priority provider.
        var youtubeMusic = new FakeProvider("YouTube Music", new LyricResult.Unavailable());
        var lrclib = new FakeProvider("LRCLib", new LyricResult.Unavailable());
        var netease = new FakeProvider("NetEase", Synced("NetEase"));
        var service = new LyricsService(new ILyricsProvider[] { youtubeMusic, lrclib, netease });

        await service.LoadForTrackAsync(Info());

        Assert.Equal("NetEase", service.ActiveProvider);
        Assert.Equal(1, lrclib.CallCount);
        Assert.Equal(1, netease.CallCount);
    }

    [Fact]
    public async Task A_throwing_first_provider_does_not_take_the_chain_down_with_it()
    {
        var exploding = new FakeProvider("YouTube Music", Synced("never"), throws: true);
        var lrclib = new FakeProvider("LRCLib", Plain("LRCLib"));
        var service = new LyricsService(new ILyricsProvider[] { exploding, lrclib });

        await service.LoadForTrackAsync(Info());

        Assert.Equal("LRCLib", service.ActiveProvider);
    }

    [Fact]
    public async Task LoadForTrackAsync_caches_by_videoId()
    {
        var provider = new FakeProvider("p", Synced());
        var service = new LyricsService(new[] { provider });

        await service.LoadForTrackAsync(Info("same"));
        await service.LoadForTrackAsync(Info("same"));

        // Second load is served from cache -> provider queried exactly once.
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task LoadForTrackAsync_unavailable_is_not_cached()
    {
        var provider = new FakeProvider("p", new LyricResult.Unavailable());
        var service = new LyricsService(new[] { provider });

        await service.LoadForTrackAsync(Info("v"));
        await service.LoadForTrackAsync(Info("v"));

        Assert.Equal(2, provider.CallCount);
        Assert.IsType<LyricResult.Unavailable>(service.CurrentLyrics);
    }

    [Fact]
    public async Task LoadForTrackAsync_newer_call_wins_over_in_flight_older_call()
    {
        var slow = new FakeProvider("slow", Plain("old"), delayMs: 120);
        var fast = new FakeProvider("fast", Synced("new"));
        var service = new LyricsService(new ILyricsProvider[] { slow, fast });

        // Kick off an older load, then immediately supersede it with a newer one for another track.
        var older = service.LoadForTrackAsync(Info("old"));
        await service.LoadForTrackAsync(Info("new"));
        await older;

        // The superseded (older) result must not clobber the newer published result.
        var synced = Assert.IsType<LyricResult.Synced>(service.CurrentLyrics);
        Assert.Equal("new", synced.Lyrics.Source);
    }

    private sealed class FakeProvider : ILyricsProvider
    {
        private readonly LyricResult _result;
        private readonly int _delayMs;
        private readonly bool _throws;
        private int _callCount;

        public FakeProvider(string name, LyricResult result, int delayMs = 0, bool throws = false)
        {
            Name = name;
            _result = result;
            _delayMs = delayMs;
            _throws = throws;
        }

        public string Name { get; }

        public int CallCount => _callCount;

        public async Task<LyricResult> SearchAsync(LyricsSearchInfo info, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, ct);
            }

            return _throws ? throw new HttpRequestException("provider exploded") : _result;
        }
    }
}
