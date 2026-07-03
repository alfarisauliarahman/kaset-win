using CsCheck;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests for <see cref="PlayerService"/> and the pure <see cref="WebQueueSync"/>
/// helper (Feature: kaset-winui3, Design Properties 6–11 and 20). All tests run headless:
/// the real <see cref="QueueService"/> (with a deterministic RNG seam) is wired to a
/// <see cref="FakePlaybackController"/> and <see cref="FakeJsBridge"/>, so no WinUI/WinRT
/// runtime is required. Async control methods are pumped synchronously via
/// <c>GetAwaiter().GetResult()</c>; the controller fakes complete synchronously so this never
/// blocks. Each property runs at least 100 CsCheck iterations.
/// </summary>
public class PlayerProperties
{
    // ── shared generators ────────────────────────────────────────────────────────────

    /// <summary>A small set of non-empty videoIds, chosen to force repeats in sequences.</summary>
    private static readonly Gen<string> VideoIdGen =
        Gen.Int[0, 4].Select(i => $"vid{i}");

    /// <summary>A title that is empty roughly a quarter of the time (Req 2.6 authority check).</summary>
    private static readonly Gen<string> TitleGen =
        Gen.Int[0, 3].Select(i => i == 0 ? string.Empty : $"Title{i}");

    /// <summary>An id that may be <c>null</c>, empty, or one of a few values (queue-authority inputs).</summary>
    private static readonly Gen<string?> MaybeIdGen =
        Gen.Int[0, 4].Select(i => i switch
        {
            0 => (string?)null,
            1 => string.Empty,
            2 => "a",
            3 => "b",
            _ => "c",
        });

    private static (PlayerService Player, QueueService Queue, FakePlaybackController Controller, FakeJsBridge Bridge) CreatePlayer()
    {
        // Deterministic RNG seam (identity → no reordering) keeps tests reproducible; the
        // shuffle behaviour itself is covered by QueueService's own tests.
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var bridge = new FakeJsBridge();
        var player = new PlayerService(queue, controller, bridge);
        return (player, queue, controller, bridge);
    }

