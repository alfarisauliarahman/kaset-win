using System;

namespace KasetWin.App.Controls;

/// <summary>Which content the right-hand now-playing side panel is showing.</summary>
public enum SidePanelMode
{
    /// <summary>The panel is closed.</summary>
    None,

    /// <summary>The play-queue panel (Berikutnya / Riwayat), Apple-Music style.</summary>
    Queue,

    /// <summary>The synced-lyrics panel, Apple-Music style.</summary>
    Lyrics,
}

/// <summary>
/// App-scoped coordinator for the right-hand now-playing side panel (queue / lyrics). The
/// <c>PlayerBar</c> buttons toggle a mode here; the shell (<c>MainWindow</c>) observes
/// <see cref="Changed"/> to slide the docked panel in/out and swap its content. Registered as a
/// singleton so both live surfaces share one state.
/// </summary>
public sealed class SidePanelController
{
    private SidePanelMode _mode = SidePanelMode.None;

    /// <summary>Raised whenever <see cref="Mode"/> changes (both the shell and the buttons observe it).</summary>
    public event Action? Changed;

    /// <summary>The currently displayed panel content (or <see cref="SidePanelMode.None"/> when closed).</summary>
    public SidePanelMode Mode
    {
        get => _mode;
        private set
        {
            if (_mode != value)
            {
                _mode = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Toggles the queue panel: opens it, or closes the panel if the queue is already shown.</summary>
    public void ToggleQueue() => Mode = _mode == SidePanelMode.Queue ? SidePanelMode.None : SidePanelMode.Queue;

    /// <summary>Toggles the lyrics panel: opens it, or closes the panel if lyrics are already shown.</summary>
    public void ToggleLyrics() => Mode = _mode == SidePanelMode.Lyrics ? SidePanelMode.None : SidePanelMode.Lyrics;

    /// <summary>Closes the panel.</summary>
    public void Close() => Mode = SidePanelMode.None;
}
