using KasetWin.Core.Services.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;

namespace KasetWin.App.Auth;

/// <summary>
/// App-layer entry point for presenting the interactive Google sign-in flow (Task 9.3,
/// Req 4.2/4.3). The shell resolves this from DI and calls <see cref="ShowAsync"/> when a sign-in
/// is needed (e.g. when <c>IAuthService.State</c> is <see cref="AuthState.LoggedOut"/> or
/// <c>NeedsReauth</c> is set).
/// </summary>
public interface ILoginFlow
{
    /// <summary>
    /// Transitions the auth service into <see cref="AuthState.LoggingIn"/> and presents the
    /// <see cref="LoginDialog"/> for the supplied <see cref="XamlRoot"/>. Resolves when the dialog
    /// closes â€” either automatically after a valid session is detected or because the user
    /// cancelled.
    /// </summary>
    /// <param name="xamlRoot">The presenting window's <see cref="XamlRoot"/> (required for a content dialog).</param>
    /// <returns><see langword="true"/> when the session is <see cref="AuthState.LoggedIn"/> after the dialog closes.</returns>
    Task<bool> ShowAsync(XamlRoot xamlRoot);
}

/// <summary>
/// Default <see cref="ILoginFlow"/> implementation. Owns no UI state of its own; it wires the
/// <see cref="IAuthService"/> state machine to a freshly-constructed <see cref="LoginDialog"/>
/// per invocation.
/// </summary>
internal sealed class LoginFlow : ILoginFlow
{
    private readonly IAuthService _authService;
    private readonly CoreWebView2Accessor _accessor;
    private readonly KasetWin.Platform.Playback.WebViewEnvironmentProvider _environmentProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LoginFlow> _logger;

    /// <summary>Creates the login flow.</summary>
    /// <param name="authService">The auth state machine (Req 4).</param>
    /// <param name="accessor">Shared holder exposing the active WebView2 core to the cookie source.</param>
    /// <param name="environmentProvider">Shared WebView2 environment so the login WebView shares the session profile.</param>
    /// <param name="loggerFactory">Optional factory used to create the dialog's category logger.</param>
    public LoginFlow(
        IAuthService authService,
        CoreWebView2Accessor accessor,
        KasetWin.Platform.Playback.WebViewEnvironmentProvider environmentProvider,
        ILoggerFactory? loggerFactory = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<LoginFlow>();
    }

    /// <inheritdoc />
    public async Task<bool> ShowAsync(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        // Req 4.2: begin login â€” the service moves to LoggingIn and the dialog presents the flow.
        await _authService.StartLoginAsync().ConfigureAwait(true);
        _logger.LogInformation("Presenting Google sign-in dialog.");

        var dialog = new LoginDialog(
            _authService,
            _accessor,
            _environmentProvider,
            xamlRoot,
            _loggerFactory.CreateLogger<LoginDialog>());

        await dialog.ShowAsync();

        // The dialog auto-closes on detection; do a final evaluation in case it was cancelled
        // after cookies were set but before the auto-close handler ran.
        await _authService.CheckLoginStatusAsync().ConfigureAwait(true);

        var loggedIn = _authService.State == AuthState.LoggedIn;
        _logger.LogInformation("Sign-in dialog closed; signed in: {LoggedIn}.", loggedIn);
        return loggedIn;
    }
}
