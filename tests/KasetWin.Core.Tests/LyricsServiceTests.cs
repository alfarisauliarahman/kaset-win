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
        private int _callCount;

        public FakeProvider(string name, LyricResult result, int delayMs = 0)
        {
            Name = name;
            _result = result;
            _delayMs = delayMs;
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

            return _result;
        }
    }
}
