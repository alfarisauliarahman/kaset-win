using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Pure, dependency-free helpers for building authenticated InnerTube (YouTube Music)
/// requests. The surface is intentionally <c>static</c> and deterministic so it can be
/// exercised headless and via property-based tests (see tasks 2.3/2.4).
/// </summary>
/// <remarks>
/// <para>
/// The SHA1 usage here is dictated by the YouTube <c>SAPISIDHASH</c> wire protocol and is
/// used purely for protocol compatibility — it is NOT a security primitive.
/// </para>
/// <para>
/// This type lives in <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.
/// </para>
/// </remarks>
public static class InnerTubeSupport
{
    /// <summary>Origin required for YouTube Music requests (Req 3.2/3.4).</summary>
    public const string MusicOrigin = "https://music.youtube.com";

    /// <summary>InnerTube <c>clientName</c> used for the music origin (Req 3.2).</summary>
    public const string ClientNameMusic = "WEB_REMIX";

    /// <summary>InnerTube <c>clientVersion</c> paired with <see cref="ClientNameMusic"/>.</summary>
    // REVERTED to the known-good version: the guessed "1.20250630.01.00" bump made BOTH the
    // universal and the podcasts-filtered searches return query-unrelated results (dump-verified —
    // the target show vanished entirely). With this version the filtered searches carried the
    // target. Do not bump blindly; verify against a dump.
    public const string ClientVersionMusic = "1.20231204.01.00";

    /// <summary>
    /// Origin required for regular YouTube (video) requests (Req 32). Distinct from
    /// <see cref="MusicOrigin"/>: a music-origin SAPISIDHASH silently 401s on www.youtube.com,
    /// so the YouTube client computes its hash and Origin/Referer/X-Origin headers from this value.
    /// </summary>
    public const string YouTubeOrigin = "https://www.youtube.com";

    /// <summary>
    /// InnerTube <c>clientName</c> for regular YouTube (Req 32). <c>WEB</c> (not
    /// <see cref="ClientNameMusic"/> <c>WEB_REMIX</c>).
    /// </summary>
    public const string ClientNameWeb = "WEB";

    /// <summary>
    /// InnerTube <c>clientVersion</c> paired with <see cref="ClientNameWeb"/>. InnerTube accepts
    /// moderately stale versions; this mirrors the macOS reference value.
    /// </summary>
    public const string ClientVersionWeb = "2.20240620.05.00";

    /// <summary>
    /// InnerTube <c>clientName</c> of the Android YouTube Music app. Used for exactly one surface:
    /// the lyrics browse (see <c>YTMusicClient.Lyrics.cs</c>). The desktop <see cref="ClientNameMusic"/>
    /// client only ever receives plain, untimed lyrics; the Android Music client receives the same
    /// lyrics with per-line cue ranges.
    /// </summary>
    public const string ClientNameAndroidMusic = "ANDROID_MUSIC";

    /// <summary>
    /// InnerTube <c>clientVersion</c> paired with <see cref="ClientNameAndroidMusic"/>.
    /// <para>
    /// ⚠️ <b>This value goes stale.</b> Verified live on 2026-07-22 against
    /// <c>browse MPLYt…</c>: <c>7.21.50</c> → timed lyrics; <c>6.33.52</c> → the plain description
    /// shelf (a silent downgrade, not an error); <c>5.01</c> → HTTP 400; a fabricated <c>9.99.99</c>
    /// → HTTP 404. Every one of those outcomes must degrade to plain text, never to "no lyrics" —
    /// that is why the lyrics call always has a WEB_REMIX fallback behind it. Do not bump this
    /// blindly; re-verify with a real request first (same rule as <see cref="ClientVersionMusic"/>).
    /// </para>
    /// </summary>
    public const string ClientVersionAndroidMusic = "7.21.50";

