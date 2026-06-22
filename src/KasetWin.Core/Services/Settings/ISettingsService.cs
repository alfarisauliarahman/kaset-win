using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Settings;

/// <summary>
/// Persists and exposes user preferences (Req 18, Req 7.3). Each property setter writes through to
/// the backing <see cref="KasetWin.Core.Abstractions.ISettingsStore"/> immediately (Req 18.4) and
/// raises <see cref="Changed"/>. Reads are served from in-memory backing fields hydrated from the
/// store at construction. The persisted preference set round-trips exactly (Property 32).
/// </summary>
/// <remarks>
/// The service is intentionally WinUI/WinRT-free so it can be exercised headless; the platform store
/// (<c>LocalSettingsStore</c>) supplies real persistence in the app. The
/// <see cref="RememberPlaybackSettings"/> flag gates whether a consumer (PlayerService) restores the
/// saved shuffle/repeat values on launch; the saved values themselves are always persisted so the
/// snapshot remains loss-less for round-trip purposes.
/// </remarks>
public interface ISettingsService
{
    /// <summary>Page the app opens to on next launch (Req 18.1).</summary>
    LaunchPage DefaultLaunchPage { get; set; }

    /// <summary>Whether shuffle/repeat should be restored on next launch (Req 18.2).</summary>
    bool RememberPlaybackSettings { get; set; }

    /// <summary>Whether synced lyrics are searched before plain-lyric fallback (Req 18.3).</summary>
    bool SyncedLyricsEnabled { get; set; }

    /// <summary>Preferred streaming audio quality (Req 7.3).</summary>
    AudioQuality PreferredAudioQuality { get; set; }

    /// <summary>Last persisted repeat mode, restored on launch when remembering is enabled (Req 18.2).</summary>
    RepeatMode SavedRepeatMode { get; set; }

    /// <summary>Last persisted shuffle state, restored on launch when remembering is enabled (Req 18.2).</summary>
    bool SavedShuffle { get; set; }

    /// <summary>Raised after any preference changes, carrying a snapshot of the new state.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// (Re)hydrates all properties from the backing store, applying defaults for absent keys.
    /// Does not raise <see cref="Changed"/>.
    /// </summary>
    void Load();

    /// <summary>Returns an immutable snapshot of the current preferences.</summary>
    SettingsState Snapshot();

    /// <summary>Applies every preference from <paramref name="state"/>, persisting each one.</summary>
    void Apply(SettingsState state);
}

/// <summary>
/// Event payload for <see cref="ISettingsService.Changed"/>.
/// </summary>
/// <param name="PropertyName">
/// Name of the changed property, or <see langword="null"/> when several changed at once
/// (e.g. via <see cref="ISettingsService.Apply"/>).
/// </param>
/// <param name="State">Snapshot of the preferences after the change.</param>
public sealed record SettingsChangedEventArgs(string? PropertyName, SettingsState State);
