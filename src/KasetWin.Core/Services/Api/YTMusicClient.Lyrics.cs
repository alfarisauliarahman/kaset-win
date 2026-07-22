using System.Text.Json.Nodes;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// YouTube Music's own lyrics surface: <c>next</c> → the "Lyrics" tab's <c>browseId</c> →
/// <c>browse</c> → the description shelf. The payload is plain text (no line timings), so it is
/// consumed as a fallback behind the synced providers.
/// </summary>
public sealed partial class YTMusicClient
{
    /// <inheritdoc />
    public async Task<YouTubeMusicLyrics?> GetYouTubeMusicLyricsAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        // 1) Watch-next carries the tabs; the Lyrics tab exposes an MPLYt… browseId.
        // NOTE: ConfigureAwait(true) — the SECOND request below must resume on the caller's thread
        // because the WebView2 cookie source is thread-affine (same reason as GetSongRelatedAsync).
        var next = await RequestAsync(
            "next",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["enablePersistentPlaylistPanel"] = true,
                ["isAudioOnly"] = true,
                ["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
            },
            ApiCacheTtl.SongMetadata,
            ct).ConfigureAwait(true);

        var browseId = YouTubeMusicLyricsParser.FindLyricsBrowseId(next);
        Diag.Write($"ytm-lyrics videoId={videoId} browseId={browseId ?? "<null>"}");
        if (string.IsNullOrEmpty(browseId))
        {
            return null;
        }

        // 2) Browse the lyrics surface itself. Lyrics rarely change → the long TTL.
        var browse = await RequestAsync(
            "browse",
            new JsonObject { ["browseId"] = browseId },
            ApiCacheTtl.Lyrics,
            ct).ConfigureAwait(false);

        var lyrics = YouTubeMusicLyricsParser.ParseLyrics(browse);
        Diag.Write($"ytm-lyrics videoId={videoId} chars={lyrics?.Text.Length ?? 0}");
        return lyrics;
    }
}
