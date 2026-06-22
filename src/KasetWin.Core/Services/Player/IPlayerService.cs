using System.ComponentModel;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Coordinates playback state and control by orchestrating the <see cref="IQueueService"/>
/// (source of truth for the queue, Req 6/8) and the <see cref="IPlaybackController"/>
/// (the hidden WebView2, Req 1/7). It also consumes <see cref="IJsBridge"/> messages and
/// keeps Kaset's queue authoritative over YouTube autoplay (Req 2).
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency, so the full control surface
/// can be exercised headless against fake controllers/bridges (Design: "Inversi dependensi
/// WebView2/SMTC"). Observable via <see cref="INotifyPropertyChanged"/> for UI binding.
/// </remarks>
public interface IPlayerService : INotifyPropertyChanged
{
    /// <summary>The track currently playing, or <c>null</c> when nothing is loaded.</summary>
    Song? CurrentTrack { get; }

    /// <summary>Whether playback is currently active (Req 5.1).</summary>
    bool IsPlaying { get; }

    /// <summary>Current playback position in seconds (Req 2.1).</summary>
    double Progress { get; }

    /// <summary>Track duration in seconds (Req 2.1).</summary>
    double Duration { get; }

    /// <summary>Volume on the 0–100 range (Req 5.5).</summary>
    int Volume { get; }

    /// <summary>Whether audio is muted; the prior volume is preserved for restoration (Req 5.6).</summary>
    bool IsMuted { get; }

    /// <summary>Whether the current content is a live stream — disables seek (Req 9).</summary>
    bool IsLive { get; }

    /// <summary>The active repeat mode (Req 5.8).</summary>
    RepeatMode RepeatMode { get; }

    /// <summary>Whether the queue is currently shuffled (Req 5.7).</summary>
    bool IsShuffled { get; }

    /// <summary>The preferred audio quality applied to the running player (Req 7).</summary>
    AudioQuality AudioQuality { get; }

    /// <summary>Plays a single videoId, replacing the queue with that one track (Req 1.2).</summary>
    Task PlayAsync(string videoId);

    /// <summary>Plays a single song, replacing the queue with that one track (Req 1.2).</summary>
    Task PlaySongAsync(Song song);

    /// <summary>
    /// Loads <paramref name="songs"/> into the queue and plays from <paramref name="startIndex"/>
    /// — album / playlist / artist playback (Req 6.5, 8.1–8.3).
    /// </summary>
    Task PlayCollectionAsync(IReadOnlyList<Song> songs, int startIndex = 0);

    /// <summary>Toggles between playing and paused (Req 5.1).</summary>
    Task TogglePlayPauseAsync();

    /// <summary>
    /// Pauses playback if currently playing (Req 5.1). Unlike <see cref="TogglePlayPauseAsync"/>
    /// this never resumes — used by the playback arbiter to silence music when another audio source
    /// starts (Req 32.3). A no-op when already paused.
    /// </summary>
    Task PauseAsync();

    /// <summary>Advances to and plays the next queued track (Req 5.2).</summary>
    Task NextAsync();

    /// <summary>Moves back to and plays the previous queued track (Req 5.3).</summary>
    Task PreviousAsync();

    /// <summary>
    /// Seeks to <paramref name="seconds"/>, clamped to <c>[0, Duration]</c> (Req 5.4).
    /// Disabled while <see cref="IsLive"/> is <c>true</c> (Req 9.2).
    /// </summary>
    Task SeekAsync(double seconds);

    /// <summary>Sets the volume, clamped to <c>[0, 100]</c> (Req 5.5).</summary>
    void SetVolume(int volume);

    /// <summary>Mutes / unmutes while preserving the prior volume for restoration (Req 5.6).</summary>
    void ToggleMute();

    /// <summary>Toggles shuffle on the queue, keeping the active track (Req 5.7).</summary>
    void ToggleShuffle();

    /// <summary>Cycles the repeat mode <c>Off → All → One → Off</c> (Req 5.8).</summary>
    void CycleRepeat();

    /// <summary>
    /// Sets the preferred audio quality and re-applies it to the running player (Req 7.1/7.2).
    /// </summary>
    Task SetAudioQualityAsync(AudioQuality quality);

    /// <summary>
    /// Marks whether the current content is a live stream (Req 9.1). Set from track context
    /// (e.g. <see cref="SongMetadata.IsLive"/>) for live episodes; disables seek when <c>true</c>.
    /// </summary>
    void SetLive(bool isLive);

    /// <summary>
    /// Handles a <c>TRACK_ENDED</c> event. Validates <paramref name="observedVideoId"/> against
    /// the expected current track before advancing — queue authority over YouTube autoplay
    /// (Req 2.3/2.4/2.5). On a mismatch it keeps/replays the expected track; at the end of a
    /// non-repeating queue it marks playback ended.
    /// </summary>
    Task HandleTrackEndedAsync(string? observedVideoId);

    /// <summary>
    /// Applies a <c>STATE_UPDATE</c> message to player state (Req 2.1/2.2). A newly reported
    /// videoId is treated as authoritative even if the DOM title is empty/stale (Req 2.6).
    /// </summary>
    void HandleStateUpdate(PlaybackStateMessage message);
}
