namespace KasetWin.Core.Services.Api;

/// <summary>
/// A WinUI/WinRT-free, neutral representation of a single cookie name/value pair.
/// </summary>
/// <remarks>
/// This record intentionally lives in <c>KasetWin.Core</c> and carries no dependency on
/// WebView2 / WinRT types. The platform-layer <c>WebView2CookieSource</c> (task 9.1) is
/// responsible for mapping <c>CoreWebView2Cookie</c> instances onto this neutral type so
/// that SAPISID resolution stays a pure, headless-testable operation.
/// </remarks>
/// <param name="Name">The cookie name (matched case-sensitively, e.g. <c>__Secure-3PAPISID</c>).</param>
/// <param name="Value">The cookie value. Treated as a secret — never logged.</param>
public readonly record struct CookiePair(string Name, string Value);

/// <summary>
/// Pure, dependency-free resolver that extracts the SAPISID value from a collection of
/// cookies (Req 3.3). Resolution is deterministic and easily testable (see task 2.5,
/// Property 3): it prefers <c>__Secure-3PAPISID</c>, falls back to <c>SAPISID</c>, and
/// reports failure when neither is present.
/// </summary>
/// <remarks>
/// This type has no WinUI/WinRT dependency and performs no I/O. The resolved value is a
/// secret and must never be written to logs, fixtures, or documentation.
/// </remarks>
public static class CookieSapisidResolver
{
    /// <summary>Primary cookie name carrying the SAPISID value.</summary>
    public const string PrimaryCookieName = "__Secure-3PAPISID";

    /// <summary>Fallback cookie name used when <see cref="PrimaryCookieName"/> is absent.</summary>
    public const string FallbackCookieName = "SAPISID";

    /// <summary>
    /// Resolves the SAPISID value from the supplied cookie collection.
    /// </summary>
    /// <param name="cookies">The cookies to inspect. Order is irrelevant.</param>
    /// <returns>
    /// The value of <see cref="PrimaryCookieName"/> when present and non-empty; otherwise the
    /// value of <see cref="FallbackCookieName"/> when present and non-empty; otherwise
    /// <see langword="null"/> when neither cookie supplies a usable value.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="cookies"/> is <see langword="null"/>.</exception>
    public static string? Resolve(IEnumerable<CookiePair> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        // Materialize once so the collection is only enumerated a single time.
        string? primary = null;
        string? fallback = null;

        foreach (var cookie in cookies)
        {
            if (string.IsNullOrEmpty(cookie.Value))
            {
                // An empty value cannot authenticate; treat it as absent so the
                // fallback (or failure) path can take over deterministically.
                continue;
            }

            if (primary is null && string.Equals(cookie.Name, PrimaryCookieName, StringComparison.Ordinal))
            {
                primary = cookie.Value;
            }
            else if (fallback is null && string.Equals(cookie.Name, FallbackCookieName, StringComparison.Ordinal))
            {
                fallback = cookie.Value;
            }
        }

        return primary ?? fallback;
    }

    /// <summary>
    /// Attempts to resolve the SAPISID value from the supplied cookie collection.
    /// </summary>
    /// <param name="cookies">The cookies to inspect. Order is irrelevant.</param>
    /// <param name="sapisid">
    /// When this method returns <see langword="true"/>, contains the resolved SAPISID value;
    /// otherwise an empty string.
    /// </param>
    /// <returns><see langword="true"/> when a SAPISID value was resolved; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cookies"/> is <see langword="null"/>.</exception>
    public static bool TryResolve(IEnumerable<CookiePair> cookies, out string sapisid)
    {
        var resolved = Resolve(cookies);
        sapisid = resolved ?? string.Empty;
        return resolved is not null;
    }
}
