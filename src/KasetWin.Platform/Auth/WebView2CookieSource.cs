using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace KasetWin.Platform.Auth;

/// <summary>
/// <see cref="ICookieSource"/> adapter that reads YouTube Music cookies from a live
/// <see cref="CoreWebView2"/> instance and maps each <see cref="CoreWebView2Cookie"/> onto the
/// WinRT-free <see cref="CookiePair"/> record, returning a <see cref="CookieSnapshot"/>
/// (task 9.1, Req 3.3).
/// </summary>
/// <remarks>
/// <para>
/// The real WebView2 controller (task 8.2 / 8.3) owns the <see cref="CoreWebView2"/> lifecycle.
/// To avoid forcing WebView2 creation at construction time (important for DI and tests) this
/// source receives a <see cref="Func{TResult}"/> provider that returns the current
/// <see cref="CoreWebView2"/> (or <see langword="null"/> when it has not been initialized yet).
/// </para>
/// <para>
/// No WinRT type escapes this class: callers only ever observe <see cref="CookiePair"/> /
/// <see cref="CookieSnapshot"/> values. Cookie values are secrets and are never logged.
/// </para>
/// </remarks>
public sealed class WebView2CookieSource : ICookieSource
{
    /// <summary>The music origin whose cookies authenticate InnerTube requests (Req 3.5).</summary>
    public const string MusicOrigin = "https://music.youtube.com";

    private readonly Func<CoreWebView2?> _coreWebViewProvider;
    private readonly ILogger<WebView2CookieSource> _logger;

    /// <summary>
    /// Creates a cookie source backed by the supplied provider.
    /// </summary>
    /// <param name="coreWebViewProvider">
    /// Returns the current <see cref="CoreWebView2"/>, or <see langword="null"/> when the WebView2
    /// has not been initialized. The provider is invoked on each read so the source always sees
    /// the latest instance without holding a strong reference at construction time.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public WebView2CookieSource(Func<CoreWebView2?> coreWebViewProvider, ILogger<WebView2CookieSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(coreWebViewProvider);
        _coreWebViewProvider = coreWebViewProvider;
        _logger = logger ?? NullLogger<WebView2CookieSource>.Instance;
    }

    /// <inheritdoc />
    public async Task<CookieSnapshot> GetCookiesAsync(string origin, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ct.ThrowIfCancellationRequested();

        var core = _coreWebViewProvider();
        if (core is null)
        {
            // WebView2 not initialized yet: return an empty (non-null) snapshot so callers can
            // still issue unauthenticated requests instead of having to handle an exception.
            _logger.LogDebug("Cookie source queried before WebView2 initialization; returning empty snapshot.");
            return CookieSnapshot.Empty(origin);
        }

        var cookies = await core.CookieManager.GetCookiesAsync(origin);
        ct.ThrowIfCancellationRequested();

        if (cookies is null || cookies.Count == 0)
        {
            return CookieSnapshot.Empty(origin);
        }

        var pairs = new List<CookiePair>(cookies.Count);
        foreach (var cookie in cookies)
        {
            // Map WinRT cookie → neutral Core type. Value is a secret: never logged.
            pairs.Add(new CookiePair(cookie.Name, cookie.Value));
        }

        // Log only the count (non-sensitive) for diagnostics.
        _logger.LogDebug("Read {CookieCount} cookies from WebView2 for origin {Origin}.", pairs.Count, origin);
        return new CookieSnapshot(origin, pairs);
    }
}
