namespace KasetWin.Core.Services.Auth;

/// <summary>
/// The authentication lifecycle states surfaced by <see cref="IAuthService"/> (Req 4.1–4.3).
/// </summary>
public enum AuthState
{
    /// <summary>No valid session exists (Req 4.1). The default state on a cold launch.</summary>
    LoggedOut,

    /// <summary>The Google login flow is in progress in the WebView2 (Req 4.2).</summary>
    LoggingIn,

    /// <summary>A valid <c>__Secure-3PAPISID</c> session was detected (Req 4.3).</summary>
    LoggedIn,
}

/// <summary>
/// The discrete events that drive the authentication state machine. Kept internal-facing and
/// distinct from the public API so the transition rules can be exercised as a pure function
/// (Property 5) without any cookie I/O.
/// </summary>
public enum AuthEvent
{
    /// <summary>The user asked to begin logging in (Req 4.2).</summary>
    LoginStarted,

    /// <summary>A re-evaluation found a usable SAPISID cookie (Req 4.3/4.6).</summary>
    CookiesPresent,

    /// <summary>A re-evaluation found no usable SAPISID cookie (Req 4.1/4.4/4.6).</summary>
    CookiesAbsent,

    /// <summary>The client reported <c>AuthExpired</c> (HTTP 401/403) (Req 4.5).</summary>
    AuthExpired,
}

/// <summary>
/// The immutable result of a state-machine transition: the resulting <see cref="State"/> and
/// the <see cref="NeedsReauth"/> flag that tells the UI whether a re-auth prompt is required.
/// </summary>
/// <param name="State">The state after applying an event.</param>
/// <param name="NeedsReauth">Whether the session expired and a re-auth flow should be offered.</param>
public readonly record struct AuthStatus(AuthState State, bool NeedsReauth);

/// <summary>
/// Pure, dependency-free transition function for the authentication state machine
/// (Req 4, Property 5). Because it performs no I/O and has no state of its own, the full
/// transition table can be verified headless across arbitrary event sequences.
/// </summary>
/// <remarks>
/// <para>Invariants enforced here (relied upon by <see cref="AuthService"/> and Property 5):</para>
/// <list type="bullet">
///   <item><description>The resulting <see cref="AuthState"/> is always one of the three defined values.</description></item>
///   <item><description><see cref="AuthEvent.AuthExpired"/> <em>always</em> yields
///   <see cref="AuthState.LoggedOut"/> with <c>NeedsReauth = true</c>, regardless of the prior state.</description></item>
///   <item><description>Beginning login or detecting a session clears <c>NeedsReauth</c> (the user is acting on / has resolved the prompt).</description></item>
/// </list>
/// </remarks>
public static class AuthTransition
{
    /// <summary>
    /// Computes the next <see cref="AuthStatus"/> from the current status and an event.
    /// </summary>
    /// <param name="current">The current authentication state.</param>
    /// <param name="needsReauth">The current re-auth flag (preserved where a transition does not change it).</param>
    /// <param name="ev">The event to apply.</param>
    /// <returns>The resulting state and re-auth flag.</returns>
    public static AuthStatus Next(AuthState current, bool needsReauth, AuthEvent ev) => ev switch
    {
        // Req 4.5: an expired session is terminal — always drop to LoggedOut and flag re-auth.
        AuthEvent.AuthExpired => new AuthStatus(AuthState.LoggedOut, NeedsReauth: true),

        // Req 4.2: starting login moves to LoggingIn and clears any pending re-auth prompt.
        AuthEvent.LoginStarted => new AuthStatus(AuthState.LoggingIn, NeedsReauth: false),

        // Req 4.3/4.6: a detected session means LoggedIn; the re-auth need is resolved.
        AuthEvent.CookiesPresent => new AuthStatus(AuthState.LoggedIn, NeedsReauth: false),

        // Req 4.1/4.4: no session. While the user is mid-login the cookies simply have not been
        // set yet, so stay in LoggingIn; otherwise fall back to LoggedOut, preserving NeedsReauth.
        AuthEvent.CookiesAbsent => current == AuthState.LoggingIn
            ? new AuthStatus(AuthState.LoggingIn, needsReauth)
            : new AuthStatus(AuthState.LoggedOut, needsReauth),

        _ => new AuthStatus(current, needsReauth),
    };
}
