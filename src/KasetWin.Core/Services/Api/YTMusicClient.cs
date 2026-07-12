using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Tunable knobs for <see cref="YTMusicClient"/>. All values are non-secret.
/// </summary>
public sealed record YTMusicClientOptions
{
    /// <summary>Request origin used for SAPISIDHASH and the Origin/Referer/X-Origin headers.</summary>
    public string Origin { get; init; } = InnerTubeSupport.MusicOrigin;

    /// <summary>Per-request timeout applied when this client owns its <see cref="HttpClient"/>.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum attempts handed to the retry policy for retryable failures.</summary>
    public int MaxAttempts { get; init; } = 3;
}

/// <summary>
/// YouTube Music (InnerTube) client. This file (task 7.1) holds the request core: HTTP
/// configuration, authorization headers, cache + retry integration, error mapping, and the
/// pure id helpers. Endpoint groups are implemented in sibling partial files
/// (<c>YTMusicClient.Browse.cs</c> / <c>.Search.cs</c> / <c>.Mutations.cs</c>, tasks 7.2–7.4).
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency, so it can be driven headless
/// and reused by the API Explorer CLI. Cookies and the SAPISID value are secrets and are never
/// logged.
/// </remarks>
public sealed partial class YTMusicClient : IYTMusicClient
{
    /// <summary>InnerTube v1 base URL. Endpoint names are appended to this path.</summary>
    private const string BaseUrl = "https://music.youtube.com/youtubei/v1/";

    /// <summary>
    /// Browser-style (Safari-like) User-Agent. Non-secret; YouTube Music gates some responses
    /// on a browser-shaped agent string.
    /// </summary>
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.4 Safari/605.1.15";

    private readonly HttpClient _http;
    private readonly ICookieSource _cookieSource;
    private readonly IApiCache _cache;
    private readonly IRetryPolicy _retryPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YTMusicClient> _logger;
    private readonly YTMusicClientOptions _options;

