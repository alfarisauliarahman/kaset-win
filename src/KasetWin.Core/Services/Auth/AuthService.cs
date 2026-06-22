using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Auth;

/// <summary>
/// Observable implementation of <see cref="IAuthService"/> (Task 9.2). Owns the authentication
/// state machine for the music origin and is WinUI/WinRT-free so it can be exercised headless
/// (Property 5).
/// </summary>
/// <remarks>
/// <para>
/// Cookie reading is asynchronous (via <see cref="ICookieSource"/>); the transition rules
/// themselves are a pure function (<see cref="AuthTransition"/>). The service reads the current
/// cookie snapshot for the music origin, asks the pure <see cref="CookieSapisidResolver"/>
/// whether a usable SAPISID is present, and maps that to a <see cref="AuthEvent.CookiesPresent"/>
/// or <see cref="AuthEvent.CookiesAbsent"/> event.
/// </para>
/// <para>
/// State mutation is guarded by a single lock; <see cref="INotifyPropertyChanged"/> notifications
/// are raised <em>after</em> the lock is released to avoid re-entrancy. Cookie and SAPISID values
/// are treated as secrets and are <strong>never</strong> logged.
/// </para>
/// </remarks>
public sealed class AuthService : ObservableObject, IAuthService
{
    /// <summary>Credential-store key under which a backup of the SAPISID value is kept (Req 22.1).</summary>
    private const string SapisidBackupKey = "kaset.sapisid.backup";

    private readonly object _gate = new();
    private readonly ICookieSource _cookieSource;
    private readonly ICredentialStore? _credentialStore;
    private readonly IApiCache? _cache;
    private readonly ILogger<AuthService> _logger;

    private AuthState _state = AuthState.LoggedOut;
    private bool _needsReauth;
    private string? _activeAuthUserIndex;
    private string? _onBehalfOfUser;

    /// <summary>
    /// Creates the auth service.
    /// </summary>
    /// <param name="cookieSource">Reads the session cookie snapshot for an origin (required).</param>
    /// <param name="credentialStore">
    /// Optional secure store used to back up the SAPISID value when a session is detected
    /// (Req 22.1). When <see langword="null"/>, no backup is performed.
    /// </param>
    /// <param name="cache">
    /// Optional response cache cleared on account switch so cached data never leaks across
    /// accounts. When <see langword="null"/>, cache invalidation is skipped.
    /// </param>
    /// <param name="logger">Optional category logger; never receives cookie or SAPISID values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cookieSource"/> is <see langword="null"/>.</exception>
    public AuthService(
        ICookieSource cookieSource,
        ICredentialStore? credentialStore = null,
        IApiCache? cache = null,
        ILogger<AuthService>? logger = null)
    {
        _cookieSource = cookieSource ?? throw new ArgumentNullException(nameof(cookieSource));
        _credentialStore = credentialStore;
        _cache = cache;
        _logger = logger ?? NullLogger<AuthService>.Instance;
    }

    /// <inheritdoc />
    public AuthState State
    {
        get { lock (_gate) { return _state; } }
    }

    /// <inheritdoc />
    public bool NeedsReauth
    {
        get { lock (_gate) { return _needsReauth; } }
    }

    /// <inheritdoc />
    public string? ActiveAuthUserIndex
    {
        get { lock (_gate) { return _activeAuthUserIndex; } }
    }

    /// <inheritdoc />
    public string? OnBehalfOfUser
    {
        get { lock (_gate) { return _onBehalfOfUser; } }
    }

