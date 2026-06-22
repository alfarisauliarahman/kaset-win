using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests for <see cref="QueueService"/> (Tasks 10.2–10.7,
/// Properties 12–17). Each property runs a minimum of 100 CsCheck iterations.
///
/// Songs are generated with unique <c>videoId</c>s (<c>v0, v1, …</c>) so that
/// multiset comparisons and "only new items" assertions reduce to comparing
/// videoIds — there are never coincidental id collisions to reason about.
/// Shuffle randomness is supplied through the deterministic <c>Func&lt;int,int&gt;</c>
/// seam on <see cref="QueueService"/> so counterexamples reproduce exactly.
/// </summary>
public class QueueProperties
{
    /// <summary>Builds a song whose identity is the unique id <c>v{i}</c>.</summary>
    private static Song MakeSong(int i) => new()
    {
        Id = $"v{i}",
        VideoId = $"v{i}",
        Title = $"Title {i}",
    };

    /// <summary>A list of <paramref name="count"/> songs with unique videoIds <c>v0..v(count-1)</c>.</summary>
    private static List<Song> DistinctSongs(int count) =>
        Enumerable.Range(0, count).Select(MakeSong).ToList();

    /// <summary>Asserts two song collections contain exactly the same videoIds (multiset equality).</summary>
    private static void AssertSameMultiset(IEnumerable<Song> expected, IEnumerable<Song> actual)
    {
        var e = expected.Select(s => s.VideoId).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var a = actual.Select(s => s.VideoId).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(e, a);
    }

    // Feature: kaset-winui3, Property 12: Shuffle adalah permutasi yang mempertahankan track aktif
    // Validates: Requirements 5.7, 6.4
    [Fact]
    public void Property12_Shuffle_is_a_permutation_that_preserves_the_active_track()
    {
        // For any queue and active index, Shuffle keeps the multiset of tracks identical
        // (nothing lost or duplicated) and leaves the currently playing track unchanged.
        var scenario =
            from count in Gen.Int[1, 12]
            from currentIndex in Gen.Int[0, count - 1]
            from seed in Gen.Int
            select (count, currentIndex, seed);

        scenario.Sample(
            s =>
            {
                var (count, currentIndex, seed) = s;

                var songs = DistinctSongs(count);
                var rng = new Random(seed);
                var queue = new QueueService(bound => rng.Next(bound));
                queue.SetQueue(songs, currentIndex);

                var before = queue.Tracks.ToList();
                var activeBefore = queue.CurrentTrack!;

                queue.Shuffle();

                // Multiset of tracks is unchanged.
                AssertSameMultiset(before, queue.Tracks);

                // The active track is still the same song, and CurrentIndex points at it.
                Assert.Equal(activeBefore.VideoId, queue.CurrentTrack!.VideoId);
                Assert.Equal(activeBefore.VideoId, queue.Tracks[queue.CurrentIndex].VideoId);
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 13: PeekNext mengikuti mode repeat
    // Validates: Requirements 5.8, 2.5
    [Fact]
    public void Property13_PeekNext_follows_the_repeat_mode()
    {
        // For any non-empty queue, active index, and repeat mode, PeekNext returns:
        //   One -> the same track; All -> the next track with wrap-around;
        //   Off -> null at the end of the queue, otherwise the next track.
        var scenario =
            from count in Gen.Int[1, 12]
            from currentIndex in Gen.Int[0, count - 1]
            from mode in Gen.Int[0, 2].Select(i => (RepeatMode)i)
            select (count, currentIndex, mode);

        scenario.Sample(
            s =>
            {
                var (count, currentIndex, mode) = s;

                var songs = DistinctSongs(count);
                var queue = new QueueService();
                queue.SetRepeatMode(mode);
                queue.SetQueue(songs, currentIndex);

                var next = queue.PeekNext();

                switch (mode)
                {
                    case RepeatMode.One:
                        Assert.Equal(songs[currentIndex].VideoId, next!.VideoId);
                        break;
                    case RepeatMode.All:
                        Assert.Equal(songs[(currentIndex + 1) % count].VideoId, next!.VideoId);
                        break;
                    default: // Off
                        if (currentIndex == count - 1)
                        {
                            Assert.Null(next);
                        }
                        else
                        {
                            Assert.Equal(songs[currentIndex + 1].VideoId, next!.VideoId);
                        }

                        break;
                }
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 14: Move mempertahankan multiset dan menempatkan item di target
    // Validates: Requirements 6.2
    [Fact]
    public void Property14_Move_places_the_item_at_target_and_preserves_the_multiset()
    {
        // For any queue and valid (from, to), the item originally at `from` ends up at `to`
        // and the multiset of tracks is unchanged.
        var scenario =
            from count in Gen.Int[1, 12]
            from fromIndex in Gen.Int[0, count - 1]
            from toIndex in Gen.Int[0, count - 1]
            select (count, fromIndex, toIndex);

        scenario.Sample(
            s =>
            {
                var (count, fromIndex, toIndex) = s;

                var songs = DistinctSongs(count);
                var queue = new QueueService();
                queue.SetQueue(songs);

                var before = queue.Tracks.ToList();
                var movedVideoId = queue.Tracks[fromIndex].VideoId;

                queue.Move(fromIndex, toIndex);

                Assert.Equal(movedVideoId, queue.Tracks[toIndex].VideoId);
                AssertSameMultiset(before, queue.Tracks);
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 15: Clear mengosongkan antrian dan menghentikan track berikutnya
    // Validates: Requirements 6.3
    [Fact]
    public void Property15_Clear_empties_the_queue_and_stops_the_next_track()
    {
        // For any queue (any repeat mode), Clear leaves the track list empty and PeekNext null.
        var scenario =
            from count in Gen.Int[0, 12]
            from startIndex in Gen.Int[0, 20]
            from mode in Gen.Int[0, 2].Select(i => (RepeatMode)i)
            select (count, startIndex, mode);

        scenario.Sample(
            s =>
            {
                var (count, startIndex, mode) = s;

                var songs = DistinctSongs(count);
                var queue = new QueueService();
                queue.SetRepeatMode(mode);
                queue.SetQueue(songs, startIndex);

                queue.Clear();

                Assert.Empty(queue.Tracks);
                Assert.Null(queue.PeekNext());
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 16: SetQueue/PlayCollection mengisi antrian dari sumber
    // Validates: Requirements 6.5, 8.1, 8.2, 8.3, 14.4, 15.4
    [Fact]
    public void Property16_SetQueue_populates_tracks_and_clamps_the_start_index()
    {
        // For any non-empty list and any startIndex (including out of range), Tracks equals the
        // source list and CurrentIndex equals clamp(startIndex, 0, count - 1).
        var scenario =
            from count in Gen.Int[1, 12]
            from startIndex in Gen.Int[-5, 17]
            select (count, startIndex);

        scenario.Sample(
            s =>
            {
                var (count, startIndex) = s;

                var songs = DistinctSongs(count);
                var queue = new QueueService();
                queue.SetQueue(songs, startIndex);

                Assert.Equal(songs, queue.Tracks);
                Assert.Equal(
                    songs.Select(t => t.VideoId),
                    queue.Tracks.Select(t => t.VideoId));
                Assert.Equal(Math.Clamp(startIndex, 0, count - 1), queue.CurrentIndex);
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 17: AppendDeduplicated menjaga keunikan dan hanya menambah item baru
    // Validates: Requirements 25.3
    [Fact]
    public void Property17_AppendDeduplicated_keeps_uniqueness_and_only_adds_new_items()
    {
        // For any initial queue and batch (with overlaps and internal duplicates),
        // the result has no duplicate videoIds, only previously-absent songs are appended,
        // and the return value equals the number of songs actually added.
        var scenario =
            from initialCount in Gen.Int[0, 8]
            from extraIds in Gen.Int[1, 6]
            from batchIndices in Gen.Int[0, initialCount + extraIds - 1].Array[0, 16]
            select (initialCount, batchIndices);

        scenario.Sample(
            s =>
            {
                var (initialCount, batchIndices) = s;

                var initial = DistinctSongs(initialCount);
                var batch = batchIndices.Select(MakeSong).ToList();

                var queue = new QueueService();
                queue.SetQueue(initial);

                var added = queue.AppendDeduplicated(batch);

                // Recompute the expectation: walk the batch, tracking everything already seen.
                var seen = new HashSet<string>(
                    initial.Select(t => t.VideoId),
                    StringComparer.Ordinal);
                var expectedNew = new List<string>();
                foreach (var idx in batchIndices)
                {
                    var videoId = $"v{idx}";
                    if (seen.Add(videoId))
                    {
                        expectedNew.Add(videoId);
                    }
                }

                // Return value == number of songs actually added.
                Assert.Equal(expectedNew.Count, added);

                var resultIds = queue.Tracks.Select(t => t.VideoId).ToList();

                // No duplicate videoIds remain in the queue.
                Assert.Equal(resultIds.Count, resultIds.Distinct(StringComparer.Ordinal).Count());

                // Only new items were appended (existing order preserved, new ids in batch order).
                var expectedFinal = initial.Select(t => t.VideoId).Concat(expectedNew).ToList();
                Assert.Equal(expectedFinal, resultIds);
            },
            iter: 100);
    }
}
