using CsCheck;
using KasetWin.Core.Services.Api;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Property-based test for <see cref="CookieSapisidResolver"/> (Task 2.5).
/// </summary>
public class CookieSapisidResolverPropertyTests
{
    /// <summary>Noise cookies whose names can never collide with the SAPISID cookie names.</summary>
    private static readonly Gen<CookiePair[]> NoiseCookies =
        Gen.Select(PbtGenerators.ShortToken, PbtGenerators.ShortToken)
            .Select((name, value) => new CookiePair("zz-" + name, value))
            .Array[0, 4];

    // Feature: kaset-winui3, Property 3: Resolusi SAPISID dari koleksi cookie
    // Validates: Requirements 3.3
    [Fact]
    public void Property3_Sapisid_resolution_prefers_primary_then_fallback_else_empty()
    {
        // For any cookie collection: the resolver returns __Secure-3PAPISID when present and
        // non-empty; otherwise SAPISID when present and non-empty; otherwise null (failure).
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

                // TryResolve mirrors Resolve: success iff a value was found.
                var ok = CookieSapisidResolver.TryResolve(cookies, out var resolved);
                Assert.Equal(expected is not null, ok);
                Assert.Equal(expected ?? string.Empty, resolved);
            },
            iter: 100);
    }
}
