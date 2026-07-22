using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// YouTube Music's own lyrics surface: <c>next</c> → the "Lyrics" tab's <c>browseId</c> →
/// <c>browse</c>.
/// <para>
/// The <b>fidelity depends on which InnerTube client asks for the browse</b>. The desktop client
/// this app otherwise uses (<c>WEB_REMIX</c>) is only ever served plain, untimed text. The Android
/// Music client is served the same lyrics with per-line cue ranges — the mechanism <c>ytmusicapi</c>
/// uses for timestamped lyrics. So this one call is made under a second, spoofed client identity.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the fallback is mandatory (ADR-worthy; verified live 2026-07-22).</b> A pinned foreign
/// client version goes stale, and it does not fail in one predictable way:
/// <list type="bullet">
///   <item><c>7.21.50</c> (current) → <c>timedLyricsModel</c>, timed lines.</item>
///   <item><c>6.33.52</c> (older) → HTTP 200 carrying the <i>plain</i> description shelf. A silent
///   downgrade, not an error — nothing throws, the lyrics are simply untimed.</item>
///   <item><c>5.01</c> → HTTP 400 <c>FAILED_PRECONDITION</c>.</item>
///   <item>a fabricated <c>9.99.99</c> → HTTP 404 <c>NOT_FOUND</c>.</item>
/// </list>
/// Both error shapes surface as <see cref="KasetError"/>. The rule this method enforces is that
/// <b>every</b> one of those outcomes degrades to plain text via a second, ordinary
/// <c>WEB_REMIX</c> browse — never to "no lyrics". The Android attempt can only ever add timings;
/// it can never remove lyrics the desktop client would have returned.
/// </para>
/// <para>
/// The spoofing is confined to this one browse: origin, cookies and <c>SAPISIDHASH</c> handling are
/// untouched, and no other endpoint ever sees the Android identity.
/// </para>
/// </remarks>
public sealed partial class YTMusicClient
{
    /// <inheritdoc />
    public async Task<YouTubeMusicLyrics?> GetYouTubeMusicLyricsAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        // 1) Watch-next carries the tabs; the Lyrics tab exposes an MPLYt… browseId. The desktop
        // client's id is accepted by the Android browse (verified live), so this stays an ordinary
        // WEB_REMIX request and shares its cache entry with the rest of the app.
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

        // 2) Preferred: the Android Music client, which is served per-line cue ranges.
        var timed = await TryBrowseLyricsAsync(browseId, android: true, ct).ConfigureAwait(false);
        if (timed is { HasTimings: true })
        {
            Diag.Write($"ytm-lyrics videoId={videoId} timed lines={timed.TimedLines!.Count}");
            return timed;
        }

        // 3) Fallback: the ordinary desktop client. Reached when the Android attempt errored (stale
        // pinned version), when it silently downgraded to plain, or when the track simply has no
        // synced lyrics. Whatever the reason, plain lyrics beat none.
        var plain = await TryBrowseLyricsAsync(browseId, android: false, ct).ConfigureAwait(false);
        var result = plain ?? timed;
        Diag.Write($"ytm-lyrics videoId={videoId} plain chars={result?.Text?.Length ?? 0}");
        return result;
    }

    /// <summary>
    /// Browses the lyrics id under one client identity, returning <c>null</c> instead of throwing —
    /// a failed attempt must always be able to fall through to the other client.
    /// </summary>
    private async Task<YouTubeMusicLyrics?> TryBrowseLyricsAsync(string browseId, bool android, CancellationToken ct)
    {
        try
        {
            var node = await RequestAsync(
                "browse",
                new JsonObject { ["browseId"] = browseId },
                ApiCacheTtl.Lyrics, // Lyrics rarely change. The payload differs per client and cache
                ct,                 // keys include the payload, so the two clients never collide.
                clientVersionOverride: android ? InnerTubeSupport.ClientVersionAndroidMusic : null,
                clientNameOverride: android ? InnerTubeSupport.ClientNameAndroidMusic : null,
                clientExtras: android ? InnerTubeSupport.AndroidMusicClientExtras : null)
                .ConfigureAwait(false);

            return YouTubeMusicLyricsParser.Parse(node);
        }
        catch (KasetError ex)
        {
            // A stale/rejected client version lands here (HTTP 400/404). Never fatal: the caller
            // retries under the desktop client.
            Diag.Write($"ytm-lyrics browse {(android ? "android" : "web")} failed: {ex.Kind}");
            return null;
        }
    }
}
