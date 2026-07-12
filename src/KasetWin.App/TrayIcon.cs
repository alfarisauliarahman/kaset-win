using System;
using System.Runtime.InteropServices;

namespace KasetWin.App;

/// <summary>
/// A notification-area (system tray) icon for the shell window. It appears when the window is hidden
/// to the tray on close (background-audio model, Req 1.4) so the user always has a way back into the
/// running app: left-click restores the window, right-click offers "Buka Kaset" / "Keluar".
/// </summary>
/// <remarks>
/// WinUI 3 exposes no managed tray-icon API, so this is direct Win32 interop over
/// <c>Shell_NotifyIcon</c>. The tray callbacks (mouse events on the icon) are delivered as a private
/// <see cref="WM_TRAYCALLBACK"/> window message, captured via a comctl32 window subclass on the shell
/// HWND — the same composition technique used by <see cref="TaskbarMediaControls"/> and
/// <see cref="MainWindowLayout"/>. A distinct subclass id (<see cref="SubclassId"/>) keeps the three
/// subclasses from colliding. The icon is extracted from the running executable so it matches the
/// taskbar branding.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private const uint SubclassId = 2; // 0 = TaskbarMediaControls, 1 = MainWindowLayout.
    private const int IconUid = 1;
    private const uint WM_TRAYCALLBACK = 0x0400 + 1; // WM_APP + 1.

    private const int IdmOpen = 1;
    private const int IdmQuit = 2;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0x0;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;

    private const uint MF_STRING = 0x0;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_RETURNCMD = 0x100;

    private readonly IntPtr _hwnd;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly Action _onOpen;
    private readonly Action _onQuit;
    private readonly SUBCLASSPROC _subclassProc; // kept alive for the subclass lifetime.
    private readonly uint _taskbarCreatedMessage;

    private IntPtr _icon;
    private bool _shown;
    private bool _subclassed;
    private bool _disposed;

    public TrayIcon(
        IntPtr hwnd,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        Action onOpen,
        Action onQuit)
    {
        _hwnd = hwnd;
        _dispatcher = dispatcher;
        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        _onQuit = onQuit ?? throw new ArgumentNullException(nameof(onQuit));
        _subclassProc = SubclassWndProc;
        // Re-add the icon if Explorer restarts (it broadcasts "TaskbarCreated" to every top window).
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Adds the icon to the notification area. Best-effort and idempotent.</summary>
    public void Show()
    {
        if (_disposed || _shown)
        {
            return;
        }

        try
        {
            EnsureSubclassed();
            EnsureIcon();

            var data = CreateData();
            if (Shell_NotifyIcon(NIM_ADD, ref data))
            {
                _shown = true;
            }
        }
        catch
        {
            // The tray icon is a convenience; never let its failure affect the app.
        }
    }

    /// <summary>Removes the icon from the notification area. Best-effort and idempotent.</summary>
    public void Hide()
    {
        if (!_shown)
        {
            return;
        }

        try
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        catch
        {
        }

        _shown = false;
    }

    private void EnsureSubclassed()
    {
        if (_subclassed)
        {
            return;
        }

        _subclassed = SetWindowSubclass(_hwnd, _subclassProc, SubclassId, IntPtr.Zero);
    }

    private void EnsureIcon()
    {
        if (_icon != IntPtr.Zero)
        {
            return;
        }

        // Reuse the executable's embedded icon so the tray matches the taskbar branding.
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            if (ExtractIconEx(exePath, 0, large, small, 1) > 0)
            {
                _icon = small[0] != IntPtr.Zero ? small[0] : large[0];
                if (_icon == small[0] && large[0] != IntPtr.Zero)
                {
                    DestroyIcon(large[0]);
                }
                else if (_icon == large[0] && small[0] != IntPtr.Zero)
                {
                    DestroyIcon(small[0]);
                }
            }
        }

        if (_icon == IntPtr.Zero)
        {
            _icon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION fallback.
        }
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconUid,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_TRAYCALLBACK,
        hIcon = _icon,
        szTip = "Kaset",
    };

    private IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_TRAYCALLBACK)
        {
            var mouseMsg = (uint)((long)lParam & 0xFFFF);
            switch (mouseMsg)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    _dispatcher.TryEnqueue(() => _onOpen());
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowContextMenu();
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        // Explorer restarted: the icon was wiped from the fresh taskbar, so re-add it.
        if (msg == _taskbarCreatedMessage && _shown)
        {
            _shown = false;
            Show();
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MF_STRING, IdmOpen, "Buka Kaset");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, IdmQuit, "Keluar");

            // A popup from a tray icon requires the owner to be foreground, else it won't dismiss.
            SetForegroundWindow(_hwnd);
            GetCursorPos(out var pt);

            var cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            switch (cmd)
            {
                case IdmOpen:
                    _dispatcher.TryEnqueue(() => _onOpen());
                    break;
                case IdmQuit:
                    _dispatcher.TryEnqueue(() => _onQuit());
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();

        if (_subclassed)
        {
            try { RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId); } catch { }
            _subclassed = false;
        }

        if (_icon != IntPtr.Zero)
        {
            try { DestroyIcon(_icon); } catch { }
            _icon = IntPtr.Zero;
        }
    }

    // ── Win32 interop ────────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
