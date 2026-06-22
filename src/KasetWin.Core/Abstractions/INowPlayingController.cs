namespace KasetWin.Core.Abstractions;

/// <summary>
/// Abstraction over the OS "Now Playing" surface and system media buttons — on Windows the
/// System Media Transport Controls (SMTC) (Req 10).
/// </summary>
/// <remarks>
/// <para>
/// Defined in <c>Core</c> (no WinUI/WinRT dependency) so the player layer never references the
/// SMTC directly; the WinRT implementation (<c>SmtcController</c>) lives in
/// <c>KasetWin.Platform</c> (Design: "Inversi dependensi WebView2/SMTC"). The implementation
/// observes <see cref="KasetWin.Core.Services.Player.IPlayerService"/> to push display metadata
/// and playback status (Req 10.1/10.3), and forwards media-button presses back into the player
/// (Req 10.2).
/// </para>
/// <para>
/// On WinUI 3 desktop the SMTC is acquired through a single <c>MediaPlayer</c> in manual-control
/// mode (its command manager disabled), which is Microsoft's recommended way to host one SMTC
/// instance for an app that performs its own playback — here via the hidden WebView2. That path
/// needs no window handle, so the lifecycle is the simple <see cref="Start"/> / <see cref="Stop"/>
/// pair below. (An alternative HWND-bound path, <c>SystemMediaTransportControlsInterop
/// .GetForWindow(hwnd)</c>, would instead require the App to supply its window handle after the
/// main window is created.)
/// </para>
/// </remarks>
public interface INowPlayingController : IDisposable
{
    /// <summary>Whether the controller is currently bound to the system controls and observing the player.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Activates the system controls and begins mirroring player state to them (Req 10.1/10.3)
    /// while forwarding media-button presses to the player (Req 10.2). Idempotent — calling it
    /// again while active is a no-op.
    /// </summary>
    void Start();

    /// <summary>
    /// Deactivates the system controls and stops observing the player. Idempotent — calling it
    /// while inactive is a no-op.
    /// </summary>
    void Stop();
}
