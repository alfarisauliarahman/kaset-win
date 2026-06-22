using KasetWin.Core.Services.Player;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless fake of <see cref="IYouTubeWatchController"/> for the YouTube watch / arbiter tests
/// (Feature: kaset-winui3, Req 32.2/32.3). Records the control calls the real WinRT
/// <c>YouTubeWatchController</c> would marshal to the watch WebView2, and exposes
/// <see cref="SimulateObservedPlaying"/> so the observer→player wiring (the bridge that the App
/// composition root attaches) can be exercised without any WebView2/WinRT runtime.
/// </summary>
internal sealed class FakeYouTubeWatchController : IYouTubeWatchController
{
    /// <summary>Number of times <see cref="LoadVideoAsync"/> was invoked.</summary>
    public int LoadCount { get; private set; }

    /// <summary>Number of times <see cref="PlayAsync"/> was invoked.</summary>
    public int PlayCount { get; private set; }

    /// <summary>Number of times <see cref="PauseAsync"/> was invoked.</summary>
    public int PauseCount { get; private set; }

    /// <summary>Number of times <see cref="ReleaseAsync"/> was invoked.</summary>
    public int ReleaseCount { get; private set; }

    /// <inheritdoc />
    public string? CurrentVideoId { get; private set; }

    /// <summary>
    /// Mirrors the real controller's observer event: raised with the observed play-state so the
    /// player can reflect in-page play/pause into the arbitrated audio source.
    /// </summary>
    public event EventHandler<bool>? PlaybackStateObserved;

    /// <inheritdoc />
    public Task LoadVideoAsync(string videoId)
    {
        LoadCount++;
        CurrentVideoId = videoId;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PlayAsync()
    {
        PlayCount++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PauseAsync()
    {
        PauseCount++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync()
    {
        ReleaseCount++;
        CurrentVideoId = null;
        return Task.CompletedTask;
    }

    /// <summary>Simulates the injected observer reporting a play-state change from the watch page.</summary>
    public void SimulateObservedPlaying(bool isPlaying) =>
        PlaybackStateObserved?.Invoke(this, isPlaying);
}
