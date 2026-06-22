namespace KasetWin.Core.Services.Player;

/// <summary>
/// The decision a track-ended event resolves to once Kaset's queue is treated as the source
/// of truth (Req 2.3/2.4/2.5). See <see cref="WebQueueSync.ResolveTrackEnded"/>.
/// </summary>
public enum TrackEndedAction
{
    /// <summary>No active track / nothing to act on (empty queue).</summary>
    Ignore,

    /// <summary>The ended track matched the expected track and a next track exists — advance.</summary>
    AdvanceToNext,

    /// <summary>
    /// The ended track did <em>not</em> match the expected track — keep/replay the expected
    /// track instead of inheriting YouTube autoplay (queue authority). This is idempotent when
    /// the expected track is already loaded, which absorbs stale/duplicate ended events.
    /// </summary>
    ReplayExpected,

    /// <summary>The ended track matched but there is no next track — end of a non-repeat queue.</summary>
    EndPlayback,
}

/// <summary>
/// Pure decision logic that keeps Kaset's native queue authoritative when WebView2 playback
/// events drift from the queue (port of <c>PlayerService+WebQueueSync</c>, Design: "Queue
/// Authority"). Kept free of any state or I/O so it is fully testable headless
/// (Properties 6, 7, 8).
/// </summary>
public static class WebQueueSync
{
    /// <summary>
    /// Resolves what should happen when a <c>TRACK_ENDED</c> event arrives (Req 2.3/2.4/2.5).
    /// The queue only advances when <paramref name="observedVideoId"/> matches the
    /// <paramref name="expectedVideoId"/> (the current queue track); otherwise the expected
    /// track is kept/replayed so a stale or out-of-order ended event cannot double-advance the
    /// queue.
    /// </summary>
    /// <param name="observedVideoId">The videoId reported by the ended event (may be null/empty).</param>
    /// <param name="expectedVideoId">The videoId of the expected current queue track (may be null when empty).</param>
    /// <param name="hasNext">Whether the queue has a next track per its repeat mode (e.g. <c>PeekNext() is not null</c>).</param>
    /// <returns>The <see cref="TrackEndedAction"/> the player should perform.</returns>
    public static TrackEndedAction ResolveTrackEnded(string? observedVideoId, string? expectedVideoId, bool hasNext)
    {
        // No active track → nothing the queue can authoritatively do.
        if (string.IsNullOrEmpty(expectedVideoId))
        {
            return TrackEndedAction.Ignore;
        }

        // Queue authority: only advance when the track that actually ended is the one we
        // expected to be playing (Req 2.4). A mismatch means YouTube autoplay drifted or a
        // stale event arrived after we already advanced — keep/replay the expected track.
        if (!IsReportedVideoIdMatch(observedVideoId, expectedVideoId))
        {
            return TrackEndedAction.ReplayExpected;
        }

        return hasNext ? TrackEndedAction.AdvanceToNext : TrackEndedAction.EndPlayback;
    }

    /// <summary>
    /// Whether the ended event's videoId is considered to match the expected track. Equality is
    /// ordinal; a null/empty observed id is treated as a match because the JS observer falls back
    /// to its last known videoId and cannot disprove the expected track (Req 2.4).
    /// </summary>
    public static bool IsReportedVideoIdMatch(string? observedVideoId, string? expectedVideoId)
    {
        if (string.IsNullOrEmpty(expectedVideoId))
        {
            return false;
        }

        if (string.IsNullOrEmpty(observedVideoId))
        {
            return true;
        }

        return string.Equals(observedVideoId, expectedVideoId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a <c>STATE_UPDATE</c> reports a new videoId that should be adopted as the current
    /// track even if the DOM title is empty/stale (Req 2.6). True when the reported videoId is
    /// non-empty and differs from <paramref name="currentVideoId"/>.
    /// </summary>
    /// <param name="reportedVideoId">The videoId from the state-update message.</param>
    /// <param name="currentVideoId">The videoId of the player's current track, if any.</param>
    public static bool ShouldAdoptReportedVideoId(string? reportedVideoId, string? currentVideoId)
    {
        if (string.IsNullOrEmpty(reportedVideoId))
        {
            return false;
        }

        return !string.Equals(reportedVideoId, currentVideoId, StringComparison.Ordinal);
    }
}
