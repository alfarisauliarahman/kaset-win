using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Api.Parsers.YouTube;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Tunable knobs for <see cref="YouTubeClient"/>. All values are non-secret.
/// </summary>
public sealed record YouTubeClientOptions
{
    /// <summary>Request origin used for SAPISIDHASH and the Origin/Referer/X-Origin headers.</summary>
    public string Origin { get; init; } = InnerTubeSupport.YouTubeOrigin;

    /// <summary>Per-request timeout applied when this client owns its <see cref="HttpClient"/>.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum attempts handed to the retry policy for retryable failures.</summary>
    public int MaxAttempts { get; init; } = 3;
}

/// <summary>
/// Regular YouTube (video) InnerTube client (Req 32). Parallel to <see cref="YTMusicClient"/> by
/// design (ADR-0020): the request scaffolding is deliberately duplicated so the proven music path
/// stays untouched, sharing only the origin-neutral pure helpers (<see cref="InnerTubeSupport"/>),
/// the cookie source, the API cache, and the retry policy.
/// </summary>
/// <remarks>
/// The critical difference from the music client is the origin: SAPISIDHASH input and the
/// <c>Origin</c>/<c>Referer</c>/<c>X-Origin</c> headers must all be <c>https://www.youtube.com</c>
/// (a music-origin hash silently 401s), the context client is <c>WEB</c> (not <c>WEB_REMIX</c>), and
/// no <c>key=</c> query parameter is required. Cache keys are prefixed <c>yt:</c> so they never
/// collide with the music client's invalidation patterns. Cookies and SAPISID are never logged.
/// </remarks>
public sealed class YouTubeClient : IYouTubeClient
{
    /// <summary>InnerTube v1 base URL for regular YouTube. Endpoint names are appended to this path.</summary>
    private const string BaseUrl = "https://www.youtube.com/youtubei/v1/";

    /// <summary>Cache-key prefix so YouTube entries never collide with music invalidation patterns.</summary>
    private const string CachePrefix = "yt:";

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.4 Safari/605.1.15";

    private readonly HttpClient _http;
    private readonly ICookieSource _cookieSource;
    private readonly IApiCache _cache;
    private readonly IRetryPolicy _retryPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YouTubeClient> _logger;
    private readonly YouTubeClientOptions _options;

