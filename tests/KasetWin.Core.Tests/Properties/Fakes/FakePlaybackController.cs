using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless fake of <see cref="IPlaybackController"/> for the PlayerService property tests
/// (Feature: kaset-winui3). It records the ordered control operations so pause-before-load
/// (Req 1.6) and load idempotency (Req 1.2) can be asserted, and tracks the latest
/// volume / muted / seek values for the clamp and live-seek properties (Req 5.5, 9.2).
/// </summary>
/// <remarks>
/// No WebView2/WinRT dependency — this is a pure in-memory stand-in so
/// <see cref="KasetWin.Core.Services.Player.PlayerService"/> can be exercised on the plain
/// .NET test runner.
/// </remarks>
internal sealed class FakePlaybackController : IPlaybackController
{
    /// <summary>Ordered log of operations, e.g. <c>"pause"</c>, <c>"load:vid1"</c>, <c>"play"</c>.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>The videoIds actually (re)loaded, in order. Excludes idempotent no-op loads.</summary>
    public List<string> LoadedVideoIds { get; } = [];

    public int Volume { get; private set; } = 100;

    public bool Muted { get; private set; }

    public double SeekPosition { get; private set; }

    /// <inheritdoc />
    public bool IsDrmAvailable => true;

    /// <inheritdoc />
    public string? CurrentVideoId { get; private set; }

    /// <inheritdoc />
    public Task EnsureInitializedAsync()
    {
        Operations.Add("init");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LoadVideoAsync(string videoId)
    {
        // Idempotency (Req 1.2): loading the already-loaded videoId is a no-op — no pause,
        // no navigation. A genuinely different videoId pauses current playback before loading
        // (pause-before-load, Req 1.6).
        if (string.Equals(videoId, CurrentVideoId, StringComparison.Ordinal))
        {
            Operations.Add($"load-skip:{videoId}");
            return Task.CompletedTask;
        }

        Operations.Add("pause");
        Operations.Add($"load:{videoId}");
        LoadedVideoIds.Add(videoId);
        CurrentVideoId = videoId;
        SeekPosition = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PlayAsync()
    {
        Operations.Add("play");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PauseAsync()
    {
        Operations.Add("pause");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SkipToNextAsync()
    {
        Operations.Add("web-next");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SkipToPreviousAsync()
    {
        Operations.Add("web-previous");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(double positionSeconds)
    {
        SeekPosition = positionSeconds;
        Operations.Add($"seek:{positionSeconds}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetVolumeAsync(int volume0to100)
    {
        Volume = volume0to100;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetMutedAsync(bool muted)
    {
        Muted = muted;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetAudioQualityAsync(AudioQuality quality) => Task.CompletedTask;
    public Task SetEqualizerAsync(bool enabled, IReadOnlyList<int> gainsDb) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetPlaybackRateAsync(double rate) => Task.CompletedTask;

    /// <summary>Records the last Repeat One (native loop) request so tests can assert it.</summary>
    public bool RepeatOne { get; private set; }

    /// <inheritdoc />
    public Task SetRepeatOneAsync(bool enabled)
    {
        RepeatOne = enabled;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDisplayModeAsync(PlaybackDisplayMode mode) => Task.CompletedTask;

    /// <inheritdoc />
    public Task ReleaseAsync() => Task.CompletedTask;
}
