using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Observable implementation of <see cref="IPlayerService"/> (Task 11.1). Coordinates the
/// <see cref="IQueueService"/> (queue source of truth) and the <see cref="IPlaybackController"/>
/// (hidden WebView2), and consumes <see cref="IJsBridge"/> messages to keep Kaset's queue
/// authoritative over YouTube autoplay (Req 2). Faithful port of the macOS <c>PlayerService</c>
/// and <c>PlayerService+WebQueueSync</c>.
/// </summary>
/// <remarks>
/// All queue-authority decisions are delegated to the pure <see cref="WebQueueSync"/> helper so
/// they can be exercised headless (Properties 6, 7, 8). The service holds no WinUI/WinRT
/// dependency and runs against fake controllers/bridges in tests.
/// </remarks>
public sealed class PlayerService : ObservableObject, IPlayerService, IDisposable
{
    private const int DefaultVolume = 100;

    private readonly IQueueService _queue;
    private readonly IPlaybackController _controller;
    private readonly IJsBridge _bridge;

    /// <summary>
    /// Optional infinite-mix driver (Req 25). When present, regular and song-radio playback reset
    /// its token and queue advances trigger continuation top-ups; <c>null</c> disables the mix flow.
    /// </summary>
    private readonly InfiniteMixCoordinator? _mixCoordinator;

    /// <summary>
    /// Optional metadata fetcher (videoId → enriched <see cref="Song"/>). When present, a track loaded
    /// without its album is enriched in the background so the now-playing UI always shows the album and
    /// "Go to album" works, regardless of which surface playback started from. <c>null</c> disables it.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<Song?>>? _metadataFetcher;

    private readonly EventHandler<PlaybackStateMessage> _stateHandler;
    private readonly EventHandler<TrackEndedMessage> _trackEndedHandler;

    /// <summary>
    /// The UI <see cref="SynchronizationContext"/> captured at construction. The service is first
    /// resolved on the UI thread via DI (from <c>MainWindow</c>), so this is the WinUI dispatcher
    /// context. <c>PropertyChanged</c> is marshalled back onto it (see <see cref="OnPropertyChanged"/>)
    /// so bound XAML never observes a mutation on a thread-pool thread (the root cause of the
    /// <c>0xc000027b</c> XAML stowed exception when a track is loaded after <c>ConfigureAwait(false)</c>).
    /// <c>null</c> in headless unit tests (no <see cref="SynchronizationContext"/>), where marshalling
    /// is a no-op and notifications are raised inline exactly as before.
    /// </summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>
    /// The videoId Kaset is deliberately loading right now, set for the duration of
    /// <see cref="LoadTrackAsync"/> (and the repeat-one replay). While it is set, a STATE_UPDATE that
    /// reports a <em>different</em> videoId is ignored: the music host is headless, so a foreign id
    /// arriving mid-load is YouTube autoplay / transient drift bleeding in, and adopting it would yank
    /// the now-playing UI (and the queue) to an unrelated track before snapping back once our track
    /// settles — the "queue jumps around when changing tracks in an album/playlist" bug. Cleared once
    /// the expected id is observed, so genuine autoplay drift after playback has settled is still
    /// adopted (Req 2.6).
    ///
    /// The release point matters: the guard is dropped when the expected videoId is actually
    /// <em>observed</em>, not when <see cref="IPlaybackController.LoadVideoAsync"/> returns. That call
    /// only starts the navigation, and YouTube Music keeps reporting the outgoing page for seconds
    /// afterwards. Releasing on return let those reports through, and each unmatched one was appended
    /// to the queue as ephemeral history — the "album queue turns into a mix after pressing Next" bug.
    /// </summary>
    private volatile string? _expectedVideoId;

    /// <summary>
    /// How many consecutive foreign <c>STATE_UPDATE</c>s the load guard has already swallowed.
    /// </summary>
    /// <remarks>
    /// A navigation that never reports its videoId (page error, region block, a pulled video) must not
    /// wedge the player into ignoring reality forever, so the guard gives up after
    /// <see cref="MaxIgnoredUpdatesDuringLoad"/> reports and adopts whatever is actually playing.
    /// Counted rather than timed, so the behaviour stays deterministic under test.
    /// </remarks>
    private int _ignoredUpdatesDuringLoad;

