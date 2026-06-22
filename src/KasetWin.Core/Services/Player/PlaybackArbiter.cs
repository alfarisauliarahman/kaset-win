using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// The audio source that most recently started playing. Media keys route to whichever source
/// played last (Req 32.3).
/// </summary>
public enum AppSource
{
    /// <summary>The YouTube Music player.</summary>
    Music,

    /// <summary>The regular-YouTube (video) player.</summary>
    Video,
}

/// <summary>
/// Ensures exactly one audio source plays at a time (Req 32.3): when the YouTube video player
/// starts, music is paused, and when music starts, the video is paused. Faithful port of the macOS
/// <c>PlaybackArbiter</c> (see <c>docs/youtube.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// The arbiter subscribes to each source's <see cref="IPausableAudioSource.PlaybackStarted"/> event
/// and pauses the competing source. The music path is not modified beyond the additive
/// <c>IPlayerService.PauseAsync</c> seam; the adapters raise <c>PlaybackStarted</c> on a
/// paused→playing transition.
/// </para>
/// <para>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency so the single-audio-source invariant
/// is headless-testable against fake sources (Design: "mode YouTube … arbiter audio").
/// </para>
/// </remarks>
public sealed class PlaybackArbiter : ObservableObject, IDisposable
{
    private readonly IPausableAudioSource _music;
    private readonly IPausableAudioSource _video;
    private readonly ILogger<PlaybackArbiter> _logger;

    private readonly EventHandler _musicStartedHandler;
    private readonly EventHandler _videoStartedHandler;

    private AppSource _activeSource = AppSource.Music;

    /// <summary>
    /// Wires the arbiter to the music and video sources and begins enforcing a single audio source.
    /// </summary>
    /// <param name="music">The music player audio source (adapter over <c>IPlayerService</c>).</param>
    /// <param name="video">The YouTube video player audio source.</param>
    /// <param name="logger">Optional structured logger (secrets are never logged).</param>
    public PlaybackArbiter(
        IPausableAudioSource music,
        IPausableAudioSource video,
        ILogger<PlaybackArbiter>? logger = null)
    {
        _music = music ?? throw new ArgumentNullException(nameof(music));
        _video = video ?? throw new ArgumentNullException(nameof(video));
        _logger = logger ?? NullLogger<PlaybackArbiter>.Instance;

        _musicStartedHandler = (_, _) => MusicDidStartPlaying();
        _videoStartedHandler = (_, _) => _ = VideoWillStartPlayingAsync();

        _music.PlaybackStarted += _musicStartedHandler;
        _video.PlaybackStarted += _videoStartedHandler;
    }

    /// <summary>The source that most recently started playback. Media keys route here (Req 32.3).</summary>
    public AppSource ActiveSource
    {
        get => _activeSource;
        private set => SetProperty(ref _activeSource, value);
    }

    /// <summary>Whether media keys should currently control the YouTube video player (Req 32.3).</summary>
    public bool RoutesMediaKeysToVideo => ActiveSource == AppSource.Video;

    /// <summary>
    /// Video playback is about to start — make video the active source and pause music if it is
    /// playing (Req 32.3). Safe to call repeatedly; pausing already-paused music is a no-op.
    /// </summary>
    public async Task VideoWillStartPlayingAsync()
    {
        ActiveSource = AppSource.Video;

        if (_music.IsPlaying)
        {
            _logger.LogInformation("Arbiter: pausing music for video playback");
            await _music.PauseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Music playback started — make music the active source and pause the video if it is playing
    /// (Req 32.3). Idempotent when music is already the active source and the video is paused.
    /// </summary>
    public void MusicDidStartPlaying()
    {
        if (ActiveSource == AppSource.Music && !_video.IsPlaying)
        {
            return;
        }

        ActiveSource = AppSource.Music;

        if (_video.IsPlaying)
        {
            _logger.LogInformation("Arbiter: pausing video for music playback");
            _ = _video.PauseAsync();
        }
    }

    /// <summary>Unsubscribes from the audio sources' events.</summary>
    public void Dispose()
    {
        _music.PlaybackStarted -= _musicStartedHandler;
        _video.PlaybackStarted -= _videoStartedHandler;
    }
}
