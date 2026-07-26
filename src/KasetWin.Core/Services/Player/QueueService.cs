using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Observable, in-memory implementation of <see cref="IQueueService"/> (Task 10.1).
/// Faithful port of the macOS <c>QueueService</c>: it is the single source of truth for
/// the playback queue and active track (Req 6, 8, 5.7, 5.8, 25.3).
/// </summary>
/// <remarks>
/// <para>
/// All mutations are guarded by a single lock so the queue is safe to share across the
/// player and UI threads; <see cref="INotifyPropertyChanged"/> events are always raised
/// <em>after</em> the lock is released to avoid re-entrancy deadlocks.
/// </para>
/// <para>
/// Shuffle randomness is injected via a <c>Func&lt;int,int&gt;</c> seam (an exclusive upper
/// bound in, an index in <c>[0, bound)</c> out) so <see cref="Shuffle"/> is deterministic
/// under test while remaining a true permutation in production.
/// </para>
/// </remarks>
public sealed class QueueService : ObservableObject, IQueueService
{
    private readonly object _gate = new();

    /// <summary>Randomness seam: given an exclusive upper bound, returns an index in <c>[0, bound)</c>.</summary>
    private readonly Func<int, int> _nextRandom;

    private readonly List<Song> _tracks = [];

    /// <summary>Immutable snapshot exposed via <see cref="Tracks"/>; rebuilt on each mutation.</summary>
    private IReadOnlyList<Song> _snapshot = [];

    private int _currentIndex = -1;
    private RepeatMode _repeatMode = RepeatMode.Off;

    /// <summary>Creates a queue using the shared system RNG for shuffling.</summary>
    public QueueService()
        : this((Func<int, int>?)null)
    {
    }

    /// <summary>
    /// Creates a queue with an explicit randomness seam.
    /// </summary>
    /// <param name="nextRandom">
    /// Given an exclusive upper bound, returns an index in <c>[0, bound)</c>. When
    /// <c>null</c> the shared system RNG is used. Inject a deterministic delegate in tests.
    /// </param>
    public QueueService(Func<int, int>? nextRandom)
    {
        _nextRandom = nextRandom ?? (exclusiveUpperBound => Random.Shared.Next(exclusiveUpperBound));
    }

    /// <summary>Creates a queue seeded by the supplied <see cref="Random"/> (deterministic when seeded).</summary>
    /// <param name="random">The random source; its <see cref="Random.Next(int)"/> drives the shuffle.</param>
    public QueueService(Random random)
        : this((random ?? throw new ArgumentNullException(nameof(random))).Next)
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<Song> Tracks
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    /// <inheritdoc />
    public int CurrentIndex
    {
        get
        {
            lock (_gate)
            {
                return _currentIndex;
            }
        }
    }

    /// <inheritdoc />
    public Song? CurrentTrack
    {
        get
        {
            lock (_gate)
            {
                return CurrentTrackLocked();
            }
        }
    }

    /// <summary>The active repeat mode (governs <see cref="PeekNext"/>).</summary>
    public RepeatMode RepeatMode
    {
        get
        {
            lock (_gate)
            {
                return _repeatMode;
            }
        }
    }

    /// <inheritdoc />
    public void SetQueue(IReadOnlyList<Song> songs, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(songs);

        lock (_gate)
        {
            _tracks.Clear();
            _tracks.AddRange(songs);
            _currentIndex = _tracks.Count == 0
                ? -1
                : Math.Clamp(startIndex, 0, _tracks.Count - 1);
            RebuildSnapshotLocked();
        }

        RaiseQueueChanged();
    }

    /// <summary>
    /// Merge rule for thumbnails: keep what exists, except that proper artwork may replace a
    /// bare video frame (an <c>i.ytimg.com/vi/…</c> URL — the player page's 16:9 still). Frames
    /// never replace artwork, and null never replaces anything.
    /// </summary>
    private static Uri? PickThumbnail(Uri? existing, Uri? incoming)
    {
        if (existing is null)
        {
            return incoming;
        }

        if (incoming is not null && IsVideoFrame(existing) && !IsVideoFrame(incoming))
        {
            return incoming;
        }

        return existing;
    }

    /// <summary>Whether <paramref name="url"/> is a video-frame still rather than album artwork.</summary>
    private static bool IsVideoFrame(Uri url) =>
        url.Host.EndsWith("ytimg.com", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.StartsWith("/vi/", StringComparison.Ordinal);

    /// <inheritdoc />
    public bool TryEnrichTrack(string videoId, Song metadata)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);
        ArgumentNullException.ThrowIfNull(metadata);

