namespace KasetWin.Core.Services.Player;

/// <summary>
/// Abstraction over the second (YouTube video) playback WebView2 that loads
/// <c>www.youtube.com/watch?v={id}</c> (Req 32.2). Parallel to the music
/// <see cref="KasetWin.Core.Abstractions.IPlaybackController"/> by design (ADR-0020): the watch-page
/// DOM and observer script differ from the music player, so the surfaces are kept separate.
/// </summary>
/// <remarks>
/// Defined in <c>Core</c> so <see cref="YouTubePlayerService"/> is unaware of WebView2/WinRT; the
/// WinRT implementation lives in <c>KasetWin.App</c>/<c>KasetWin.Platform</c> (a YouTube watch WebView
/// host) and is part of the YouTube-mode foundation. All control surfaces are asynchronous because
/// they marshal to the WebView2 message loop.
/// </remarks>
public interface IYouTubeWatchController
{
    /// <summary>The videoId currently loaded into the watch WebView, or <c>null</c> when none.</summary>
    string? CurrentVideoId { get; }

    /// <summary>Loads <c>www.youtube.com/watch?v={videoId}</c>, pausing current audio first (Req 32.3).</summary>
    Task LoadVideoAsync(string videoId);

    /// <summary>Resumes playback of the loaded video.</summary>
    Task PlayAsync();

    /// <summary>Pauses playback of the loaded video.</summary>
    Task PauseAsync();

    /// <summary>Stops audio and releases the watch WebView.</summary>
    Task ReleaseAsync();
}

/// <summary>
/// A no-op <see cref="IYouTubeWatchController"/> used while the YouTube watch WebView host is part of
/// the YouTube-mode foundation (not yet wired to a real WebView2). Tracks the loaded videoId so
/// <see cref="YouTubePlayerService"/> behaves correctly headless and in the shell until the real
/// controller is attached.
/// </summary>
public sealed class NullYouTubeWatchController : IYouTubeWatchController
{
    /// <inheritdoc />
    public string? CurrentVideoId { get; private set; }

    /// <inheritdoc />
    public Task LoadVideoAsync(string videoId)
    {
        CurrentVideoId = videoId;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PlayAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task PauseAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task ReleaseAsync()
    {
        CurrentVideoId = null;
        return Task.CompletedTask;
    }
}
