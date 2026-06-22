using KasetWin.Core.Abstractions;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless fake of <see cref="IJsBridge"/> for the PlayerService property tests
/// (Feature: kaset-winui3). Exposes <see cref="RaiseStateUpdated"/> / <see cref="RaiseTrackEnded"/>
/// so tests can synthesize <c>STATE_UPDATE</c> / <c>TRACK_ENDED</c> messages the way the WinRT
/// implementation would after validating untrusted WebView2 payloads (Req 2).
/// </summary>
internal sealed class FakeJsBridge : IJsBridge
{
    /// <inheritdoc />
    public event EventHandler<PlaybackStateMessage>? StateUpdated;

    /// <inheritdoc />
    public event EventHandler<TrackEndedMessage>? TrackEnded;

    /// <summary>Raises a <c>STATE_UPDATE</c> as the JS observer would.</summary>
    public void RaiseStateUpdated(PlaybackStateMessage message) =>
        StateUpdated?.Invoke(this, message);

    /// <summary>Raises a <c>TRACK_ENDED</c> as the JS observer would.</summary>
    public void RaiseTrackEnded(TrackEndedMessage message) =>
        TrackEnded?.Invoke(this, message);
}
