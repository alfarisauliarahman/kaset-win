using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Podcasts;

/// <summary>
/// Persists per-episode podcast playback progress and played state (Req 27.3). Entries are keyed by
/// episode <c>videoId</c> and saved through the backing
/// <see cref="KasetWin.Core.Abstractions.ISettingsStore"/>, so a fresh store-backed instance over
/// the same persistence reproduces the exact progress/played state (Property 41).
/// </summary>
/// <remarks>
/// The service is intentionally WinUI/WinRT-free so it can be exercised headless; the platform store
/// (<c>LocalSettingsStore</c>) supplies real on-disk persistence in the app, while
/// <see cref="KasetWin.Core.Services.Settings.InMemorySettingsStore"/> backs tests.
/// </remarks>
public interface IEpisodeProgressStore
{
    /// <summary>All persisted episode progress entries (order unspecified).</summary>
    IReadOnlyCollection<EpisodeProgress> Entries { get; }

    /// <summary>Returns the stored progress for <paramref name="episodeId"/>, or <see langword="null"/>.</summary>
    EpisodeProgress? Get(string episodeId);

    /// <summary>Whether progress is stored for <paramref name="episodeId"/>.</summary>
    bool Contains(string episodeId);

    /// <summary>
    /// Stores (or overwrites) the progress for <paramref name="episodeId"/> and persists it
    /// immediately (Req 27.3). <paramref name="positionSeconds"/> is clamped to be non-negative.
    /// </summary>
    /// <returns>The stored (clamped) <see cref="EpisodeProgress"/>.</returns>
    EpisodeProgress Save(string episodeId, double positionSeconds, bool played);

    /// <summary>Removes any stored progress for <paramref name="episodeId"/> if present.</summary>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    bool Remove(string episodeId);

    /// <summary>(Re)hydrates <see cref="Entries"/> from the backing store.</summary>
    void Reload();

    /// <summary>Raised after the stored progress changes (save/remove).</summary>
    event EventHandler? Changed;
}
