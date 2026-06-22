using KasetWin.Platform.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Hosting;

/// <summary>
/// App-layer owner of the <see cref="WebView2"/> that plays a regular-YouTube (video) watch page
/// (Req 32.2). Analogous to <see cref="PlaybackWebViewHost"/> (the always-on hidden music WebView2),
/// but this surface is <b>visible</b> — it is mounted into the <c>YouTubeWatchPage</c>'s video region
/// — and is created and torn down with the page lifecycle so navigating away stops the video and
/// releases its audio (Req 32.3).
/// </summary>
/// <remarks>
/// <para>
/// Creating a <see cref="Microsoft.Web.WebView2.Core.CoreWebView2"/> requires the element to be live
/// in a window's visual tree, so the host exposes <see cref="Element"/> for the page to mount into
/// its video host panel. Once the element is live, <see cref="InitializeAsync"/> creates the core and
/// hands it to the singleton <see cref="YouTubeWatchController"/> via <c>AttachAsync</c>.
/// </para>
/// <para>
/// The host is created per page (not a DI singleton) — the <em>controller</em> is the singleton that
/// outlives pages so the <c>PlaybackArbiter</c> can keep enforcing a single audio source. On
/// <see cref="DisposeAsync"/> the host releases the controller (stops the video, blanks the page) and
/// closes the owned element.
/// </para>
/// </remarks>
public sealed class YouTubeWatchWebViewHost : IAsyncDisposable
{
    private readonly YouTubeWatchController _controller;
    private readonly ILogger<YouTubeWatchWebViewHost> _logger;
    private readonly WebView2 _webView;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates the host and its owned <see cref="WebView2"/> element. Must be constructed on the UI
    /// thread (resolved from the page) because it instantiates a XAML control.
    /// </summary>
    /// <param name="controller">The singleton YouTube watch controller the element's core attaches to.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public YouTubeWatchWebViewHost(
        YouTubeWatchController controller,
        ILogger<YouTubeWatchWebViewHost>? logger = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? NullLogger<YouTubeWatchWebViewHost>.Instance;

        // Fills the watch page's video region. Hit-test invisible so YouTube's own chrome (hidden by
        // the extraction script) never steals clicks; native controls/metadata sit around it.
        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    /// <summary>
    /// The watch <see cref="WebView2"/> element. The page must place this into a live window's
    /// visual tree (its video host panel) so the core can be created. Owned by the host; the page
    /// must not dispose it (call <see cref="DisposeAsync"/> instead).
    /// </summary>
    public WebView2 Element => _webView;

    /// <summary>
    /// Ensures the element's core is created and attaches it to the controller (Req 32.2). Idempotent
    /// and safe to call once the element has been mounted into a live window.
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

            await _webView.EnsureCoreWebView2Async();
            await _controller.AttachAsync(_webView.CoreWebView2).ConfigureAwait(true);

            _initialized = true;
            _logger.LogInformation("YouTube watch WebView2 host initialized and attached to controller.");
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Releases the controller (stops the video + blanks the page) and closes the owned element.
    /// Called when the watch page is navigated away from (Req 32.3 single-audio cleanup).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await _controller.ReleaseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Releasing the YouTube watch controller during host disposal failed.");
        }

        _webView.Close();
        _initGate.Dispose();
    }
}
