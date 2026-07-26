using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Regression tests for the four playback defects found by running the app (manual-test checklist
/// 73/90, 77b, 88b, 111b): overlapping track loads, the load guard being disarmed by a superseded
/// load, re-selecting a track whose page is dead, and a queue entry that stays empty when playback
/// started from nothing but a videoId.
/// </summary>
/// <remarks>
/// The interleaving is real, not simulated: <see cref="GatedPlaybackController"/> holds each
/// <c>LoadVideoAsync</c> open on a <see cref="TaskCompletionSource"/> so several
/// <c>NextAsync</c> calls are genuinely in flight at once, exactly as they are when the user hammers
/// the Next button or the media key.
/// </remarks>
public class PlayerLoadAndMetadataTests
{
    private static IReadOnlyList<Song> MakeSongs(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Song
        {
            Id = $"q{i}",
            VideoId = $"q{i}",
            Title = $"Song {i}",
        })];

    private static PlaybackStateMessage Report(string videoId, string title = "T", bool playing = true) =>
        new(playing, 0, 100, videoId, title, string.Empty, true, null, null);

    // ── 77b / 88b: rapid Next must leave the newest track playing ────────────────────

    [Fact]
    public async Task RapidNext_LeavesTheNewestTrackLoadedAndPlaying()
    {
        var queue = new QueueService(bound => 0);
        var controller = new GatedPlaybackController();
        var player = new PlayerService(queue, controller, new FakeJsBridge());

        await player.PlayCollectionAsync(MakeSongs(5), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));

        // Three Next presses in a row, none of which has finished loading yet.
        controller.HoldLoads = true;
        Task first = player.NextAsync();
        Task second = player.NextAsync();
        Task third = player.NextAsync();

        Assert.Equal(3, queue.CurrentIndex); // the queue advanced synchronously, as it always did

        controller.ReleaseHeldLoads();
        await Task.WhenAll(first, second, third);

        // The middle load was superseded before it ever reached the controller, and the newest one
        // is the one that ends up loaded and playing. Before the generation guard, an older load
        // could finish last and leave the player pointing at a track nobody asked for — or, when it
        // failed, disarm the guard and leave nothing playing at all.
        Assert.Equal("q3", controller.CurrentVideoId);
        Assert.DoesNotContain("q2", controller.LoadedVideoIds);
        Assert.Equal("q3", player.CurrentTrack?.VideoId);
        Assert.True(player.IsPlaying);

        // The load guard belongs to the newest load: q3's own report releases it.
        player.HandleStateUpdate(Report("q3", "Song 3"));
        player.HandleStateUpdate(Report("autoplay-pick", "Something else"));
        Assert.Equal("autoplay-pick", player.CurrentTrack?.VideoId);
    }

    [Fact]
    public async Task SupersededLoadThatFails_DoesNotDisarmTheNewestLoadsGuard()
    {
        var queue = new QueueService(bound => 0);
        var controller = new GatedPlaybackController { FailingVideoId = "q1" };
        var player = new PlayerService(queue, controller, new FakeJsBridge());

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));

        controller.HoldLoads = true;
        Task first = player.NextAsync();  // q1 — will fail once released
        Task second = player.NextAsync(); // q2 — the load the user actually wants

        controller.ReleaseHeldLoads();
        await Task.WhenAll(first, second); // the superseded failure must not surface either

        Assert.Equal("q2", controller.CurrentVideoId);

        // The guard must still be armed for q2: the outgoing page keeps reporting for seconds, and
        // adopting those reports is what turned the queue into a mix.
        player.HandleStateUpdate(Report("drift", "Outgoing page"));
        Assert.Equal(3, queue.Tracks.Count);
        Assert.Equal("q2", player.CurrentTrack?.VideoId);
    }

    // ── 111b: re-selecting a track whose page died must really reload it ─────────────

    [Fact]
    public async Task PlayingTheTrackThatIsAlreadyLoaded_ReloadsIt()
    {
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var player = new PlayerService(queue, controller, new FakeJsBridge());

        var song = MakeSongs(1)[0];
        await player.PlayCollectionAsync([song], startIndex: 0);
        await player.PlayCollectionAsync([song], startIndex: 0);

        // Both plays navigate. Without this, the connection dropping mid-song left the page dead
        // while its videoId was still "loaded", so clicking that same song did nothing whatsoever
        // and the user had to pick a different song first.
        Assert.Equal(["q0", "q0"], controller.LoadedVideoIds);
    }

    [Fact]
    public async Task AutomaticAdvance_StillTreatsAnAlreadyLoadedTrackAsANoOp()
    {
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var player = new PlayerService(queue, controller, new FakeJsBridge());

        await player.PlayCollectionAsync(MakeSongs(2), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));

        // A stale TRACK_ENDED for another video replays the expected track. That path relies on the
        // idempotent no-op: it must not restart the song the user is listening to.
        await player.HandleTrackEndedAsync("some-other-video");

        Assert.Equal(["q0"], controller.LoadedVideoIds);
    }

    // ── 73/90: a track played from nothing but a videoId must still fill the queue ───

    [Fact]
    public async Task ProtocolLaunch_EnrichesTheQueueEntry_NotJustTheCurrentTrack()
    {
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var enriched = new Song
        {
            Id = "proto1",
            VideoId = "proto1",
            Title = "i hate u, i love u",
            Artists = [new Artist { Id = "UC1", Name = "gnash" }],
            Album = new Album { Id = "MPREb_x", Title = "us" },
            Duration = TimeSpan.FromSeconds(211),
        };
        var player = new PlayerService(
            queue,
            controller,
            new FakeJsBridge(),
            metadataFetcher: (_, _) => Task.FromResult<Song?>(enriched));

        // What `kaset://play?v=…` produces: a Song with an id and nothing else.
        await player.PlayAsync("proto1");

        // Let the background enrichment settle (it is deliberately fire-and-forget).
        await WaitUntilAsync(() => queue.CurrentTrack?.Album is not null);

        // The queue — not CurrentTrack — is what the queue panel's "Now playing" row renders, so an
        // enrichment that only touched CurrentTrack left that row blank for the whole song.
        Song queued = Assert.Single(queue.Tracks);
        Assert.Equal("i hate u, i love u", queued.Title);
        Assert.Equal("us", queued.Album?.Title);
        Assert.Equal("gnash", queued.Artists.SingleOrDefault()?.Name);
        Assert.Equal(TimeSpan.FromSeconds(211), queued.Duration);
        Assert.Equal("i hate u, i love u", player.CurrentTrack?.Title);
    }

    [Fact]
    public async Task StateUpdate_FillsTheQueueEntryOfATrackQueuedFromNothingButAVideoId()
    {
        var queue = new QueueService(bound => 0);
        var player = new PlayerService(queue, new FakePlaybackController(), new FakeJsBridge());

        await player.PlayAsync("proto1");

        player.HandleStateUpdate(new PlaybackStateMessage(
            IsPlaying: true, Progress: 1, Duration: 100, VideoId: "proto1",
            Title: "i hate u, i love u", Artist: "gnash", TrackChanged: true,
            HasVideo: null, VideoType: null,
            ThumbnailUrl: new Uri("https://example.invalid/art.jpg")));

        Song queued = Assert.Single(queue.Tracks);
        Assert.Equal("i hate u, i love u", queued.Title);
        Assert.Equal("gnash", queued.Artists.SingleOrDefault()?.Name);
        Assert.Equal(new Uri("https://example.invalid/art.jpg"), queued.ThumbnailUrl);
    }

    [Fact]
    public void TryEnrichTrack_NeverOverwritesATitleTheQueueEntryAlreadyHas()
    {
        var queue = new QueueService(bound => 0);
        queue.SetQueue(MakeSongs(2), startIndex: 0);

        bool changed = queue.TryEnrichTrack(
            "q0",
            new Song { Id = "q0", VideoId = "q0", Title = "Something the page made up" });

        Assert.False(changed);
        Assert.Equal("Song 0", queue.Tracks[0].Title);
    }

    // ── The album line: an album with an id but no name cannot be rendered ───────────

    [Fact]
    public async Task Enricher_NamesAnAlbumThatArrivedWithOnlyABrowseId()
    {
        var enricher = new TrackMetadataEnricher(
            (videoId, _) => Task.FromResult<Song?>(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = "Track",
                // What the watch-next response carries: the album's browse id and no title at all.
                Album = new Album { Id = "MPREb_x", Title = string.Empty },
            }),
            (_, _) => Task.FromResult<string?>("us"));

        Song? song = await enricher.FetchAsync("proto1");

        Assert.Equal("us", song?.Album?.Title);
    }

    [Fact]
    public async Task Enricher_LeavesANamedAlbumAlone_AndSwallowsLookupFailures()
    {
        var albumLookups = 0;
        var enricher = new TrackMetadataEnricher(
            (videoId, _) => Task.FromResult<Song?>(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = "Track",
                Album = new Album { Id = "MPREb_x", Title = "Already named" },
            }),
            (_, _) =>
            {
                albumLookups++;
                return Task.FromResult<string?>("Should not be used");
            });

        Song? song = await enricher.FetchAsync("proto1");

        Assert.Equal("Already named", song?.Album?.Title);
        Assert.Equal(0, albumLookups);

        // A failing album lookup must not cost the song metadata that was already fetched.
        var failing = new TrackMetadataEnricher(
            (videoId, _) => Task.FromResult<Song?>(new Song
            {
                Id = videoId,
                VideoId = videoId,
                Title = "Track",
                Album = new Album { Id = "MPREb_x", Title = string.Empty },
            }),
            (_, _) => throw new InvalidOperationException("offline"));

        Song? degraded = await failing.FetchAsync("proto1");

        Assert.Equal("Track", degraded?.Title);
        Assert.Equal("MPREb_x", degraded?.Album?.Id);
    }

    [Fact]
    public async Task Enricher_ReturnsNull_WhenTheSongLookupFails()
    {
        var enricher = new TrackMetadataEnricher((_, _) => throw new InvalidOperationException("offline"));

        Assert.Null(await enricher.FetchAsync("proto1"));
    }

    // ── 81 (putaran 6): a fired sleep timer must not let the queue march on ──────────

    [Fact]
    public async Task SleepTimerStop_IgnoresForeignReports_SoTheQueueDoesNotMarchOn()
    {
        var queue = new QueueService(bound => 0);
        var controller = new GatedPlaybackController();
        var timer = new SleepTimer();
        var player = new PlayerService(queue, controller, new FakeJsBridge(), sleepTimer: timer);

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));

        timer.StartEndOfTrack();
        await player.HandleTrackEndedAsync("q0");
        Assert.False(player.IsPlaying);

        int tracksAfterStop = queue.Tracks.Count;

        // What the page really does after the stop: it walks its own autoplay chain, and every pause
        // Kaset sends comes back as a *paused* report naming whichever video it had already moved to.
        // These used to be adopted — one appended track and one index move each — so a night's sleep
        // ended with the queue tens of songs away from where the user left it.
        for (int i = 0; i < 20; i++)
        {
            player.HandleStateUpdate(Report($"autoplay{i}", $"Autoplay {i}", playing: false));
        }

        Assert.Equal(tracksAfterStop, queue.Tracks.Count);
        Assert.Equal("q0", queue.CurrentTrack?.VideoId);
        Assert.Equal("q0", player.CurrentTrack?.VideoId);
    }

    [Fact]
    public async Task SleepTimerStop_StillFollowsReportsForTheStoppedTrack()
    {
        var queue = new QueueService(bound => 0);
        var timer = new SleepTimer();
        var player = new PlayerService(
            queue, new GatedPlaybackController(), new FakeJsBridge(), sleepTimer: timer);

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));
        timer.StartEndOfTrack();
        await player.HandleTrackEndedAsync("q0");

        // Suppression is aimed at where YouTube wandered off to, not at the track itself: a paused
        // tick for the stopped track must still be honoured, or the UI freezes on a stale position.
        player.HandleStateUpdate(new PlaybackStateMessage(
            false, 42, 100, "q0", "Song 0", string.Empty, true, null, null));

        Assert.Equal(42, player.Progress);
        Assert.Equal("q0", player.CurrentTrack?.VideoId);
    }

    [Fact]
    public async Task PressingPlayAfterASleepStop_LetsTheQueueFollowAgain()
    {
        var queue = new QueueService(bound => 0);
        var timer = new SleepTimer();
        var player = new PlayerService(
            queue, new GatedPlaybackController(), new FakeJsBridge(), sleepTimer: timer);

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));
        timer.StartEndOfTrack();
        await player.HandleTrackEndedAsync("q0");

        // The stop lasts until the user asks for playback again — and then normal autoplay adoption
        // has to come back, otherwise the suppression would outlive the reason for it.
        await player.TogglePlayPauseAsync();
        player.HandleStateUpdate(Report("elsewhere", "Elsewhere"));

        Assert.Equal("elsewhere", player.CurrentTrack?.VideoId);
    }

    // ── 148 (putaran 8): play after a sleep stop must resume the stopped track ───────

    [Fact]
    public async Task PlayAfterASleepStop_ReloadsTheStoppedTrack_WhenThePageWanderedOff()
    {
        var queue = new QueueService(bound => 0);
        var controller = new GatedPlaybackController();
        var timer = new SleepTimer();
        var player = new PlayerService(queue, controller, new FakeJsBridge(), sleepTimer: timer);

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));
        timer.StartEndOfTrack();
        await player.HandleTrackEndedAsync("q0");

        // The page walks its autoplay chain under the stop; the reports are suppressed, but the
        // page itself is now far away. A bare play() here resumed "autoplay7" — the queue survived
        // the night only to be thrown away at the first keypress.
        for (int i = 0; i < 8; i++)
        {
            player.HandleStateUpdate(Report($"autoplay{i}", $"Autoplay {i}", playing: false));
        }

        await player.TogglePlayPauseAsync();

        Assert.Equal("q0", controller.CurrentVideoId);
        Assert.Equal("q0", player.CurrentTrack?.VideoId);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public async Task PlayAfterASleepStop_JustResumes_WhenThePageStayedPut()
    {
        var queue = new QueueService(bound => 0);
        var controller = new GatedPlaybackController();
        var timer = new SleepTimer();
        var player = new PlayerService(queue, controller, new FakeJsBridge(), sleepTimer: timer);

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);
        player.HandleStateUpdate(Report("q0", "Song 0"));
        int loadsBeforeStop = controller.LoadedVideoIds.Count;
        timer.StartEndOfTrack();
        await player.HandleTrackEndedAsync("q0");

        // Only paused ticks for the SAME track — no drift. Reloading here would restart the song
        // from 0:00 when a plain resume was all that was asked for.
        player.HandleStateUpdate(Report("q0", "Song 0", playing: false));
        await player.TogglePlayPauseAsync();

        Assert.Equal(loadsBeforeStop, controller.LoadedVideoIds.Count);
        Assert.True(player.IsPlaying);
    }

    // ── 151 (putaran 8): an adopted autoplay track must get enriched too ─────────────

    [Fact]
    public async Task AdoptedAutoplayTrack_GetsEnriched_WithoutAManualPrevNext()
    {
        var queue = new QueueService(bound => 0);
        var requested = new List<string>();
        var enriched = new Song
        {
            Id = "auto1",
            VideoId = "auto1",
            Title = "Autoplay Pick",
            Album = new Album { Id = "MPREb_a", Title = "Their Album" },
        };
        var player = new PlayerService(
            queue,
            new FakePlaybackController(),
            new FakeJsBridge(),
            metadataFetcher: (id, _) =>
            {
                requested.Add(id);
                return Task.FromResult<Song?>(enriched);
            });

        // A Home-card start: one song, already carrying its album, so no enrichment yet.
        await player.PlayCollectionAsync(
            [new Song { Id = "seed", VideoId = "seed", Title = "Seed", Album = new Album { Id = "x", Title = "X" } }],
            startIndex: 0);
        player.HandleStateUpdate(Report("seed", "Seed"));
        requested.Clear();

        // YouTube autoplays past the queue. This track never passes through LoadTrackAsync, which
        // is where enrichment used to live — so it played to the end with no album line.
        player.HandleStateUpdate(Report("auto1", "Autoplay Pick"));
        await WaitUntilAsync(() => queue.CurrentTrack?.Album is not null);

        Assert.Equal(["auto1"], requested);
        Assert.Equal("Their Album", queue.CurrentTrack?.Album?.Title);

        // The once-a-second tick stream must not re-request it.
        player.HandleStateUpdate(Report("auto1", "Autoplay Pick"));
        player.HandleStateUpdate(Report("auto1", "Autoplay Pick"));
        Assert.Equal(["auto1"], requested);
    }

    // ── 169 (putaran 9): album art must beat a video-frame thumbnail, never lose to it ─

    [Fact]
    public void Enrichment_UpgradesAVideoFrameThumbnail_ToRealArtwork()
    {
        var queue = new QueueService(bound => 0);
        var frame = new Uri("https://i.ytimg.com/vi/BEPSc8q6Bd8/hqdefault.jpg");
        var art = new Uri("https://yt3.googleusercontent.com/abc=w544-h544-l90-rj");
        queue.SetQueue([new Song { Id = "v1", VideoId = "v1", Title = "T", ThumbnailUrl = frame }], 0);

        // The page's 16:9 still arrived first and used to win forever; the square art from
        // metadata enrichment was discarded and the now-playing card showed a cropped video frame.
        Assert.True(queue.TryEnrichTrack("v1", new Song { Id = "v1", VideoId = "v1", Title = "T", ThumbnailUrl = art }));
        Assert.Equal(art, queue.CurrentTrack?.ThumbnailUrl);
    }

    [Fact]
    public void Enrichment_NeverDowngradesArtwork_ToAVideoFrame()
    {
        var queue = new QueueService(bound => 0);
        var art = new Uri("https://yt3.googleusercontent.com/abc=w544-h544-l90-rj");
        var frame = new Uri("https://i.ytimg.com/vi/BEPSc8q6Bd8/hqdefault.jpg");
        queue.SetQueue([new Song { Id = "v1", VideoId = "v1", Title = "T", ThumbnailUrl = art }], 0);

        queue.TryEnrichTrack("v1", new Song { Id = "v1", VideoId = "v1", Title = "T", ThumbnailUrl = frame });
        Assert.Equal(art, queue.CurrentTrack?.ThumbnailUrl);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>Polls <paramref name="condition"/> briefly; fails the test if it never holds.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The awaited condition never became true.");
    }

    /// <summary>
    /// A playback controller whose loads can be held open, so several <c>LoadTrackAsync</c> calls
    /// are genuinely in flight at the same time.
    /// </summary>
    private sealed class GatedPlaybackController : IPlaybackController
    {
        private readonly List<TaskCompletionSource> _held = [];

        public List<string> LoadedVideoIds { get; } = [];

        /// <summary>When set, a load of this videoId throws once it is released.</summary>
        public string? FailingVideoId { get; init; }

        /// <summary>While true, every real load stays pending until <see cref="ReleaseHeldLoads"/>.</summary>
        public bool HoldLoads { get; set; }

        public bool IsDrmAvailable => true;

        public string? CurrentVideoId { get; private set; }

        public Task EnsureInitializedAsync() => Task.CompletedTask;

        public Task LoadVideoAsync(string videoId, bool forceReload = false)
        {
            if (!forceReload && string.Equals(videoId, CurrentVideoId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            LoadedVideoIds.Add(videoId);
            bool fails = string.Equals(videoId, FailingVideoId, StringComparison.Ordinal);
            if (!fails)
            {
                CurrentVideoId = videoId;
            }

            if (!HoldLoads)
            {
                return fails
                    ? Task.FromException(new InvalidOperationException("load failed"))
                    : Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _held.Add(tcs);
            return fails
                ? tcs.Task.ContinueWith(
                    _ => throw new InvalidOperationException("load failed"),
                    TaskScheduler.Default)
                : tcs.Task;
        }

        /// <summary>Completes every held load, in the order they were requested.</summary>
        public void ReleaseHeldLoads()
        {
            HoldLoads = false;
            foreach (TaskCompletionSource tcs in _held)
            {
                tcs.TrySetResult();
            }

            _held.Clear();
        }

        public Task PlayAsync() => Task.CompletedTask;

        public Task PauseAsync() => Task.CompletedTask;

        public Task SkipToNextAsync() => Task.CompletedTask;

        public Task SkipToPreviousAsync() => Task.CompletedTask;

        public Task SeekAsync(double positionSeconds) => Task.CompletedTask;

        public Task SetVolumeAsync(int volume0to100) => Task.CompletedTask;

        public Task SetMutedAsync(bool muted) => Task.CompletedTask;

        public Task SetAudioQualityAsync(AudioQuality quality) => Task.CompletedTask;

        public Task SetEqualizerAsync(bool enabled, IReadOnlyList<int> gainsDb) => Task.CompletedTask;

        public Task SetPlaybackRateAsync(double rate) => Task.CompletedTask;

        public Task SetRepeatOneAsync(bool enabled) => Task.CompletedTask;

        public Task SetDisplayModeAsync(PlaybackDisplayMode mode) => Task.CompletedTask;

        public Task ReleaseAsync() => Task.CompletedTask;
    }
}
