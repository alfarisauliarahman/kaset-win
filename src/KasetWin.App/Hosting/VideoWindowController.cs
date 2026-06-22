using KasetWin.App.Views;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Hosting;

/// <summary>
/// Drives the floating video window pop-out / pop-in flow (Task 19.1, Req 26.2–26.4). It owns no
/// WebView2 — it <em>reparents</em> the single hidden playback element owned by
/// <see cref="PlaybackWebViewHost"/> between the shell's hidden mount and a <see cref="VideoWindow"/>,
/// switching the controller's display mode accordingly. Moving the same element (never recreating it)
/// is what keeps audio/video playback uninterrupted across the transition (Req 26.2 / background
/// audio Req 1.4).
/// </summary>
/// <remarks>
/// <para>
/// Registered as an App-layer singleton. The shell (<c>MainWindow</c>) calls <see cref="AttachHomeMount"/>
/// once it has placed <see cref="PlaybackWebViewHost.Element"/> into its root grid so the controller
/// knows where to return the element on pop-in. The bottom <c>PlayerBar</c> resolves this controller
/// and toggles pop-out, enabling the affordance only when the current track has real video
/// (<see cref="VideoAvailability"/>, Req 26.1).
/// </para>
/// <para>
/// All element moves and window operations must run on the UI thread; the public methods are intended
/// to be invoked from UI event handlers (button click, window close).
/// </para>
/// </remarks>
public sealed class VideoWindowController
{
    private readonly PlaybackWebViewHost _host;
    private readonly IPlaybackController _controller;
    private readonly ILogger<VideoWindowController> _logger;

    private Panel? _homeMount;
    private int _homeIndex;
    private int _homeRow;
    private VideoWindow? _window;

    /// <summary>
    /// Creates the controller. Both dependencies are app-wide singletons; the controller does not
    /// create any UI until <see cref="PopOutAsync"/> is called.
    /// </summary>
    public VideoWindowController(
        PlaybackWebViewHost host,
        IPlaybackController controller,
        ILogger<VideoWindowController>? logger = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? NullLogger<VideoWindowController>.Instance;
    }

    /// <summary>Whether the playback element is currently shown in the floating window.</summary>
    public bool IsPoppedOut => _window is not null;

    /// <summary>
    /// Whether navigating away from a video surface should move playback into the floating window
    /// (Req 26.3) rather than reverting to the hidden audio-only surface (Req 26.4). Defaults to
    /// enabled; the shell may bind this to a user preference.
    /// </summary>
    public bool PopOutPreferenceEnabled { get; set; } = true;

    /// <summary>
    /// Registers the shell's mount for the hidden playback element so the controller can return it on
    /// pop-in (Task 14.1 mounts it at <paramref name="childIndex"/> / <see cref="Grid"/> row
    /// <paramref name="gridRow"/>). Must be called on the UI thread after the element is mounted.
    /// </summary>
    public void AttachHomeMount(Panel homeMount, int childIndex, int gridRow)
    {
        _homeMount = homeMount ?? throw new ArgumentNullException(nameof(homeMount));
        _homeIndex = childIndex < 0 ? 0 : childIndex;
        _homeRow = gridRow < 0 ? 0 : gridRow;
    }

    /// <summary>
    /// Moves the hidden playback element into a new floating <see cref="VideoWindow"/> and switches the
    /// surface to <see cref="PlaybackDisplayMode.Video"/> (Req 26.2). No-op if already popped out.
    /// Playback is not interrupted because the same element is reparented, never recreated.
    /// </summary>
    public async Task PopOutAsync()
    {
        if (_window is not null)
        {
            return;
        }

        var element = _host.Element;

        // Detach from the shell's hidden mount (if mounted there) before reparenting.
        if (element.Parent is Panel currentParent)
        {
            currentParent.Children.Remove(element);
        }

        var window = new VideoWindow();
        window.Closed += OnWindowClosed;
        window.VideoHost.Children.Add(element);
        _window = window;

        // Video mode stretches the element to fill the floating window (host applies the sizing).
        await _controller.SetDisplayModeAsync(PlaybackDisplayMode.Video).ConfigureAwait(true);

        window.Activate();
        _logger.LogInformation("Video popped out to floating window (playback element reparented).");
    }

    /// <summary>
    /// Closes the floating window and returns the playback element to the shell's hidden mount,
    /// restoring <see cref="PlaybackDisplayMode.Hidden"/> (audio-only) without stopping playback.
    /// No-op if not popped out. Safe to call from a UI event handler.
    /// </summary>
    public void PopIn()
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        // Closing raises Window.Closed → OnWindowClosed performs the reparent back to the mount.
        window.Close();
    }

    /// <summary>Pops out when hidden, pops in when shown — the single PlayerBar toggle (Req 26.2).</summary>
    public Task TogglePopOutAsync()
    {
        if (_window is null)
        {
            return PopOutAsync();
        }

        PopIn();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reacts to the user leaving the video surface (Req 26.3/26.4). When the pop-out preference is on
    /// and the current track has real video, playback moves to the floating window; otherwise the
    /// surface reverts to hidden audio-only (the video stops being shown). This is the seam the shell's
    /// navigation can call once a dedicated video page exists.
    /// </summary>
    public Task HandleLeftVideoSurfaceAsync(Song? currentTrack)
    {
        if (PopOutPreferenceEnabled && VideoAvailability.IsVideoAvailable(currentTrack))
        {
            return PopOutAsync();
        }

        PopIn();
        return Task.CompletedTask;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        window.Closed -= OnWindowClosed;
        _window = null;

        var element = _host.Element;
        window.VideoHost.Children.Remove(element);

        // Return the element to the shell's hidden mount at its original slot so background audio
        // continues from the live element (Req 1.4 / 26.2). Guard against double-insert.
        if (_homeMount is not null && element.Parent is null)
        {
            var index = Math.Clamp(_homeIndex, 0, _homeMount.Children.Count);
            _homeMount.Children.Insert(index, element);
            Grid.SetRow(element, _homeRow);
        }

        // Revert to the 1×1 hidden audio-only surface; never recreates the element.
        _ = _controller.SetDisplayModeAsync(PlaybackDisplayMode.Hidden);
        _logger.LogInformation("Video popped in; playback element returned to the hidden mount.");
    }
}
