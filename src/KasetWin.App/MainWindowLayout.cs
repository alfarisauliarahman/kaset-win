using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace KasetWin.App;

/// <summary>
/// Shared sizing contract for KasetWin's primary window (Task 30.8, Req 37.8) — the WinUI port of
/// the macOS <c>MainWindowLayout</c> (upstream #322). It pins a minimum size so live resizing (and,
/// once window-geometry persistence is added, a restored frame) can never shrink the window below
/// the point where the sidebar + player controls stay usable, and opens the window at a sensible
/// default on launch.
/// </summary>
/// <remarks>
/// <para>
/// Logical values mirror the macOS contract (min 980×600, default 1100×760). WinUI window sizes are
/// in physical pixels, so values are scaled by the window's DPI before being applied.
/// </para>
/// <para>
/// The minimum is enforced by subclassing the window and answering <c>WM_GETMINMAXINFO</c> — the
/// <c>OverlappedPresenter.PreferredMinimum*</c> properties only exist in Windows App SDK 1.7+, and
/// this project targets 1.6. Subclassing via <c>SetWindowSubclass</c> composes with the WinUI window
/// procedure instead of replacing it.
/// </para>
/// </remarks>
internal static class MainWindowLayout
{
    /// <summary>Minimum usable width in logical (96-DPI) pixels.</summary>
    public const int MinimumWidth = 980;

    /// <summary>Minimum usable height in logical (96-DPI) pixels.</summary>
    public const int MinimumHeight = 600;

    /// <summary>Default launch width in logical (96-DPI) pixels.</summary>
    public const int DefaultWidth = 1100;

    /// <summary>Default launch height in logical (96-DPI) pixels.</summary>
    public const int DefaultHeight = 760;

    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint SubclassId = 1;

    // Kept alive for the process lifetime so the native subclass callback is never collected.
    private static readonly SubclassProc s_subclassProc = MinMaxSubclassProc;

    /// <summary>Clamps a logical width to the minimum contract (pure; mirrors the macOS clamp).</summary>
    public static int ClampWidth(int width) => Math.Max(width, MinimumWidth);

    /// <summary>Clamps a logical height to the minimum contract (pure; mirrors the macOS clamp).</summary>
    public static int ClampHeight(int height) => Math.Max(height, MinimumHeight);

    /// <summary>
    /// Applies the primary-window sizing contract to <paramref name="window"/>: installs a minimum
    /// size and opens at the default size on launch. A no-op when the window handle or AppWindow is
    /// unavailable, so it never blocks the shell from coming up.
    /// </summary>
    public static void Configure(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr hwnd;
        try
        {
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch (COMException)
        {
            return;
        }

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Brand the title bar / taskbar with the Kaset icon (matches the exe-embedded icon).
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "kaset.ico");
            if (window.AppWindow is { } iconWindow && System.IO.File.Exists(iconPath))
            {
                iconWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // A missing/locked icon must never block the window from coming up.
        }

        // Enforce the minimum size for all live resizes (DPI is read per-message inside the proc).
        SetWindowSubclass(hwnd, s_subclassProc, SubclassId, IntPtr.Zero);

        // First-run sizing: KasetWin does not persist window geometry yet, so open at the default
        // size, floored to the minimum. When geometry persistence is added later, gate this resize
        // to the no-saved-frame case (the WM_GETMINMAXINFO floor already guards restored frames).
        if (window.AppWindow is { } appWindow)
        {
            var scale = DpiScale(hwnd);
            var width = Math.Max(Scale(DefaultWidth, scale), Scale(MinimumWidth, scale));
            var height = Math.Max(Scale(DefaultHeight, scale), Scale(MinimumHeight, scale));
            appWindow.Resize(new SizeInt32(width, height));
        }
    }

    private static IntPtr MinMaxSubclassProc(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint idSubclass, IntPtr refData)
    {
        if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var scale = DpiScale(hwnd);
            info.ptMinTrackSize.X = Scale(MinimumWidth, scale);
            info.ptMinTrackSize.Y = Scale(MinimumHeight, scale);
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private static double DpiScale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static int Scale(int logical, double scale) => (int)Math.Round(logical * scale);

    // ── Native interop ──────────────────────────────────────────────────────────────────────────

    private delegate IntPtr SubclassProc(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint idSubclass, IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd, SubclassProc callback, uint idSubclass, IntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
