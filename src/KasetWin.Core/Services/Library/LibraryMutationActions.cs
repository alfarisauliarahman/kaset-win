using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;

namespace KasetWin.Core.Services.Library;

/// <summary>The terminal status of a Library mutation orchestrated by <see cref="LibraryMutationActions"/>.</summary>
public enum LibraryMutationStatus
{
    /// <summary>The backend call succeeded; the published state reflects the accepted change.</summary>
    Succeeded,

    /// <summary>The backend call failed; the state was rolled back to the pre-mutation snapshot.</summary>
    RolledBack,
}

/// <summary>
/// The outcome of <see cref="LibraryMutationActions.ExecuteAsync"/>: the final
/// <see cref="State"/> that was published, the <see cref="Status"/>, the server-resolved playlist
/// id for a create (else <c>null</c>), and the <see cref="Error"/> that triggered a rollback.
/// </summary>
public sealed record LibraryMutationOutcome(
    LibraryState State,
    LibraryMutationStatus Status,
    string? CreatedPlaylistId = null,
    KasetError? Error = null);

/// <summary>
/// Orchestrates a single Library mutation end to end (CONTEXT.md "Library Mutation Orchestration",
/// Req 13.2/13.3/13.4/13.6/13.7): apply the optimistic update immediately, invoke the matching
/// <see cref="IYTMusicClient"/> call, then either schedule reconciliation against a fresh backend
/// snapshot (success) or roll the UI state back (failure).
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> with only an <see cref="IYTMusicClient"/> dependency so the whole
/// optimistic-update / reconcile / rollback sequence can be exercised headless against a fake
/// client (Property 30). The view-model passes a <c>publish</c> callback that mirrors the produced
/// <see cref="LibraryState"/> into its observable collections on the UI thread.
/// </remarks>
public sealed class LibraryMutationActions
{
    private readonly IYTMusicClient _client;

    /// <summary>Creates the orchestrator over the given InnerTube client.</summary>
    public LibraryMutationActions(IYTMusicClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// Executes <paramref name="mutation"/> against <paramref name="current"/>.
    /// </summary>
    /// <param name="current">The current library state (source of truth before the mutation).</param>
    /// <param name="mutation">The user-requested change.</param>
    /// <param name="publish">
    /// Invoked with each state the UI should display: first the optimistic state, then either the
    /// reconciled state (success) or the rolled-back state (failure). Never invoked with a partial
    /// state. May be <c>null</c> when the caller only needs the returned outcome.
    /// </param>
    /// <param name="fetchSnapshot">
    /// Optional backend-snapshot fetch used for reconciliation after a successful mutation. When
    /// provided, the mutation effect is re-asserted on the snapshot via
    /// <see cref="LibraryContentReconciler.Reconcile"/> so a lagging snapshot never drops the
    /// accepted change. When <c>null</c>, the optimistic state stands as the final state.
    /// </param>
    /// <param name="ct">Cancels the backend calls.</param>
    public async Task<LibraryMutationOutcome> ExecuteAsync(
        LibraryState current,
        LibraryMutation mutation,
        Action<LibraryState>? publish = null,
        Func<CancellationToken, Task<LibraryState>>? fetchSnapshot = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);

        var application = LibraryContentReconciler.ApplyOptimistic(current, mutation);
        publish?.Invoke(application.After);

        string? createdId = null;
        LibraryState successState = application.After;
        LibraryMutation reconcileMutation = mutation;

        try
        {
            switch (mutation)
            {
                case CreatePlaylistMutation m:
                    createdId = await _client
                        .CreatePlaylistAsync(m.Title, m.Description, m.Privacy, m.VideoIds, ct)
                        .ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(createdId))
                    {
                        // Swap the temporary optimistic id for the real one so reconciliation
                        // matches the backend snapshot by identity.
                        successState = LibraryContentReconciler.ReplacePlaylistId(
                            application.After, m.Playlist.Id, createdId);
                        reconcileMutation = m with { Playlist = m.Playlist with { Id = createdId } };
                    }

                    break;

                case DeletePlaylistMutation m:
                    await _client.DeletePlaylistAsync(m.PlaylistId, ct).ConfigureAwait(false);
                    break;

                case AddSongToPlaylistMutation m:
                    await _client.AddSongToPlaylistAsync(m.Song.VideoId, m.PlaylistId, ct).ConfigureAwait(false);
                    break;

                case FollowArtistMutation m:
                    await _client.SubscribeArtistAsync(m.Artist.Id, ct).ConfigureAwait(false);
                    break;

                case UnfollowArtistMutation m:
                    await _client.UnsubscribeArtistAsync(m.ArtistId, ct).ConfigureAwait(false);
                    break;
            }
        }
        catch (KasetError error)
        {
            var restored = LibraryContentReconciler.Rollback(application);
            publish?.Invoke(restored);
            return new LibraryMutationOutcome(restored, LibraryMutationStatus.RolledBack, Error: error);
        }

        // Success: reconcile against a fresh backend snapshot when one is available, otherwise the
        // optimistic state stands (Req 13.6).
        if (fetchSnapshot is not null)
        {
            var snapshot = await fetchSnapshot(ct).ConfigureAwait(false);
            var reconciled = LibraryContentReconciler.Reconcile(snapshot, reconcileMutation);
            publish?.Invoke(reconciled);
            return new LibraryMutationOutcome(reconciled, LibraryMutationStatus.Succeeded, createdId);
        }

        publish?.Invoke(successState);
        return new LibraryMutationOutcome(successState, LibraryMutationStatus.Succeeded, createdId);
    }
}
