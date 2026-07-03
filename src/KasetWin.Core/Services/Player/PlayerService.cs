using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;

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
    public PlayerService(
        IQueueService queue,
        IPlaybackController controller,
        IJsBridge bridge,
        InfiniteMixCoordinator? mixCoordinator = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _mixCoordinator = mixCoordinator;

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

        KasetWin.Core.Diagnostics.KasetTrace.Log("BugA:PlayerService.PlayCollectionAsync.enter", $"count={songs.Count} start={startIndex}");

        // The queue is the source of truth (Req 6.5, 8.1–8.3); SetQueue clamps the index.
        _queue.SetQueue(songs, startIndex);

        // A regular collection is not a mix — clear any pending mix continuation (Req 25.4).
        _mixCoordinator?.OnRegularQueueStarted();

        Song? track = _queue.CurrentTrack;
        if (track is not null)
        {
            await LoadTrackAsync(track).ConfigureAwait(false);
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
            await LoadTrackAsync(track).ConfigureAwait(false);
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
            await LoadTrackAsync(track).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task TogglePlayPauseAsync()
    {
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
        Song? next = _queue.AdvanceToNext(ignoreRepeatOne: true);
        if (next is not null)
        {
            await LoadTrackAsync(next).ConfigureAwait(false);
        }

        await TopUpMixIfNeededAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PreviousAsync()
    {
        Song? previous = _queue.AdvanceToPrevious();
        if (previous is not null)
        {
            await LoadTrackAsync(previous).ConfigureAwait(false);
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
    }

    /// <inheritdoc />
    public async Task SetAudioQualityAsync(AudioQuality quality)
    {
        AudioQuality = quality;
        // Re-apply immediately to the running player (Req 7.2).
        await _controller.SetAudioQualityAsync(quality).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void SetLive(bool isLive) => IsLive = isLive;

    /// <inheritdoc />
    public async Task HandleTrackEndedAsync(string? observedVideoId)
    {
        string? expected = _queue.CurrentTrack?.VideoId;
        bool hasNext = _queue.PeekNext() is not null;

        TrackEndedAction action = WebQueueSync.ResolveTrackEnded(observedVideoId, expected, hasNext);
        switch (action)
        {
            case TrackEndedAction.AdvanceToNext:
                Song? next = _queue.AdvanceToNext();
                if (next is null)
                {
                    break;
                }

                // Repeat-One resolves to the same track: restart it from the beginning rather
                // than relying on a (no-op) reload of the already-loaded video.
                if (string.Equals(next.VideoId, expected, StringComparison.Ordinal))
                {
                    CurrentTrack = next;
                    await _controller.SeekAsync(0).ConfigureAwait(false);
                    await _controller.PlayAsync().ConfigureAwait(false);
                    Progress = 0;
                    IsPlaying = true;
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

        IsPlaying = message.IsPlaying;
        Progress = message.Progress;
        Duration = message.Duration;

        // A non-empty reported videoId is authoritative even if the DOM title is stale (Req 2.6).
        if (!string.IsNullOrEmpty(message.VideoId))
        {
            CurrentTrack = ResolveTrackFromMessage(message);
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
            return hasTitle && !string.Equals(queued.Title, message.Title, StringComparison.Ordinal)
                ? queued with { Title = message.Title }
                : queued;
        }

        // No queue match — synthesize a minimal song from the (authoritative) videoId/title.
        return new Song
        {
            Id = message.VideoId,
            VideoId = message.VideoId,
            Title = message.Title,
            HasVideo = message.HasVideo,
            VideoType = message.VideoType,
        };
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

    /// <summary>
    /// Loads a track into the controller and starts playback: sets <see cref="CurrentTrack"/>,
    /// performs pause-before-load via <see cref="IPlaybackController.LoadVideoAsync"/> (Req 1.6),
    /// re-applies the audio-quality preference (Req 7), and resumes playback.
    /// </summary>
    private async Task LoadTrackAsync(Song track)
    {
        CurrentTrack = track;
        // A freshly loaded on-demand track is not live until proven otherwise (set via SetLive).
        IsLive = false;
        Progress = 0;

        KasetWin.Core.Diagnostics.KasetTrace.Log("BugA:PlayerService.LoadTrackAsync.loadVideo.start");
        var __lt = KasetWin.Core.Diagnostics.KasetTrace.Now();
        await _controller.LoadVideoAsync(track.VideoId).ConfigureAwait(false);
        KasetWin.Core.Diagnostics.KasetTrace.LogSince("BugA:PlayerService.LoadTrackAsync.loadVideo.end", __lt);
        await _controller.SetAudioQualityAsync(_audioQuality).ConfigureAwait(false);
        await _controller.PlayAsync().ConfigureAwait(false);
        IsPlaying = true;
        KasetWin.Core.Diagnostics.KasetTrace.Log("BugA:PlayerService.LoadTrackAsync.play.done");
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
    public void Dispose()
    {
        _bridge.StateUpdated -= _stateHandler;
        _bridge.TrackEnded -= _trackEndedHandler;
    }
}
