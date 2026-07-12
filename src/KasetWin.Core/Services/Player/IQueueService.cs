using System.ComponentModel;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// The single source of truth for the playback queue (Req 6, 8). Owns the ordered
/// list of <see cref="Song"/>s, the index of the active track, and the repeat mode
/// that governs what plays next (Req 5.8 / 2.5).
/// </summary>
/// <remarks>
/// This abstraction is intentionally free of any WinUI/WinRT dependency so the queue
/// logic can be exercised headless (Design: "kenapa tiga proyek"). It is observable via
/// <see cref="INotifyPropertyChanged"/> so the UI can bind to <see cref="Tracks"/>,
/// <see cref="CurrentIndex"/>, and <see cref="CurrentTrack"/>.
/// </remarks>
public interface IQueueService : INotifyPropertyChanged
{
    /// <summary>The ordered queue. Empty when nothing is queued.</summary>
    IReadOnlyList<Song> Tracks { get; }

    /// <summary>Index of the active track within <see cref="Tracks"/>, or <c>-1</c> when empty.</summary>
    int CurrentIndex { get; }

    /// <summary>The active track, or <c>null</c> when the queue is empty.</summary>
    Song? CurrentTrack { get; }

    /// <summary>
    /// Replaces the queue with <paramref name="songs"/> and sets the active track to
    /// <paramref name="startIndex"/> (Req 6.5, 8.1–8.3). The index is clamped into range.
    /// </summary>
    void SetQueue(IReadOnlyList<Song> songs, int startIndex = 0);

    /// <summary>
    /// Fills in missing display metadata (album, artists, video type, thumbnail) on the queued track
    /// with the matching <paramref name="videoId"/> from <paramref name="metadata"/>, keeping the
    /// track's position. Used to enrich a now-playing track that was started from a surface (e.g. a
    /// Home card) whose song lacked its album. Returns <c>true</c> when a track was found and changed.
    /// </summary>
    bool TryEnrichTrack(string videoId, Song metadata);

    /// <summary>
    /// Moves the track at <paramref name="fromIndex"/> to <paramref name="toIndex"/>
    /// (Req 6.2). The multiset of tracks is preserved and the active track stays the
    /// same track (its index follows the move).
    /// </summary>
    void Move(int fromIndex, int toIndex);

    /// <summary>Empties the queue so that <see cref="PeekNext"/> returns <c>null</c> (Req 6.3).</summary>
    void Clear();

    /// <summary>
    /// Randomly reorders the queue while keeping the multiset identical and the active
    /// track unchanged (Req 5.7 / 6.4). Randomness comes from the injected seam so this
    /// is deterministic under test.
    /// </summary>
    void Shuffle();

    /// <summary>Sets the repeat mode that governs <see cref="PeekNext"/> (Req 5.8).</summary>
    void SetRepeatMode(RepeatMode mode);

    /// <summary>
    /// Returns the track that would play next without mutating state — the source of
    /// truth for queue authority (Req 2.5 / 5.8). Follows the repeat mode:
    /// <c>One</c> → the same track; <c>All</c> → the next track with wrap-around;
    /// <c>Off</c> → <c>null</c> at the end of the queue.
    /// </summary>
    Song? PeekNext();

    /// <summary>
    /// Advances the active track following the repeat mode and returns the new
    /// <see cref="CurrentTrack"/>, or <c>null</c> if there is nothing to advance to.
    /// </summary>
    /// <param name="ignoreRepeatOne">
    /// When <c>true</c>, a <see cref="RepeatMode.One"/> queue still moves to the next track
    /// (wrapping like <see cref="RepeatMode.All"/>) instead of staying on the current one. Set by an
    /// explicit user "Next" (media key / player-bar button): Repeat One governs auto-advance at
    /// track end, not a deliberate skip — otherwise pressing Next merely replays the same song
    /// (Task 30.7, Req 37.7; upstream #319).
    /// </param>
    Song? AdvanceToNext(bool ignoreRepeatOne = false);

    /// <summary>
    /// Moves the active index to the queued track with <paramref name="videoId"/> without
    /// reordering the queue. Used when WebView2 reports a queued track before the native
    /// ended event arrives, so transport controls stay aligned with actual playback.
    /// </summary>
    bool TrySetCurrentByVideoId(string videoId);

    /// <summary>
    /// Moves the active track backwards following the repeat mode and returns the new
    /// <see cref="CurrentTrack"/>, or <c>null</c> if there is nothing to go back to.
    /// </summary>
    Song? AdvanceToPrevious();

    /// <summary>
    /// Appends only the songs whose <see cref="Song.VideoId"/> is not already present
    /// (in the queue or earlier in the batch) and returns the number actually added
    /// (Req 25.3). Guarantees no duplicate videoId in the resulting queue.
    /// </summary>
    int AppendDeduplicated(IEnumerable<Song> songs);

    /// <summary>
    /// Inserts the songs immediately after the current track ("play next"), skipping any whose
    /// <see cref="Song.VideoId"/> is already present (in the queue or earlier in the batch), and
    /// returns the number actually inserted. When the queue is empty the songs are appended and the
    /// first becomes current. Guarantees no duplicate videoId in the resulting queue.
    /// </summary>
    int InsertNext(IEnumerable<Song> songs);
}
