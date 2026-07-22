using System.Runtime.InteropServices;
using KasetWin.Core.Services.Player;
using Microsoft.UI.Dispatching;

namespace KasetWin.App;

/// <summary>
/// System-wide playback hotkeys: they work while Kaset is in the background, unfocused, or hidden to
/// the tray — unlike the in-app accelerators, which need the window focused.
/// </summary>
/// <remarks>
/// <para>
/// This complements rather than replaces media-key support (SMTC): a keyboard without dedicated
/// ▶️/⏭️ keys has no way to control playback from another app, which is exactly the gap here.
/// </para>
/// <para>
/// <b>Combinations are deliberately conservative.</b> <c>RegisterHotKey</c> grants a combination to
/// exactly one process system-wide, first come first served, so anything popular (Ctrl+Shift+letter)
/// risks stealing a shortcut from the user's editor or browser — or silently failing when that app
/// started first. Ctrl+Alt+Arrow is rarely claimed, and every registration failure is tolerated
/// individually: three of four hotkeys still work if one is taken.
/// </para>
/// <para>
/// Messages arrive on the window procedure, so this installs its own subclass (a second one, with a
/// distinct id, alongside the minimum-size subclass in <see cref="MainWindowLayout"/>) and marshals
/// the actual transport call onto the UI thread.
/// </para>
/// </remarks>
internal sealed class GlobalHotkeys : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint SubclassId = 2;

    // RegisterHotKey modifier flags.
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint VK_LEFT = 0x25;
    private const uint VK_UP = 0x26;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_DOWN = 0x28;

    /// <summary>Hotkey ids, also used as the <c>wParam</c> discriminator in WM_HOTKEY.</summary>
    private const int IdPlayPause = 9001;
    private const int IdNext = 9002;
    private const int IdPrevious = 9003;
    private const int IdMute = 9004;

    private static GlobalHotkeys? s_instance;

    // Kept alive for the object's lifetime so the native subclass callback is never collected.
    private readonly SubclassProc _subclassProc;

    private readonly IntPtr _hwnd;
    private readonly IPlayerService _player;
    private readonly DispatcherQueue _dispatcher;
    private readonly List<int> _registered = [];
    private bool _disposed;

    private GlobalHotkeys(IntPtr hwnd, IPlayerService player, DispatcherQueue dispatcher)
    {
        _hwnd = hwnd;
        _player = player;
        _dispatcher = dispatcher;
        _subclassProc = HotkeySubclassProc;
    }

    /// <summary>
    /// Registers the hotkeys against <paramref name="hwnd"/>. Returns <c>null</c> when nothing could
    /// be registered at all, so the caller can tell "off" from "partially claimed by another app".
    /// </summary>
    public static GlobalHotkeys? TryCreate(IntPtr hwnd, IPlayerService player, DispatcherQueue dispatcher)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var hotkeys = new GlobalHotkeys(hwnd, player, dispatcher);
        SetWindowSubclass(hwnd, hotkeys._subclassProc, SubclassId, IntPtr.Zero);

        const uint mods = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        hotkeys.TryRegister(IdPlayPause, mods, VK_DOWN);
        hotkeys.TryRegister(IdNext, mods, VK_RIGHT);
        hotkeys.TryRegister(IdPrevious, mods, VK_LEFT);
        hotkeys.TryRegister(IdMute, mods, VK_UP);

        if (hotkeys._registered.Count == 0)
        {
            hotkeys.Dispose();
            return null;
        }

        s_instance = hotkeys;
        return hotkeys;
    }

    /// <summary>How many of the four combinations were actually granted by Windows.</summary>
    public int RegisteredCount => _registered.Count;

    private void TryRegister(int id, uint modifiers, uint virtualKey)
    {
        // A false return means another process already owns the combination. That is a normal
        // outcome, not an error — skip this one and keep the rest.
        if (RegisterHotKey(_hwnd, id, modifiers, virtualKey))
        {
            _registered.Add(id);
        }
    }

    private static IntPtr HotkeySubclassProc(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint idSubclass, IntPtr refData)
    {
        if (msg == WM_HOTKEY && s_instance is { _disposed: false } instance)
        {
            instance.Handle(wParam.ToInt32());
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Runs the transport action for a hotkey id on the UI thread.</summary>
    private void Handle(int id) => _dispatcher.TryEnqueue(() =>
    {
        try
        {
            switch (id)
            {
                case IdPlayPause:
                    _ = _player.TogglePlayPauseAsync();
                    break;
                case IdNext:
                    _ = _player.NextAsync();
                    break;
                case IdPrevious:
                    _ = _player.PreviousAsync();
                    break;
                case IdMute:
                    _player.ToggleMute();
                    break;
            }
        }
        catch (Exception)
        {
            // A transport failure is reported by the player itself; a hotkey must never crash the app.
        }
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var id in _registered)
        {
            UnregisterHotKey(_hwnd, id);
        }

        _registered.Clear();
        RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);

        if (ReferenceEquals(s_instance, this))
        {
            s_instance = null;
        }
    }

    // ── Native interop ──────────────────────────────────────────────────────────────────────────

    private delegate IntPtr SubclassProc(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint idSubclass, IntPtr refData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd, SubclassProc callback, uint idSubclass, IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, uint idSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