        bool currentAffected;
        lock (_gate)
        {
            int index = _tracks.FindIndex(t => string.Equals(t.VideoId, videoId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            Song existing = _tracks[index];
            Song merged = existing with
            {
                Album = existing.Album ?? metadata.Album,
                Artists = existing.Artists is { Count: > 0 } ? existing.Artists : metadata.Artists,
                VideoType = existing.VideoType ?? metadata.VideoType,
                // Thumbnails are gaps-only PLUS one upgrade: real square art may replace a video
                // frame. On a `kaset://` launch the first thumbnail to arrive is the player page's
                // 16:9 frame (i.ytimg.com/vi/…), and "first URL wins" then locked the proper album
                // art out forever — the now-playing card showed a cropped video still for the whole
                // song. The reverse is never allowed: a frame cannot replace square art.
                ThumbnailUrl = PickThumbnail(existing.ThumbnailUrl, metadata.ThumbnailUrl),
                // A track queued from nothing but a videoId (a `kaset://play?v=…` launch) has no
                // title and no duration either, and the queue is what the queue panel renders — so
                // leaving those blank showed an empty "Now playing" row. Gaps only: a queue entry
                // that already has a title keeps it.
                Title = string.IsNullOrEmpty(existing.Title) ? metadata.Title : existing.Title,
                Duration = existing.Duration ?? metadata.Duration,
            };

            if (merged == existing)
            {
                return false; // Nothing new to add.
            }

            _tracks[index] = merged;
            currentAffected = index == _currentIndex;
            RebuildSnapshotLocked();
        }

        OnPropertyChanged(nameof(Tracks));
        if (currentAffected)
        {
            OnPropertyChanged(nameof(CurrentTrack));
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryReplaceTrack(string videoId, Song replacement)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);
        ArgumentNullException.ThrowIfNull(replacement);

        bool currentAffected;
        lock (_gate)
        {
            int index = _tracks.FindIndex(t => string.Equals(t.VideoId, videoId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            _tracks[index] = replacement;
            currentAffected = index == _currentIndex;
            RebuildSnapshotLocked();
        }

        OnPropertyChanged(nameof(Tracks));
        if (currentAffected)
        {
            OnPropertyChanged(nameof(CurrentTrack));
        }

        return true;
    }

    /// <inheritdoc />
    public void Move(int fromIndex, int toIndex)
    {
        lock (_gate)
        {
            if (fromIndex < 0 || fromIndex >= _tracks.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            }

            if (toIndex < 0 || toIndex >= _tracks.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(toIndex));
            }

            if (fromIndex == toIndex)
            {
                return;
            }

            Song item = _tracks[fromIndex];
            _tracks.RemoveAt(fromIndex);
            _tracks.Insert(toIndex, item);
            _currentIndex = AdjustIndexForMove(_currentIndex, fromIndex, toIndex);
            RebuildSnapshotLocked();
        }

        RaiseQueueChanged();
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            if (_tracks.Count == 0 && _currentIndex == -1)
            {
                return;
            }

            _tracks.Clear();
            _currentIndex = -1;
            RebuildSnapshotLocked();
        }

        RaiseQueueChanged();
    }

    /// <inheritdoc />
    public void Shuffle()
    {
        lock (_gate)
        {
            if (_tracks.Count <= 1)
            {
                return;
            }

            // Capture the active track by reference so we can restore its position after
            // the permutation — this keeps the currently playing track unchanged (Req 6.4).
            Song? current = CurrentTrackLocked();

            // Fisher–Yates over the live list using the injected randomness seam.
            for (int i = _tracks.Count - 1; i > 0; i--)
            {
                int j = _nextRandom(i + 1);
                if (j < 0 || j > i)
                {
                    throw new InvalidOperationException(
                        $"Randomness seam returned {j}, which is outside the expected range [0, {i}].");
                }

                (_tracks[i], _tracks[j]) = (_tracks[j], _tracks[i]);
            }

            if (current is not null)
            {
                _currentIndex = IndexByReferenceLocked(current);
            }

            RebuildSnapshotLocked();
        }

        RaiseQueueChanged();
    }

    /// <inheritdoc />
    public void SetRepeatMode(RepeatMode mode)
    {
        lock (_gate)
        {
            if (_repeatMode == mode)
            {
                return;
            }

            _repeatMode = mode;
        }

        OnPropertyChanged(nameof(RepeatMode));
    }

    /// <inheritdoc />
    public Song? PeekNext()
    {
        lock (_gate)
        {
            int? next = NextIndexLocked();
            return next is int idx ? _tracks[idx] : null;
        }
    }

    /// <inheritdoc />
    public Song? AdvanceToNext(bool ignoreRepeatOne = false)
    {
        bool changed;
        lock (_gate)
        {
            int? next = NextIndexLocked(ignoreRepeatOne);
            if (next is not int idx)
            {
                return null;
            }

            changed = idx != _currentIndex;
            _currentIndex = idx;
        }

        if (changed)
        {
            RaiseCurrentChanged();
        }

        return CurrentTrack;
    }

    /// <inheritdoc />
    public bool TrySetCurrentByVideoId(string videoId)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        bool changed;
        lock (_gate)
        {
            int index = _tracks.FindIndex(track => string.Equals(track.VideoId, videoId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            changed = index != _currentIndex;
            _currentIndex = index;
        }

        if (changed)
        {
            RaiseCurrentChanged();
        }

        return true;
    }

    /// <inheritdoc />
    public Song? AdvanceToPrevious()
    {
        bool changed;
        lock (_gate)
        {
            int? previous = PreviousIndexLocked();
            if (previous is not int idx)
            {
                return null;
            }

            changed = idx != _currentIndex;
            _currentIndex = idx;
        }

        if (changed)
        {
            RaiseCurrentChanged();
        }

        return CurrentTrack;
    }

    /// <inheritdoc />
    public int AppendDeduplicated(IEnumerable<Song> songs)
    {
        ArgumentNullException.ThrowIfNull(songs);

        int added;
        bool currentBecameValid = false;

        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Song existing in _tracks)
            {
                seen.Add(existing.VideoId);
            }

            int before = _tracks.Count;
            foreach (Song song in songs)
            {
                // Dedupe against the queue *and* earlier items in this batch.
                if (seen.Add(song.VideoId))
                {
                    _tracks.Add(song);
                }
            }

            added = _tracks.Count - before;
            if (added == 0)
            {
                return 0;
            }

            // A previously empty queue now has an active track at the front.
            if (_currentIndex == -1)
            {
                _currentIndex = 0;
                currentBecameValid = true;
            }

            RebuildSnapshotLocked();
        }

        OnPropertyChanged(nameof(Tracks));
        if (currentBecameValid)
        {
            OnPropertyChanged(nameof(CurrentIndex));
            OnPropertyChanged(nameof(CurrentTrack));
        }

        return added;
    }

