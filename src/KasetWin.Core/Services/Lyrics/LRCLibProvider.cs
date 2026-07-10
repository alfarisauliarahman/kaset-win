using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// <see cref="ILyricsProvider"/> backed by the public <a href="https://lrclib.net">LRCLib</a> API
/// (Req 17.1). LRCLib is a free, keyless, public service; this provider sends <b>no</b>
/// credentials, cookies, or secrets.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="HttpClient"/> is injected so the provider is testable via a stubbed
/// <see cref="HttpMessageHandler"/>. It first tries the precise <c>/api/get</c> endpoint
/// (title + artist, plus album/duration when available); on a miss it falls back to
/// <c>/api/search</c> and takes the first usable hit.
/// </para>
/// <para>
/// A <c>404</c>, an empty body, or a result that carries neither synced nor plain lyrics maps to
/// <see cref="LyricResult.Unavailable"/>. Synced LRC payloads are parsed with
/// <see cref="LrcParser.Parse(string)"/>; when no timed lines result the provider falls back to
/// the plain text, if any.
/// </para>
/// </remarks>
public sealed class LRCLibProvider : ILyricsProvider
{
    /// <summary>Provider label surfaced on results and via <see cref="Name"/>.</summary>
    public const string ProviderName = "LRCLib";

    /// <summary>Default public LRCLib API base address (no auth required).</summary>
    private static readonly Uri DefaultBaseAddress = new("https://lrclib.net/");

    // Non-secret identifier LRCLib asks clients to send so it can attribute traffic.
    private const string UserAgent = "KasetWin (https://github.com/sertacozercan/kaset)";

    private readonly HttpClient _http;
    private readonly Uri _baseAddress;
    private readonly ILogger<LRCLibProvider> _logger;

    /// <summary>
    /// Creates a provider over an injected <see cref="HttpClient"/> (DI / <c>IHttpClientFactory</c>
    /// friendly). When the client has no <see cref="HttpClient.BaseAddress"/> the public LRCLib
    /// endpoint is used.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for requests (injected for testability).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public LRCLibProvider(HttpClient httpClient, ILogger<LRCLibProvider>? logger = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseAddress = _http.BaseAddress ?? DefaultBaseAddress;
        _logger = logger ?? NullLogger<LRCLibProvider>.Instance;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<LyricResult> SearchAsync(LyricsSearchInfo info, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        // Podcast episodes have no song lyrics here — skip the network round-trips entirely
        // (the YouTube CC provider serves them).
        if (info.IsPodcast)
        {
            return new LyricResult.Unavailable();
        }

        // 1) Precise lookup: /api/get keyed on title + artist (+ album/duration when known).
        var exact = await TryGetAsync(info, ct).ConfigureAwait(false);
        if (exact is not null)
        {
            var mapped = Map(exact);
            if (mapped is not LyricResult.Unavailable)
            {
                return mapped;
            }
        }

        // 2) Fuzzy fallback: /api/search, take the first hit that actually carries lyrics.
        var searchHit = await TrySearchAsync(info, ct).ConfigureAwait(false);
        return searchHit is null ? new LyricResult.Unavailable() : Map(searchHit);
    }

    private async Task<LrcLibRecord?> TryGetAsync(LyricsSearchInfo info, CancellationToken ct)
    {
        var query = new List<string>
        {
            $"track_name={Uri.EscapeDataString(info.Title)}",
            $"artist_name={Uri.EscapeDataString(info.Artist)}",
        };

        if (!string.IsNullOrWhiteSpace(info.Album))
        {
            query.Add($"album_name={Uri.EscapeDataString(info.Album)}");
        }

        if (info.Duration is { } duration && duration > TimeSpan.Zero)
        {
            var seconds = (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero);
            query.Add($"duration={seconds.ToString(CultureInfo.InvariantCulture)}");
        }

        var uri = new Uri(_baseAddress, "api/get?" + string.Join('&', query));
        return await SendAsync(uri, ct).ConfigureAwait(false);
    }

    private async Task<LrcLibRecord?> TrySearchAsync(LyricsSearchInfo info, CancellationToken ct)
    {
        var uri = new Uri(
            _baseAddress,
            "api/search?track_name=" + Uri.EscapeDataString(info.Title) +
            "&artist_name=" + Uri.EscapeDataString(info.Artist));

        using var request = CreateRequest(uri);
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var records = await response.Content
                .ReadFromJsonAsync<LrcLibRecord[]>(KasetJson.Options, ct)
                .ConfigureAwait(false);

            if (records is null)
            {
                return null;
            }

            // Prefer the first hit with synced lyrics, then the first with any lyrics.
            return Array.Find(records, static r => !string.IsNullOrEmpty(r.SyncedLyrics))
                ?? Array.Find(records, static r => !r.Instrumental && !string.IsNullOrEmpty(r.PlainLyrics));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "LRCLib search request failed for {Title}.", info.Title);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LRCLib search returned an unexpected payload for {Title}.", info.Title);
            return null;
        }
    }

    private async Task<LrcLibRecord?> SendAsync(Uri uri, CancellationToken ct)
    {
        using var request = CreateRequest(uri);
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content
                .ReadFromJsonAsync<LrcLibRecord>(KasetJson.Options, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "LRCLib get request failed.");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LRCLib get returned an unexpected payload.");
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // Per-request header avoids mutating a shared HttpClient's default headers.
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }

    /// <summary>
    /// Maps an LRCLib record onto a <see cref="LyricResult"/>: synced first, then plain, else
    /// unavailable. Instrumental tracks and empty payloads are treated as unavailable.
    /// </summary>
    private static LyricResult Map(LrcLibRecord record)
    {
        if (record.Instrumental)
        {
            return new LyricResult.Unavailable();
        }

        if (!string.IsNullOrEmpty(record.SyncedLyrics))
        {
            var synced = LrcParser.Parse(record.SyncedLyrics);
            if (!synced.IsEmpty)
            {
                // Re-label the source as this provider while keeping the parsed lines.
                return new LyricResult.Synced(synced with { Source = ProviderName });
            }
        }

        if (!string.IsNullOrEmpty(record.PlainLyrics))
        {
            return new LyricResult.Plain(new PlainLyrics(record.PlainLyrics, ProviderName));
        }

        return new LyricResult.Unavailable();
    }

    /// <summary>
    /// Minimal projection of an LRCLib response object. Only the fields Kaset consumes are mapped;
    /// LRCLib returns no secrets. Property names are matched case-insensitively via
    /// <see cref="KasetJson.Options"/>.
    /// </summary>
    private sealed record LrcLibRecord
    {
        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; init; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; init; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; init; }
    }
}