    /// <summary>
    /// The <c>context.client</c> fields that turn a music request into an Android Music request.
    /// Merged over the standard music context by the request core, so cookies, origin, and
    /// <c>SAPISIDHASH</c> handling stay exactly as they are for every other endpoint.
    /// </summary>
    /// <remarks>
    /// These are part of the client identity, not decoration: an <c>ANDROID_MUSIC</c> request that
    /// omits them is served the web-shaped (plain) response.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> AndroidMusicClientExtras { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["androidSdkVersion"] = "34",
            ["osName"] = "Android",
            ["osVersion"] = "14",
            ["platform"] = "MOBILE",
        };

    /// <summary>
    /// Computes the <c>SAPISIDHASH</c> authorization value for a request.
    /// </summary>
    /// <param name="unixSeconds">The request timestamp in Unix seconds.</param>
    /// <param name="sapisid">The SAPISID cookie value (never logged; treated as a secret).</param>
    /// <param name="origin">The request origin (e.g. <see cref="MusicOrigin"/>).</param>
    /// <returns>
    /// A string of the form <c>"SAPISIDHASH {unixSeconds}_{hash}"</c>, where <c>hash</c> is the
    /// lowercase 40-character hexadecimal SHA1 digest of <c>"{unixSeconds} {sapisid} {origin}"</c>.
    /// The result is deterministic for identical inputs (Req 3.1).
    /// </returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "SHA1 is mandated by the YouTube SAPISIDHASH wire protocol; used for " +
            "protocol compatibility only, not for security.")]
    public static string ComputeSapisidHash(long unixSeconds, string sapisid, string origin)
    {
        ArgumentNullException.ThrowIfNull(sapisid);
        ArgumentNullException.ThrowIfNull(origin);

        var timestamp = unixSeconds.ToString(CultureInfo.InvariantCulture);
        var payload = $"{timestamp} {sapisid} {origin}";

        // SHA1 is mandated by the YouTube SAPISIDHASH protocol; not used for security.
        var digest = SHA1.HashData(Encoding.UTF8.GetBytes(payload));
        var hash = Convert.ToHexString(digest).ToLowerInvariant();

        return $"SAPISIDHASH {timestamp}_{hash}";
    }

    /// <summary>
    /// Optional content-language pin (<c>hl</c>, e.g. <c>"id"</c>/<c>"en"</c>) applied to every
    /// music request. <c>null</c> (default) sends no <c>hl</c> so the account's language wins.
    /// Set at startup from the app's language setting; a change takes effect on the next request
    /// (cache keys include the payload, so stale-language entries are missed, never served).
    /// </summary>
    public static string? LanguageOverride { get; set; }

    /// <summary>
    /// Builds the InnerTube request <c>context</c> object for music requests, pinned to the
    /// <see cref="ClientNameMusic"/>/<see cref="ClientVersionMusic"/> client (Req 3.2/3.4).
    /// </summary>
    /// <param name="onBehalfOfUser">
    /// Optional brand-account identity. When non-null, it is emitted as
    /// <c>context.user.onBehalfOfUser</c>.
    /// </param>
    /// <returns>A fresh <see cref="JsonObject"/> describing the request context.</returns>
    public static JsonObject BuildContext(string? onBehalfOfUser = null)
    {
        var client = new JsonObject
        {
            ["clientName"] = ClientNameMusic,
            ["clientVersion"] = ClientVersionMusic,
        };

        // Optional UI-language pin: with hl set the server localises section titles, dates and
        // descriptions to the chosen language; without it the account's language wins.
        if (!string.IsNullOrEmpty(LanguageOverride))
        {
            client["hl"] = LanguageOverride;
        }

        var context = new JsonObject
        {
            ["client"] = client,
        };

        if (onBehalfOfUser is not null)
        {
            context["user"] = new JsonObject
            {
                ["onBehalfOfUser"] = onBehalfOfUser,
            };
        }

        return new JsonObject
        {
            ["context"] = context,
        };
    }

    /// <summary>
    /// Builds the InnerTube request <c>context</c> object for regular YouTube (video) requests,
    /// pinned to the <see cref="ClientNameWeb"/>/<see cref="ClientVersionWeb"/> client (Req 32).
    /// Parallel to <see cref="BuildContext"/> by design — the music path stays untouched.
    /// </summary>
    /// <param name="onBehalfOfUser">
    /// Optional brand-account identity emitted as <c>context.user.onBehalfOfUser</c>.
    /// </param>
    /// <returns>A fresh <see cref="JsonObject"/> describing the WEB request context.</returns>
    public static JsonObject BuildWebContext(string? onBehalfOfUser = null)
    {
        var user = new JsonObject
        {
            ["lockedSafetyMode"] = false,
        };

        if (onBehalfOfUser is not null)
        {
            user["onBehalfOfUser"] = onBehalfOfUser;
        }

        var context = new JsonObject
        {
            ["client"] = new JsonObject
            {
                ["clientName"] = ClientNameWeb,
                ["clientVersion"] = ClientVersionWeb,
                ["hl"] = "en",
                ["gl"] = "US",
                ["platform"] = "DESKTOP",
            },
            ["user"] = user,
        };

        return new JsonObject
        {
            ["context"] = context,
        };
    }
}
