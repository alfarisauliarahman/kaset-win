using System.ComponentModel;

namespace KasetWin.Core.Services.Auth;

/// <summary>
/// Manages the authentication session lifecycle (Req 4): cold-launch session checks, the
/// login transition, cookie-change re-evaluation, session expiry, and multi-account /
/// brand-account switching.
/// </summary>
/// <remarks>
/// <para>
/// The service is the single source of truth for <see cref="AuthState"/>. UI concerns — showing
/// the Google login WebView2, prompting on re-auth — observe this service via
/// <see cref="INotifyPropertyChanged"/>; the service itself never touches WinUI. The actual
/// login UI is owned by the App layer (<c>LoginDialog</c>, task 9.3); this service only manages
/// state and transitions.
/// </para>
/// <para>
/// All transition rules are delegated to the pure <see cref="AuthTransition"/> helper so the
/// state machine is headless-testable (Property 5).
/// </para>
/// </remarks>
public interface IAuthService : INotifyPropertyChanged
{
    /// <summary>The current authentication state. Always one of the defined <see cref="AuthState"/> values.</summary>
    AuthState State { get; }

    /// <summary>
    /// <see langword="true"/> when the previous session expired (Req 4.5) and the UI should offer
    /// a re-auth flow. Cleared when login begins or a valid session is detected.
    /// </summary>
    bool NeedsReauth { get; }

    /// <summary>
    /// The active Google multi-account index emitted as the <c>X-Goog-AuthUser</c> header, or
    /// <see langword="null"/> for the default account.
    /// </summary>
    string? ActiveAuthUserIndex { get; }

    /// <summary>
    /// The active 21-digit brand-account id emitted as <c>context.user.onBehalfOfUser</c>, or
    /// <see langword="null"/> when not acting on behalf of a brand account.
    /// </summary>
    string? OnBehalfOfUser { get; }

    /// <summary>
    /// Re-evaluates the stored cookies to determine whether a valid session exists, transitioning
    /// to <see cref="AuthState.LoggedIn"/> or <see cref="AuthState.LoggedOut"/> accordingly
    /// (Req 4.3, 4.6). Called on launch and whenever a fresh evaluation is needed.
    /// </summary>
    Task CheckLoginStatusAsync();

    /// <summary>
    /// Transitions to <see cref="AuthState.LoggingIn"/> (Req 4.2). The App layer reacts by showing
    /// the Google login flow in a WebView2; this service only owns the state transition.
    /// </summary>
    Task StartLoginAsync();

    /// <summary>
    /// Notifies the service that the WebView2 session cookies changed (Req 4.4) so it can
    /// re-evaluate login status. Returns immediately; the evaluation runs asynchronously.
    /// </summary>
    void OnCookiesChanged();

    /// <summary>
    /// Records that the client reported an expired session (Req 4.5). Always transitions to
    /// <see cref="AuthState.LoggedOut"/> and sets <see cref="NeedsReauth"/> to <see langword="true"/>.
    /// </summary>
    void SessionExpired();

    /// <summary>
    /// Switches the active account/brand (multi-account support), updating
    /// <see cref="ActiveAuthUserIndex"/> and <see cref="OnBehalfOfUser"/>, invalidating cached
    /// responses tied to the previous account, then re-evaluating login status.
    /// </summary>
    /// <param name="authUserIndex">The Google multi-account index for the target account.</param>
    /// <param name="brandId">The optional 21-digit brand-account id, or <see langword="null"/>.</param>
    Task SwitchAccountAsync(string authUserIndex, string? brandId);
}
