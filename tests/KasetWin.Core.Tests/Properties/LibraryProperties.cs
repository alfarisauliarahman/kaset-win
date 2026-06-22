using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Library;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests for the pure Library logic of the kaset-winui3 feature: text filtering
/// produces a matching subset (Property 29) and optimistic mutations roll back / reconcile and
/// converge (Property 30). Each property is a single <see cref="FactAttribute"/> running a minimum
/// of 100 CsCheck iterations.
/// </summary>
public class LibraryProperties
{
    /// <summary>
    /// Short display strings drawn from a mixed-case alphabet with spaces/digits, so generated
    /// queries are sometimes substrings of generated titles (exercising real matches) and the
    /// case-insensitive comparison is meaningfully tested.
    /// </summary>
    private static readonly Gen<string> Text =
        Gen.Char["abAB12 "].Array[0, 6].Select(chars => new string(chars));

    // Feature: kaset-winui3, Property 29: Filter library menghasilkan subset yang cocok
    // Validates: Requirements 13.5
    [Fact]
    public void Property29_Library_filter_yields_a_matching_subset()
    {
        // For any library content and query, every result item comes from the original collection
        // (subset) and matches the query (case-insensitive substring on title/name); the result is
        // exactly the matching items in original order; and a blank query returns everything.
        var scenario =
            from playlists in Text.Array[0, 6]
            from albums in Text.Array[0, 6]
            from artists in Text.Array[0, 6]
            from songs in Text.Array[0, 6]
            from query in Gen.OneOf(Gen.Const(string.Empty), Gen.Const("   "), Text)
            select (playlists, albums, artists, songs, query);

        scenario.Sample(
            s =>
            {
                var (playlists, albums, artists, songs, query) = s;

                var content = new LibraryContent
                {
                    Playlists = playlists.Select((t, i) => new Playlist { Id = $"PL{i}", Title = t }).ToList(),
                    Albums = albums.Select((t, i) => new Album { Id = $"AL{i}", Title = t }).ToList(),
                    Artists = artists.Select((t, i) => new Artist { Id = $"AR{i}", Name = t }).ToList(),
                    Songs = songs.Select((t, i) => new Song { Id = $"v{i}", VideoId = $"v{i}", Title = t }).ToList(),
                };

                var result = LibraryFilter.Filter(content, query);

                if (string.IsNullOrWhiteSpace(query))
                {
                    // Blank query returns every item.
                    Assert.Equal(content.Playlists, result.Playlists);
                    Assert.Equal(content.Albums, result.Albums);
                    Assert.Equal(content.Artists, result.Artists);
                    Assert.Equal(content.Songs, result.Songs);
                    return;
                }

                AssertMatchingSubset(content.Playlists, result.Playlists, p => p.Title, query);
                AssertMatchingSubset(content.Albums, result.Albums, a => a.Title, query);
                AssertMatchingSubset(content.Artists, result.Artists, a => a.Name, query);
                AssertMatchingSubset(content.Songs, result.Songs, x => x.Title, query);
            },
            iter: 100);
    }