    /// <summary>
    /// Creates a client over an injected <see cref="HttpClient"/>. Use
    /// <see cref="CreateConfiguredHttpClient"/> for a browser-shaped handler when not supplying one.
    /// </summary>
    public YouTubeClient(
        HttpClient httpClient,
        ICookieSource cookieSource,
        IApiCache cache,
        IRetryPolicy retryPolicy,
        TimeProvider? timeProvider = null,
        ILogger<YouTubeClient>? logger = null,
        YouTubeClientOptions? options = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cookieSource = cookieSource ?? throw new ArgumentNullException(nameof(cookieSource));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<YouTubeClient>.Instance;
        _options = options ?? new YouTubeClientOptions();
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> backed by a <see cref="SocketsHttpHandler"/> configured
    /// like the music client: 6 connections per server, automatic decompression, manual cookie
    /// handling, a 15s timeout, and a browser-style User-Agent.
    /// </summary>
    public static HttpClient CreateConfiguredHttpClient(TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 6,
            AutomaticDecompression = DecompressionMethods.All,
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

    // ── Feeds (Req 32.1) ─────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<YouTubeFeed> GetHomeFeedAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEwhat_to_watch"), ApiCacheTtl.Home, ct).ConfigureAwait(false);
        return YouTubeFeedParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<YouTubeFeed> GetFeedContinuationAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var node = await RequestAsync("browse", ContinuationBody(token), ApiCacheTtl.Home, ct).ConfigureAwait(false);
        return YouTubeFeedParser.ParseContinuation(node);
    }

    /// <inheritdoc />
    public async Task<YouTubeFeed> GetSubscriptionsFeedAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEsubscriptions"), ApiCacheTtl.Home, ct).ConfigureAwait(false);
        return YouTubeFeedParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<YouTubeFeed> GetHistoryAsync(CancellationToken ct = default)
    {
        // History changes with every play; intentionally uncached (mirrors the music history path).
        var node = await RequestAsync("browse", BrowseBody("FEhistory"), ttl: null, ct).ConfigureAwait(false);
        return YouTubeFeedParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<YouTubeFeed> GetDestinationFeedAsync(YouTubeDestination destination, CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody(destination.BrowseId()), ApiCacheTtl.Home, ct).ConfigureAwait(false);
        return YouTubeFeedParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<YouTubeVideo>> GetShortsAsync(CancellationToken ct = default)
    {
        // Shorts ride along in the home FEwhat_to_watch response — a cache hit right after Home loads.
        var node = await RequestAsync("browse", BrowseBody("FEwhat_to_watch"), ApiCacheTtl.Home, ct).ConfigureAwait(false);
        return YouTubeFeedParser.Parse(node).Shorts;
    }

    // ── Watch page (Req 32.2) ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<WatchNextData> GetWatchNextAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);
        var node = await RequestAsync("next", new JsonObject { ["videoId"] = videoId }, ApiCacheTtl.SongMetadata, ct)
            .ConfigureAwait(false);
        return WatchNextParser.Parse(node, videoId);
    }

    /// <inheritdoc />
    public async Task<YouTubeCommentsPage> GetCommentsAsync(string continuation, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(continuation);
        var node = await RequestAsync("next", ContinuationBody(continuation), ttl: null, ct).ConfigureAwait(false);
        return YouTubeCommentsParser.Parse(node);
    }

    // ── Search (Req 32.1) ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<YouTubeSearchResponse> SearchAsync(
        string query,
        YouTubeSearchFilter filter = YouTubeSearchFilter.All,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        var body = new JsonObject { ["query"] = query };
        if (filter.Params() is { } p)
        {
            body["params"] = p;
        }

        var node = await RequestAsync("search", body, ApiCacheTtl.Search, ct).ConfigureAwait(false);
        return YouTubeSearchParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<YouTubeSearchResponse> GetSearchContinuationAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var node = await RequestAsync("search", ContinuationBody(token), ApiCacheTtl.Search, ct).ConfigureAwait(false);
        return YouTubeSearchParser.ParseContinuation(node);
    }

    // ── Browse detail (Req 32.1) ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<YouTubeChannelDetail> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);

        var node = await RequestAsync("browse", BrowseBody(channelId), ApiCacheTtl.Artist, ct).ConfigureAwait(false);

