using KasetWin.Core.Models;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// Display mode for the (singleton) playback WebView2. Hidden uses a 1×1 surface for
/// background audio (Req 1.1/1.4), MiniPlayer a small floating surface, and Video a full
/// video surface (Req 26). Implementation of the non-hidden modes is phased — the seam
/// lives here so the contract does not change later (Design: "Video & floating/PiP").
/// </summary>
public enum PlaybackDisplayMode
{
    /// <summary>1×1 hidden surface — audio only (default).</summary>
    Hidden,

    /// <summary>Small floating surface (mini player).</summary>
    MiniPlayer,

    /// <summary>Full video surface (Req 26).</summary>
    Video,
}

/// <summary>
/// Abstraction over the single hidden WebView2 that performs DRM playback (Req 1, 7).
/// </summary>
/// <remarks>
/// Defined in <c>Core</c> so <c>PlayerService</c> is unaware of WebView2/WinRT; the WinRT
/// implementation (<c>WebView2PlaybackController</c>) lives in <c>KasetWin.Platform</c>
/// (Design: "Inversi dependensi WebView2/SMTC"). All control surfaces are asynchronous
/// because they marshal to the WebView2 message loop and execute scripts on the page.
/// </remarks>
public interface IPlaybackController
{
    /// <summary>
    /// Whether Widevine DRM is available on the current WebView2 runtime (Req 1.7).
    /// When <c>false</c>, the app surfaces an error explaining playback is unavailable.
    /// </summary>
    bool IsDrmAvailable { get; }

    /// <summary>The videoId currently loaded into the WebView2, or <c>null</c> when none.</summary>
    string? CurrentVideoId { get; }

    /// <summary>Creates the singleton WebView2 once, if it has not been created yet (Req 1.1).</summary>
    Task EnsureInitializedAsync();

    /// <summary>
    /// Loads <c>https://music.youtube.com/watch?v={videoId}</c> into the WebView2, pausing
    /// the current audio before navigating to the new videoId (pause-before-load, Req 1.2/1.6).
    /// </summary>
    Task LoadVideoAsync(string videoId);

    /// <summary>Resumes playback of the loaded video.</summary>
    Task PlayAsync();

    /// <summary>Pauses playback of the loaded video.</summary>
    Task PauseAsync();

    /// <summary>
    /// Requests YouTube Music's own next item. Used when the native queue has reached the end and
    /// playback is following the WebView autoplay chain.
    /// </summary>
    Task SkipToNextAsync();

    /// <summary>
    /// Requests YouTube Music's own previous item. Used when there is no native queue history to
    /// restore.
    /// </summary>
    Task SkipToPreviousAsync();

    /// <summary>
    /// Seeks to <paramref name="positionSeconds"/>. Disabled while a live stream is playing
    /// (Req 9.2) — the caller is responsible for not seeking on live content.
    /// </summary>
    Task SeekAsync(double positionSeconds);

    /// <summary>Sets the playback volume on the 0–100 range (Req 5.5).</summary>
    Task SetVolumeAsync(int volume0to100);

    /// <summary>Mutes or unmutes the audio (Req 5.6).</summary>
    Task SetMutedAsync(bool muted);

    /// <summary>
    /// Requests an audio-quality preference on the running player (Req 7). Treated as a
    /// preference request rather than a stream guarantee. See
    /// <see cref="AudioQualityMap.ToYouTubeValue"/> for the mapping to the YouTube value.
    /// </summary>
    Task SetAudioQualityAsync(AudioQuality quality);

    /// <summary>
    /// Applies the 9-band graphic equalizer to the running player. <paramref name="gainsDb"/> holds
    /// the per-band gains in dB (-12..+12) for 62 Hz … 16 kHz; when <paramref name="enabled"/> is
    /// <see langword="false"/> the bands are flattened (transparent). Best-effort: the audio path is
    /// only routed through the EQ graph once enabled, so it never silences playback when unused.
    /// </summary>
    Task SetEqualizerAsync(bool enabled, IReadOnlyList<int> gainsDb);

    /// <summary>Sets the playback speed of the underlying media element (0.25–3.0, 1 = normal).</summary>
    Task SetPlaybackRateAsync(double rate);

    /// <summary>
    /// Enables/disables native looping of the current media element (<c>video.loop</c>). Used for
    /// Repeat One so the same track loops seamlessly on the web player itself instead of relying on
    /// the <c>ended</c> → re-seek round-trip, which YouTube Music's own autoplay can beat to the punch
    /// (the "repeat doesn't stick / jumps to another song" bug). Re-applied after each navigation.
    /// </summary>
    Task SetRepeatOneAsync(bool enabled);

    /// <summary>Switches the WebView2 surface between Hidden / MiniPlayer / Video (Req 26).</summary>
    Task SetDisplayModeAsync(PlaybackDisplayMode mode);

    /// <summary>Stops audio and releases the WebView2 when the app quits (Req 1.5).</summary>
    Task ReleaseAsync();
}