    /// <summary>
    /// Serializes <see cref="LoadTrackAsync"/> so two loads cannot interleave their controller
    /// calls. Nothing awaits it while holding another lock, and it is only ever held across the
    /// controller's own asynchronous calls.
    /// </summary>
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    /// <summary>
    /// Monotonic id of the newest requested load. Every <see cref="LoadTrackAsync"/> call takes a
    /// ticket; a call whose ticket is no longer the newest has been superseded and becomes a no-op
    /// (it neither touches the controller nor releases the load guard).
    /// </summary>
    /// <remarks>
    /// Pressing Next several times quickly starts several loads. Without this, the older ones kept
    /// running: their queued controller calls landed <em>after</em> the newest navigation (loading a
    /// track the user had already skipped past, or issuing play() at a moment the page had nothing
    /// to play), and a failing older load cleared <see cref="_expectedVideoId"/> — the guard the
    /// newest load had just armed — so the player ended up with nothing playing at all
    /// (checklist 77b, and the silent media-key skip 88b). Same shape as
    /// <c>LyricsService</c>'s generation counter: newest wins, older ones become no-ops.
    /// </remarks>
    private int _loadGeneration;

    /// <summary>
    /// Foreign reports tolerated before the load guard gives up. The observer ticks roughly once a
    /// second, so this is about half a minute — comfortably longer than a normal navigation, and short
    /// enough that a genuinely failed load recovers on its own.
    /// </summary>
    private const int MaxIgnoredUpdatesDuringLoad = 30;

    private Song? _currentTrack;
    private bool _isPlaying;
    private double _progress;
    private double _duration;
    private int _volume = DefaultVolume;
    private bool _isMuted;
    private bool _isLive;
    private RepeatMode _repeatMode = RepeatMode.Off;
    private bool _isShuffled;
    private AudioQuality _audioQuality = AudioQuality.High;

    /// <summary>Volume captured before muting so it can be restored on unmute (Req 5.6).</summary>
    private int _volumeBeforeMute = DefaultVolume;

    /// <summary>Sleep timer consulted at track end; <c>null</c> when the feature is not wired.</summary>
    private readonly SleepTimer? _sleepTimer;

    /// <summary>
    /// Set when an "end of this track" sleep timer has stopped playback, and cleared by the next
    /// deliberate transport action from the user.
    /// </summary>
    /// <remarks>
    /// A single pause is not enough. YouTube Music reacts to the same <c>ended</c> event Kaset does
    /// and starts the next song itself, so a pause sent at track end can land on the outgoing video
    /// while the page is already bringing up the next one — the timer then looks like it did nothing
    /// (the icon goes out, the music plays on). While this flag is set, any report of playback
    /// running is pushed straight back down to paused, so whatever the page starts is stopped again.
    /// It also suppresses queue adoption, so the now-playing UI stays on the track the user fell
    /// asleep to instead of following YouTube's pick.
    /// </remarks>
    private bool _sleepStopEnforced;

    /// <summary>
    /// The videoId the sleep timer stopped on, while <see cref="_sleepStopEnforced"/> holds.
    /// </summary>
    /// <remarks>
    /// Needed because "suppress queue adoption" cannot be expressed by the flag alone: the page
    /// keeps reporting after the stop, and a report has to be classified as "still the track the
    /// user fell asleep to" or "somewhere YouTube wandered to on its own". Only the second is
    /// ignored, so a genuine progress/pause tick for the stopped track still updates the UI.
    /// </remarks>
    private string? _sleepStoppedVideoId;

    /// <summary>
    /// Creates a player wired to the queue, the playback controller, and the JS bridge. The
    /// constructor subscribes to <see cref="IJsBridge.StateUpdated"/> and
    /// <see cref="IJsBridge.TrackEnded"/>; call <see cref="Dispose"/> to unsubscribe.
    /// </summary>
    /// <param name="queue">The queue source of truth.</param>
    /// <param name="controller">The hidden WebView2 playback controller.</param>
    /// <param name="bridge">The JS bridge raising playback state / track-ended events.</param>
    /// <param name="mixCoordinator">
    /// Optional infinite-mix coordinator (Req 25). When supplied it shares the same
    /// <paramref name="queue"/>; when <c>null</c> the mix flow is inert.
    /// </param>
    /// <param name="sleepTimer">
    /// Optional sleep timer (Req: sleep timer). Consulted at track end so an "end of this track"
    /// timer stops playback instead of advancing; <c>null</c> leaves the flow untouched.
    /// </param>
    public PlayerService(
        IQueueService queue,
        IPlaybackController controller,
        IJsBridge bridge,
        InfiniteMixCoordinator? mixCoordinator = null,
        Func<string, CancellationToken, Task<Song?>>? metadataFetcher = null,
        SleepTimer? sleepTimer = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _mixCoordinator = mixCoordinator;
        _metadataFetcher = metadataFetcher;
        _sleepTimer = sleepTimer;

        _stateHandler = (_, message) => HandleStateUpdate(message);
        _trackEndedHandler = (_, message) => _ = HandleTrackEndedAsync(message.VideoId);

        _bridge.StateUpdated += _stateHandler;
        _bridge.TrackEnded += _trackEndedHandler;
    }

