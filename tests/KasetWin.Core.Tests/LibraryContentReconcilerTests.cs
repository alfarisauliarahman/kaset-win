using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Library;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Example-based unit tests for the pure Library reconciliation core (task 14.5, Req 13.6/13.7).
/// These exercise <see cref="LibraryContentReconciler"/> (apply / reconcile / rollback) and the
/// <see cref="LibraryMutationActions"/> orchestration against a fake client. The exhaustive
/// universal guarantees are covered later by Property 30 (task 14.12).
/// </summary>
public class LibraryContentReconcilerTests
{
    private static Playlist Playlist(string id, string title, int? count = 0, bool owned = true)
        => new() { Id = id, Title = title, TrackCount = count, IsOwnedByUser = owned };

    private static Artist Artist(string id, string name) => new() { Id = id, Name = name };

    private static Song Song(string videoId, string title)
        => new() { Id = videoId, VideoId = videoId, Title = title };

    private static LibraryState SampleState() => new()
    {
        Playlists = [Playlist("VLPL_a", "Alpha", 3), Playlist("VLPL_b", "Beta", 1)],
        FollowedArtists = [Artist("UC_one", "One")],
        LikedSongs = [Song("v1", "Liked 1")],
        UploadedSongs = [Song("u1", "Upload 1")],
    };

    [Fact]
    public void ApplyOptimistic_then_Rollback_restores_original_for_create()
    {
        var state = SampleState();
        var mutation = new CreatePlaylistMutation(Playlist("PL_temp", "New Mix"), "New Mix");

        var application = LibraryContentReconciler.ApplyOptimistic(state, mutation);

        Assert.NotEqual(state, application.After);                  // optimistic change visible
        Assert.Equal(3, application.After.Playlists.Count);
        Assert.Equal("PL_temp", application.After.Playlists[0].Id); // newest prepended
        Assert.Equal(state, LibraryContentReconciler.Rollback(application));
    }

    [Fact]
    public void ApplyOptimistic_then_Rollback_restores_original_for_delete()
    {
        var state = SampleState();
        var mutation = new DeletePlaylistMutation("VLPL_a");

        var application = LibraryContentReconciler.ApplyOptimistic(state, mutation);

        Assert.DoesNotContain(application.After.Playlists, p => p.Id == "VLPL_a");
        Assert.Equal(state, LibraryContentReconciler.Rollback(application));
    }

    [Fact]
    public void Delete_is_identity_aware_across_VL_and_raw_ids()
    {
        var state = SampleState();

        // The raw playlist id (no VL wrapper) removes the VL-prefixed entry.
        var after = LibraryContentReconciler.ApplyOptimistic(state, new DeletePlaylistMutation("PL_a")).After;

        Assert.DoesNotContain(after.Playlists, p => p.Id == "VLPL_a");
        Assert.Single(after.Playlists);
    }

    [Fact]
    public void AddSong_bumps_track_count_and_rolls_back()
    {
        var state = SampleState();
        var mutation = new AddSongToPlaylistMutation("VLPL_b", Song("v9", "Track"));

        var application = LibraryContentReconciler.ApplyOptimistic(state, mutation);

        Assert.Equal(2, application.After.Playlists.Single(p => p.Id == "VLPL_b").TrackCount);
        Assert.Equal(state, LibraryContentReconciler.Rollback(application));
    }

    [Fact]
    public void Reconcile_converges_to_backend_snapshot_when_it_reflects_the_create()
    {
        // Backend snapshot already contains the created playlist (by identity: VL + raw match).
        var snapshot = SampleState() with
        {
            Playlists = [Playlist("VLPL_new", "New Mix", 0), Playlist("VLPL_a", "Alpha", 3), Playlist("VLPL_b", "Beta", 1)],
        };
        var mutation = new CreatePlaylistMutation(Playlist("PL_new", "New Mix"), "New Mix");

        var reconciled = LibraryContentReconciler.Reconcile(snapshot, mutation);

        Assert.Equal(snapshot, reconciled); // converged — no duplicate added
    }

    [Fact]
    public void Reconcile_keeps_accepted_create_visible_when_snapshot_lags()
    {
        // Backend snapshot has NOT yet caught up to the create.
        var snapshot = SampleState();
        var mutation = new CreatePlaylistMutation(Playlist("VLPL_new", "New Mix"), "New Mix");

        var reconciled = LibraryContentReconciler.Reconcile(snapshot, mutation);

        Assert.Contains(reconciled.Playlists, p => p.Id == "VLPL_new");
    }

    [Fact]
    public void Reconcile_keeps_accepted_delete_suppressed_when_snapshot_lags()
    {
        var snapshot = SampleState(); // still lists VLPL_a
        var mutation = new DeletePlaylistMutation("VLPL_a");

        var reconciled = LibraryContentReconciler.Reconcile(snapshot, mutation);

        Assert.DoesNotContain(reconciled.Playlists, p => p.Id == "VLPL_a");
    }

    [Fact]
    public async Task Orchestrator_publishes_optimistic_then_resolves_created_id_on_success()
    {
        var state = SampleState();
        var client = new FakeYTMusicClient { CreatedPlaylistId = "PL_real" };
        var actions = new LibraryMutationActions(client);
        var published = new List<LibraryState>();
        var mutation = new CreatePlaylistMutation(Playlist("PL_temp", "Fresh"), "Fresh");

        var outcome = await actions.ExecuteAsync(state, mutation, published.Add);

        Assert.Equal(LibraryMutationStatus.Succeeded, outcome.Status);
        Assert.Equal("PL_real", outcome.CreatedPlaylistId);
        Assert.Equal("PL_temp", published[0].Playlists[0].Id); // optimistic first
        Assert.Equal("PL_real", outcome.State.Playlists[0].Id); // resolved id last
    }

    [Fact]
    public async Task Orchestrator_rolls_back_on_api_failure()
    {
        var state = SampleState();
        var client = new FakeYTMusicClient { DeleteError = new KasetError(KasetErrorKind.NetworkError, "offline") };
        var actions = new LibraryMutationActions(client);
        var published = new List<LibraryState>();

        var outcome = await actions.ExecuteAsync(state, new DeletePlaylistMutation("VLPL_a"), published.Add);

        Assert.Equal(LibraryMutationStatus.RolledBack, outcome.Status);
        Assert.Equal(state, outcome.State);                              // restored
        Assert.DoesNotContain(published[0].Playlists, p => p.Id == "VLPL_a"); // optimistic removal shown first
        Assert.Equal(state, published[^1]);                             // rollback published last
    }

    [Fact]
    public async Task Orchestrator_reconciles_with_snapshot_when_provided()
    {
        var state = SampleState();
        var client = new FakeYTMusicClient();
        var actions = new LibraryMutationActions(client);
        var backend = SampleState(); // backend still shows the deleted playlist (lagging)

        var outcome = await actions.ExecuteAsync(
            state,
            new DeletePlaylistMutation("VLPL_a"),
            publish: null,
            fetchSnapshot: _ => Task.FromResult(backend));

        Assert.Equal(LibraryMutationStatus.Succeeded, outcome.Status);
        Assert.DoesNotContain(outcome.State.Playlists, p => p.Id == "VLPL_a");
    }
}