    private static IReadOnlyList<Song> MakeSongs(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Song
        {
            Id = $"q{i}",
            VideoId = $"q{i}",
            Title = $"Song {i}",
        })];

    // ── Property 6 ───────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 6: Pause-before-load & idempotensi pemuatan video
    // Validates: Requirements 1.6, 1.2
    [Fact]
    public void Property6_LoadVideo_pauses_before_load_and_is_idempotent()
    {
        // For any sequence of LoadVideoAsync calls: a different videoId pauses current playback
        // then loads; the same videoId as currently loaded is a no-op (idempotent). The pure
        // WebQueueSync.ShouldAdoptReportedVideoId predicate must agree with whether a load occurs.
        VideoIdGen.Array[1, 12].Sample(
            sequence =>
            {
                var controller = new FakePlaybackController();
                string? previous = null;
                var expectedLoads = new List<string>();

                foreach (string id in sequence)
                {
                    bool shouldLoad = WebQueueSync.ShouldAdoptReportedVideoId(id, previous);

                    controller.LoadVideoAsync(id).GetAwaiter().GetResult();

                    if (shouldLoad)
                    {
                        expectedLoads.Add(id);
                        previous = id;
                    }

                    // After every call the loaded videoId is authoritative.
                    Assert.Equal(id, controller.CurrentVideoId);
                }

                // Idempotency: only the distinct-consecutive ids were actually (re)loaded.
                Assert.Equal(expectedLoads, controller.LoadedVideoIds);

                // Pause-before-load: every real load is immediately preceded by a pause.
                for (int i = 0; i < controller.Operations.Count; i++)
                {
                    if (controller.Operations[i].StartsWith("load:", StringComparison.Ordinal))
                    {
                        Assert.True(i > 0, "A load was not preceded by a pause.");
                        Assert.Equal("pause", controller.Operations[i - 1]);
                    }
                }
            },
            iter: 100);
    }

    // ── Property 7 ───────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 7: STATE_UPDATE memetakan state player secara setia
    // Validates: Requirements 2.1, 2.2, 2.6
    [Fact]
    public void Property7_StateUpdate_maps_player_state_faithfully()
    {
        // For any valid PlaybackStateMessage with a non-empty videoId, HandleStateUpdate maps
        // IsPlaying/Progress/Duration verbatim and adopts the reported videoId as the current
        // track — even when the title is empty/stale (Req 2.6 authority).
        Gen<PlaybackStateMessage> messageGen =
            from playing in Gen.Bool
            from progress in Gen.Double[0.0, 100_000.0]
            from duration in Gen.Double[0.0, 100_000.0]
            from videoId in VideoIdGen
            from title in TitleGen
            from changed in Gen.Bool
            select new PlaybackStateMessage(playing, progress, duration, videoId, title, string.Empty, changed, null, null);

        messageGen.Sample(
            message =>
            {
                var (player, _, _, _) = CreatePlayer();

                player.HandleStateUpdate(message);

                Assert.Equal(message.IsPlaying, player.IsPlaying);
                Assert.Equal(message.Progress, player.Progress);
                Assert.Equal(message.Duration, player.Duration);

                // New videoId is authoritative regardless of the title.
                Assert.NotNull(player.CurrentTrack);
                Assert.Equal(message.VideoId, player.CurrentTrack!.VideoId);
                Assert.Equal(message.Title, player.CurrentTrack.Title);
            },
            iter: 100);
    }

    // ── Property 8 ───────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 8: Otoritas antrian pada akhir track
    // Validates: Requirements 2.3, 2.4, 2.5
    [Fact]
    public async Task StateUpdate_ForQueuedAutoAdvancedTrack_AlignsQueueIndex()
    {
        var (player, queue, controller, _) = CreatePlayer();

        await player.PlayCollectionAsync(MakeSongs(3), startIndex: 0);

        player.HandleStateUpdate(
            new PlaybackStateMessage(
                IsPlaying: true,
                Progress: 1,
                Duration: 100,
                VideoId: "q1",
                Title: "Song 1",
                Artist: string.Empty,
                TrackChanged: true,
                HasVideo: null,
                VideoType: null));

        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal("q1", player.CurrentTrack?.VideoId);

        await player.NextAsync();

        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal("q2", controller.CurrentVideoId);
        Assert.Equal("q2", player.CurrentTrack?.VideoId);
    }

    [Fact]
    public async Task LoadingNextTrack_ReappliesUserVolume()
    {
        var (player, _, controller, _) = CreatePlayer();

        await player.PlayCollectionAsync(MakeSongs(2), startIndex: 0);
        player.SetVolume(37);

        await player.NextAsync();

        Assert.Equal(37, player.Volume);
        Assert.Equal(37, controller.Volume);
    }

    [Fact]
    public async Task StateUpdate_ForAutoplayTrack_AppendsHistoryAndAllowsPrevious()
    {
        var (player, queue, controller, _) = CreatePlayer();

        await player.PlayCollectionAsync(MakeSongs(2), startIndex: 1);

        player.HandleStateUpdate(
            new PlaybackStateMessage(
                IsPlaying: true,
                Progress: 1,
                Duration: 100,
                VideoId: "auto1",
                Title: "Autoplay 1",
                Artist: "Auto Artist",
                TrackChanged: true,
                HasVideo: null,
                VideoType: null,
                ThumbnailUrl: new Uri("https://example.invalid/auto.jpg")));

        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal(3, queue.Tracks.Count);
        Assert.Equal("auto1", player.CurrentTrack?.VideoId);
        Assert.Equal(new Uri("https://example.invalid/auto.jpg"), player.CurrentTrack?.ThumbnailUrl);

        await player.PreviousAsync();

        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal("q1", controller.CurrentVideoId);
        Assert.Equal("q1", player.CurrentTrack?.VideoId);
    }

    [Fact]
    public async Task NextAtEnd_DelegatesToWebViewAutoplayChain()
    {
        var (player, queue, controller, _) = CreatePlayer();

        await player.PlayCollectionAsync(MakeSongs(1), startIndex: 0);

        await player.NextAsync();

        Assert.Equal(0, queue.CurrentIndex);
        Assert.Contains("web-next", controller.Operations);
    }

    [Fact]
    public void Property8_QueueAuthority_resolves_track_ended_correctly()
    {
        // For any (observed, expected, hasNext): the queue only advances when the observed id
        // matches the expected current track; a real mismatch replays the expected track; an
        // empty expected track is ignored.
        Gen.Select(MaybeIdGen, MaybeIdGen, Gen.Bool).Sample(
            t =>
            {
                var (observed, expected, hasNext) = t;

                TrackEndedAction action = WebQueueSync.ResolveTrackEnded(observed, expected, hasNext);

                if (string.IsNullOrEmpty(expected))
                {
                    // No expected track → nothing the queue can authoritatively do.
                    Assert.Equal(TrackEndedAction.Ignore, action);
                    return;
                }

                bool matches = string.IsNullOrEmpty(observed)
                    || string.Equals(observed, expected, StringComparison.Ordinal);

                if (!matches)
                {
                    // Drifted / stale ended event → keep the expected track, never advance.
                    Assert.Equal(TrackEndedAction.ReplayExpected, action);
                }
                else
                {
                    Assert.Equal(
                        hasNext ? TrackEndedAction.AdvanceToNext : TrackEndedAction.EndPlayback,
                        action);
                }
            },
            iter: 100);
    }

    // ── Property 9 ───────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 9: Toggle play/pause dan mute adalah involusi
    // Validates: Requirements 5.1, 5.6
    [Fact]
    public void Property9_Toggle_play_pause_and_mute_are_involutions()
    {
        // TogglePlayPause twice restores the original playing state.
        Gen.Bool.Sample(
            initialPlaying =>
            {
                var (player, _, _, _) = CreatePlayer();

                // Seed the playing state via a STATE_UPDATE with an empty videoId (no track change).
                player.HandleStateUpdate(
                    new PlaybackStateMessage(initialPlaying, 0.0, 0.0, string.Empty, string.Empty, string.Empty, false, null, null));
                bool original = player.IsPlaying;

                player.TogglePlayPauseAsync().GetAwaiter().GetResult();
                player.TogglePlayPauseAsync().GetAwaiter().GetResult();

                Assert.Equal(original, player.IsPlaying);
            },
            iter: 100);

        // ToggleMute twice (with a positive volume) restores the volume level and unmutes.
        Gen.Int[1, 100].Sample(
            volume =>
            {
                var (player, _, _, _) = CreatePlayer();
                player.SetVolume(volume);
                int original = player.Volume;

                player.ToggleMute();
                player.ToggleMute();

                Assert.Equal(original, player.Volume);
                Assert.False(player.IsMuted);
            },
            iter: 100);
    }

    // ── Property 10 ──────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 10: Next lalu Previous adalah round-trip di tengah antrian
    // Validates: Requirements 5.2, 5.3
    [Fact]
    public void Property10_Next_then_Previous_round_trips_in_middle_of_queue()
    {
        // For any queue and a non-boundary current index (RepeatMode.Off), NextAsync followed by
        // PreviousAsync returns the current index to where it started.
        Gen<(int Count, int Index)> gen =
            from count in Gen.Int[3, 10]
            from index in Gen.Int[1, count - 2]
            select (count, index);

        gen.Sample(
            t =>
            {
                var (count, index) = t;
                var (player, queue, _, _) = CreatePlayer();

                player.PlayCollectionAsync(MakeSongs(count), index).GetAwaiter().GetResult();
                Assert.Equal(index, queue.CurrentIndex);

                player.NextAsync().GetAwaiter().GetResult();
                player.PreviousAsync().GetAwaiter().GetResult();

                Assert.Equal(index, queue.CurrentIndex);
            },
            iter: 100);
    }

    // ── Property 11 ──────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 11: Clamp seek dan volume
    // Validates: Requirements 5.4, 5.5
    [Fact]
    public void Property11_Seek_and_volume_are_clamped()
    {
        // For any seek position the applied progress is clamped to [0, Duration]; for any volume
        // the stored volume is clamped to [0, 100]. Inputs deliberately include negatives / overshoot.
        Gen<(double Duration, double SeekPos, int Volume)> gen =
            from duration in Gen.Double[0.0, 10_000.0]
            from seekPos in Gen.Double[-10_000.0, 20_000.0]
            from volume in Gen.Int[-1_000, 1_000]
            select (duration, seekPos, volume);

        gen.Sample(
            t =>
            {
                var (duration, seekPos, volume) = t;
                var (player, _, _, _) = CreatePlayer();

                // Establish a non-live track with a known duration (no SetLive → IsLive stays false).
                player.HandleStateUpdate(
                    new PlaybackStateMessage(true, 0.0, duration, "vidX", "T", string.Empty, true, null, null));

                player.SeekAsync(seekPos).GetAwaiter().GetResult();
                double expectedProgress = seekPos < 0 ? 0.0 : (seekPos > duration ? duration : seekPos);
                Assert.Equal(expectedProgress, player.Progress);
                Assert.InRange(player.Progress, 0.0, duration);

                player.SetVolume(volume);
                Assert.Equal(Math.Clamp(volume, 0, 100), player.Volume);
                Assert.InRange(player.Volume, 0, 100);
            },
            iter: 100);
    }

    // ── Property 20 ──────────────────────────────────────────────────────────────────

    // Feature: kaset-winui3, Property 20: Live menonaktifkan seek
    // Validates: Requirements 9.2
    [Fact]
    public void Property20_Live_disables_seek()
    {
        // For any seek position, when IsLive is true SeekAsync leaves Progress unchanged.
        Gen<(double InitialProgress, double Duration, double SeekPos)> gen =
            from initial in Gen.Double[0.0, 10_000.0]
            from duration in Gen.Double[0.0, 10_000.0]
            from seekPos in Gen.Double[-10_000.0, 20_000.0]
            select (initial, duration, seekPos);

        gen.Sample(
            t =>
            {
                var (initialProgress, duration, seekPos) = t;
                var (player, _, _, _) = CreatePlayer();

                player.HandleStateUpdate(
                    new PlaybackStateMessage(true, initialProgress, duration, "vidLive", "Live", string.Empty, true, null, null));
                player.SetLive(true);

                double before = player.Progress;
                player.SeekAsync(seekPos).GetAwaiter().GetResult();

                Assert.Equal(before, player.Progress);
            },
            iter: 100);
    }
}
