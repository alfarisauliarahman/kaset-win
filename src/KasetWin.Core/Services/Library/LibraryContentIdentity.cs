namespace KasetWin.Core.Services.Library;

/// <summary>
/// Pure identity rules used to decide whether two Library items refer to the same saved content
/// (CONTEXT.md "Library Content Identity"). YouTube Music can expose the same playlist as either a
/// <c>VL...</c> browse id or a raw playlist id, and the same followed artist as either an
/// <c>MPLAUC...</c> library browse id or a public <c>UC...</c> channel id. Kaset treats those
/// equivalent forms as one Library item so optimistic mutations and backend snapshots reconcile
/// against a stable key (Req 13.6/13.7).
/// </summary>
/// <remarks>
/// All comparisons are ordinal. The helpers are total and never throw: <c>null</c>/empty input maps
/// to the empty string. They normalise the well-known prefixes only and otherwise return the id
/// unchanged, so unrecognised ids still compare by their raw value.
/// </remarks>
public static class LibraryContentIdentity
{
    /// <summary>
    /// Canonical key for a playlist id. A leading <c>VL</c> wrapper is stripped so that the browse
    /// form (<c>VLPL123</c>) and the raw playlist id (<c>PL123</c>) collapse to the same key.
    /// </summary>
    /// <param name="playlistId">A playlist browse id or raw playlist id. <c>null</c>/empty → empty.</param>
    public static string PlaylistKey(string? playlistId)
    {
        if (string.IsNullOrEmpty(playlistId))
        {
            return string.Empty;
        }

        return playlistId.StartsWith("VL", StringComparison.Ordinal)
            ? playlistId[2..]
            : playlistId;
    }

    /// <summary>
    /// Canonical key for an artist/channel id. A leading <c>MPLA</c> library wrapper is stripped so
    /// that the library form (<c>MPLAUC...</c>) and the public channel id (<c>UC...</c>) collapse to
    /// the same key.
    /// </summary>
    /// <param name="artistId">An artist browse id or channel id. <c>null</c>/empty → empty.</param>
    public static string ArtistKey(string? artistId)
    {
        if (string.IsNullOrEmpty(artistId))
        {
            return string.Empty;
        }

        return artistId.StartsWith("MPLA", StringComparison.Ordinal)
            ? artistId[4..]
            : artistId;
    }

    /// <summary>Whether two playlist ids refer to the same saved playlist (identity-aware).</summary>
    public static bool SamePlaylist(string? a, string? b)
        => string.Equals(PlaylistKey(a), PlaylistKey(b), StringComparison.Ordinal);

    /// <summary>Whether two artist ids refer to the same followed artist (identity-aware).</summary>
    public static bool SameArtist(string? a, string? b)
        => string.Equals(ArtistKey(a), ArtistKey(b), StringComparison.Ordinal);
}
