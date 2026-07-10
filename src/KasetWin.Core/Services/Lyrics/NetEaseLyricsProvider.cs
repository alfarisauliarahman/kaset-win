using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// <see cref="ILyricsProvider"/> backed by NetEase Cloud Music's public (unofficial) API. NetEase
/// has strong coverage for Asian / K-pop / Mandopop catalogues that LRCLib often misses, and returns
/// line-level LRC. This provider sends no credentials; it searches by title+artist, matches the best
/// candidate (by duration when available), then fetches its LRC.
/// </summary>
/// <remarks>
/// Any transport fault, miss, or unparsable payload maps to <see cref="LyricResult.Unavailable"/> —
/// the provider never throws for an ordinary "no lyrics" outcome.
/// </remarks>
public sealed class NetEaseLyricsProvider : ILyricsProvider
{
    public const string ProviderName = "NetEase";

    private static readonly Uri DefaultBaseAddress = new("https://music.163.com/");

    private readonly HttpClient _http;
    private readonly Uri _baseAddress;
    private readonly ILogger<NetEaseLyricsProvider> _logger;

    public NetEaseLyricsProvider(HttpClient httpClient, ILogger<NetEaseLyricsProvider>? logger = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseAddress = _http.BaseAddress ?? DefaultBaseAddress;
        _logger = logger ?? NullLogger<NetEaseLyricsProvider>.Instance;
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

        try
        {
            var songId = await FindBestSongIdAsync(info, ct).ConfigureAwait(false);
            if (songId is null)
            {
                return new LyricResult.Unavailable();
            }

            var lrc = await FetchLrcAsync(songId.Value, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(lrc))
            {
                return new LyricResult.Unavailable();
            }

            var synced = LrcParser.Parse(lrc);
            if (synced is { Lines.Count: > 0 })
            {
                return new LyricResult.Synced(synced with { Source = ProviderName });
            }

            // No timestamps parsed but text exists → plain fallback (strip any stray [..] tags).
            var plain = StripLrcTags(lrc);
            return string.IsNullOrWhiteSpace(plain)
                ? new LyricResult.Unavailable()
                : new LyricResult.Plain(new PlainLyrics(plain, ProviderName));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NetEase lyrics lookup failed for {VideoId}.", info.VideoId);
            return new LyricResult.Unavailable();
        }
    }

    private async Task<long?> FindBestSongIdAsync(LyricsSearchInfo info, CancellationToken ct)
    {
        var query = Uri.EscapeDataString($"{info.Title} {info.Artist}".Trim());
        var url = new Uri(_baseAddress, $"api/search/get/web?type=1&offset=0&total=true&limit=8&s={query}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = _baseAddress;
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var songs = JsonNode.Parse(body)?["result"]?["songs"] as JsonArray;
        if (songs is null || songs.Count == 0)
        {
            return null;
        }

        long? best = null;
        long bestDurationDelta = long.MaxValue;
        foreach (var song in songs)
        {
            var id = song?["id"]?.GetValue<long>();
            if (id is null)
            {
                continue;
            }

            // Prefer the candidate whose duration is closest to the known track length (when any).
            if (info.Duration is { } dur && song?["duration"]?.GetValue<long>() is { } ms)
            {
                var delta = Math.Abs(ms - (long)dur.TotalMilliseconds);
                if (delta < bestDurationDelta)
                {
                    bestDurationDelta = delta;
                    best = id;
                }
            }
            else
            {
                best ??= id; // no duration to match on: first hit wins.
            }
        }

        // If we matched on duration but the closest is still wildly off (>15s), keep the first hit.
        if (best is null || (info.Duration is not null && bestDurationDelta > 15_000))
        {
            best ??= songs[0]?["id"]?.GetValue<long>();
        }

        return best;
    }

    private async Task<string?> FetchLrcAsync(long songId, CancellationToken ct)
    {
        var url = new Uri(_baseAddress, $"api/song/lyric?id={songId}&lv=-1&kv=-1&tv=-1");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = _baseAddress;
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonNode.Parse(body)?["lrc"]?["lyric"]?.GetValue<string>();
    }

    private static string StripLrcTags(string lrc) =>
        string.Join(
            '\n',
            lrc.Split('\n')
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"\[[^\]]*\]", string.Empty).Trim())
                .Where(l => l.Length > 0));
}
