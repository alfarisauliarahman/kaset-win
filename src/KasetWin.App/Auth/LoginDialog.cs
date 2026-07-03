using System.ComponentModel;
using KasetWin.Core.Services.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;

namespace KasetWin.App.Auth;

/// <summary>
/// Interactive Google sign-in dialog (Task 9.3, Req 4.2/4.3). Hosts a <em>visible</em>
/// <see cref="WebView2"/> â€” distinct from the hidden playback WebView â€” that loads the YouTube
/// Music Google login page. The dialog observes the WebView2's session and closes itself
/// automatically once <see cref="IAuthService"/> reports <see cref="AuthState.LoggedIn"/>
/// (a valid <c>__Secure-3PAPISID</c> cookie was detected), and offers a Cancel button otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Built entirely in code (no XAML) so it can be presented from any shell that supplies a
/// <see cref="Microsoft.UI.Xaml.XamlRoot"/>. While shown, the dialog publishes its own
/// <see cref="CoreWebView2"/> into the shared <see cref="CoreWebView2Accessor"/> so
/// <c>AuthService.CheckLoginStatusAsync</c> reads cookies from the page the user is actually
/// signing in on, then restores the previous value on close.
/// </para>
/// <para>
/// The WebView2 uses the app's default (persistent) user-data folder, so the session cookie is
/// retained for subsequent launches and is shared with the hidden playback WebView. Cookie and
/// token values are secrets and are never logged.
/// </para>
/// </remarks>
internal sealed class LoginDialog : ContentDialog
{
    /// <summary>
    /// Google sign-in entry point that returns to YouTube Music on success, so the redirect lands
    /// on the music origin and sets the session cookies the app authenticates with (Req 4.3).
    /// </summary>
    public const string LoginUrl =
        "https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fmusic.youtube.com%2F";

    private const double WebViewWidth = 480;
    private const double WebViewHeight = 600;

    private readonly IAuthService _authService;
    private readonly CoreWebView2Accessor _accessor;
    private readonly KasetWin.Platform.Playback.WebViewEnvironmentProvider _environmentProvider;
    private readonly ILogger _logger;
    private readonly WebView2 _webView;
    private readonly DispatcherQueue _dispatcherQueue;

    private CoreWebView2? _attachedCore;
    private CoreWebView2? _previousAccessorCore;
    private bool _navigated;
    private bool _autoClosed;

    /// <summary>
    /// Creates the sign-in dialog.
    /// </summary>
    /// <param name="authService">The auth state machine driving auto-close (Req 4.3).</param>
    /// <param name="accessor">Shared holder that exposes this dialog's core to the cookie source.</param>
    /// <param name="xamlRoot">The presenting <see cref="Microsoft.UI.Xaml.XamlRoot"/> (required for a content dialog).</param>
    /// <param name="logger">Optional logger; never receives cookie or token values.</param>
    public LoginDialog(
        IAuthService authService,
        CoreWebView2Accessor accessor,
        KasetWin.Platform.Playback.WebViewEnvironmentProvider environmentProvider,
        XamlRoot xamlRoot,
        ILogger? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
        ArgumentNullException.ThrowIfNull(xamlRoot);
        _logger = logger ?? NullLogger.Instance;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        Title = "Sign in to YouTube Music";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Close;
        XamlRoot = xamlRoot;

        _webView = new WebView2
        {
            Width = WebViewWidth,
            Height = WebViewHeight,
        };
        Content = _webView;

        Opened += OnOpened;
        Closed += OnClosed;
        _authService.PropertyChanged += OnAuthPropertyChanged;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        try
        {
            // Shared environment so the login WebView writes cookies to the same profile the
            // playback/watch WebViews read (session sharing) and extensions are consistent.
            var environment = await _environmentProvider.GetAsync();
            await _webView.EnsureCoreWebView2Async(environment);
            var core = _webView.CoreWebView2;
            _attachedCore = core;

            // Publish this dialog's core so the cookie source reads from the page the user signs
            // in on; remember the previous value to restore on close.
            _previousAccessorCore = _accessor.Current;
            _accessor.Current = core;

            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += OnSourceChanged;

            core.Navigate(LoginUrl);
            _navigated = true;
            _logger.LogInformation("Login dialog opened; navigating to Google sign-in.");

            // The session might already be valid (cookies persisted from a prior launch); evaluate
            // once up-front so an already-signed-in user is not asked to sign in again.
            await ReevaluateLoginAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize the login WebView2.");
        }
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args) =>
        QueueReevaluate();

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) =>
        QueueReevaluate();

    /// <summary>
    /// Re-evaluates login status after a navigation/redirect (Req 4.4). Also pokes the auth
    /// service's cookie-changed hook so the documented Req 4.4 path is exercised.
    /// </summary>
    private void QueueReevaluate()
    {
        _authService.OnCookiesChanged();
        _ = ReevaluateLoginAsync();
    }

    private async Task ReevaluateLoginAsync()
    {
        try
        {
            await _authService.CheckLoginStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Best-effort: a failed evaluation must not break the sign-in flow.
            _logger.LogWarning(ex, "Login status re-evaluation failed.");
        }
    }

    private void OnAuthPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IAuthService.State) or null))
        {
            return;
        }

        if (_authService.State != AuthState.LoggedIn)
        {
            return;
        }

        // Auto-close once a valid session is detected (Req 4.3). Marshal onto the UI thread; Hide()
        // must run there.
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            AutoClose();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(AutoClose);
        }
    }

    private void AutoClose()
    {
        if (_autoClosed)
        {
            return;
        }

        _autoClosed = true;
        _logger.LogInformation("Session detected; closing login dialog.");
        Hide();
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _authService.PropertyChanged -= OnAuthPropertyChanged;
        Opened -= OnOpened;
        Closed -= OnClosed;

        if (_attachedCore is not null)
        {
            _attachedCore.NavigationCompleted -= OnNavigationCompleted;
            _attachedCore.SourceChanged -= OnSourceChanged;
        }

        // Restore the previously-published core only if we are still the active publisher, so a
        // core published by another producer in the meantime is not clobbered.
        if (ReferenceEquals(_accessor.Current, _attachedCore))
        {
            _accessor.Current = _previousAccessorCore;
        }

        _attachedCore = null;

        // The dialog owns its WebView2 element; close it to release the browser process resources.
        _ = _navigated; // navigation state retained for diagnostics; no further use after close.
        _webView.Close();
    }
}
