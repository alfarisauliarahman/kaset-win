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
        var timed = await TryBrowseLyricsAsync(browseId, android: true, ct).ConfigureAwait(true);

        // 3) The desktop browse. Always issued, for two different reasons:
        //    - when the Android attempt produced nothing usable it IS the result (plain beats none);
        //    - when it produced timings it is still needed for its FOOTER, because the timed payload
        //      carries no attribution at all (verified live 2026-07-22 across 13 tracks: every plain
        //      shape has "Source: Musixmatch"/"Source: LyricFind", no 7.21.50 response has any).
        //      YouTube requires that licensor credit be displayed, so dropping it to save a request
        //      was not an option. The browse is cached (ApiCacheTtl.Lyrics) and the provider memoizes
        //      per videoId, so this costs one extra request per track per session at most.
        var plain = await TryBrowseLyricsAsync(browseId, android: false, ct).ConfigureAwait(false);

        if (timed is { HasTimings: true })
        {
            var credited = string.IsNullOrWhiteSpace(timed.Attribution) && !string.IsNullOrWhiteSpace(plain?.Attribution)
                ? timed with { Attribution = plain!.Attribution }
                : timed;

            Diag.Write($"ytm-lyrics videoId={videoId} timed lines={credited.TimedLines!.Count} credit={credited.Attribution ?? "<none>"}");
            s_androidTimedMisses = 0;
            return credited;
        }

        NoteAndroidTimedMiss(plain is not null);

        // Reached when the Android attempt errored (stale pinned version), when it silently
        // downgraded to plain, or when the track simply has no synced lyrics.
        var result = plain ?? timed;
        Diag.Write($"ytm-lyrics videoId={videoId} plain chars={result?.Text?.Length ?? 0}");
        return result;
    }

    /// <summary>
    /// Consecutive tracks that had lyrics but no timings from the Android client. Reset by any
    /// timed hit.
    /// </summary>
    private static int s_androidTimedMisses;

    /// <summary>Whether the "pin looks retired" warning has already been written this session.</summary>
    private static bool s_androidPinWarned;

    /// <summary>
    /// How many consecutive misses before the pinned client version is presumed retired. Some tracks
    /// genuinely have no synced version (verified: a Musixmatch track returned 34 lines, zero cue
    /// ranges), so a handful of misses is normal and must not cry wolf. A long unbroken run is not.
    /// </summary>
    private const int AndroidPinSuspectThreshold = 12;

    /// <summary>
    /// Records a track that came back without timings, and warns once when the run gets long enough
    /// to look like a retired client version rather than ordinary unsynced tracks.
    /// </summary>
    /// <remarks>
    /// This exists because the failure is otherwise <b>silent</b>: when YouTube retires the pinned
    /// version the app keeps working and simply stops highlighting lyrics, which nobody reports as a
    /// bug for months. Only counts tracks that DID have lyrics, so a library of instrumentals cannot
    /// trigger it.
    /// </remarks>
    private static void NoteAndroidTimedMiss(bool trackHadLyrics)
    {
        if (!trackHadLyrics || s_androidPinWarned)
        {
            return;
        }

        if (++s_androidTimedMisses < AndroidPinSuspectThreshold)
        {
            return;
        }

        s_androidPinWarned = true;
        Diag.Write(
            $"ytm-lyrics WARNING: {AndroidPinSuspectThreshold} consecutive tracks had lyrics but no " +
            $"timings from ANDROID_MUSIC {InnerTubeSupport.EffectiveAndroidMusicClientVersion}. The " +
            "pinned client version has probably been retired — synced lyrics have degraded to plain " +
            "text. Find a working version with: dotnet run --project src/KasetWin.ApiExplorer -- " +
            "lyrics t82Q3f4pNUY   (then set it via InnerTubeSupport.AndroidMusicClientVersionOverride).");
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
                clientVersionOverride: android ? InnerTubeSupport.EffectiveAndroidMusicClientVersion : null,
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
