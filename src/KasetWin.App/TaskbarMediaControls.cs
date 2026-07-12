using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using KasetWin.Core.Services.Player;

namespace KasetWin.App;

/// <summary>
/// Adds Previous / Play-Pause / Next buttons to the app's taskbar thumbnail toolbar (Win32
/// <c>ITaskbarList3</c> thumb-bar). Clicks are routed back to <see cref="IPlayerService"/>; the
/// middle button's icon swaps between play and pause as the player state changes.
/// </summary>
/// <remarks>
/// WinUI 3 exposes no managed thumb-bar API, so this is direct COM/Win32 interop: the buttons are
/// registered against the window <c>HWND</c>, their clicks arrive as <c>WM_COMMAND</c> /
/// <c>THBN_CLICKED</c> messages (captured via a comctl32 window subclass), and the button icons are
/// rendered from Segoe Fluent glyphs into <c>HICON</c>s.
/// </remarks>
public sealed class TaskbarMediaControls : IDisposable
{
    private const int BtnPrev = 1;
    private const int BtnPlayPause = 2;
    private const int BtnNext = 3;

    private const uint WM_COMMAND = 0x0111;
    private const int THBN_CLICKED = 0x1800;
    private const uint THB_ICON = 0x8;
    private const uint THB_TOOLTIP = 0x20;
    private const uint THB_FLAGS = 0x4;
    private const uint THBF_ENABLED = 0x0;

    private readonly IntPtr _hwnd;
    private readonly IPlayerService _player;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly SUBCLASSPROC _subclassProc; // kept alive for the subclass lifetime
    private ITaskbarList3? _taskbar;
    private IntPtr _iconPrev, _iconPlay, _iconPause, _iconNext;
    private uint _taskbarButtonCreatedMessage;
    private bool _added;
    private bool _disposed;

    public TaskbarMediaControls(IntPtr hwnd, IPlayerService player, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _hwnd = hwnd;
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _dispatcher = dispatcher;
        _subclassProc = SubclassWndProc;
    }

    /// <summary>Creates the thumb-bar buttons and starts mirroring the player state. Best-effort.</summary>
    public void Start()
    {
        try
        {
            _taskbar = (ITaskbarList3)new CTaskbarList();
            _taskbar.HrInit();

            _iconPrev = CreateGlyphIcon("");
            _iconPlay = CreateGlyphIcon("");
            _iconPause = CreateGlyphIcon("");
            _iconNext = CreateGlyphIcon("");

            // The shell only accepts thumb-bar buttons AFTER it has created the taskbar button for
            // the window, which it announces by broadcasting the "TaskbarButtonCreated" message.
            // Adding the buttons before then silently fails, so we defer to that message.
            _taskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");

            SetWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
            _player.PropertyChanged += OnPlayerPropertyChanged;

            // If the taskbar button was already created (e.g. the window has been visible for a
            // while), the message may have already fired and won't fire again. Try once now;
            // AddThumbButtons is idempotent, and the message handler retries if it's not ready yet.
            AddThumbButtons();
        }
        catch
        {
            // Thumb-bar is a nicety; never let its failure affect the app.
        }
    }

    /// <summary>
    /// Registers the Previous / Play-Pause / Next buttons against the taskbar thumbnail. Only ever
    /// adds the button set once (the shell forbids adding twice); later calls are no-ops. Must run
    /// after the shell has created the window's taskbar button. Best-effort.
    /// </summary>
    private void AddThumbButtons()
    {
        if (_disposed || _added || _taskbar is null)
        {
            return;
        }

        try
        {
            var buttons = new[]
            {
                MakeButton(BtnPrev, _iconPrev, "Sebelumnya"),
                MakeButton(BtnPlayPause, _player.IsPlaying ? _iconPause : _iconPlay, _player.IsPlaying ? "Jeda" : "Putar"),
                MakeButton(BtnNext, _iconNext, "Berikutnya"),
            };

            var hr = _taskbar.ThumbBarAddButtons(_hwnd, (uint)buttons.Length, buttons);
            _added = hr == 0;
        }
        catch
        {
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPlayerService.IsPlaying))
        {
            _dispatcher.TryEnqueue(UpdatePlayPauseButton);
        }
    }

    private void UpdatePlayPauseButton()
    {
        if (_disposed || !_added || _taskbar is null)
        {
            return;
        }

        try
        {
            var playing = _player.IsPlaying;
            var button = MakeButton(BtnPlayPause, playing ? _iconPause : _iconPlay, playing ? "Jeda" : "Putar");
            _taskbar.ThumbBarUpdateButtons(_hwnd, 1, new[] { button });
        }
        catch
        {
        }
    }

    private static THUMBBUTTON MakeButton(uint id, IntPtr icon, string tip) => new()
    {
        dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS,
        iId = id,
        hIcon = icon,
        szTip = tip,
        dwFlags = THBF_ENABLED,
    };

    private IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        // The shell broadcasts this once the window's taskbar button exists; only now can the
        // thumb-bar buttons be registered. AddThumbButtons is idempotent, so a repeat is harmless.
        if (_taskbarButtonCreatedMessage != 0 && msg == _taskbarButtonCreatedMessage)
        {
            AddThumbButtons();
            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        if (msg == WM_COMMAND && ((int)((long)wParam >> 16) & 0xFFFF) == THBN_CLICKED)
        {
            var id = (int)((long)wParam & 0xFFFF);
            _dispatcher.TryEnqueue(() =>
            {
                switch (id)
                {
                    case BtnPrev: _ = _player.PreviousAsync(); break;
                    case BtnPlayPause: _ = _player.TogglePlayPauseAsync(); break;
                    case BtnNext: _ = _player.NextAsync(); break;
                }
            });
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private static IntPtr CreateGlyphIcon(string glyph)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // Segoe Fluent Icons ships on Win11; fall back to the MDL2 assets on older builds.
            string family = System.Drawing.FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons")
                ? "Segoe Fluent Icons"
                : "Segoe MDL2 Assets";
            using var font = new Font(family, 16, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, 32, 32), fmt);
        }

        return bmp.GetHicon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try { _player.PropertyChanged -= OnPlayerPropertyChanged; } catch { }
        try { RemoveWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero); } catch { }
        foreach (var icon in new[] { _iconPrev, _iconPlay, _iconPause, _iconNext })
        {
            if (icon != IntPtr.Zero) { DestroyIcon(icon); }
        }

        if (_taskbar is not null) { Marshal.ReleaseComObject(_taskbar); _taskbar = null; }
    }

    // ── Win32 interop ────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public uint dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public uint dwFlags;
    }

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3 (only the members we use are typed accurately; the rest are placeholders in
        // vtable order so the layout stays correct).
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButton);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class CTaskbarList
    {
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);
}
