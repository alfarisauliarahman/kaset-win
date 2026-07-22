using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace KasetWin.App;

/// <summary>
/// Mini player: collapses the shell to a small always-on-top overlay showing just the now-playing
/// track and transport, and restores it again. The Windows-native equivalent of the macOS mini
/// player, implemented with <see cref="AppWindowPresenterKind.CompactOverlay"/> (picture-in-picture),
/// which gives always-on-top and a system-managed compact frame for free.
/// </summary>
/// <remarks>
/// <para>
/// The window is reused rather than a second one created, so the hidden playback WebView2 hosted in
/// <c>RootGrid</c> is never re-parented — re-parenting it would tear down playback. Entering the
/// mini player only changes which chrome is visible.
/// </para>
/// <para>
/// The <c>WM_GETMINMAXINFO</c> minimum-size floor installed by <see cref="MainWindowLayout"/> would
/// otherwise refuse the compact size, so it is suspended while compact and reinstated on restore.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>Compact overlay size in logical pixels — wide enough for artwork, title and transport.</summary>
    private const int MiniPlayerWidth = 400;
    private const int MiniPlayerHeight = 150;

    /// <summary>Whether the shell is currently in mini-player mode.</summary>
    private bool _isMiniPlayer;

    /// <summary>Presenter to return to when leaving the mini player.</summary>
    private AppWindowPresenterKind _presenterBeforeMini = AppWindowPresenterKind.Overlapped;

    /// <summary>Panel mode open before entering the mini player, restored on the way out.</summary>
    private Controls.SidePanelMode _sidePanelModeBeforeMini = Controls.SidePanelMode.None;

    /// <summary>
    /// The window frame before shrinking, restored on the way out. Captured explicitly rather than
    /// trusting the presenter switch to remember it — <c>CompactOverlay</c> manages its own size, and
    /// coming back to <c>Overlapped</c> does not reliably hand back the size the user had chosen.
    /// </summary>
    private RectInt32? _frameBeforeMini;

    /// <summary>
    /// The pre-shrink frame, for callers that need to persist a sensible window size while the live
    /// frame is still the mini player's 400×150.
    /// </summary>
    internal RectInt32? FrameBeforeMiniPlayer => _frameBeforeMini;

    /// <summary>Flips into or out of mini-player mode. Safe to call when the AppWindow is unavailable.</summary>
    internal void ToggleMiniPlayer()
    {
        if (_isMiniPlayer)
        {
            ExitMiniPlayer();
        }
        else
        {
            EnterMiniPlayer();
        }
    }

    private void EnterMiniPlayer()
    {
        if (AppWindow is not { } appWindow || _isMiniPlayer)
        {
            return;
        }

        try
        {
            _presenterBeforeMini = appWindow.Presenter.Kind;
            _frameBeforeMini = new RectInt32(
                appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);

            // The docked queue/lyrics panel has no room at 400×150; remember which one was open so
            // restoring the full window puts the user back exactly where they were.
            _sidePanelModeBeforeMini = _sidePanel?.Mode ?? Controls.SidePanelMode.None;
            _sidePanel?.Close();

            // Suspend the 980×600 floor first: CompactOverlay picks its own size, and the floor
            // would fight it.
            MainWindowLayout.SuspendMinimumSize();

            AppTitleBar.Visibility = Visibility.Collapsed;
            NavView.Visibility = Visibility.Collapsed;
            MiniPlayerHost.Visibility = Visibility.Visible;

            appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            appWindow.Resize(new SizeInt32(
                ScaleForWindow(MiniPlayerWidth),
                ScaleForWindow(MiniPlayerHeight)));

            _isMiniPlayer = true;
            MiniPlayerHost.OnEnteredMiniPlayer();
        }
        catch (COMException)
        {
            // Presenter changes can fail on unusual window states; fall back to the full shell
            // rather than leaving the user with hidden chrome and no way back.
            ExitMiniPlayer();
        }
    }

    private void ExitMiniPlayer()
    {
        if (AppWindow is not { } appWindow)
        {
            return;
        }

        try
        {
            appWindow.SetPresenter(
                _presenterBeforeMini == AppWindowPresenterKind.CompactOverlay
                    ? AppWindowPresenterKind.Overlapped
                    : _presenterBeforeMini);
        }
        catch (COMException)
        {
            // Keep going: the chrome must be restored even if the presenter refused to change.
        }

        MiniPlayerHost.Visibility = Visibility.Collapsed;
        AppTitleBar.Visibility = Visibility.Visible;
        NavView.Visibility = Visibility.Visible;

        // Reinstate the size floor before resizing, so the restored frame is validated against it.
        MainWindowLayout.RestoreMinimumSize();

        if (_frameBeforeMini is { } frame)
        {
            try
            {
                appWindow.MoveAndResize(frame);
            }
            catch (COMException)
            {
                // Worst case the window keeps whatever size the presenter gave it; the chrome is
                // already back, so the user can resize by hand.
            }

            _frameBeforeMini = null;
        }

        switch (_sidePanelModeBeforeMini)
        {
            case Controls.SidePanelMode.Queue:
                _sidePanel?.ToggleQueue();
                break;
            case Controls.SidePanelMode.Lyrics:
                _sidePanel?.ToggleLyrics();
                break;
        }

        _sidePanelModeBeforeMini = Controls.SidePanelMode.None;
        _isMiniPlayer = false;
    }

    /// <summary>Scales a logical pixel value by the window's current DPI.</summary>
    private int ScaleForWindow(int logical)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? logical : (int)Math.Round(logical * dpi / 96.0);
        }
        catch (COMException)
        {
            return logical;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