    /// <summary>
    /// Creates a client over an injected <see cref="HttpClient"/> (DI / <c>IHttpClientFactory</c>
    /// friendly). Use <see cref="CreateConfiguredHttpClient"/> to obtain a handler configured with
    /// the connection limit, timeout, and browser User-Agent when not supplying your own.
    /// </summary>
    public YTMusicClient(
        HttpClient httpClient,
        ICookieSource cookieSource,
        IApiCache cache,
        IRetryPolicy retryPolicy,
        TimeProvider? timeProvider = null,
        ILogger<YTMusicClient>? logger = null,
        YTMusicClientOptions? options = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cookieSource = cookieSource ?? throw new ArgumentNullException(nameof(cookieSource));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<YTMusicClient>.Instance;
        _options = options ?? new YTMusicClientOptions();
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> backed by a <see cref="SocketsHttpHandler"/> configured
    /// for YouTube Music: 6 connections per server, automatic decompression, manual cookie
    /// handling (cookies are supplied per-request via <see cref="ICookieSource"/>), a 15s timeout,
    /// and a browser-style User-Agent.
    /// </summary>
    public static HttpClient CreateConfiguredHttpClient(TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 6,
            AutomaticDecompression = DecompressionMethods.All,
            // We attach the Cookie header explicitly per request; disable the cookie container
            // so the handler never mixes in stale/auto-managed cookies.
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        return client;
    }

    /// <summary>
    /// Resolves the authorization material for <paramref name="origin"/>: a snapshot of cookies
    /// plus the derived <c>Cookie</c> header and <c>SAPISIDHASH</c> Authorization value. When no
    /// SAPISID can be resolved the request is treated as unauthenticated (public endpoints still
    /// work) and <see cref="AuthHeaders.Authorization"/> is <see langword="null"/>.
    /// </summary>
    internal async Task<AuthHeaders> BuildAuthHeadersAsync(string origin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var snapshot = await _cookieSource.GetCookiesAsync(origin, ct).ConfigureAwait(false);
        return BuildAuthHeaders(origin, snapshot);
    }

    /// <summary>
    /// Pure header construction from an already-resolved <see cref="CookieSnapshot"/>. Separated
    /// from the async cookie fetch so the request core can reuse a single snapshot for both cache
    /// scoping and header building.
    /// </summary>
    private AuthHeaders BuildAuthHeaders(string origin, CookieSnapshot snapshot)
    {
        // Build the Cookie header from every name=value pair (order preserved, non-empty only).
        var cookieHeader = new StringBuilder();
        foreach (var cookie in snapshot.Cookies)
        {
            if (string.IsNullOrEmpty(cookie.Name) || string.IsNullOrEmpty(cookie.Value))
            {
                continue;
            }

            if (cookieHeader.Length > 0)
            {
                cookieHeader.Append("; ");
            }

            cookieHeader.Append(cookie.Name).Append('=').Append(cookie.Value);
        }

        // Resolve SAPISID and compute the SAPISIDHASH Authorization value (null when signed out).
        string? authorization = null;
        if (CookieSapisidResolver.TryResolve(snapshot.Cookies, out var sapisid))
        {
            var unixSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            authorization = InnerTubeSupport.ComputeSapisidHash(unixSeconds, sapisid, origin);
        }

        var authUser = snapshot.AuthUserIndex is { } index
            ? index.ToString(CultureInfo.InvariantCulture)
            : null;

        return new AuthHeaders(
            origin,
            cookieHeader.Length > 0 ? cookieHeader.ToString() : null,
            authorization,
            authUser,
            snapshot.OnBehalfOfUser);
    }

    /// <summary>
    /// Core request helper: POSTs <paramref name="body"/> (merged with the WEB_REMIX context) to
    /// <c>youtubei/v1/{endpoint}</c>, integrating the API cache (when <paramref name="ttl"/> is
    /// supplied) and the retry policy. HTTP 401/403 map to <see cref="KasetErrorKind.AuthExpired"/>,
    /// transport failures to <see cref="KasetErrorKind.NetworkError"/>, and other non-success
    /// statuses to <see cref="KasetErrorKind.ApiError"/>.
    /// </summary>
    /// <param name="endpoint">InnerTube endpoint path (e.g. <c>browse</c>, <c>search</c>).</param>
    /// <param name="body">Endpoint-specific parameters merged on top of the request context.</param>
    /// <param name="ttl">Cache lifetime; when <see langword="null"/> the response is not cached.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed JSON response root.</returns>
    private async Task<JsonNode> RequestAsync(
        string endpoint,
        JsonObject body,
        TimeSpan? ttl,
        CancellationToken ct,
        string? glOverride = null,
        string? clientVersionOverride = null,
        Func<JsonNode, bool>? shouldCache = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentNullException.ThrowIfNull(body);

        // Resolve cookies/account selectors once; reuse for cache scoping and headers.
        var snapshot = await _cookieSource.GetCookiesAsync(_options.Origin, ct).ConfigureAwait(false);

        // Merge the WEB_REMIX context (+ optional brand account) with the endpoint body.
        var payload = InnerTubeSupport.BuildContext(snapshot.OnBehalfOfUser);

        // Optional client overrides (e.g. podcasts, a surface the pinned 2023 WEB_REMIX version
        // 404s — a newer clientVersion unlocks it). gl/hl pin the region; clientVersion the build.
        if ((glOverride is not null || clientVersionOverride is not null)
            && payload["context"] is JsonObject ctx
            && ctx["client"] is JsonObject client)
        {
            if (glOverride is not null)
            {
                // Region only — DON'T pin hl, or YT translates text (e.g. podcast descriptions) to
                // English instead of the account's original language.
                client["gl"] = glOverride;
            }

            if (clientVersionOverride is not null)
            {
                client["clientVersion"] = clientVersionOverride;
            }
        }
        foreach (var pair in body)
        {
            payload[pair.Key] = pair.Value?.DeepClone();
        }

        var authUser = snapshot.AuthUserIndex?.ToString(CultureInfo.InvariantCulture);
        var cacheKey = _cache.ComputeKey(endpoint, payload, authUser, snapshot.OnBehalfOfUser);

        if (ttl is not null && _cache.TryGet<string>(cacheKey, out var cached) && cached is not null)
        {
            return JsonNode.Parse(cached) ?? new JsonObject();
        }

        var headers = BuildAuthHeaders(_options.Origin, snapshot);

        var responseBody = await _retryPolicy.ExecuteAsync(
            () => SendAsync(endpoint, payload, headers, ct),
            static ex => ex is KasetError { IsRetryable: true },
            _options.MaxAttempts,
            initialDelay: null,
            ct).ConfigureAwait(false);

        JsonNode node;
        try
        {
            node = JsonNode.Parse(responseBody) ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new KasetError(KasetErrorKind.ParseError, "Failed to parse API response JSON.", ex);
        }

        // Some InnerTube failures arrive as HTTP 200 carrying a top-level error object.
        if (node is JsonObject obj
            && obj.TryGetPropertyValue("error", out var errorNode)
            && errorNode is JsonObject errorObj)
        {
            var statusCode = errorObj.TryGetPropertyValue("code", out var codeNode)
                && codeNode is not null
                && int.TryParse(codeNode.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;

            if (statusCode is 401 or 403)
            {
                throw YTMusicErrorMapping.MapHttpError((HttpStatusCode)statusCode.Value);
            }

            throw new KasetError(KasetErrorKind.ApiError, "API returned an error payload.", statusCode: statusCode);
        }

        // A caller-supplied gate keeps transient junk envelopes (e.g. a Home answered before the
        // WebView2 cookies are ready) out of the cache — otherwise the bad response is replayed
        // for the whole TTL and the resulting error toast repeats even after the server recovered.
        if (ttl is { } setTtl && (shouldCache?.Invoke(node) ?? true))
        {
            _cache.Set(cacheKey, responseBody, setTtl);
        }

        return node;
    }

    /// <summary>
    /// Issues a single POST attempt and returns the raw response body. Translates non-success
    /// statuses and transport exceptions into <see cref="KasetError"/> so the retry policy can
    /// classify retryability uniformly.
    /// </summary>
    private async Task<string> SendAsync(
        string endpoint,
        JsonObject payload,
        AuthHeaders headers,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        // Authorization (SAPISIDHASH) — omitted for unauthenticated/public requests.
        if (headers.Authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", headers.Authorization);
        }

        if (headers.Cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", headers.Cookie);
        }

        request.Headers.TryAddWithoutValidation("Origin", headers.Origin);
        request.Headers.TryAddWithoutValidation("Referer", headers.Origin + "/");
        request.Headers.TryAddWithoutValidation("X-Origin", headers.Origin);

        if (headers.AuthUser is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", headers.AuthUser);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw YTMusicErrorMapping.MapNetworkError(ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout (not a caller-initiated cancellation) — treat as a retryable network error.
            throw YTMusicErrorMapping.MapNetworkError(ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw YTMusicErrorMapping.MapHttpError(response.StatusCode);
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolved per-request authorization headers. The <see cref="Cookie"/> and
    /// <see cref="Authorization"/> values are secrets and must never be logged.
    /// </summary>
    internal readonly record struct AuthHeaders(
        string Origin,
        string? Cookie,
        string? Authorization,
        string? AuthUser,
        string? OnBehalfOfUser);
}
