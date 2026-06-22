using KasetWin.Core.Errors;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Pure, dependency-free helpers for the YouTube Music id quirks that mutation endpoints
/// depend on. Kept <c>static</c> and side-effect-free so they can be exercised directly by
/// property-based tests (Property 28 and Property 36) without spinning up the HTTP client.
/// </summary>
/// <remarks>
/// These behaviours are documented in <c>docs/api-discovery.md</c> (Podcast ID Format and
/// Edit Playlist sections). The conversions are deterministic and carry no secret material.
/// </remarks>
public static class YTMusicIds
{
    /// <summary>Prefix that playlist <em>browse</em> ids carry but mutation endpoints reject.</summary>
    public const string PlaylistBrowsePrefix = "VL";

    /// <summary>Prefix identifying a podcast show browse id.</summary>
    public const string PodcastShowPrefix = "MPSPP";

    /// <summary>
    /// Strips a leading <c>VL</c> from a playlist browse id so it is accepted by mutation
    /// endpoints such as <c>browse/edit_playlist</c> (Req 13.3). Ids without the prefix are
    /// returned unchanged.
    /// </summary>
    /// <param name="playlistId">The playlist id, possibly prefixed with <c>VL</c>.</param>
    /// <returns>The id with at most one leading <c>VL</c> removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="playlistId"/> is <see langword="null"/>.</exception>
    public static string StripVlPrefix(string playlistId)
    {
        ArgumentNullException.ThrowIfNull(playlistId);

        return playlistId.StartsWith(PlaylistBrowsePrefix, StringComparison.Ordinal)
            ? playlistId[PlaylistBrowsePrefix.Length..]
            : playlistId;
    }

    /// <summary>
    /// Converts a podcast show browse id (<c>MPSPP{L...}</c>) to the playlist id used for
    /// subscribe / unsubscribe mutations (Req 27.4). The five-character <c>MPSPP</c> prefix is
    /// stripped and a single <c>P</c> is prepended; because the remaining suffix already starts
    /// with <c>L</c>, the result begins with <c>PL</c> without ever producing a double-<c>L</c>.
    /// </summary>
    /// <param name="showId">A podcast show browse id of the form <c>MPSPPL{suffix}</c>.</param>
    /// <returns>The converted playlist id of the form <c>P{suffix}</c> (i.e. <c>PL...</c>).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="showId"/> is <see langword="null"/>.</exception>
    /// <exception cref="KasetError">
    /// Thrown (<see cref="KasetErrorKind.ParseError"/>) when <paramref name="showId"/> lacks the
    /// <c>MPSPP</c> prefix, has an empty suffix, or whose suffix does not start with <c>L</c>.
    /// Adding <c>"PL"</c> to such ids would create a double-<c>L</c> that returns HTTP 404.
    /// </exception>
    public static string ConvertPodcastShowIdToPlaylistId(string showId)
    {
        ArgumentNullException.ThrowIfNull(showId);

        if (!showId.StartsWith(PodcastShowPrefix, StringComparison.Ordinal))
        {
            throw new KasetError(
                KasetErrorKind.ParseError,
                "Podcast show id is missing the expected 'MPSPP' prefix.");
        }

        // Drop the 5-char "MPSPP" prefix; the remaining suffix must already start with 'L'.
        var suffix = showId[PodcastShowPrefix.Length..];

        if (suffix.Length == 0)
        {
            throw new KasetError(
                KasetErrorKind.ParseError,
                "Podcast show id has an empty suffix after the 'MPSPP' prefix.");
        }

        if (suffix[0] != 'L')
        {
            throw new KasetError(
                KasetErrorKind.ParseError,
                "Podcast show id suffix does not start with 'L'; converting would produce an invalid playlist id.");
        }

        // Prepend "P" (NOT "PL") — the suffix already carries the leading 'L'.
        return string.Concat("P", suffix);
    }
}