    /// <summary>
    /// Raises <see cref="ObservableObject.PropertyChanged"/> on the captured UI thread. This is the
    /// single funnel CommunityToolkit's <c>SetProperty</c> uses, so overriding it covers every
    /// observable property (<see cref="IsPlaying"/>, <see cref="CurrentTrack"/>, <see cref="Progress"/>,
    /// <see cref="Duration"/>, <see cref="IsLive"/>, …). The play path awaits controller I/O with
    /// <c>ConfigureAwait(false)</c> and then mutates these properties, which would otherwise raise
    /// <c>PropertyChanged</c> on a thread-pool thread and update bound XAML off the UI thread → a
    /// <c>0xc000027b</c> stowed exception / crash. When the current context differs from the captured
    /// UI context we <see cref="SynchronizationContext.Post(SendOrPostCallback, object?)"/> (async, to
    /// avoid deadlocks/reentrancy from a blocking <c>Send</c>); when already on the UI thread, or when
    /// no context was captured (headless tests), we invoke the base implementation inline.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        SynchronizationContext? ui = _uiContext;
        if (ui is not null && !ReferenceEquals(SynchronizationContext.Current, ui))
        {
            ui.Post(_ => RaiseBasePropertyChanged(e), null);
            return;
        }

        base.OnPropertyChanged(e);
    }

    /// <summary>Invokes the base <see cref="ObservableObject.OnPropertyChanged"/> (called on the UI thread).</summary>
    private void RaiseBasePropertyChanged(PropertyChangedEventArgs e) => base.OnPropertyChanged(e);

    /// <inheritdoc />
    public Song? CurrentTrack
    {
        get => _currentTrack;
        private set => SetProperty(ref _currentTrack, value);
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    /// <inheritdoc />
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <inheritdoc />
    public double Duration
    {
        get => _duration;
        private set => SetProperty(ref _duration, value);
    }

    /// <inheritdoc />
    public int Volume
    {
        get => _volume;
        private set => SetProperty(ref _volume, value);
    }

    /// <inheritdoc />
    public bool IsMuted
    {
        get => _isMuted;
        private set => SetProperty(ref _isMuted, value);
    }

    /// <inheritdoc />
    public bool IsLive
    {
        get => _isLive;
        private set => SetProperty(ref _isLive, value);
    }

    /// <inheritdoc />
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        private set => SetProperty(ref _repeatMode, value);
    }

    /// <inheritdoc />
    public bool IsShuffled
    {
        get => _isShuffled;
        private set => SetProperty(ref _isShuffled, value);
    }

    /// <inheritdoc />
    public AudioQuality AudioQuality
    {
        get => _audioQuality;
        private set => SetProperty(ref _audioQuality, value);
    }

    /// <inheritdoc />
    public Task PlayAsync(string videoId)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var song = new Song { Id = videoId, VideoId = videoId, Title = string.Empty };
        return PlaySongAsync(song);
    }

    /// <inheritdoc />
    public Task PlaySongAsync(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return PlayCollectionAsync([song], 0);
    }

    /// <inheritdoc />
    public async Task PlayCollectionAsync(IReadOnlyList<Song> songs, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(songs);
        if (songs.Count == 0)
        {
            return;
        }

        // The queue is the source of truth (Req 6.5, 8.1-8.3); SetQueue clamps the index.
        _queue.SetQueue(songs, startIndex);

        // A regular collection is not a mix — clear any pending mix continuation (Req 25.4).
        _mixCoordinator?.OnRegularQueueStarted();

        Song? track = _queue.CurrentTrack;
        if (track is not null)
        {
            // The user picked this track: reload it even when it is the one already loaded, so a
            // page that died (network drop) is replaced instead of the request being swallowed.
            await LoadTrackAsync(track, forceReload: true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts an infinite mix from an initial <c>next</c> result (Req 25.1): seeds the queue and
    /// stores the continuation token, then plays from <paramref name="startIndex"/>. Subsequent
    /// queue advances top the queue up automatically once it falls to the threshold (Req 25.2).
    /// When no mix coordinator is wired this degrades to plain collection playback.
    /// </summary>
    public async Task PlayMixAsync(RadioQueueResult mix, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(mix);
        if (mix.Songs.Count == 0)
        {
            return;
        }

        if (_mixCoordinator is null)
        {
            await PlayCollectionAsync(mix.Songs, startIndex).ConfigureAwait(false);
            return;
        }

        _mixCoordinator.StartMix(mix, startIndex);

        Song? track = _queue.CurrentTrack;
        if (track is not null)
        {
            await LoadTrackAsync(track, forceReload: true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts a song radio (Req 25.4): seeds the queue from <paramref name="radio"/> and resets
    /// any mix continuation token, since a song radio is not an infinite mix. Plays from
    /// <paramref name="startIndex"/>.
    /// </summary>
    public async Task PlaySongRadioAsync(RadioQueueResult radio, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(radio);
        if (radio.Songs.Count == 0)
        {
            return;
        }

        _queue.SetQueue(radio.Songs, startIndex);
        _mixCoordinator?.OnSongRadioStarted();

        Song? track = _queue.CurrentTrack;
        if (track is not null)
        {
            await LoadTrackAsync(track, forceReload: true).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task TogglePlayPauseAsync()
    {
        // Asking for playback is the user overruling a fired sleep timer.
        ReleaseSleepStop();

        // Involution: two toggles return to the original state (Property 9).
        if (IsPlaying)
        {
            await _controller.PauseAsync().ConfigureAwait(false);
            IsPlaying = false;
        }
        else
        {
            await _controller.PlayAsync().ConfigureAwait(false);
            IsPlaying = true;
        }
    }

    /// <inheritdoc />
    public async Task PauseAsync()
    {
        // Pause-only (never resumes) — used by the playback arbiter to silence music when a YouTube
        // video starts (Req 32.3). Idempotent: a no-op when already paused.
        if (!IsPlaying)
        {
            return;
        }

        await _controller.PauseAsync().ConfigureAwait(false);
        IsPlaying = false;
    }

    /// <inheritdoc />
    public async Task NextAsync()
    {
        // Explicit skip (player-bar / media key): move to the next track even under Repeat One, which
        // only governs auto-advance at track end — otherwise "Next" replays the same song (Req 37.7).
        ReleaseSleepStop();
        Song? next = _queue.AdvanceToNext(ignoreRepeatOne: true);
        if (next is not null)
        {
            await LoadTrackAsync(next).ConfigureAwait(false);
        }
        else
        {
            await _controller.SkipToNextAsync().ConfigureAwait(false);
        }

        await TopUpMixIfNeededAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PreviousAsync()
    {
        ReleaseSleepStop();
        Song? previous = _queue.AdvanceToPrevious();
        if (previous is not null)
        {
            await LoadTrackAsync(previous).ConfigureAwait(false);
        }
        else
        {
            await _controller.SkipToPreviousAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SeekAsync(double seconds)
    {
        // Live streams disable seeking — position is left unchanged (Req 9.2, Property 20).
        if (IsLive)
        {
            return;
        }

        double clamped = ClampSeek(seconds, Duration);
        Progress = clamped;
        await _controller.SeekAsync(clamped).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void SetVolume(int volume)
    {
        int clamped = Math.Clamp(volume, 0, 100);
        Volume = clamped;

        // Changing volume implicitly unmutes (Req 5.5/5.6).
        if (IsMuted)
        {
            IsMuted = false;
            _ = _controller.SetMutedAsync(false);
        }

        _ = _controller.SetVolumeAsync(clamped);
    }

    /// <inheritdoc />
    public void ToggleMute()
    {
        // Involution: two toggles restore the prior volume (Req 5.6, Property 9).
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _volumeBeforeMute;
            _ = _controller.SetMutedAsync(false);
            _ = _controller.SetVolumeAsync(Volume);
        }
        else
        {
            _volumeBeforeMute = Volume;
            IsMuted = true;
            _ = _controller.SetMutedAsync(true);
        }
    }

    /// <inheritdoc />
    public void ToggleShuffle()
    {
        _queue.Shuffle();
        IsShuffled = !IsShuffled;
    }

    /// <inheritdoc />
    public void CycleRepeat()
    {
        RepeatMode next = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off,
        };

        _queue.SetRepeatMode(next);
        RepeatMode = next;

        // Repeat One loops the media element natively (video.loop) so the track repeats seamlessly and
        // YouTube Music's own autoplay can't advance past it; the controller re-applies this after each
        // navigation, so it follows to whatever track is loaded next.
        _ = _controller.SetRepeatOneAsync(next == RepeatMode.One);
    }

    /// <inheritdoc />
    public async Task SetAudioQualityAsync(AudioQuality quality)
    {
        AudioQuality = quality;
        // Re-apply immediately to the running player (Req 7.2).
        await _controller.SetAudioQualityAsync(quality).ConfigureAwait(false);
    }

    /// <summary>
    /// Lifts the sleep-timer stop. Called from every path where the user themselves asked for
    /// playback (play/pause, next, previous, a deliberate load) — the two fields must fall together,
    /// or a stale videoId would keep suppressing reports long after the stop was released.
    /// </summary>
    private void ReleaseSleepStop()
    {
        _sleepStopEnforced = false;
        _sleepStoppedVideoId = null;
    }

    /// <inheritdoc />
    public void SetLive(bool isLive) => IsLive = isLive;

    /// <inheritdoc />
    public async Task HandleTrackEndedAsync(string? observedVideoId)
    {
        string? expected = _queue.CurrentTrack?.VideoId;
        bool hasNext = _queue.PeekNext() is not null;

        TrackEndedAction action = WebQueueSync.ResolveTrackEnded(observedVideoId, expected, hasNext);

        // An "end of this track" sleep timer wins over advancing — but only for a genuine end of the
        // expected track. Checking after WebQueueSync has classified the event keeps a stray
        // TRACK_ENDED for some other video (YouTube autoplay drift) from silently disarming the
        // timer and pausing the wrong thing.
        if (action is TrackEndedAction.AdvanceToNext or TrackEndedAction.EndPlayback
            && _sleepTimer?.NotifyTrackEnded() == true)
        {
            // Pause the controller directly rather than through PauseAsync(): that one returns early
            // when IsPlaying is already false, which it usually is by the time a track-ended event
            // arrives — so the timer silently did nothing at all.
            _sleepStopEnforced = true;
            _sleepStoppedVideoId = expected;
            await _controller.PauseAsync().ConfigureAwait(false);
            IsPlaying = false;
            return;
        }
        switch (action)
        {
            case TrackEndedAction.AdvanceToNext:
                Song? next = _queue.AdvanceToNext();
                if (next is null)
                {
                    break;
                }

                // Repeat-One resolves to the same track: restart it from the beginning rather
                // than relying on a (no-op) reload of the already-loaded video. Guard the replay
                // window so a YouTube-autoplay pick that fired at track end can't be adopted over
                // the repeated track (the "repeat jumps to another song" bug).
                if (string.Equals(next.VideoId, expected, StringComparison.Ordinal))
                {
                    // Take a load ticket for the replay too, so a Next pressed during it wins and
                    // this replay does not clear the guard the newer load armed.
                    int replayGeneration = Interlocked.Increment(ref _loadGeneration);
                    _expectedVideoId = next.VideoId;
                    try
                    {
                        CurrentTrack = next;
                        await _controller.SeekAsync(0).ConfigureAwait(false);
                        await _controller.PlayAsync().ConfigureAwait(false);
                        Progress = 0;
                        IsPlaying = true;
                    }
                    finally
                    {
                        if (!IsSuperseded(replayGeneration))
                        {
                            _expectedVideoId = null;
                        }
                    }
                }
                else
                {
                    await LoadTrackAsync(next).ConfigureAwait(false);
                }

                // Top the queue up while a mix is running (Req 25.2).
                await TopUpMixIfNeededAsync().ConfigureAwait(false);
                break;

            case TrackEndedAction.ReplayExpected:
                // Queue authority: keep/replay the expected track. LoadVideoAsync is idempotent
                // when that track is already loaded, so this safely absorbs stale ended events
                // without advancing the queue (Req 2.4/2.5).
                if (expected is not null)
                {
                    await _controller.LoadVideoAsync(expected).ConfigureAwait(false);
                }

                break;

            case TrackEndedAction.EndPlayback:
                // End of a non-repeating queue — stop rather than inherit YouTube autoplay.
                await _controller.PauseAsync().ConfigureAwait(false);
                IsPlaying = false;
                break;

            case TrackEndedAction.Ignore:
            default:
                break;
        }
    }

    /// <inheritdoc />
    public void HandleStateUpdate(PlaybackStateMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A sleep timer that ended playback outranks whatever the page decides to do next: keep
        // pushing it back to paused until the user themselves asks for playback again.
        if (_sleepStopEnforced)
        {
            if (message.IsPlaying)
            {
                _ = _controller.PauseAsync();
                IsPlaying = false;
                return;
            }

            // Suppressing only the *playing* reports was not enough, and the gap was wide. Each
            // pause we send makes the page report itself paused — on whatever video it had already
            // moved to — and a paused report used to fall straight through to queue adoption below.
            // YouTube walks its autoplay chain, we pause each one, and every one of those pauses
            // appended a track and advanced the index: the queue marched forward by tens of songs
            // while the user was asleep and nothing was audibly playing.
            if (!string.IsNullOrEmpty(message.VideoId)
                && _sleepStoppedVideoId is { } stopped
                && !string.Equals(message.VideoId, stopped, StringComparison.Ordinal))
            {
                return;
            }
        }

        // Queue authority during a load: while Kaset is deliberately loading a track, ignore any
        // STATE_UPDATE that reports a *different* videoId. Such a report is YouTube autoplay or
        // transient drift bleeding in mid-transition; adopting it would jump the now-playing UI and
        // queue to an unrelated track before snapping back once our track settles (see
        // <see cref="_expectedVideoId"/>). Empty reported ids (pure play/pause/progress ticks) always
        // pass through.
        if (!string.IsNullOrEmpty(message.VideoId)
            && _expectedVideoId is { } pending
            && !string.Equals(message.VideoId, pending, StringComparison.Ordinal))
        {
            if (++_ignoredUpdatesDuringLoad < MaxIgnoredUpdatesDuringLoad)
            {
                // Only the first report of a guarded window is logged: the observer ticks about
                // twice a second, and a line per tick would drown the file.
                if (_ignoredUpdatesDuringLoad == 1)
                {
                    Diag.Write($"player guard ignoring reports for videoId={message.VideoId}; expecting {pending}");
                }

                return;
            }

            // The expected track never reported. Stop guarding and adopt what is really playing —
            // being permanently out of step with the page is worse than a queue that drifted once.
            Diag.Write(
                $"player guard GAVE UP after {MaxIgnoredUpdatesDuringLoad} reports; expected {pending} never "
                + $"reported, adopting videoId={message.VideoId}");
            _expectedVideoId = null;
            _ignoredUpdatesDuringLoad = 0;
        }

        IsPlaying = message.IsPlaying;
        Progress = message.Progress;
        Duration = message.Duration;

        if (!string.IsNullOrEmpty(message.VideoId))
        {
            // The deliberately-loaded track has now reported — stop guarding so later genuine drift
            // (autoplay past the queue) is adopted again.
            if (string.Equals(message.VideoId, _expectedVideoId, StringComparison.Ordinal))
            {
                _expectedVideoId = null;
                _ignoredUpdatesDuringLoad = 0;
            }

            // A non-empty reported videoId is authoritative even if the DOM title is stale (Req 2.6).
            // If YouTube Music autoplay moves outside Kaset's native queue, append the reported
            // track as ephemeral history before aligning the index. Without this, transport controls
            // keep acting from the stale album/playlist index while WebView2 plays another song.
            Song resolved = ResolveTrackFromMessage(message);
            if (!_queue.TrySetCurrentByVideoId(message.VideoId))
            {
                _queue.AppendDeduplicated([resolved]);
                _queue.TrySetCurrentByVideoId(message.VideoId);
            }
            else
            {
                // Push what the page reported back into the queue entry. A track queued from
                // nothing but a videoId (`kaset://play?v=…`) is a Song with no title, artist or
                // artwork, and the queue — not CurrentTrack — is what the queue panel renders, so
                // its "Now playing" row stayed blank for the whole song. TryEnrichTrack only fills
                // genuine gaps, so a rich queue entry is left exactly as it is.
                _queue.TryEnrichTrack(message.VideoId, resolved);
            }

            CurrentTrack = resolved;
        }
    }

    /// <summary>
    /// Builds the current track from a state-update message, enriching from the queue when a
    /// track with the same videoId is present (so artists/album/thumbnail survive) while honoring
    /// the reported title when present (Req 2.6, Property 7).
    /// </summary>
    private Song ResolveTrackFromMessage(PlaybackStateMessage message)
    {
        Song? queued = FindQueuedByVideoId(message.VideoId);
        bool hasTitle = !string.IsNullOrEmpty(message.Title);

        if (queued is not null)
        {
            // Keep the rich queue metadata; only override the title when the message has one.
            //
            // Artists are the exception: a track queued without any (a bare `kaset://play?v=…`
            // launch builds a Song from nothing but the videoId) would otherwise show a title with a
            // blank artist line forever, because the queued entry always wins here. The page knows
            // who the artist is, so borrow it — but only to fill a gap, never to overwrite the
            // richer artist objects a real queue entry carries.
            bool hasArtist = !string.IsNullOrWhiteSpace(message.Artist);
            return queued with
            {
                Artists = queued.Artists.Count == 0 && hasArtist
                    ? ArtistsFromFlatLine(message.Artist)
                    : queued.Artists,
                Title = hasTitle ? message.Title : queued.Title,
                ThumbnailUrl = queued.ThumbnailUrl ?? message.ThumbnailUrl ?? queued.FallbackThumbnailUrl,
                HasVideo = message.HasVideo ?? queued.HasVideo,
                VideoType = message.VideoType ?? queued.VideoType,
            };
        }

        // No queue match — synthesize a song from the (authoritative) videoId/title, including the
        // artist the page reported so the now-playing line is not just the bare title.
        return new Song
        {
            Id = message.VideoId,
            VideoId = message.VideoId,
            Title = message.Title,
            Artists = ArtistsFromFlatLine(message.Artist),
            ThumbnailUrl = message.ThumbnailUrl ?? FallbackThumbnailUrl(message.VideoId),
            HasVideo = message.HasVideo,
            VideoType = message.VideoType,
        };
    }

    /// <summary>
    /// Turns the player page's flat artist line ("Tenxi • Anangga • dan Suisei") into artist
    /// entries. The page byline is already bullet/comma separated, and the localized conjunction
    /// ("dan"/"and") rides along on the last item — <see cref="ParsingHelpers.SplitArtistNames"/>
    /// undoes both, and leaves any line it cannot confidently split as a single artist.
    /// Ids stay empty: the page reports no browse target, so none is fabricated.
    /// </summary>
    private static IReadOnlyList<Artist> ArtistsFromFlatLine(string? line)
    {
        var names = ParsingHelpers.SplitArtistNames(line);
        if (names.Count == 0)
        {
            return Array.Empty<Artist>();
        }

        var artists = new Artist[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            artists[i] = new Artist { Id = string.Empty, Name = names[i] };
        }

        return artists;
    }

    private Song? FindQueuedByVideoId(string videoId)
    {
        foreach (Song track in _queue.Tracks)
        {
            if (string.Equals(track.VideoId, videoId, StringComparison.Ordinal))
            {
                return track;
            }
        }

        return null;
    }

    private static Uri FallbackThumbnailUrl(string videoId) => new($"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");

    /// <summary>
    /// Loads a track into the controller and starts playback: sets <see cref="CurrentTrack"/>,
    /// performs pause-before-load via <see cref="IPlaybackController.LoadVideoAsync"/> (Req 1.6),
    /// re-applies the audio-quality preference (Req 7), and resumes playback.
    /// </summary>
    /// <param name="track">The track to load.</param>
    /// <param name="forceReload">
    /// Passed through to <see cref="IPlaybackController.LoadVideoAsync"/>. <c>true</c> for loads the
    /// user asked for explicitly, so re-selecting the track that is already loaded really reloads it
    /// instead of hitting the idempotent no-op (checklist 111b).
    /// </param>
    private async Task LoadTrackAsync(Song track, bool forceReload = false)
    {
        // Take a load ticket. Anything started after this one supersedes it (see _loadGeneration).
        int generation = Interlocked.Increment(ref _loadGeneration);
        Diag.Write($"player load videoId={track.VideoId} force={forceReload} gen={generation}");

        // Guard the load window: transient STATE_UPDATEs for other videos (autoplay/drift) that
        // arrive while the controller is switching tracks must not hijack the queue (see
        // <see cref="_expectedVideoId"/>). The guard stays armed until the new track actually
        // reports — navigation keeps running long after LoadVideoAsync returns — and is bounded by
        // MaxIgnoredUpdatesDuringLoad so a load that never lands cannot wedge the player.
        _expectedVideoId = track.VideoId;
        _ignoredUpdatesDuringLoad = 0;

        // Deliberately loading something overrules a fired sleep timer.
        ReleaseSleepStop();

        CurrentTrack = track;
        // A freshly loaded on-demand track is not live until proven otherwise (set via SetLive).
        IsLive = false;
        Progress = 0;

        // One load at a time: overlapping loads used to interleave their controller calls, so an
        // older navigation could land after a newer one.
        await _loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsSuperseded(generation))
            {
                // A newer load was requested while this one waited — drop it entirely. The newer
                // load owns _expectedVideoId and will drive the controller.
                Diag.Write($"player load videoId={track.VideoId} gen={generation} superseded at=gate");
                return;
            }

            await _controller.LoadVideoAsync(track.VideoId, forceReload).ConfigureAwait(false);
            if (IsSuperseded(generation))
            {
                Diag.Write($"player load videoId={track.VideoId} gen={generation} superseded at=after-navigate");
                return;
            }

            await _controller.SetAudioQualityAsync(_audioQuality).ConfigureAwait(false);
            // A freshly loaded <video> defaults to full volume, so re-apply the user's volume/mute
            // state - otherwise volume jumps back to 100% on every track change.
            await _controller.SetVolumeAsync(_isMuted ? 0 : _volume).ConfigureAwait(false);
            if (IsSuperseded(generation))
            {
                Diag.Write($"player load videoId={track.VideoId} gen={generation} superseded at=before-play");
                return;
            }

            await _controller.PlayAsync().ConfigureAwait(false);
            IsPlaying = true;
            Diag.Write($"player load videoId={track.VideoId} gen={generation} play issued");
        }
        catch (Exception ex)
        {
            // A superseded load's failure is not the current load's problem: swallow it rather than
            // disarming the guard the newer load just armed.
            if (IsSuperseded(generation))
            {
                Diag.Write($"player load videoId={track.VideoId} gen={generation} superseded, error swallowed: {ex.GetType().Name}");
                return;
            }

            Diag.Write($"player load videoId={track.VideoId} gen={generation} FAILED: {ex.GetType().Name}: {ex.Message}");

            // Only a *failed* load releases the guard here. A successful one keeps it armed until
            // HandleStateUpdate sees the track report, which is the whole point of the guard.
            _expectedVideoId = null;
            _ignoredUpdatesDuringLoad = 0;
            throw;
        }
        finally
        {
            _loadGate.Release();
        }

        // Background: pull the album (and any missing metadata) so the now-playing UI is complete even
        // when playback started from a surface whose song lacked it (e.g. a Home card).
        if (_metadataFetcher is not null && track.Album is null && !string.IsNullOrEmpty(track.VideoId))
        {
            _ = EnrichTrackMetadataAsync(track.VideoId);
        }
    }

    /// <summary>
    /// Whether a newer load has been requested since <paramref name="generation"/> was issued.
    /// </summary>
    private bool IsSuperseded(int generation) => Volatile.Read(ref _loadGeneration) != generation;

    /// <summary>
    /// Fetches full metadata for <paramref name="videoId"/> and merges the album/artists/etc. into the
    /// queued track, refreshing <see cref="CurrentTrack"/> when it is still the active one. Best-effort.
    /// </summary>
    private async Task EnrichTrackMetadataAsync(string videoId)
    {
        try
        {
            Song? enriched = await _metadataFetcher!(videoId, CancellationToken.None).ConfigureAwait(false);
            if (enriched is null)
            {
                return;
            }

            if (_queue.TryEnrichTrack(videoId, enriched)
                && string.Equals(CurrentTrack?.VideoId, videoId, StringComparison.Ordinal)
                && _queue.CurrentTrack is { } merged
                && string.Equals(merged.VideoId, videoId, StringComparison.Ordinal))
            {
                CurrentTrack = merged;
            }
        }
        catch
        {
            // Enrichment is a nicety; never let a metadata failure disturb playback.
        }
    }

    /// <summary>
    /// Asks the mix coordinator (when wired) to top the queue up if it has fallen to the
    /// threshold (Req 25.2). A no-op when no mix is active or no coordinator is present.
    /// </summary>
    private Task TopUpMixIfNeededAsync() =>
        _mixCoordinator is null ? Task.CompletedTask : _mixCoordinator.MaybeLoadMoreAsync();

    /// <summary>Clamps a seek position into <c>[0, duration]</c> (Property 11).</summary>
    private static double ClampSeek(double seconds, double duration)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            return 0;
        }

        double upper = duration > 0 ? duration : 0;
        return seconds > upper ? upper : seconds;
    }

    /// <summary>Unsubscribes from the JS bridge events.</summary>
    /// <remarks>
    /// <see cref="_loadGate"/> is deliberately not disposed: a load can still be in flight at
    /// shutdown, and its <c>Release</c> on a disposed semaphore would throw on a fire-and-forget
    /// task. It holds no wait handle (nothing calls the blocking <c>Wait</c>), so the GC reclaims it.
    /// </remarks>
    public void Dispose()
    {
        _bridge.StateUpdated -= _stateHandler;
        _bridge.TrackEnded -= _trackEndedHandler;
    }
}
