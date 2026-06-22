using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace KasetWin.App.Views;

/// <summary>
/// Floating picture-in-picture video window (Task 19.1, Req 26.2). It does not own or create any
/// WebView2 — it merely provides a black, 16:9-ish host surface (<see cref="VideoHost"/>) into which
/// <see cref="Hosting.VideoWindowController"/> <em>reparents</em> the app's single hidden playback
/// <see cref="WebView2"/> element. Because the very same element (and its <c>CoreWebView2</c>) is
/// moved rather than recreated, audio/video playback continues uninterrupted across the pop-out
/// (background-audio model, Req 1.4 / 26.2).
/// </summary>
/// <remarks>
/// Defined as a code-only <see cref="Window"/> (no XAML) so it can be created on demand by the
/// controller. The window must be created on the UI thread. Closing it is the user's "pop in"
/// gesture; the controller listens for <see cref="Window.Closed"/> and moves the element back to the
/// shell's hidden mount.
/// </remarks>
public sealed class VideoWindow : Window
{
    private const int DefaultWidth = 640;
    private const int DefaultHeight = 360;

    private readonly Grid _root;

    /// <summary>Creates the floating window with an empty black host surface.</summary>
    public VideoWindow()
    {
        this.Title = "Kaset";

        // Black letterbox background so non-16:9 content (or the brief load gap) never flashes white.
        _root = new Grid
        {
            Background = new SolidColorBrush(Colors.Black),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        this.Content = _root;

        try
        {
            AppWindow?.Resize(new SizeInt32(DefaultWidth, DefaultHeight));
        }
        catch
        {
            // Sizing is best-effort; a locked-down AppWindow must not prevent the window from showing.
        }
    }

    /// <summary>
    /// The panel that hosts the reparented playback <see cref="WebView2"/>. The controller adds the
    /// element here on pop-out and removes it on pop-in; it is the controller's responsibility to keep
    /// the element parented to exactly one place at a time (a XAML element has a single parent).
    /// </summary>
    public Panel VideoHost => _root;
}
