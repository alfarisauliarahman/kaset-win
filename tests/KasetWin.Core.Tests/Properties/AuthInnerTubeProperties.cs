using System.Net;
using System.Security.Cryptography;
using System.Text;
using CsCheck;
using KasetWin.Core.Errors;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Auth;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests covering the authentication / InnerTube pure logic for the
/// kaset-winui3 feature: SAPISIDHASH (Property 1), music request origin/context
/// consistency (Property 2), SAPISID cookie resolution (Property 3), HTTP error
/// mapping (Property 4), and the auth state machine (Property 5).
///
/// Each property is a single [Fact] running a minimum of 100 CsCheck iterations.
///
/// SECURITY: all "secret-like" values are randomly generated placeholders — never real
/// cookies/tokens/SAPISID values.
/// </summary>
public class AuthInnerTubeProperties
{
    private static readonly Gen<string> Origins = Gen.OneOfConst(
        InnerTubeSupport.MusicOrigin,
        "https://www.youtube.com",
        "https://accounts.google.com");

    /// <summary>Noise cookies whose names can never collide with the SAPISID cookie names.</summary>
    private static readonly Gen<CookiePair[]> NoiseCookies =
        Gen.Select(PbtGenerators.ShortToken, PbtGenerators.ShortToken)
            .Select((name, value) => new CookiePair("zz-" + name, value))
            .Array[0, 4];

    private static readonly Gen<AuthEvent> AuthEvents = Gen.OneOfConst(
        AuthEvent.LoginStarted,
        AuthEvent.CookiesPresent,
        AuthEvent.CookiesAbsent,
        AuthEvent.AuthExpired);

    private static readonly Gen<AuthState> AuthStates = Gen.OneOfConst(
        AuthState.LoggedOut,
        AuthState.LoggingIn,
        AuthState.LoggedIn);