        var header = ResponseTreeSearch.FindFirst(node, "c4TabbedHeaderRenderer");
        var name = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(header, "title")) ?? channelId;

        var channel = new YouTubeChannel
        {
            ChannelId = channelId,
            Name = name,
            SubscriberCountText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(header, "subscriberCountText")),
            ThumbnailUrl = YouTubeParsingHelpers.BestThumbnailUrl(header),
        };

        return new YouTubeChannelDetail
        {
            Channel = channel,
            Videos = YouTubeFeedParser.CollectVideos(node),
            IsSubscribed = IsHeaderSubscribed(node),
        };
    }

    private static bool IsHeaderSubscribed(JsonNode? node)
    {
        var button = ResponseTreeSearch.FindFirst(node, "subscribeButtonRenderer");
        return button is JsonObject obj
            && obj.TryGetPropertyValue("subscribed", out var value)
            && value is JsonValue jv
            && jv.TryGetValue<bool>(out var subscribed)
            && subscribed;
    }

    // ── Mutations (Req 32.5) ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task RateVideoAsync(string videoId, YouTubeRating rating, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);
        var body = new JsonObject { ["target"] = new JsonObject { ["videoId"] = videoId } };
        await RequestAsync(rating.Endpoint(), body, ttl: null, ct).ConfigureAwait(false);
        _cache.Invalidate(CachePrefix);
    }

    /// <inheritdoc />
    public async Task SetSubscribedAsync(bool subscribed, string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);
        var endpoint = subscribed ? "subscription/subscribe" : "subscription/unsubscribe";
        var body = new JsonObject { ["channelIds"] = new JsonArray { channelId } };
        await RequestAsync(endpoint, body, ttl: null, ct).ConfigureAwait(false);
        _cache.Invalidate(CachePrefix);
    }

    /// <inheritdoc />
    public Task AddToWatchLaterAsync(string videoId, CancellationToken ct = default) =>
        EditWatchLaterAsync(videoId, add: true, ct);

    /// <inheritdoc />
    public Task RemoveFromWatchLaterAsync(string videoId, CancellationToken ct = default) =>
        EditWatchLaterAsync(videoId, add: false, ct);

    private async Task EditWatchLaterAsync(string videoId, bool add, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var action = add
            ? new JsonObject { ["addedVideoId"] = videoId, ["action"] = "ACTION_ADD_VIDEO" }
            : new JsonObject { ["removedVideoId"] = videoId, ["action"] = "ACTION_REMOVE_VIDEO_BY_VIDEO_ID" };

        var body = new JsonObject
        {
            ["playlistId"] = "WL",
            ["actions"] = new JsonArray { action },
        };

        await RequestAsync("browse/edit_playlist", body, ttl: null, ct).ConfigureAwait(false);
        _cache.Invalidate(CachePrefix);
    }

    // ── Request core (parallel to YTMusicClient, YouTube origin + WEB context) ───────────

    /// <summary>
    /// Core request helper: POSTs <paramref name="body"/> (merged with the WEB context) to
    /// <c>youtubei/v1/{endpoint}</c>, integrating the API cache and the retry policy. HTTP 401/403
    /// map to <see cref="KasetErrorKind.AuthExpired"/>, transport failures to
    /// <see cref="KasetErrorKind.NetworkError"/>, other non-success statuses to
    /// <see cref="KasetErrorKind.ApiError"/>.
    /// </summary>
    private async Task<JsonNode> RequestAsync(string endpoint, JsonObject body, TimeSpan? ttl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentNullException.ThrowIfNull(body);

        var snapshot = await _cookieSource.GetCookiesAsync(_options.Origin, ct).ConfigureAwait(false);

        var payload = InnerTubeSupport.BuildWebContext(snapshot.OnBehalfOfUser);
        foreach (var pair in body)
        {
            payload[pair.Key] = pair.Value?.DeepClone();
        }

        var authUser = snapshot.AuthUserIndex?.ToString(CultureInfo.InvariantCulture);
        var cacheKey = _cache.ComputeKey(CachePrefix + endpoint, payload, authUser, snapshot.OnBehalfOfUser);

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

        if (ttl is { } setTtl)
        {
            _cache.Set(cacheKey, responseBody, setTtl);
        }

        return node;
    }

    private async Task<string> SendAsync(
        string endpoint,
        JsonObject payload,
        YouTubeAuthHeaders headers,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };

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
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw YTMusicErrorMapping.MapNetworkError(ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
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
    /// Pure header construction from a resolved <see cref="CookieSnapshot"/>, computing the
    /// SAPISIDHASH against the YouTube <paramref name="origin"/> (Req 32.3). When no SAPISID can be
    /// resolved the request is treated as unauthenticated (public endpoints still work).
    /// </summary>
    private YouTubeAuthHeaders BuildAuthHeaders(string origin, CookieSnapshot snapshot)
    {
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

        string? authorization = null;
        if (CookieSapisidResolver.TryResolve(snapshot.Cookies, out var sapisid))
        {
            var unixSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            authorization = InnerTubeSupport.ComputeSapisidHash(unixSeconds, sapisid, origin);
        }

        var authUser = snapshot.AuthUserIndex is { } index ? index.ToString(CultureInfo.InvariantCulture) : null;

        return new YouTubeAuthHeaders(
            origin,
            cookieHeader.Length > 0 ? cookieHeader.ToString() : null,
            authorization,
            authUser);
    }

    private static JsonObject BrowseBody(string browseId) => new() { ["browseId"] = browseId };

    private static JsonObject ContinuationBody(string token) => new() { ["continuation"] = token };

    /// <summary>
    /// Resolved per-request authorization headers (YouTube origin). The <see cref="Cookie"/> and
    /// <see cref="Authorization"/> values are secrets and must never be logged.
    /// </summary>
    private readonly record struct YouTubeAuthHeaders(string Origin, string? Cookie, string? Authorization, string? AuthUser);
}
