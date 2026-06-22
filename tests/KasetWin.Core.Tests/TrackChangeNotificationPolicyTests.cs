using KasetWin.Core.Services.Notifications;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="TrackChangeNotificationPolicy"/> (task 28.1, Req 35.1). Verify the
/// toast-eligibility rule ported from the macOS <c>NotificationService</c>: notify once active
/// playback runs for a new, fully-resolved track, and never twice for the same track.
/// </summary>
public class TrackChangeNotificationPolicyTests
{
    private const string TrackA = "vidA";
    private const string TrackB = "vidB";
    private const string Title = "Some Song";

    [Fact]
    public void Notifies_when_a_new_track_starts_playing()
    {
        Assert.True(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: TrackA,
            currentTitle: Title,
            isPlaying: true,
            previousTrackId: null,
            previousIsPlaying: false,
            lastNotifiedTrackId: null));
    }

    [Fact]
    public void Notifies_when_track_changes_to_a_different_track()
    {
        Assert.True(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: TrackB,
            currentTitle: Title,
            isPlaying: true,
            previousTrackId: TrackA,
            previousIsPlaying: true,
            lastNotifiedTrackId: TrackA));
    }

    [Fact]
    public void Notifies_when_playback_just_started_for_the_current_track()
    {
        // Same track id as previously observed but playback transitioned paused → playing and it has
        // not been notified yet (e.g. resume / first play after load).
        Assert.True(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: TrackA,
            currentTitle: Title,
            isPlaying: true,
            previousTrackId: TrackA,
            previousIsPlaying: false,
            lastNotifiedTrackId: null));
    }

    [Fact]
    public void Does_not_notify_twice_for_the_same_track()
    {
        Assert.False(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: TrackA,
            currentTitle: Title,
            isPlaying: true,
            previousTrackId: TrackA,
            previousIsPlaying: true,
            lastNotifiedTrackId: TrackA));
    }

    [Fact]
    public void Does_not_notify_while_paused()
    {
        Assert.False(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: TrackA,
            currentTitle: Title,
            isPlaying: false,
            previousTrackId: null,
            previousIsPlaying: false,
            lastNotifiedTrackId: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Does_not_notify_without_a_track(string? trackId)
    {
        Assert.False(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: trackId,
            currentTitle: Title,
            isPlaying: true,
            previousTrackId: null,
            previousIsPlaying: false,
            lastNotifiedTrackId: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(TrackChangeNotificationPolicy.LoadingPlaceholderTitle)]
    public void Does_not_notify_for_unresolved_title(string? title)
    {
        // Avoids a spurious toast on the very first silent/placeholder state.
        Assert.False(TrackChangeNotificationPolicy.ShouldNotify(
            currentTrackId: TrackA,
            currentTitle: title,
            isPlaying: true,
            previousTrackId: null,
            previousIsPlaying: false,
            lastNotifiedTrackId: null));
    }
}