    // Feature: kaset-winui3, Property 1: SAPISIDHASH deterministik dan well-formed
    // Validates: Requirements 3.1
    [Fact]
    public void Property1_SapisidHash_is_deterministic_and_wellformed()
    {
        // For any timestamp, SAPISID value, and origin: ComputeSapisidHash returns
        // "SAPISIDHASH {ts}_{sha1hex}" where sha1hex is the lowercase 40-char SHA1 of
        // "{ts} {sapisid} {origin}", and repeated calls with identical inputs are identical.
        Gen.Select(PbtGenerators.UnixSeconds, PbtGenerators.Token, Origins)
            .Sample(
                t =>
                {
                    var (ts, sapisid, origin) = t;

                    var result = InnerTubeSupport.ComputeSapisidHash(ts, sapisid, origin);

                    // Independent reference computation of the expected hash.
                    // SHA1 mirrors the SAPISIDHASH wire protocol (not used for security).
#pragma warning disable CA5350
                    var expectedHash = Convert.ToHexString(
                            SHA1.HashData(Encoding.UTF8.GetBytes($"{ts} {sapisid} {origin}")))
                        .ToLowerInvariant();
#pragma warning restore CA5350

                    Assert.Equal($"SAPISIDHASH {ts}_{expectedHash}", result);

                    // Well-formed: "SAPISIDHASH " prefix + "{ts}_" + 40 lowercase hex chars.
                    Assert.StartsWith("SAPISIDHASH ", result, System.StringComparison.Ordinal);
                    var hashPart = result["SAPISIDHASH ".Length..].Split('_', 2)[1];
                    Assert.Equal(40, hashPart.Length);
                    Assert.All(hashPart, c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));

                    // Deterministic: a second call yields an identical result.
                    Assert.Equal(result, InnerTubeSupport.ComputeSapisidHash(ts, sapisid, origin));
                },
                iter: 100);
    }

    // Feature: kaset-winui3, Property 2: Header dan konteks request musik konsisten origin
    // Validates: Requirements 3.2, 3.4
    [Fact]
    public void Property2_Music_request_context_and_origin_are_consistent()
    {
        // For any music request context built (with or without a brand account):
        //   - context.client.clientName == "WEB_REMIX";
        //   - the music request origin is exactly MusicOrigin, the same origin the
        //     SAPISIDHASH is computed against.
        var onBehalfOfUser = Gen.OneOf(Gen.Const((string?)null), PbtGenerators.ShortToken.Select(s => (string?)s));

        Gen.Select(onBehalfOfUser, PbtGenerators.UnixSeconds, PbtGenerators.Token)
            .Sample(
                t =>
                {
                    var (user, ts, sapisid) = t;

                    var payload = InnerTubeSupport.BuildContext(user);
                    var clientName = (string?)payload["context"]!["client"]!["clientName"];

                    Assert.Equal("WEB_REMIX", clientName);
                    Assert.Equal(InnerTubeSupport.ClientNameMusic, clientName);

                    // The origin pinned for music requests is the constant music origin, and it
                    // is the very origin the SAPISIDHASH is computed against for the request.
                    var origin = new YTMusicClientOptions().Origin;
                    Assert.Equal(InnerTubeSupport.MusicOrigin, origin);
                    Assert.Equal("https://music.youtube.com", origin);

                    var requestAuth = InnerTubeSupport.ComputeSapisidHash(ts, sapisid, origin);
                    var musicAuth = InnerTubeSupport.ComputeSapisidHash(ts, sapisid, InnerTubeSupport.MusicOrigin);
                    Assert.Equal(musicAuth, requestAuth);
                },
                iter: 100);
    }

    // Feature: kaset-winui3, Property 3: Resolusi SAPISID dari koleksi cookie
    // Validates: Requirements 3.3
    [Fact]
    public void Property3_Sapisid_resolution_prefers_primary_then_fallback_else_null()
    {
        // For any cookie collection: Resolve returns __Secure-3PAPISID when present and
        // non-empty; otherwise SAPISID when present and non-empty; otherwise null.
        // Empty values are treated as absent.
        var scenario =
            from primaryVal in Gen.OneOf(Gen.Const(string.Empty), PbtGenerators.Token)
            from fallbackVal in Gen.OneOf(Gen.Const(string.Empty), PbtGenerators.Token)
            from includePrimary in Gen.Bool
            from includeFallback in Gen.Bool
            from noise in NoiseCookies
            select (primaryVal, fallbackVal, includePrimary, includeFallback, noise);

        scenario.Sample(
            s =>
            {
                var (primaryVal, fallbackVal, includePrimary, includeFallback, noise) = s;

                var cookies = new List<CookiePair>(noise);
                if (includePrimary)
                {
                    cookies.Add(new CookiePair(CookieSapisidResolver.PrimaryCookieName, primaryVal));
                }

                if (includeFallback)
                {
                    cookies.Add(new CookiePair(CookieSapisidResolver.FallbackCookieName, fallbackVal));
                }

                string? expected =
                    includePrimary && primaryVal.Length > 0 ? primaryVal
                    : includeFallback && fallbackVal.Length > 0 ? fallbackVal
                    : null;

                Assert.Equal(expected, CookieSapisidResolver.Resolve(cookies));

                // TryResolve mirrors Resolve: success iff a usable value was found.
                var ok = CookieSapisidResolver.TryResolve(cookies, out var resolved);
                Assert.Equal(expected is not null, ok);
                Assert.Equal(expected ?? string.Empty, resolved);
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 4: Pemetaan status HTTP ke KasetError
    // Validates: Requirements 3.6, 20.2
    [Fact]
    public void Property4_HttpError_maps_to_AuthExpired_iff_401_or_403()
    {
        // For any HTTP status code in [100, 599]: MapHttpError produces AuthExpired if and
        // only if status is 401 or 403; every other status maps to ApiError carrying the code.
        Gen.Int[100, 599].Sample(
            status =>
            {
                var error = YTMusicErrorMapping.MapHttpError((HttpStatusCode)status);

                var isAuthStatus = status is 401 or 403;
                if (isAuthStatus)
                {
                    Assert.Equal(KasetErrorKind.AuthExpired, error.Kind);
                }
                else
                {
                    Assert.Equal(KasetErrorKind.ApiError, error.Kind);
                }

                // AuthExpired IFF status in {401, 403}.
                Assert.Equal(isAuthStatus, error.Kind == KasetErrorKind.AuthExpired);

                // The status code is always carried through on the mapped error.
                Assert.Equal(status, error.ApiStatusCode);
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 5: Auth state machine selalu valid dan mengikuti transisi
    // Validates: Requirements 5.1, 5.6
    [Fact]
    public void Property5_Auth_state_machine_stays_valid_and_follows_transitions()
    {
        // For any random initial state/flag and any random sequence of auth events, folding
        // through AuthTransition.Next keeps the resulting state in {LoggedOut, LoggingIn,
        // LoggedIn}; and an AuthExpired event ALWAYS yields (LoggedOut, NeedsReauth = true).
        var scenario =
            from initialState in AuthStates
            from initialReauth in Gen.Bool
            from events in AuthEvents.Array[0, 20]
            select (initialState, initialReauth, events);

        scenario.Sample(
            s =>
            {
                var (state, needsReauth, events) = s;

                foreach (var ev in events)
                {
                    var next = AuthTransition.Next(state, needsReauth, ev);

                    // The resulting state is always one of the three defined values.
                    Assert.Contains(
                        next.State,
                        new[] { AuthState.LoggedOut, AuthState.LoggingIn, AuthState.LoggedIn });

                    // AuthExpired is terminal: always LoggedOut with re-auth required.
                    if (ev == AuthEvent.AuthExpired)
                    {
                        Assert.Equal(AuthState.LoggedOut, next.State);
                        Assert.True(next.NeedsReauth);
                    }

                    state = next.State;
                    needsReauth = next.NeedsReauth;
                }

                // The folded final state is always valid as well.
                Assert.Contains(
                    state,
                    new[] { AuthState.LoggedOut, AuthState.LoggingIn, AuthState.LoggedIn });
            },
            iter: 100);
    }
}
