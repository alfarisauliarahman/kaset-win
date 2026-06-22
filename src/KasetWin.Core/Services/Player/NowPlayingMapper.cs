using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// WinRT-free playback status reported to the OS "Now Playing" surface (Req 10.3). Maps onto
/// the platform <c>MediaPlaybackStatus</c> (Closed / Playing / Paused) in the SMTC adapter.
/// </summary>
public enum NowPlayingStatus
{
    /// <summary>Nothing is loaded — the surface is cleared/closed.</summary>
    Closed,

    /// <summary>A track is loaded and playing.</summary>
    Playing,

    /// <summary>A track is loaded but paused.</summary>
    Paused,
}

/// <summary>
/// WinRT-free projection of the current track onto the metadata the OS "Now Playing" surface
/// displays (Req 10.1): title, artist(s), optional album title, and an always-present artwork URI.
/// </summary>
/// <param name="Title">Track title (never <see langword="null"/>; empty when unknown).</param>
/// <param name="Artist">Comma-joined artist names for display.</param>
/// <param name="AlbumTitle">Album title when present and non-empty; otherwise <see langword="null"/>.</param>
/// <param name="ArtworkUri">
/// The track thumbnail, or the deterministic videoId fallback thumbnail so the surface always has
/// artwork.
/// </param>
public sealed record NowPlayingDisplay(string Title, string Artist, string? AlbumTitle, Uri ArtworkUri);

/// <summary>
/// Pure mapping from player state to the OS "Now Playing" surface inputs (Req 10.1/10.3). Lifted
/// out of the WinRT <c>SmtcController</c> so the metadata/status projection is headless-testable;
/// the controller now consumes these results and only owns the SMTC-specific plumbing
/// (<c>DisplayUpdater</c>, <c>MediaPlaybackStatus</c>, thumbnail stream references).
/// </summary>
public static class NowPlayingMapper
{
    /// <summary>
    /// Maps the current track + playing flag onto the system playback status (Req 10.3):
    /// no track → <see cref="NowPlayingStatus.Closed"/>; otherwise
    /// <see cref="NowPlayingStatus.Playing"/> / <see cref="NowPlayingStatus.Paused"/>.
    /// </summary>
    public static NowPlayingStatus MapStatus(Song? track, bool isPlaying) =>
        track is null
            ? NowPlayingStatus.Closed
            : isPlaying
                ? NowPlayingStatus.Playing
                : NowPlayingStatus.Paused;

    /// <summary>
    /// Projects the current track onto display metadata (Req 10.1), or <see langword="null"/> when
    /// nothing is playing (the caller clears the surface).
    /// </summary>
    public static NowPlayingDisplay? MapDisplay(Song? track)
    {
        if (track is null)
        {
            return null;
        }

        var albumTitle = track.Album is { Title.Length: > 0 } album ? album.Title : null;

        // Prefer the track thumbnail; fall back to the deterministic videoId thumbnail so the
        // surface always has artwork.
        var artwork = track.ThumbnailUrl ?? track.FallbackThumbnailUrl;

        return new NowPlayingDisplay(
            Title: track.Title ?? string.Empty,
            Artist: track.ArtistsDisplay,
            AlbumTitle: albumTitle,
            ArtworkUri: artwork);
    }
}
