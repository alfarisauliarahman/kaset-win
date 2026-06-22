using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Player service for the regular-YouTube (video) experience (Req 32.2/32.3). Parallel to the music
/// <see cref="PlayerService"/> by design (ADR-0020): it tracks the currently-playing video and play
/// state and drives a separate watch WebView via <see cref="IYouTubeWatchController"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IPausableAudioSource"/> so <see cref="PlaybackArbiter"/> can guarantee a
/// single active audio source (Req 32.3): it raises <see cref="PlaybackStarted"/> when video playback
/// begins (so music is paused) and exposes <see cref="PauseAsync"/> (so music starting pauses video).
/// </para>
/// <para>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency. The watch WebView host is part of
/// the YouTube-mode foundation; until it is attached, a <see cref="NullYouTubeWatchController"/>
/// keeps the service fully functional headless and in the shell.
/// </para>
/// </remarks>
public sealed class YouTubePlayerService : ObservableObject, IPausableAudioSource
{
    private readonly IYouTubeWatchController _controller;

    private YouTubeVideo? _currentVideo;
    private bool _isPlaying;

    /// <summary>Creates the service over a watch controller (defaults to a no-op foundation controller).</summary>
    public YouTubePlayerService(IYouTubeWatchController? controller = null)
    {
        _controller = controller ?? new NullYouTubeWatchController();
    }

    /// <inheritdoc />
    public event EventHandler? PlaybackStarted;

    /// <summary>The video currently loaded, or <c>null</c> when nothing is playing.</summary>
    public YouTubeVideo? CurrentVideo
    {
        get => _currentVideo;
        private set => SetProperty(ref _currentVideo, value);
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>
    /// Opens <paramref name="video"/> in the watch surface and starts playback (Req 32.2). Raises
    /// <see cref="PlaybackStarted"/> so the arbiter pauses music (Req 32.3).
    /// </summary>
    public async Task PlayVideoAsync(YouTubeVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);

        CurrentVideo = video;
        await _controller.LoadVideoAsync(video.VideoId).ConfigureAwait(false);
        await _controller.PlayAsync().ConfigureAwait(false);
        SetPlaying(true);
    }

    /// <summary>Resumes the loaded video (Req 32.2). Raises <see cref="PlaybackStarted"/>.</summary>
    public async Task PlayAsync()
    {
        if (CurrentVideo is null)
        {
            return;
        }

        await _controller.PlayAsync().ConfigureAwait(false);
        SetPlaying(true);
    }

    /// <inheritdoc />
    public async Task PauseAsync()
    {
        if (!IsPlaying)
        {
            return;
        }

        await _controller.PauseAsync().ConfigureAwait(false);
        IsPlaying = false;
    }

    /// <summary>
    /// Applies an observed play-state update from the watch WebView's JS observer (Req 32.2). When it
    /// reports a paused→playing transition, <see cref="PlaybackStarted"/> fires so the arbiter pauses
    /// music (Req 32.3).
    /// </summary>
    public void ReportPlaying(bool isPlaying) => SetPlaying(isPlaying);

    private void SetPlaying(bool value)
    {
        bool wasPlaying = IsPlaying;
        IsPlaying = value;

        if (value && !wasPlaying)
        {
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }
    }
}
