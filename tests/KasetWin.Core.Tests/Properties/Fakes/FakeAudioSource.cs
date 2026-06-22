using KasetWin.Core.Abstractions;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless fake of <see cref="IPausableAudioSource"/> for the <c>PlaybackArbiter</c> tests
/// (Feature: kaset-winui3, Req 32.3). Tracks play state and pause invocations so the
/// single-audio-source invariant can be asserted without any WebView2/WinRT dependency.
/// </summary>
internal sealed class FakeAudioSource : IPausableAudioSource
{
    /// <summary>Number of times <see cref="PauseAsync"/> was invoked.</summary>
    public int PauseCount { get; private set; }

    /// <inheritdoc />
    public bool IsPlaying { get; private set; }

    /// <inheritdoc />
    public event EventHandler? PlaybackStarted;

    /// <summary>Simulates this source beginning playback: sets <see cref="IsPlaying"/> and raises the event.</summary>
    public void StartPlaying()
    {
        IsPlaying = true;
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task PauseAsync()
    {
        if (IsPlaying)
        {
            PauseCount++;
        }

        IsPlaying = false;
        return Task.CompletedTask;
    }
}
