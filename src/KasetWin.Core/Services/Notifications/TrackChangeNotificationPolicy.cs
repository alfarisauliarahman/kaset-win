namespace KasetWin.Core.Services.Notifications;

/// <summary>
/// Pure decision for whether a track-change toast should be raised (Req 35.1). Faithful port of the
/// macOS <c>NotificationService</c> observation rule: only notify once active playback is running
/// for a new, fully-resolved track, and never twice for the same track.
/// </summary>
/// <remarks>
/// Kept WinUI/WinRT-free in <c>Core</c> so the eligibility rule is unit-testable headless. The
/// stateful caller (the App's <c>ToastNotificationService</c>) supplies the previously observed
/// track / playing state and the id it last notified, and persists the new values after each call.
/// </remarks>
public static class TrackChangeNotificationPolicy
{
    /// <summary>
    /// Placeholder title used while a track's metadata is still resolving. A track in this state is
    /// not toast-eligible so the first silent/placeholder state does not produce a spurious toast.
    /// </summary>
    public const string LoadingPlaceholderTitle = "Loading...";

    /// <summary>
    /// Returns <see langword="true"/> when a "now playing" toast should be shown for the current
    /// track, matching the macOS rule (Req 35.1):
    /// <list type="bullet">
    /// <item>there is a current track with a non-empty id and a resolved (non-placeholder) title,</item>
    /// <item>playback is active,</item>
    /// <item>the track has not already been notified, and</item>
    /// <item>either the track changed since the last observation or playback just started.</item>
    /// </list>
    /// </summary>
    /// <param name="currentTrackId">The current track's stable id (videoId), or <c>null</c>/empty if none.</param>
    /// <param name="currentTitle">The current track's title; an empty or <see cref="LoadingPlaceholderTitle"/> value is treated as unresolved.</param>
    /// <param name="isPlaying">Whether playback is currently active.</param>
    /// <param name="previousTrackId">The track id observed on the previous evaluation, or <c>null</c>.</param>
    /// <param name="previousIsPlaying">Whether playback was active on the previous evaluation.</param>
    /// <param name="lastNotifiedTrackId">The track id most recently notified, or <c>null</c> if none.</param>
    public static bool ShouldNotify(
        string? currentTrackId,
        string? currentTitle,
        bool isPlaying,
        string? previousTrackId,
        bool previousIsPlaying,
        string? lastNotifiedTrackId)
    {
        if (string.IsNullOrEmpty(currentTrackId))
        {
            return false;
        }

        if (string.Equals(currentTrackId, lastNotifiedTrackId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(currentTitle)
            || string.Equals(currentTitle, LoadingPlaceholderTitle, StringComparison.Ordinal))
        {
            return false;
        }

        if (!isPlaying)
        {
            return false;
        }

        bool trackChanged = !string.Equals(currentTrackId, previousTrackId, StringComparison.Ordinal);
        bool playbackJustStarted = !previousIsPlaying;

        return trackChanged || playbackJustStarted;
    }
}
