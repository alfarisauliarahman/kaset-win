using KasetWin.Core.Abstractions;
using KasetWin.Platform.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Hosting;

/// <summary>
/// App-layer owner of the single hidden <see cref="WebView2"/> that performs DRM playback
/// (Task 8.3, Req 1.1/1.4). The element is created once and lives for the lifetime of the
/// application so audio keeps playing across page navigation and while the main window is
/// hidden ("hide window, keep app alive" background-audio model, Req 1.4).
/// </summary>
/// <remarks>
/// <para>
/// Creating a <see cref="Microsoft.Web.WebView2.Core.CoreWebView2"/> requires the element to be
/// live in a window's visual tree, so the host exposes <see cref="Element"/> for the shell
/// (<c>MainWindow</c>, Task 14.1) to mount into its root grid. The element starts as a 1Ã—1,
/// hit-test-invisible surface (Hidden mode); it is resized to 160Ã—90 for the mini player and
/// stretched for full Video mode via <see cref="WebView2PlaybackController.DisplayModeChanged"/>
/// (Req 26 seam).
/// </para>
/// <para>
/// The host never disposes the <see cref="WebView2"/> when pages change â€” only an explicit
/// app shutdown (<see cref="DisposeAsync"/> / <c>IPlaybackController.ReleaseAsync</c>) tears it
/// down. This is what keeps background audio alive (Req 1.4).
/// </para>
/// </remarks>
public sealed class PlaybackWebViewHost : IAsyncDisposable
{
    /// <summary>Edge length of the 1Ã—1 hidden audio surface (Req 1.1).</summary>
    private const double HiddenEdge = 1;

    /// <summary>Mini-player surface width (Req 26 seam).</summary>
    private const double MiniPlayerWidth = 160;

    /// <summary>Mini-player surface height (Req 26 seam).</summary>
    private const double MiniPlayerHeight = 90;

    private readonly WebView2PlaybackController _controller;
    private readonly WebViewEnvironmentProvider _environmentProvider;
    private readonly ExtensionsService _extensions;
    private readonly ILogger<PlaybackWebViewHost> _logger;
    private readonly WebView2 _webView;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates the host and its owned <see cref="WebView2"/> element. Must be constructed on the
    /// UI thread (resolved from the shell) because it instantiates a XAML control.
    /// </summary>
    /// <param name="controller">
    /// The singleton playback controller (also registered as <see cref="IPlaybackController"/> /
    /// <see cref="IJsBridge"/>). The host attaches the element's core to it in
    /// <see cref="InitializeAsync"/>.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public PlaybackWebViewHost(
        WebView2PlaybackController controller,
        WebViewEnvironmentProvider environmentProvider,
        ExtensionsService extensions,
        ILogger<PlaybackWebViewHost>? logger = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _logger = logger ?? NullLogger<PlaybackWebViewHost>.Instance;

