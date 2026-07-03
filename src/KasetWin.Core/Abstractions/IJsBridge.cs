using KasetWin.Core.Models;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// Bridge surface for messages flowing from the JavaScript observer injected into the
/// playback page to the native player (Req 2). The WinRT implementation lives in
/// <c>KasetWin.Platform</c> and translates untrusted <c>WebMessageReceived</c> payloads
/// into the strongly-typed events below after validation (Design: "Keamanan bridge").
/// </summary>
public interface IJsBridge
{
    /// <summary>
    /// Raised for each <c>STATE_UPDATE</c> message reporting current playback state
    /// (Req 2.1/2.2/2.6).
    /// </summary>
    event EventHandler<PlaybackStateMessage> StateUpdated;

    /// <summary>
    /// Raised for each <c>TRACK_ENDED</c> message when a track ends naturally, carrying the
    /// videoId of the track that ended so queue authority can be validated (Req 2.3/2.4).
    /// </summary>
    event EventHandler<TrackEndedMessage> TrackEnded;
}

/// <summary>
/// A <c>STATE_UPDATE</c> message from the JS observer (Req 2.1). When <see cref="VideoId"/>
/// reports a new value while <see cref="Title"/> is still empty/stale, the reported videoId
/// is treated as authoritative (Req 2.6).
/// </summary>
/// <param name="IsPlaying">Whether the player is currently playing.</param>
/// <param name="Progress">Current playback position in seconds.</param>
/// <param name="Duration">Track duration in seconds.</param>
/// <param name="VideoId">The videoId currently reported by the player (authoritative, Req 2.6).</param>
/// <param name="Title">Track title from the DOM, may be empty/stale.</param>
/// <param name="Artist">Track artist from the DOM, may be empty/stale.</param>
/// <param name="TrackChanged">Whether the observer detected a track change (Req 2.2).</param>
/// <param name="HasVideo">Whether the page reports real video content, or <c>null</c> when unknown.</param>
/// <param name="VideoType">The detected music video type, or <c>null</c> when unknown.</param>
/// <param name="ThumbnailUrl">Current player-bar artwork, or <c>null</c> when unavailable.</param>
public sealed record PlaybackStateMessage(
    bool IsPlaying,
    double Progress,
    double Duration,
    string VideoId,
    string Title,
    string Artist,
    bool TrackChanged,
    bool? HasVideo,
    MusicVideoType? VideoType,
    Uri? ThumbnailUrl = null);

/// <summary>
/// A <c>TRACK_ENDED</c> message carrying the videoId of the track that ended naturally
/// (Req 2.3). The player validates this against the expected track before advancing (Req 2.4).
/// </summary>
/// <param name="VideoId">The videoId of the track that ended.</param>
public sealed record TrackEndedMessage(string VideoId);
