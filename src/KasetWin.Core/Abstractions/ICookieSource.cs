using KasetWin.Core.Services.Api;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// An immutable snapshot of the cookies (and account selectors) available for a single
/// request origin. Produced by <see cref="ICookieSource"/> and consumed by the
/// <c>YTMusicClient</c> when building authorization headers.
/// </summary>
/// <param name="Origin">
/// The origin the cookies belong to (e.g. <c>https://music.youtube.com</c>). Echoed back so
/// header construction (Origin / Referer / X-Origin) and SAPISIDHASH computation stay
/// consistent with whatever origin actually produced the cookies.
/// </param>
/// <param name="Cookies">
/// The cookie name/value pairs for <paramref name="Origin"/>. Treated as secrets — never logged.
/// </param>
/// <param name="AuthUserIndex">
/// Optional Google multi-account index emitted as the <c>X-Goog-AuthUser</c> header. When
/// <see langword="null"/>, the header is omitted (Req 3 brand/multi-account support).
/// </param>
/// <param name="OnBehalfOfUser">
/// Optional 21-digit brand-account id emitted as <c>context.user.onBehalfOfUser</c> in the
/// request body. Distinct from <paramref name="AuthUserIndex"/> (header vs body context).
/// </param>
public sealed record CookieSnapshot(
    string Origin,
    IReadOnlyList<CookiePair> Cookies,
    int? AuthUserIndex = null,
    string? OnBehalfOfUser = null)
{
    /// <summary>An empty, unauthenticated snapshot for the supplied origin.</summary>
    public static CookieSnapshot Empty(string origin) => new(origin, []);
}

/// <summary>
/// WinUI/WinRT-free abstraction over the session cookie store (Req 3.3). The platform layer
/// implements this against <c>CoreWebView2.CookieManager</c> (task 9.1); tests provide an
/// in-memory fake. Keeping the contract in <c>KasetWin.Core</c> lets header construction and
/// SAPISID resolution stay headless-testable.
/// </summary>
public interface ICookieSource
{
    /// <summary>
    /// Returns the current cookie snapshot for <paramref name="origin"/>. Implementations must
    /// return an empty (but non-null) snapshot rather than throwing when no session exists, so
    /// callers can still issue unauthenticated requests to public endpoints.
    /// </summary>
    /// <param name="origin">The request origin to read cookies for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CookieSnapshot> GetCookiesAsync(string origin, CancellationToken ct = default);
}
