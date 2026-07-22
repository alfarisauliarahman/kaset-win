using System.Collections.Concurrent;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Api.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// <see cref="ILyricsProvider"/> backed by YouTube Music's own lyrics surface (InnerTube
/// <c>next</c> → the "Lyrics" tab's <c>browseId</c> → <c>browse</c>). Coverage is excellent for the
/// exact track being played because the lookup is keyed on the <c>videoId</c> rather than on a
/// fuzzy title/artist search — which is why it fills the gaps LRCLib and NetEase leave.
/// </summary>
/// <remarks>
/// <para>
/// These lyrics are <b>plain text</b>: YouTube Music exposes no line timings here. The provider
/// therefore always returns <see cref="LyricResult.Plain"/>, never <see cref="LyricResult.Synced"/>,
/// and must be registered <b>after</b> the synced providers so the
/// <c>synced → plain</c> priority in <see cref="LyricsService"/> keeps karaoke-style lyrics winning.
/// </para>
/// <para>
/// YouTube returns its own attribution footer ("Source: …"); it is appended to the text so the
/// licensor credit YouTube requires stays visible. Any miss, transport fault, or unparsable payload
/// maps to <see cref="LyricResult.Unavailable"/> — the provider never throws for "no lyrics".
/// </para>
/// </remarks>
public sealed class YouTubeMusicLyricsProvider : ILyricsProvider
{
    /// <summary>Provider label surfaced on results and via <see cref="Name"/>.</summary>
    public const string ProviderName = "YouTube Music";

    private readonly IYTMusicClient _client;
    private readonly ILogger<YouTubeMusicLyricsProvider> _logger;

    // Per-video memo: the panel reopening (or a refresh) must not re-issue two InnerTube requests.
    // A miss is memoized as null too — a track without a lyrics tab will not grow one mid-session.
    private readonly ConcurrentDictionary<string, LyricResult> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates the provider over the shared authenticated InnerTube client.</summary>
    public YouTubeMusicLyricsProvider(IYTMusicClient client, ILogger<YouTubeMusicLyricsProvider>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? NullLogger<YouTubeMusicLyricsProvider>.Instance;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<LyricResult> SearchAsync(LyricsSearchInfo info, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        // Podcast episodes have no lyrics tab — the YouTube CC provider serves them.
        if (info.IsPodcast || string.IsNullOrEmpty(info.VideoId))
        {
            return new LyricResult.Unavailable();
        }

        if (_cache.TryGetValue(info.VideoId, out var cached))
        {
            return cached;
        }

        LyricResult result;
        try
        {
            var lyrics = await _client.GetYouTubeMusicLyricsAsync(info.VideoId, ct).ConfigureAwait(false);
            result = Map(lyrics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube Music lyrics lookup failed for {VideoId}.", info.VideoId);
            return new LyricResult.Unavailable();
        }

        _cache[info.VideoId] = result;
        return result;
    }

    /// <summary>
    /// Maps the raw InnerTube payload onto a <see cref="LyricResult"/>: plain text plus YouTube's
    /// own attribution footer, or unavailable when there is no usable text.
    /// </summary>
    internal static LyricResult Map(YouTubeMusicLyrics? lyrics)
    {
        if (lyrics is null || string.IsNullOrWhiteSpace(lyrics.Text))
        {
            return new LyricResult.Unavailable();
        }

        var text = string.IsNullOrWhiteSpace(lyrics.Attribution)
            ? lyrics.Text
            : lyrics.Text + "\n\n" + lyrics.Attribution;

        return new LyricResult.Plain(new PlainLyrics(text, ProviderName));
    }
}
