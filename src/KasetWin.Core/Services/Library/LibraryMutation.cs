using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Library;

/// <summary>
/// A user-requested change to the Library (CONTEXT.md "Library Mutation Orchestration"). Each
/// concrete mutation is an immutable value describing intent; <see cref="LibraryContentReconciler"/>
/// turns it into optimistic local state transforms and reconciliation against backend snapshots
/// (Req 13.2/13.3/13.4/13.6/13.7), and <see cref="LibraryMutationActions"/> orchestrates the
/// matching <c>IYTMusicClient</c> call.
/// </summary>
public abstract record LibraryMutation;

/// <summary>
/// Create a new playlist (Req 13.2). Carries the optimistic <see cref="Playlist"/> shown
/// immediately (typically with a client-generated temporary id) plus the creation parameters used
/// for the <c>playlist/create</c> call.
/// </summary>
public sealed record CreatePlaylistMutation(
    Playlist Playlist,
    string Title,
    string? Description = null,
    PlaylistPrivacy Privacy = PlaylistPrivacy.Private,
    IReadOnlyList<string>? VideoIds = null) : LibraryMutation;

/// <summary>Delete a playlist the user owns (Req 13.4).</summary>
public sealed record DeletePlaylistMutation(string PlaylistId) : LibraryMutation;

/// <summary>
/// Add a song to one of the user's playlists (Req 13.3). The optimistic effect on the Library
/// landing is a <see cref="Playlist.TrackCount"/> bump on the target playlist; the song list of the
/// playlist itself lives on its detail surface.
/// </summary>
public sealed record AddSongToPlaylistMutation(string PlaylistId, Song Song) : LibraryMutation;

/// <summary>Follow (subscribe to) an artist (Req 15.3, surfaced from the Library).</summary>
public sealed record FollowArtistMutation(Artist Artist) : LibraryMutation;

/// <summary>Unfollow (unsubscribe from) an artist (Req 15.3, surfaced from the Library).</summary>
public sealed record UnfollowArtistMutation(string ArtistId) : LibraryMutation;