    private static void AssertMatchingSubset<T>(
        IReadOnlyList<T> original,
        IReadOnlyList<T> filtered,
        Func<T, string> field,
        string query)
    {
        var needle = query.Trim();

        foreach (var item in filtered)
        {
            // Subset: every result item is one of the originals.
            Assert.Contains(item, (IEnumerable<T>)original);

            // Match: the query appears (case-insensitively) in the item's title/name.
            Assert.True(field(item).Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        // Completeness + order: result is exactly the matching originals, unchanged in order.
        var expected = original
            .Where(x => field(x).Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(expected, filtered);
    }

    // ── Property 30 generators ───────────────────────────────────────────────────────────────

    private static readonly Gen<int?> TrackCount =
        Gen.OneOf(Gen.Const((int?)null), Gen.Int[0, 50].Select(i => (int?)i));

    private static readonly Gen<List<Playlist>> Playlists =
        Gen.Select(Text, TrackCount, Gen.Bool).Array[0, 4].Select(items =>
            items.Select((d, i) => new Playlist
            {
                Id = $"p{i}",
                Title = d.Item1,
                TrackCount = d.Item2,
                IsOwnedByUser = d.Item3,
            }).ToList());

    private static readonly Gen<List<Artist>> Artists =
        Text.Array[0, 4].Select(names =>
            names.Select((n, i) => new Artist { Id = $"a{i}", Name = n }).ToList());

    private static Gen<List<Song>> SongsWithPrefix(string prefix) =>
        Text.Array[0, 4].Select(titles =>
            titles.Select((t, i) => new Song { Id = $"{prefix}{i}", VideoId = $"{prefix}{i}", Title = t }).ToList());

    private static readonly Gen<LibraryState> States =
        from playlists in Playlists
        from liked in SongsWithPrefix("ls")
        from artists in Artists
        from uploaded in SongsWithPrefix("us")
        select new LibraryState
        {
            Playlists = playlists,
            LikedSongs = liked,
            FollowedArtists = artists,
            UploadedSongs = uploaded,
        };

    private static Gen<LibraryMutation> MutationFor(LibraryState state)
    {
        var existingPlaylistIds = state.Playlists.Select(p => p.Id).ToArray();
        var existingArtistIds = state.FollowedArtists.Select(a => a.Id).ToArray();

        // "newp"/"newa" can never collide with the generated "p{i}"/"a{i}" ids.
        var create = Text.Select(t =>
            (LibraryMutation)new CreatePlaylistMutation(
                new Playlist { Id = "newp", Title = t, TrackCount = 0 }, t));

        var follow = Text.Select(n =>
            (LibraryMutation)new FollowArtistMutation(new Artist { Id = "newa", Name = n }));

        var deleteTarget = existingPlaylistIds.Length > 0
            ? Gen.OneOfConst(existingPlaylistIds)
            : Gen.Const("absentp");
        var delete = deleteTarget.Select(id => (LibraryMutation)new DeletePlaylistMutation(id));

        var unfollowTarget = existingArtistIds.Length > 0
            ? Gen.OneOfConst(existingArtistIds)
            : Gen.Const("absenta");
        var unfollow = unfollowTarget.Select(id => (LibraryMutation)new UnfollowArtistMutation(id));

        var addTarget = existingPlaylistIds.Length > 0
            ? Gen.OneOfConst(existingPlaylistIds)
            : Gen.Const("absentp");
        var add = Gen.Select(addTarget, Text).Select((id, t) =>
            (LibraryMutation)new AddSongToPlaylistMutation(
                id, new Song { Id = "addv", VideoId = "addv", Title = t }));

        return Gen.OneOf(create, delete, add, follow, unfollow);
    }

    // Feature: kaset-winui3, Property 30: Mutasi optimistik dapat di-rollback dan konvergen
    // Validates: Requirements 13.6, 13.7
    [Fact]
    public void Property30_Optimistic_mutations_roll_back_and_reconcile_converge()
    {
        var scenario = States.SelectMany(state => MutationFor(state).Select(m => (state, m)));

        scenario.Sample(
            sm =>
            {
                var (state, mutation) = sm;

                // (a) Rollback after an optimistic apply restores the exact original state,
                //     for ANY mutation.
                var application = LibraryContentReconciler.ApplyOptimistic(state, mutation);
                Assert.Equal(state, LibraryContentReconciler.Rollback(application));

                // (b) Reconcile is idempotent: re-asserting the same mutation changes nothing.
                var reconciled = LibraryContentReconciler.Reconcile(state, mutation);
                var reconciledTwice = LibraryContentReconciler.Reconcile(reconciled, mutation);
                Assert.Equal(reconciled, reconciledTwice);

                // (c) A create/follow keeps the item present after reconcile even if the snapshot
                //     lags; a delete/unfollow keeps it absent.
                switch (mutation)
                {
                    case CreatePlaylistMutation create:
                        Assert.Contains(
                            reconciled.Playlists,
                            p => LibraryContentIdentity.SamePlaylist(p.Id, create.Playlist.Id));
                        break;

                    case FollowArtistMutation follow:
                        Assert.Contains(
                            reconciled.FollowedArtists,
                            a => LibraryContentIdentity.SameArtist(a.Id, follow.Artist.Id));
                        break;

                    case DeletePlaylistMutation delete:
                        Assert.DoesNotContain(
                            reconciled.Playlists,
                            p => LibraryContentIdentity.SamePlaylist(p.Id, delete.PlaylistId));
                        break;

                    case UnfollowArtistMutation unfollow:
                        Assert.DoesNotContain(
                            reconciled.FollowedArtists,
                            a => LibraryContentIdentity.SameArtist(a.Id, unfollow.ArtistId));
                        break;
                }
            },
            iter: 100);
    }
}
