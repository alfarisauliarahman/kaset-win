using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Library;

/// <summary>
/// The result of applying an optimistic mutation: the captured <see cref="Before"/> state, the
/// optimistic <see cref="After"/> state to show immediately, and the originating
/// <see cref="Mutation"/>. The orchestrator shows <see cref="After"/> right away and either keeps
/// it (success) or restores <see cref="Before"/> via <see cref="LibraryContentReconciler.Rollback"/>
/// (failure) — Req 13.6/13.7.
/// </summary>
public sealed record OptimisticApplication(LibraryState Before, LibraryState After, LibraryMutation Mutation);

/// <summary>
/// Pure, dependency-free reconciliation of optimistic Library mutations with eventually-consistent
/// YouTube Music Library snapshots (CONTEXT.md "Library Content Reconciliation"). Kaset keeps
/// locally added items visible and locally removed items suppressed until backend responses
/// stabilise (Req 13.6/13.7).
/// </summary>
/// <remarks>
/// <para>This type is the headless-testable core behind Property 30:</para>
/// <list type="bullet">
///   <item><description>
///     <see cref="ApplyOptimistic"/> followed by <see cref="Rollback"/> returns the exact original
///     state (rollback is snapshot-restore, so it is correct for <em>any</em> mutation even when an
///     optimistic edit is not cleanly invertible from the post-state alone).
///   </item></description>
///   <item><description>
///     <see cref="Reconcile"/> idempotently re-asserts a successful mutation on top of a backend
///     snapshot. When the snapshot already reflects the mutation the result equals the snapshot
///     (convergence); when the snapshot lags, the user's accepted change stays visible/suppressed.
///   </item></description>
/// </list>
/// <para>All membership checks use <see cref="LibraryContentIdentity"/> so the <c>VL</c>/<c>PL</c>
/// and <c>MPLAUC</c>/<c>UC</c> equivalent forms collapse to one item.</para>
/// </remarks>
public static class LibraryContentReconciler
{
    /// <summary>
    /// Applies <paramref name="mutation"/> optimistically to <paramref name="state"/>, capturing the
    /// original so it can be restored on failure. The forward transform is idempotent for
    /// membership changes (create/delete/follow/unfollow); an
    /// <see cref="AddSongToPlaylistMutation"/> bumps the target playlist's track count.
    /// </summary>
    public static OptimisticApplication ApplyOptimistic(LibraryState state, LibraryMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mutation);

        var after = mutation switch
        {
            CreatePlaylistMutation m => EnsurePlaylist(state, m.Playlist),
            DeletePlaylistMutation m => RemovePlaylist(state, m.PlaylistId),
            AddSongToPlaylistMutation m => BumpTrackCount(state, m.PlaylistId, +1),
            FollowArtistMutation m => EnsureArtist(state, m.Artist),
            UnfollowArtistMutation m => RemoveArtist(state, m.ArtistId),
            _ => state,
        };

        return new OptimisticApplication(state, after, mutation);
    }

    /// <summary>
    /// Reconciles a successful <paramref name="mutation"/> with a fresh backend
    /// <paramref name="snapshot"/>. The mutation's effect is re-asserted idempotently so a lagging
    /// snapshot never drops a just-accepted change. When the snapshot already reflects the mutation,
    /// the returned state equals <paramref name="snapshot"/> (convergence).
    /// </summary>
    public static LibraryState Reconcile(LibraryState snapshot, LibraryMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mutation);

        return mutation switch
        {
            CreatePlaylistMutation m => EnsurePlaylist(snapshot, m.Playlist),
            DeletePlaylistMutation m => RemovePlaylist(snapshot, m.PlaylistId),
            FollowArtistMutation m => EnsureArtist(snapshot, m.Artist),
            UnfollowArtistMutation m => RemoveArtist(snapshot, m.ArtistId),

            // The landing snapshot is authoritative for a playlist's track count, so an
            // add-song mutation needs no re-assertion here — it is already converged.
            AddSongToPlaylistMutation => snapshot,
            _ => snapshot,
        };
    }

    /// <summary>Restores the pre-mutation state captured by <see cref="ApplyOptimistic"/> (Req 13.7).</summary>
    public static LibraryState Rollback(OptimisticApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.Before;
    }

    /// <summary>
    /// Replaces a playlist's id throughout <paramref name="state"/> — used by the orchestrator to
    /// swap a client-generated temporary id for the real id returned by <c>playlist/create</c> so
    /// the optimistic entry reconciles against the backend snapshot by identity.
    /// </summary>
    public static LibraryState ReplacePlaylistId(LibraryState state, string oldId, string newId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var playlists = state.Playlists;
        var index = IndexOfPlaylist(playlists, oldId);
        if (index < 0)
        {
            return state;
        }

        var updated = playlists.ToList();
        updated[index] = updated[index] with { Id = newId };
        return state with { Playlists = updated };
    }

    // ── Pure transforms ─────────────────────────────────────────────────────────────────────

    private static LibraryState EnsurePlaylist(LibraryState state, Playlist playlist)
    {
        if (IndexOfPlaylist(state.Playlists, playlist.Id) >= 0)
        {
            return state;
        }

        var updated = new List<Playlist>(state.Playlists.Count + 1) { playlist };
        updated.AddRange(state.Playlists);
        return state with { Playlists = updated };
    }

    private static LibraryState RemovePlaylist(LibraryState state, string playlistId)
    {
        var updated = state.Playlists
            .Where(p => !LibraryContentIdentity.SamePlaylist(p.Id, playlistId))
            .ToList();

        return updated.Count == state.Playlists.Count
            ? state
            : state with { Playlists = updated };
    }

    private static LibraryState BumpTrackCount(LibraryState state, string playlistId, int delta)
    {
        var index = IndexOfPlaylist(state.Playlists, playlistId);
        if (index < 0)
        {
            return state;
        }

        var updated = state.Playlists.ToList();
        var current = updated[index];
        var newCount = Math.Max(0, (current.TrackCount ?? 0) + delta);
        updated[index] = current with { TrackCount = newCount };
        return state with { Playlists = updated };
    }

    private static LibraryState EnsureArtist(LibraryState state, Artist artist)
    {
        if (IndexOfArtist(state.FollowedArtists, artist.Id) >= 0)
        {
            return state;
        }

        var updated = new List<Artist>(state.FollowedArtists.Count + 1) { artist };
        updated.AddRange(state.FollowedArtists);
        return state with { FollowedArtists = updated };
    }

    private static LibraryState RemoveArtist(LibraryState state, string artistId)
    {
        var updated = state.FollowedArtists
            .Where(a => !LibraryContentIdentity.SameArtist(a.Id, artistId))
            .ToList();

        return updated.Count == state.FollowedArtists.Count
            ? state
            : state with { FollowedArtists = updated };
    }

    private static int IndexOfPlaylist(IReadOnlyList<Playlist> playlists, string id)
    {
        for (var i = 0; i < playlists.Count; i++)
        {
            if (LibraryContentIdentity.SamePlaylist(playlists[i].Id, id))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfArtist(IReadOnlyList<Artist> artists, string id)
    {
        for (var i = 0; i < artists.Count; i++)
        {
            if (LibraryContentIdentity.SameArtist(artists[i].Id, id))
            {
                return i;
            }
        }

        return -1;
    }
}
