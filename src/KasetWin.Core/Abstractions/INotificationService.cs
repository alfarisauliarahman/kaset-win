namespace KasetWin.Core.Abstractions;

/// <summary>
/// Abstraction over the OS local-notification surface — on Windows a toast raised through the
/// Windows App SDK <c>AppNotificationManager</c> (Req 35.1). The implementation observes
/// <see cref="KasetWin.Core.Services.Player.IPlayerService"/> and shows a "now playing" toast
/// carrying the new track's title and artist whenever the current track changes.
/// </summary>
/// <remarks>
/// <para>
/// Defined in <c>Core</c> (no WinUI/WinRT/Windows App SDK dependency) so the player layer and DI
/// composition can reference it without taking a toast dependency. The concrete
/// <c>ToastNotificationService</c> lives in <c>KasetWin.App</c> because the toast APIs
/// (<c>Microsoft.Windows.AppNotifications</c>) ship with the Windows App SDK, which only the App
/// project references (Design: "Inversi dependensi WebView2/SMTC"; mirrors
/// <see cref="INowPlayingController"/>).
/// </para>
/// <para>
/// Whether a track-change toast is actually shown is gated by <see cref="NotificationsEnabled"/>
/// (Req 35.1 "WHERE notifikasi ganti track diaktifkan"). The eligibility decision itself is the
/// pure <c>TrackChangeNotificationPolicy</c> so it can be exercised headless.
/// </para>
/// </remarks>
public interface INotificationService : IDisposable
{
    /// <summary>Whether the service is currently observing the player and able to raise toasts.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Whether track-change toasts are shown (Req 35.1). Defaults to <see langword="true"/>.
    /// Setting it to <see langword="false"/> suppresses toasts without stopping observation.
    /// </summary>
    bool NotificationsEnabled { get; set; }

    /// <summary>
    /// Registers the notification manager and begins observing the player so track changes raise a
    /// toast (Req 35.1). Idempotent — calling it again while active is a no-op.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops observing the player and releases the notification manager registration. Idempotent —
    /// calling it while inactive is a no-op.
    /// </summary>
    void Stop();
}
