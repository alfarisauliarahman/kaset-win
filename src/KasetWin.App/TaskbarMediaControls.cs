using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
/// drawn as vector transport glyphs into 32-bit alpha <c>HICON</c>s at the shell's small-icon size.
/// </remarks>
public sealed class TaskbarMediaControls : IDisposable
{
    private const int BtnPrev = 1;
    private const int BtnPlayPause = 2;
    private const int BtnNext = 3;

    private const uint WM_COMMAND = 0x0111;
    private const int THBN_CLICKED = 0x1800;
    // THUMBBUTTONMASK, exactly as declared in shobjidl_core.h. These were previously 0x8 / 0x20 /
    // 0x4, which meant the mask never had THB_ICON (0x2) set: the shell was told the hIcon field was
    // not valid and drew the three buttons with no image at all, leaving the thumbnail toolbar
    // looking blank. 0x20 is not a defined mask bit either.
    private const uint THB_ICON = 0x2;
    private const uint THB_TOOLTIP = 0x4;
    private const uint THB_FLAGS = 0x8;
    private const uint THBF_ENABLED = 0x0;

    // winuser.h system-metric indices for the *small* icon size — the size the shell draws thumb-bar
    // button icons at. Verified on this machine rather than assumed: GetSystemMetricsForDpi(49, dpi)
    // returns 16 at 96 DPI, 24 at 144 and 32 at 192, i.e. SM_CXSMICON; 11/12 return the large (32px
    // at 96 DPI) icon size, so they are SM_CXICON/SM_CYICON and are *not* what we want here.
    private const int SM_CXSMICON = 49;

    // wingdi.h: BI_RGB = uncompressed, DIB_RGB_COLORS = the (unused, for 32bpp) colour table holds
    // literal RGB values rather than palette indices.
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

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

    /// <summary>
    /// One diag line per failure in this feature. The thumb-bar spent several test rounds broken
    /// precisely because every failure here was swallowed by an empty catch (the ADR 0007 lesson):
    /// a NULL icon, a failed subclass or a non-zero HRESULT all looked identical — "no buttons".
    /// </summary>
    private static void Trace(string message) => KasetWin.Core.Diag.Write("thumbbar: " + message);

    /// <summary>Creates the thumb-bar buttons and starts mirroring the player state. Best-effort.</summary>
    public void Start()
    {
        try
        {
            _taskbar = (ITaskbarList3)new CTaskbarList();
            _taskbar.HrInit();

            var size = ThumbButtonIconSize(_hwnd);
            _iconPrev = CreateTransportIcon(TransportGlyph.Previous, size);
            _iconPlay = CreateTransportIcon(TransportGlyph.Play, size);
            _iconPause = CreateTransportIcon(TransportGlyph.Pause, size);
            _iconNext = CreateTransportIcon(TransportGlyph.Next, size);
            Trace($"start hwnd=0x{_hwnd.ToInt64():X} size={size} icons prev=0x{_iconPrev.ToInt64():X} play=0x{_iconPlay.ToInt64():X} pause=0x{_iconPause.ToInt64():X} next=0x{_iconNext.ToInt64():X}");

            // The shell only accepts thumb-bar buttons AFTER it has created the taskbar button for
            // the window, which it announces by broadcasting the "TaskbarButtonCreated" message.
            // Adding the buttons before then silently fails, so we defer to that message.
            _taskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
            if (_taskbarButtonCreatedMessage == 0)
            {
                Trace($"RegisterWindowMessage(TaskbarButtonCreated) FAILED gle={Marshal.GetLastWin32Error()}");
            }

            // If this fails, the TaskbarButtonCreated retry never arrives, and a too-early
            // AddThumbButtons below is the only shot we get — worth a trace, not a silent shrug.
            if (!SetWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero, IntPtr.Zero))
            {
                Trace($"SetWindowSubclass FAILED gle={Marshal.GetLastWin32Error()}");
            }

            _player.PropertyChanged += OnPlayerPropertyChanged;

            // If the taskbar button was already created (e.g. the window has been visible for a
            // while), the message may have already fired and won't fire again. Try once now;
            // AddThumbButtons is idempotent, and the message handler retries if it's not ready yet.
            AddThumbButtons();
        }
        catch (Exception ex)
        {
            // Thumb-bar is a nicety; never let its failure affect the app — but always leave a trace.
            Trace($"Start FAILED: {ex.GetType().Name}: {ex.Message}");
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
                MakeButton(BtnPrev, _iconPrev, Localization.UiStrings.TipPrevious),
                MakeButton(BtnPlayPause, _player.IsPlaying ? _iconPause : _iconPlay, Localization.UiStrings.TipPlayPause),
                MakeButton(BtnNext, _iconNext, Localization.UiStrings.TipNext),
            };

