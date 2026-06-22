using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// A source of lyrics for a track (ADR-0012 provider layer). Implementations resolve a
/// <see cref="LyricsSearchInfo"/> into a <see cref="LyricResult"/> (<c>Synced</c>, <c>Plain</c>,
/// or <c>Unavailable</c>) without any UI or WinUI/WinRT dependency, so they stay headless-testable.
/// </summary>
/// <remarks>
/// Providers must never throw for an ordinary "no lyrics" outcome; they return
/// <see cref="LyricResult.Unavailable"/> instead. Transport faults (network/HTTP) may surface as
/// exceptions for <see cref="ILyricsService"/> to log and treat as unavailable.
/// </remarks>
public interface ILyricsProvider
{
    /// <summary>A short, stable, non-secret label identifying this provider (e.g. <c>"LRCLib"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Searches this provider for lyrics matching <paramref name="info"/>.
    /// </summary>
    /// <param name="info">Track-identifying search information (title/artist/album/duration).</param>
    /// <param name="ct">Cancels the lookup (e.g. when the user skips to another track).</param>
    /// <returns>
    /// A <see cref="LyricResult"/>: <see cref="LyricResult.Synced"/> when timed lyrics are found,
    /// <see cref="LyricResult.Plain"/> when only plain text is found, or
    /// <see cref="LyricResult.Unavailable"/> when nothing matches.
    /// </returns>
    Task<LyricResult> SearchAsync(LyricsSearchInfo info, CancellationToken ct = default);
}
