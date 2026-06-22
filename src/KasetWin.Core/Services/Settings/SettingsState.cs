using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Settings;

/// <summary>
/// Immutable snapshot of every persisted user preference (Req 18). Used to read or apply the
/// full preference set atomically and as the value type exercised by the persistence round-trip
/// (Property 32: <c>save(state)</c> then <c>load()</c> yields an equal state).
/// </summary>
/// <param name="DefaultLaunchPage">Page the app opens to on next launch (Req 18.1).</param>
/// <param name="RememberPlaybackSettings">
/// Whether shuffle/repeat are restored on next launch (Req 18.2). Note this flag gates
/// <em>restoration</em> at read time; the saved shuffle/repeat values themselves always
/// round-trip independently so the snapshot is loss-less.
/// </param>
/// <param name="SyncedLyricsEnabled">
/// Whether synced lyrics are searched before falling back to plain lyrics (Req 18.3).
/// </param>
/// <param name="PreferredAudioQuality">Preferred streaming quality (Req 7.3).</param>
/// <param name="SavedRepeatMode">Last persisted repeat mode (Req 18.2).</param>
/// <param name="SavedShuffle">Last persisted shuffle state (Req 18.2).</param>
public sealed record SettingsState(
    LaunchPage DefaultLaunchPage,
    bool RememberPlaybackSettings,
    bool SyncedLyricsEnabled,
    AudioQuality PreferredAudioQuality,
    RepeatMode SavedRepeatMode,
    bool SavedShuffle)
{
    /// <summary>
    /// The out-of-the-box defaults applied when no value has been stored yet: open on
    /// <see cref="LaunchPage.Home"/>, do not remember playback, prefer synced lyrics, and stream
    /// at <see cref="AudioQuality.High"/> with repeat off and shuffle disabled.
    /// </summary>
    public static SettingsState Defaults { get; } = new(
        DefaultLaunchPage: LaunchPage.Home,
        RememberPlaybackSettings: false,
        SyncedLyricsEnabled: true,
        PreferredAudioQuality: AudioQuality.High,
        SavedRepeatMode: RepeatMode.Off,
        SavedShuffle: false);
}
