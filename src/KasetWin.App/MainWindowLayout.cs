using System.Runtime.InteropServices;
using KasetWin.Platform.Storage;
using Microsoft.UI.Windowing;
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

        if (window.AppWindow is { } appWindow)
        {
            var scale = DpiScale(hwnd);

            // Restore the frame the user left behind; fall back to the default size on first run or
            // when the saved frame no longer fits any attached display (monitor unplugged, DPI
            // change, resolution drop) — an off-screen window is unrecoverable without registry
            // surgery, so validating it is not optional.
            if (TryLoadGeometry() is { } saved && IsFrameVisibleOnAnyDisplay(saved))
            {
                appWindow.MoveAndResize(new RectInt32(
                    saved.X,
                    saved.Y,
                    Math.Max(saved.Width, Scale(MinimumWidth, scale)),
                    Math.Max(saved.Height, Scale(MinimumHeight, scale))));
            }
            else
            {
                var width = Math.Max(Scale(DefaultWidth, scale), Scale(MinimumWidth, scale));
                var height = Math.Max(Scale(DefaultHeight, scale), Scale(MinimumHeight, scale));
                appWindow.Resize(new SizeInt32(width, height));
            }
        }
    }

    // ── Geometry persistence ────────────────────────────────────────────────────────────────────

    private const string GeometryKey = "window.geometry";

    /// <summary>
    /// Saves the window's current frame so the next launch reopens at the same size and place.
    /// </summary>
    /// <remarks>
    /// Skipped whenever the window is not a normal overlapped window: in mini-player
    /// (<c>CompactOverlay</c>) mode the frame is 400×150, and persisting that would reopen the full
    /// shell at mini-player size. Maximised and minimised frames are skipped for the same reason —
    /// what should be restored is the size the user chose, not the state.
    /// </remarks>
    /// <param name="window">The window whose frame to persist.</param>
    /// <param name="overrideFrame">
    /// A frame to store instead of the window's live one. Used when closing from the mini player,
    /// where the live frame is 400×150 but the frame worth remembering is the one captured before
    /// shrinking.
    /// </param>
    public static void SaveGeometry(Window window, RectInt32? overrideFrame = null)
    {
        if (window?.AppWindow is not { } appWindow)
        {
            return;
        }

        try
        {
            if (overrideFrame is { } frame)
            {
                if (frame.Width > 0 && frame.Height > 0)
                {
                    AppData.Settings[GeometryKey] = $"{frame.X},{frame.Y},{frame.Width},{frame.Height}";
                }

                return;
            }

            if (appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
            {
                return;
            }

            var size = appWindow.Size;
            var position = appWindow.Position;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            AppData.Settings[GeometryKey] =
                $"{position.X},{position.Y},{size.Width},{size.Height}";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Geometry is a convenience; never let saving it fail a window close.
        }
    }

    /// <summary>Reads the saved frame, or <c>null</c> when absent or malformed.</summary>
    private static RectInt32? TryLoadGeometry()
    {
        try
        {
            if (AppData.Settings[GeometryKey] is not string raw)
            {
                return null;
            }

            var parts = raw.Split(',');
            if (parts.Length != 4
                || !int.TryParse(parts[0], out var x)
                || !int.TryParse(parts[1], out var y)
                || !int.TryParse(parts[2], out var width)
                || !int.TryParse(parts[3], out var height)
                || width <= 0
                || height <= 0)
            {
                return null;
            }

            return new RectInt32(x, y, width, height);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a meaningful part of <paramref name="frame"/> lands on a connected display. Guards
    /// against restoring onto a monitor that is no longer attached, which would open the window
    /// somewhere the user cannot see or reach.
    /// </summary>
    private static bool IsFrameVisibleOnAnyDisplay(RectInt32 frame)
    {
        try
        {
            foreach (var area in DisplayArea.FindAll())
            {
                var bounds = area.OuterBounds;
                var overlapX = Math.Min(frame.X + frame.Width, bounds.X + bounds.Width) - Math.Max(frame.X, bounds.X);
                var overlapY = Math.Min(frame.Y + frame.Height, bounds.Y + bounds.Height) - Math.Max(frame.Y, bounds.Y);

                // Require a real patch of title bar on screen, not a single overlapping pixel.
                if (overlapX >= 200 && overlapY >= 80)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Whether the minimum-size floor is currently waived. The mini player's CompactOverlay is far
    /// smaller than 980×600, and the floor would otherwise veto that size.
    /// </summary>
    private static bool s_minimumSuspended;

    /// <summary>
    /// Waives the minimum-size floor so the window can shrink below it (mini player). Pair every
    /// call with <see cref="RestoreMinimumSize"/> — while suspended, nothing stops a user from
    /// dragging the full shell down to an unusable size.
    /// </summary>
    public static void SuspendMinimumSize() => s_minimumSuspended = true;

    /// <summary>Reinstates the minimum-size floor after <see cref="SuspendMinimumSize"/>.</summary>
    public static void RestoreMinimumSize() => s_minimumSuspended = false;

    private static IntPtr MinMaxSubclassProc(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint idSubclass, IntPtr refData)
    {
        if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero && !s_minimumSuspended)
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
