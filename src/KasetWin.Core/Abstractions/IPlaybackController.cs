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

    /// <summary>Switches the WebView2 surface between Hidden / MiniPlayer / Video (Req 26).</summary>
    Task SetDisplayModeAsync(PlaybackDisplayMode mode);

    /// <summary>Stops audio and releases the WebView2 when the app quits (Req 1.5).</summary>
    Task ReleaseAsync();
}