        // The owned element. Top-left aligned with a fixed 1Ã—1 size so it occupies no visible
        // space in Hidden mode but stays live in the visual tree (CoreWebView2 keeps rendering).
        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = HiddenEdge,
            Height = HiddenEdge,
            IsHitTestVisible = false,
        };

        _controller.DisplayModeChanged += OnDisplayModeChanged;
        ApplyDisplayMode(_controller.DisplayMode);
    }

    /// <summary>
    /// The hidden playback <see cref="WebView2"/> element. The shell must place this into a live
    /// window's visual tree (e.g. <c>MainWindow</c>'s root grid) so its
    /// <see cref="Microsoft.Web.WebView2.Core.CoreWebView2"/> can be created. Owned by the host;
    /// the shell must not dispose it.
    /// </summary>
    public WebView2 Element => _webView;

    /// <summary>
    /// Ensures the element's <see cref="Microsoft.Web.WebView2.Core.CoreWebView2"/> is created and
    /// attaches it to the controller (Req 1.1). Idempotent and safe to call once the element has
    /// been mounted into a live window. Call this after the shell adds <see cref="Element"/> to its
    /// visual tree and the window has been activated.
    /// </summary>
    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_initialized)
            {
                return;
            }

            // Creating the core requires the element to be live in a window's visual tree. Use the
            // shared environment (extensions enabled + shared session profile) so user extensions
            // like uBlock apply and the session cookie is shared with the login/watch WebViews.
            var environment = await _environmentProvider.GetAsync().ConfigureAwait(true);
            await _webView.EnsureCoreWebView2Async(environment);
            await _controller.AttachAsync(_webView.CoreWebView2).ConfigureAwait(true);

            // Load user-installed browser extensions (uBlock, etc.) onto the shared profile (ADR 0014
            // equivalent). Best-effort so a missing/unsupported extension never blocks playback.
            await _extensions.LoadIntoAsync(_webView.CoreWebView2.Profile).ConfigureAwait(true);

            _initialized = true;
            _logger.LogInformation("Playback WebView2 host initialized and attached to controller.");
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Signs the user out by clearing all cookies from the shared browser profile (the login,
    /// playback and watch WebViews all read this profile) and pausing playback. The auth state
    /// machine re-evaluates to signed-out on the next cookie check. A no-op when the core is not
    /// created yet.
    /// </summary>
    public async Task SignOutAsync()
    {
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await _controller.PauseAsync().ConfigureAwait(true);
        }
        catch
        {
            // Pausing is best-effort; sign-out must still clear the session below.
        }

        core.CookieManager.DeleteAllCookies();

        // Reload the playback surface so it drops the now-unauthenticated page state.
        core.Navigate("about:blank");
    }

    private void OnDisplayModeChanged(object? sender, PlaybackDisplayMode mode)
    {
        // DisplayModeChanged may be raised off the UI thread; marshal the resize/visibility change
        // onto the element's dispatcher.
        var dispatcher = _webView.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            ApplyDisplayMode(mode);
            return;
        }

        dispatcher.TryEnqueue(() => ApplyDisplayMode(mode));
    }

    /// <summary>
    /// Resizes / shows the owned element for the requested mode (Req 26 seam). The element is kept
    /// in the visual tree in every mode so playback never stops.
    /// </summary>
    private void ApplyDisplayMode(PlaybackDisplayMode mode)
    {
        switch (mode)
        {
            case PlaybackDisplayMode.MiniPlayer:
                _webView.HorizontalAlignment = HorizontalAlignment.Left;
                _webView.VerticalAlignment = VerticalAlignment.Top;
                _webView.Width = MiniPlayerWidth;
                _webView.Height = MiniPlayerHeight;
                _webView.IsHitTestVisible = true;
                break;

            case PlaybackDisplayMode.Video:
                // Full surface â€” stretch to fill the host slot (Req 26, phased UI).
                _webView.HorizontalAlignment = HorizontalAlignment.Stretch;
                _webView.VerticalAlignment = VerticalAlignment.Stretch;
                _webView.Width = double.NaN;
                _webView.Height = double.NaN;
                _webView.IsHitTestVisible = true;
                break;

            case PlaybackDisplayMode.Hidden:
            default:
                // 1Ã—1 audio-only surface; stays live so background audio continues (Req 1.1/1.4).
                _webView.HorizontalAlignment = HorizontalAlignment.Left;
                _webView.VerticalAlignment = VerticalAlignment.Top;
                _webView.Width = HiddenEdge;
                _webView.Height = HiddenEdge;
                _webView.IsHitTestVisible = false;
                break;
        }
    }

    /// <summary>
    /// Releases the controller (stops audio) and closes the owned element. Called only on explicit
    /// app shutdown â€” never on page navigation (Req 1.4/1.5).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.DisplayModeChanged -= OnDisplayModeChanged;

        try
        {
            await _controller.ReleaseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Releasing the playback controller during host disposal failed.");
        }

        _webView.Close();
        _initGate.Dispose();
    }
}
