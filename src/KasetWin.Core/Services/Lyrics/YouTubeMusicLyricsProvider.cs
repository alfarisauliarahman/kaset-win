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
/// Returns <see cref="LyricResult.Synced"/> when YouTube Music serves per-line cue ranges (the
/// Android Music client path in <c>YTMusicClient.Lyrics.cs</c>) and <see cref="LyricResult.Plain"/>
/// when only untimed text is available. Because it can produce synced lyrics it is registered
/// <b>first</b> among the lyric providers: the lookup is keyed on the exact <c>videoId</c>, so it
/// cannot mismatch the way a fuzzy title/artist search against LRCLib or NetEase can, and the text
/// is the licensed label copy.
/// </para>
/// <para>
/// YouTube returns its own attribution ("Source: Musixmatch" / "Source: LyricFind"); it is kept as
/// the last line so the licensor credit YouTube requires stays visible. Any miss, transport fault,
/// or unparsable payload maps to <see cref="LyricResult.Unavailable"/> — the provider never throws
/// for "no lyrics".
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
    /// Maps the raw InnerTube payload onto a <see cref="LyricResult"/>: synced when the response
    /// carried cue ranges, plain when it carried only text, unavailable when it carried neither.
    /// YouTube's attribution is kept as the trailing line in both shapes.
    /// </summary>
    internal static LyricResult Map(YouTubeMusicLyrics? lyrics)
    {
        if (lyrics is null || lyrics.IsEmpty)
        {
            return new LyricResult.Unavailable();
        }

        if (lyrics.TimedLines is { Count: > 0 } timed)
        {
            var lines = new List<SyncedLyricLine>(timed.Count + 1);
            lines.AddRange(timed);

            // The credit is timed just past the final lyric, where YouTube itself shows it: it can
            // never steal the highlight from a line that is still being sung.
            if (!string.IsNullOrWhiteSpace(lyrics.Attribution))
            {
                var last = timed[^1];
                lines.Add(new SyncedLyricLine
                {
                    TimeInMs = last.TimeInMs + Math.Max(last.Duration, 1),
                    Text = lyrics.Attribution,
                });
            }

            return new LyricResult.Synced(new SyncedLyrics(lines, ProviderName));
        }

        var text = string.IsNullOrWhiteSpace(lyrics.Attribution)
            ? lyrics.Text!
            : lyrics.Text + "\n\n" + lyrics.Attribution;

        return new LyricResult.Plain(new PlainLyrics(text, ProviderName));
    }
}
