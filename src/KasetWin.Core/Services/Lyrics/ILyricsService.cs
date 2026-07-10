using System.ComponentModel;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// Coordinates lyric resolution for the currently displayed track (ADR-0012 service boundary).
/// Owns the current <see cref="LyricResult"/>, the active provider label, an in-memory cache keyed
/// by <c>videoId</c> (Req 17.4), and stale-request protection so a quick track change never shows
/// the previous track's lyrics (Req 17.4). Extends <see cref="INotifyPropertyChanged"/> so the UI
/// can bind to its observable state.
/// </summary>
public interface ILyricsService : INotifyPropertyChanged
{
    /// <summary>The resolved lyrics for the most recently loaded track, or <see langword="null"/>.</summary>
    LyricResult? CurrentLyrics { get; }

    /// <summary>Label of the provider that produced <see cref="CurrentLyrics"/> (e.g. <c>"LRCLib"</c>), or <see langword="null"/>.</summary>
    string? ActiveProvider { get; }

    /// <summary>Preferred provider name whose results win ties; <c>null</c>/empty = automatic (any).</summary>
    string? PreferredProvider { get; set; }

    /// <summary><see langword="true"/> while a lookup is in flight.</summary>
    bool IsLoading { get; }

    /// <summary>
    /// Resolves lyrics for <paramref name="info"/> and updates <see cref="CurrentLyrics"/>,
    /// <see cref="ActiveProvider"/>, and <see cref="IsLoading"/>. Cached results for the same
    /// <c>videoId</c> are returned without re-querying providers (Req 17.4). Results from a call
    /// that has been superseded by a newer <see cref="LoadForTrackAsync"/> are discarded (Req 17.4).
    /// </summary>
    Task LoadForTrackAsync(LyricsSearchInfo info, CancellationToken ct = default);

    /// <summary>
    /// Drops the cached result for <paramref name="videoId"/> so the next
    /// <see cref="LoadForTrackAsync"/> re-queries providers (e.g. after the user picked a
    /// different caption track).
    /// </summary>
    void Invalidate(string videoId);
}
