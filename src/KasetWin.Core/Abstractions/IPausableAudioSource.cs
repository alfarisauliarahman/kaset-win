namespace KasetWin.Core.Abstractions;

/// <summary>
/// A pausable audio source arbitrated by <c>PlaybackArbiter</c> so that only one audio source plays
/// at a time (Req 32.3). Two implementations exist: an adapter over the music player
/// (<c>IPlayerService</c>) and the regular-YouTube video player (<c>YouTubePlayerService</c>).
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency so the arbiter can be exercised
/// headless against fakes. <see cref="PlaybackStarted"/> is raised by a source whenever it
/// transitions into playing, which the arbiter uses to pause the other source.
/// </remarks>
public interface IPausableAudioSource
{
    /// <summary>Whether this source is currently producing audio.</summary>
    bool IsPlaying { get; }

    /// <summary>Pauses this source. A no-op when it is already paused / has no content.</summary>
    Task PauseAsync();

    /// <summary>
    /// Raised when this source begins playing (a paused→playing transition). The arbiter subscribes
    /// to this to pause the competing source and to record the active source (Req 32.3).
    /// </summary>
    event EventHandler? PlaybackStarted;
}