            var hr = _taskbar.ThumbBarAddButtons(_hwnd, (uint)buttons.Length, buttons);
            _added = hr == 0;
            Trace($"ThumbBarAddButtons hr=0x{hr:X8} added={_added}");
        }
        catch (Exception ex)
        {
            Trace($"AddThumbButtons FAILED: {ex.GetType().Name}: {ex.Message}");
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
            var button = MakeButton(BtnPlayPause, playing ? _iconPause : _iconPlay, Localization.UiStrings.TipPlayPause);
            var hr = _taskbar.ThumbBarUpdateButtons(_hwnd, 1, new[] { button });
            if (hr != 0)
            {
                Trace($"ThumbBarUpdateButtons hr=0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            Trace($"UpdatePlayPauseButton FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Only claim THB_ICON when there really is an icon: telling the shell hIcon is valid while
    // passing NULL is what an empty button looks like.
    private static THUMBBUTTON MakeButton(uint id, IntPtr icon, string tip) => new()
    {
        dwMask = (icon == IntPtr.Zero ? 0u : THB_ICON) | THB_TOOLTIP | THB_FLAGS,
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
            if (!_added)
            {
                Trace("TaskbarButtonCreated received, retrying AddThumbButtons");
            }

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

    private enum TransportGlyph
    {
        Previous,
        Play,
        Pause,
        Next,
    }

    /// <summary>
    /// The edge length the shell draws a thumb-bar button icon at: the small-icon metric for the
    /// window's DPI. Rendering at exactly this size is the point — the previous implementation drew
    /// a 32x32 icon and let the shell shrink it, which halved every stroke.
    /// </summary>
    private static int ThumbButtonIconSize(IntPtr hwnd)
    {
        var size = 0;
        try
        {
            // A WinUI 3 window has a valid HWND from construction, but guard the zero/foreign-hwnd
            // case anyway: GetDpiForWindow(0) returns 0, which we map to the 96-DPI baseline.
            var dpi = GetDpiForWindow(hwnd);
            size = GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? 96u : dpi);
            if (dpi == 0 || size <= 0)
            {
                Trace($"icon size probe odd: hwnd=0x{hwnd.ToInt64():X} dpi={dpi} smicon={size}");
            }
        }
        catch (Exception ex)
        {
            // GetDpiForWindow / GetSystemMetricsForDpi need Windows 10 1607+; fall through.
            Trace($"GetSystemMetricsForDpi FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        if (size <= 0)
        {
            try { size = GetSystemMetrics(SM_CXSMICON); } catch (Exception ex) { Trace($"GetSystemMetrics FAILED: {ex.GetType().Name}: {ex.Message}"); }
        }

        return Math.Clamp(size <= 0 ? 16 : size, 16, 64);
    }

    /// <summary>
    /// Builds the outline of a transport glyph, sized to <paramref name="size"/>. Deliberately
    /// chunky — at 16 px each triangle is ~5 px of solid ink, where the Segoe Fluent glyphs this
    /// replaces are hairline *outline* glyphs that all but vanish once the shell scales them down.
    /// </summary>
    private static GraphicsPath BuildGlyphPath(TransportGlyph glyph, int size)
    {
        // Whole-pixel coordinates on purpose. Placing the two halves of the prev/next glyph at a
        // fractional offset from each other spreads one of them across two pixel columns, and at
        // 16 px that reads as "one arrow is bolder than the other".
        var pad = Math.Max(2, (int)MathF.Round(size * 0.13f)); // leaves room for the dark halo
        var gap = Math.Max(1, (int)MathF.Round(size * 0.055f)); // half-gap between the two halves
        var mid = size / 2;
        var top = pad;
        var bottom = size - pad;
        var left = pad;
        var right = size - pad;

        var path = new GraphicsPath();
        switch (glyph)
        {
            case TransportGlyph.Previous: // two left-pointing triangles
                path.AddPolygon(new[] { new PointF(mid - gap, top), new PointF(mid - gap, bottom), new PointF(left, mid) });
                path.AddPolygon(new[] { new PointF(right, top), new PointF(right, bottom), new PointF(mid + gap, mid) });
                break;

            case TransportGlyph.Next: // two right-pointing triangles
                path.AddPolygon(new[] { new PointF(left, top), new PointF(left, bottom), new PointF(mid - gap, mid) });
                path.AddPolygon(new[] { new PointF(mid + gap, top), new PointF(mid + gap, bottom), new PointF(right, mid) });
                break;

            case TransportGlyph.Play: // one wide right-pointing triangle
                path.AddPolygon(new[] { new PointF(left + 1, top), new PointF(left + 1, bottom), new PointF(right, mid) });
                break;

            case TransportGlyph.Pause: // two bars
                var barWidth = Math.Max(2, (int)MathF.Round(size * 0.20f));
                path.AddRectangle(new RectangleF(mid - gap - barWidth, top, barWidth, bottom - top));
                path.AddRectangle(new RectangleF(mid + gap, top, barWidth, bottom - top));
                break;
        }

        return path;
    }

    /// <summary>
    /// Draws the glyph — solid white fill over a dark halo, so the button reads on both a dark and
    /// a light thumbnail flyout without the app tracking the system theme — onto an already-cleared
    /// transparent surface. Shared by the DIB path and the GDI+ fallback so the two can never drift.
    /// </summary>
    private static void DrawGlyph(Graphics g, TransportGlyph glyph, int size)
    {
        using var path = BuildGlyphPath(glyph, size);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Halo first, fill second: FillPath covers the inner half of the stroke, leaving a
        // ~1 px dark rim that carries the shape on a light background.
        using (var halo = new Pen(Color.FromArgb(215, 0, 0, 0), Math.Max(1.25f, size / 5.5f)) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(halo, path);
        }

        using var fill = new SolidBrush(Color.White);
        g.FillPath(fill, path);
    }

    /// <summary>
    /// Renders a transport glyph into a 32-bit-with-alpha <c>HICON</c> at the shell's small icon
    /// size. Tries the DIB-section path first (controlled premultiplication); if that fails for any
    /// reason it falls back to <see cref="Bitmap.GetHicon"/>, which is the round-6 code path that
    /// verifiably produced visible (if thin) icons — a slightly-less-perfect icon always beats a
    /// blank button. Returns <see cref="IntPtr.Zero"/> only when both paths fail (each failure is
    /// traced); the caller then omits the icon rather than handing the shell a NULL handle.
    /// </summary>
    private static IntPtr CreateTransportIcon(TransportGlyph glyph, int size)
    {
        var icon = CreateTransportIconViaDib(glyph, size);
        if (icon != IntPtr.Zero)
        {
            return icon;
        }

        icon = CreateTransportIconViaGetHicon(glyph, size);
        Trace(icon == IntPtr.Zero
            ? $"icon {glyph}: BOTH paths failed, button will have no image"
            : $"icon {glyph}: DIB path failed, using GetHicon fallback 0x{icon.ToInt64():X}");
        return icon;
    }

    /// <summary>
    /// The preferred path: a DIB section (not <see cref="Bitmap.GetHicon"/>) so the icon carries a
    /// real per-pixel alpha channel and we control premultiplication, which is what AlphaBlend —
    /// and therefore the shell — expects from a 32bpp icon.
    /// </summary>
    private static IntPtr CreateTransportIconViaDib(TransportGlyph glyph, int size)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // negative => top-down, matching GDI+'s row order
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        var hbmColor = CreateDIBSection(IntPtr.Zero, ref header, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (hbmColor == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Trace($"icon {glyph}: CreateDIBSection FAILED hbm=0x{hbmColor.ToInt64():X} bits=0x{bits.ToInt64():X} gle={Marshal.GetLastWin32Error()}");
            if (hbmColor != IntPtr.Zero) { DeleteObject(hbmColor); }
            return IntPtr.Zero;
        }

        var hbmMask = IntPtr.Zero;
        try
        {
            using (var bmp = new Bitmap(size, size, size * 4, PixelFormat.Format32bppArgb, bits))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawGlyph(g, glyph, size);
            }

            PremultiplyAlpha(bits, size * size);

            // An all-zero AND mask means "take every pixel from the colour bitmap"; the alpha
            // channel does the actual shaping. CreateBitmap does not zero its own bits.
            var maskStride = ((size + 15) / 16) * 2;
            hbmMask = CreateBitmap(size, size, 1, 1, new byte[maskStride * size]);
            if (hbmMask == IntPtr.Zero)
            {
                Trace($"icon {glyph}: CreateBitmap(mask) FAILED gle={Marshal.GetLastWin32Error()}");
                return IntPtr.Zero;
            }

            var info = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hbmMask,
                hbmColor = hbmColor,
            };

            // CreateIconIndirect copies both bitmaps, so they are ours to delete afterwards.
            var hicon = CreateIconIndirect(ref info);
            if (hicon == IntPtr.Zero)
            {
                Trace($"icon {glyph}: CreateIconIndirect FAILED gle={Marshal.GetLastWin32Error()}");
            }

            return hicon;
        }
        catch (Exception ex)
        {
            Trace($"icon {glyph}: DIB path FAILED: {ex.GetType().Name}: {ex.Message}");
            return IntPtr.Zero;
        }
        finally
        {
            DeleteObject(hbmColor);
            if (hbmMask != IntPtr.Zero) { DeleteObject(hbmMask); }
        }
    }

    /// <summary>
    /// Fallback: draw the same glyph into a plain GDI+ bitmap and let <see cref="Bitmap.GetHicon"/>
    /// mint the icon. Round 7 verified GetHicon preserves the alpha channel, and this is exactly the
    /// mechanism of the round-6 icons that were confirmed visible on the real taskbar — no custom
    /// DIB/mask/premultiply steps left to get wrong.
    /// </summary>
    private static IntPtr CreateTransportIconViaGetHicon(TransportGlyph glyph, int size)
    {
        try
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawGlyph(g, glyph, size);
            }

            // GetHicon icons are released with DestroyIcon, same as CreateIconIndirect ones, so
            // Dispose() needs no knowledge of which path produced the handle.
            return bmp.GetHicon();
        }
        catch (Exception ex)
        {
            Trace($"icon {glyph}: GetHicon path FAILED: {ex.GetType().Name}: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Converts straight (GDI+) alpha to the premultiplied form a 32bpp icon is blended with.
    /// Without this the antialiased rim of every glyph is drawn too bright and haloes.
    /// </summary>
    private static void PremultiplyAlpha(IntPtr bits, int pixelCount)
    {
        var pixels = new int[pixelCount];
        Marshal.Copy(bits, pixels, 0, pixelCount);
        for (var i = 0; i < pixelCount; i++)
        {
            var px = (uint)pixels[i];
            var a = px >> 24;
            if (a == 255)
            {
                continue;
            }

            if (a == 0)
            {
                pixels[i] = 0;
                continue;
            }

            var r = (((px >> 16) & 0xFF) * a) / 255;
            var gr = (((px >> 8) & 0xFF) * a) / 255;
            var b = ((px & 0xFF) * a) / 255;
            pixels[i] = unchecked((int)((a << 24) | (r << 16) | (gr << 8) | b));
        }

        Marshal.Copy(pixels, 0, bits, pixelCount);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, byte[] lpvBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
