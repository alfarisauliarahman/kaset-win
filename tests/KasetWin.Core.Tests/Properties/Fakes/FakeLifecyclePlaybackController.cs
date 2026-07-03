using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless stand-in for the WinRT <c>WebView2PlaybackController</c> used by the lifecycle /
/// DRM smoke test (task 8.5, Feature: kaset-winui3). It cannot create a real
/// <c>CoreWebView2</c>, so it models the two pieces of behaviour that <i>are</i> reachable
/// without a live WebView2:
/// <list type="bullet">
///   <item>the singleton lifecycle — <see cref="EnsureInitializedAsync"/> creates the surface
///   exactly once and is idempotent thereafter (Req 1.1), and <see cref="ReleaseAsync"/> tears
///   it down (Req 1.5);</item>
///   <item>the DRM-availability message path — <see cref="ApplyWebMessage"/> routes an untrusted
///   bridge payload through the production <see cref="PlaybackMessageParser"/> and flips
///   <see cref="IsDrmAvailable"/> exactly as the real controller does (Req 1.3/1.7).</item>
/// </list>
/// </summary>
/// <remarks>
/// Distinct from <see cref="FakePlaybackController"/> (which is tuned for the PlayerService
/// property tests); kept separate so neither test's expectations leak into the other.
/// </remarks>
internal sealed class FakeLifecyclePlaybackController : IPlaybackController
{
    private bool _initialized;
    private bool _isDrmAvailable = true; // Evergreen runtime ships Widevine; a probe may flip it (Req 1.7).

    /// <summary>How many times the underlying surface was actually created (must stay 1 — Req 1.1).</summary>
    public int InitializeCount { get; private set; }

    /// <inheritdoc />
    public bool IsDrmAvailable => _isDrmAvailable;

    /// <inheritdoc />
    public string? CurrentVideoId { get; private set; }

    /// <summary>Whether the surface is currently initialized (created and not yet released).</summary>
    public bool IsInitialized => _initialized;

    /// <inheritdoc />
    public Task EnsureInitializedAsync()
    {
        // Singleton (Req 1.1): create the surface once; subsequent calls are no-ops.
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _initialized = true;
        InitializeCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Routes an untrusted JSON bridge payload through the production parser and applies its
    /// effect, mirroring <c>WebView2PlaybackController.OnWebMessageReceived</c> for the DRM path.
    /// </summary>
    /// <returns>The parsed, classified message (so tests can assert the classification too).</returns>
    public PlaybackWebMessage ApplyWebMessage(string json)
    {
        var message = PlaybackMessageParser.Parse(json);
        if (message.Kind == PlaybackMessageKind.DrmStatus && message.DrmAvailable is { } available)
        {
            _isDrmAvailable = available;
        }

        return message;
    }

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
    public Task SkipToNextAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task SkipToPreviousAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task SeekAsync(double positionSeconds) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetVolumeAsync(int volume0to100) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetMutedAsync(bool muted) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetAudioQualityAsync(AudioQuality quality) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetDisplayModeAsync(PlaybackDisplayMode mode) => Task.CompletedTask;

    /// <inheritdoc />
    public Task ReleaseAsync()
    {
        _initialized = false;
        CurrentVideoId = null;
        return Task.CompletedTask;
    }
}
