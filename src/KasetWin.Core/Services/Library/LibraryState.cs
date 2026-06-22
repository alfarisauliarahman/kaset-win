using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Library;

/// <summary>
/// An immutable snapshot of the signed-in user's Library landing surface (Req 13.1): their
/// playlists, liked songs, followed artists, and uploaded songs. This is the unit that
/// <see cref="LibraryContentReconciler"/> transforms when applying optimistic mutations and
/// reconciling against eventually-consistent backend snapshots (Req 13.6/13.7).
/// </summary>
/// <remarks>
/// <para>
/// The record uses <b>structural</b> equality based on item identity (so two states are equal when
/// they expose the same playlists/songs/artists in the same order, regardless of which model
/// instances back them). Playlist equality additionally considers <see cref="Playlist.Title"/> and
/// <see cref="Playlist.TrackCount"/> so an optimistic "add song to playlist" count bump is
/// observable (and therefore reversible) by the reconciler. This makes Property 30 — "apply
/// optimistic then rollback returns to the original; successful reconcile converges to the backend
/// snapshot" — expressible as plain value equality.
/// </para>
/// </remarks>
public sealed record LibraryState
{
    /// <summary>An empty library (no saved content).</summary>
    public static readonly LibraryState Empty = new();

    /// <summary>The user's playlists (Req 13.1).</summary>
    public IReadOnlyList<Playlist> Playlists { get; init; } = [];

    /// <summary>The user's liked songs (Req 13.1).</summary>
    public IReadOnlyList<Song> LikedSongs { get; init; } = [];

    /// <summary>Artists the user follows (Req 13.1).</summary>
    public IReadOnlyList<Artist> FollowedArtists { get; init; } = [];

    /// <summary>Songs the user has uploaded (Req 13.1).</summary>
    public IReadOnlyList<Song> UploadedSongs { get; init; } = [];

    /// <inheritdoc />
    public bool Equals(LibraryState? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return PlaylistsEqual(Playlists, other.Playlists)
            && IdsEqual(LikedSongs, other.LikedSongs, static s => s.Id)
            && IdsEqual(FollowedArtists, other.FollowedArtists, static a => LibraryContentIdentity.ArtistKey(a.Id))
            && IdsEqual(UploadedSongs, other.UploadedSongs, static s => s.Id);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var p in Playlists)
        {
            hash.Add(LibraryContentIdentity.PlaylistKey(p.Id), StringComparer.Ordinal);
            hash.Add(p.Title, StringComparer.Ordinal);
            hash.Add(p.TrackCount);
        }

        foreach (var s in LikedSongs)
        {
            hash.Add(s.Id, StringComparer.Ordinal);
        }

        foreach (var a in FollowedArtists)
        {
            hash.Add(LibraryContentIdentity.ArtistKey(a.Id), StringComparer.Ordinal);
        }

        foreach (var s in UploadedSongs)
        {
            hash.Add(s.Id, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static bool PlaylistsEqual(IReadOnlyList<Playlist> a, IReadOnlyList<Playlist> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!LibraryContentIdentity.SamePlaylist(a[i].Id, b[i].Id)
                || !string.Equals(a[i].Title, b[i].Title, StringComparison.Ordinal)
                || a[i].TrackCount != b[i].TrackCount)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IdsEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, string> key)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(key(a[i]), key(b[i]), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