    /// <inheritdoc />
    public int InsertNext(IEnumerable<Song> songs)
    {
        ArgumentNullException.ThrowIfNull(songs);

        int added;
        bool currentBecameValid = false;

        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Song existing in _tracks)
            {
                seen.Add(existing.VideoId);
            }

            // Insert right after the current track; for an empty queue, start at the front.
            int insertAt = _currentIndex >= 0 ? _currentIndex + 1 : _tracks.Count;
            int before = _tracks.Count;
            foreach (Song song in songs)
            {
                if (seen.Add(song.VideoId))
                {
                    _tracks.Insert(insertAt++, song);
                }
            }

            added = _tracks.Count - before;
            if (added == 0)
            {
                return 0;
            }

            if (_currentIndex == -1)
            {
                _currentIndex = 0;
                currentBecameValid = true;
            }

            RebuildSnapshotLocked();
        }

        OnPropertyChanged(nameof(Tracks));
        if (currentBecameValid)
        {
            OnPropertyChanged(nameof(CurrentIndex));
            OnPropertyChanged(nameof(CurrentTrack));
        }

        return added;
    }

    // ── helpers (all callers hold _gate) ──────────────────────────────────────────────

    private Song? CurrentTrackLocked() =>
        _currentIndex >= 0 && _currentIndex < _tracks.Count ? _tracks[_currentIndex] : null;

    private void RebuildSnapshotLocked() => _snapshot = _tracks.ToArray();

    private int IndexByReferenceLocked(Song song)
    {
        for (int i = 0; i < _tracks.Count; i++)
        {
            if (ReferenceEquals(_tracks[i], song))
            {
                return i;
            }
        }

        // Fall back to value equality if the same reference was not found.
        int valueIndex = _tracks.IndexOf(song);
        return valueIndex >= 0 ? valueIndex : _currentIndex;
    }

    /// <summary>Index of the next track per the repeat mode, or <c>null</c> when there is none.</summary>
    private int? NextIndexLocked(bool ignoreRepeatOne = false)
    {
        if (_tracks.Count == 0 || _currentIndex < 0)
        {
            return null;
        }

        // An explicit skip treats Repeat One as Repeat All so "Next" moves forward instead of
        // replaying the current track (Task 30.7, Req 37.7). Auto-advance keeps Repeat One semantics.
        RepeatMode mode = ignoreRepeatOne && _repeatMode == RepeatMode.One ? RepeatMode.All : _repeatMode;

        return mode switch
        {
            RepeatMode.One => _currentIndex,
            RepeatMode.All => (_currentIndex + 1) % _tracks.Count,
            _ => _currentIndex + 1 < _tracks.Count ? _currentIndex + 1 : null,
        };
    }

    /// <summary>Index of the previous track per the repeat mode, or <c>null</c> when there is none.</summary>
    private int? PreviousIndexLocked()
    {
        if (_tracks.Count == 0 || _currentIndex < 0)
        {
            return null;
        }

        return _repeatMode switch
        {
            RepeatMode.One => _currentIndex,
            RepeatMode.All => ((_currentIndex - 1) % _tracks.Count + _tracks.Count) % _tracks.Count,
            _ => _currentIndex - 1 >= 0 ? _currentIndex - 1 : null,
        };
    }

    /// <summary>
    /// Recomputes the active index so it follows the physical track that was moved
    /// (remove-at-from then insert-at-to semantics).
    /// </summary>
    private static int AdjustIndexForMove(int current, int from, int to)
    {
        if (current < 0)
        {
            return current;
        }

        if (current == from)
        {
            return to;
        }

        int afterRemove = current > from ? current - 1 : current;
        return afterRemove >= to ? afterRemove + 1 : afterRemove;
    }

    private void RaiseQueueChanged()
    {
        OnPropertyChanged(nameof(Tracks));
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentTrack));
    }

    private void RaiseCurrentChanged()
    {
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentTrack));
    }
}
