using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.Api;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless, in-memory fake of <see cref="ICookieSource"/> standing in for the platform
/// <c>WebView2CookieSource</c> (which reads <c>CoreWebView2.CookieManager</c>, a WinRT type).
/// Lets the cookie-extraction + SAPISID-resolution contract be exercised on the plain .NET
/// runner (task 9.5, Req 22.1 / 3.3).
///
/// SECURITY: only ever populated with synthetic placeholder cookie values in tests — never real
/// cookies / SAPISID values.
/// </summary>
internal sealed class InMemoryCookieSource : ICookieSource
{
    private readonly IReadOnlyList<CookiePair> _cookies;
    private readonly int? _authUserIndex;
    private readonly string? _onBehalfOfUser;

    public InMemoryCookieSource(
        IReadOnlyList<CookiePair> cookies,
        int? authUserIndex = null,
        string? onBehalfOfUser = null)
    {
        _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
        _authUserIndex = authUserIndex;
        _onBehalfOfUser = onBehalfOfUser;
    }

    /// <summary>The origin the most recent <see cref="GetCookiesAsync"/> call requested.</summary>
    public string? LastRequestedOrigin { get; private set; }

    /// <inheritdoc />
    public Task<CookieSnapshot> GetCookiesAsync(string origin, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ct.ThrowIfCancellationRequested();
        LastRequestedOrigin = origin;

        return _cookies.Count == 0
            ? Task.FromResult(CookieSnapshot.Empty(origin))
            : Task.FromResult(new CookieSnapshot(origin, _cookies, _authUserIndex, _onBehalfOfUser));
    }
}