    /// <inheritdoc />
    public async Task CheckLoginStatusAsync()
    {
        var snapshot = await _cookieSource
            .GetCookiesAsync(InnerTubeSupport.MusicOrigin)
            .ConfigureAwait(false);

        var sapisid = CookieSapisidResolver.Resolve(snapshot.Cookies);
        var hasSession = !string.IsNullOrEmpty(sapisid);

        if (hasSession)
        {
            // Back up the secret for resilience (Req 22.1). The value is never logged; only the
            // outcome is. A failing store must not break the login evaluation.
            await TryBackupSapisidAsync(sapisid!).ConfigureAwait(false);
            _logger.LogInformation("Login status evaluated for music origin: session present.");
        }
        else
        {
            _logger.LogInformation("Login status evaluated for music origin: session absent.");
        }

        ApplyEvent(hasSession ? AuthEvent.CookiesPresent : AuthEvent.CookiesAbsent);
    }

    /// <inheritdoc />
    public Task StartLoginAsync()
    {
        // The service only owns the state transition (Req 4.2); the App layer presents the
        // Google login WebView2 (task 9.3) and calls back via OnCookiesChanged / CheckLoginStatus.
        _logger.LogInformation("Login started; awaiting Google sign-in.");
        ApplyEvent(AuthEvent.LoginStarted);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void OnCookiesChanged()
    {
        // Cookie-change notifications arrive on a non-async event callback (Req 4.4); kick off a
        // re-evaluation without blocking the caller, swallowing/logging any failure.
        _ = ReevaluateSafelyAsync();
    }

    /// <inheritdoc />
    public void SessionExpired()
    {
        // Req 4.5: an expired session always drops to LoggedOut + NeedsReauth (Property 5).
        _logger.LogWarning("Session expired (AuthExpired); re-auth required.");
        ApplyEvent(AuthEvent.AuthExpired);
    }

    /// <inheritdoc />
    public async Task SwitchAccountAsync(string authUserIndex, string? brandId)
    {
        ArgumentNullException.ThrowIfNull(authUserIndex);

        SetLocked(ref _activeAuthUserIndex, authUserIndex, nameof(ActiveAuthUserIndex));
        SetLocked(ref _onBehalfOfUser, brandId, nameof(OnBehalfOfUser));

        // Cached responses are scoped per account; clear them so data never leaks across accounts.
        _cache?.InvalidateAll();

        if (brandId is null)
        {
            _logger.LogInformation("Switched account (authUser index set, no brand); re-evaluating session.");
        }
        else
        {
            _logger.LogInformation("Switched account (authUser index set, brand account); re-evaluating session.");
        }

        await CheckLoginStatusAsync().ConfigureAwait(false);
    }

    private async Task ReevaluateSafelyAsync()
    {
        try
        {
            await CheckLoginStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A cookie-change re-evaluation is best-effort; never surface as an unobserved task.
            _logger.LogWarning(ex, "Re-evaluation after cookie change failed.");
        }
    }

    private async Task TryBackupSapisidAsync(string sapisid)
    {
        if (_credentialStore is null)
        {
            return;
        }

        try
        {
            await _credentialStore.SaveAsync(SapisidBackupKey, sapisid).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to back up session credential.");
        }
    }

    /// <summary>
    /// Applies <paramref name="ev"/> through the pure <see cref="AuthTransition"/> table under the
    /// lock, then raises change notifications for any property that actually changed.
    /// </summary>
    private void ApplyEvent(AuthEvent ev)
    {
        bool stateChanged;
        bool reauthChanged;

        lock (_gate)
        {
            var next = AuthTransition.Next(_state, _needsReauth, ev);
            stateChanged = next.State != _state;
            reauthChanged = next.NeedsReauth != _needsReauth;
            _state = next.State;
            _needsReauth = next.NeedsReauth;
        }

        if (stateChanged)
        {
            OnPropertyChanged(nameof(State));
        }

        if (reauthChanged)
        {
            OnPropertyChanged(nameof(NeedsReauth));
        }
    }

    /// <summary>
    /// Thread-safe property assignment: assigns under the lock and raises
    /// <see cref="INotifyPropertyChanged"/> outside it only when the value actually changed.
    /// </summary>
    private void SetLocked(ref string? field, string? value, string propertyName)
    {
        lock (_gate)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
        }

        OnPropertyChanged(propertyName);
    }
}
