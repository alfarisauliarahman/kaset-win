using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CsCheck;
using KasetWin.Core.Services.Api;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Property-based tests for <see cref="InnerTubeSupport"/> (Tasks 2.3 and 2.4).
/// </summary>
public class InnerTubeSupportPropertyTests
{
    private static readonly Gen<string> Origins = Gen.OneOfConst(
        InnerTubeSupport.MusicOrigin,
        "https://www.youtube.com",
        "https://accounts.google.com");

    // Feature: kaset-winui3, Property 1: SAPISIDHASH deterministik dan well-formed
    // Validates: Requirements 3.1
    [Fact]
    public void Property1_SapisidHash_is_deterministic_and_wellformed()
    {
        // For any timestamp, SAPISID value, and origin: the output is
        // "SAPISIDHASH {ts}_{sha1hex}" where sha1hex is the lowercase 40-char SHA1 of
        // "{ts} {sapisid} {origin}", and repeated calls are identical (deterministic).
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

                    // Well-formed: prefix + 40 lowercase hex characters after the underscore.
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
        //   - context.client.clientName == WEB_REMIX
        //   - the origin used for the request is exactly the music origin, which is the
        //     same origin fed to ComputeSapisidHash.
        var onBehalfOfUser = Gen.OneOf(Gen.Const((string?)null), PbtGenerators.ShortToken.Select(s => (string?)s));

        Gen.Select(onBehalfOfUser, PbtGenerators.UnixSeconds, PbtGenerators.Token)
            .Sample(
                t =>
                {
                    var (user, ts, sapisid) = t;

                    var payload = InnerTubeSupport.BuildContext(user);
                    var clientName = (string?)payload["context"]!["client"]!["clientName"];

                    Assert.Equal(InnerTubeSupport.ClientNameMusic, clientName);
                    Assert.Equal("WEB_REMIX", clientName);

                    // The origin pinned for music requests is the constant music origin, and
                    // it is the very origin the SAPISIDHASH is computed against.
                    var origin = new YTMusicClientOptions().Origin;
                    Assert.Equal(InnerTubeSupport.MusicOrigin, origin);
                    Assert.Equal("https://music.youtube.com", origin);

                    var expectedAuth = InnerTubeSupport.ComputeSapisidHash(ts, sapisid, origin);
                    var musicAuth = InnerTubeSupport.ComputeSapisidHash(ts, sapisid, InnerTubeSupport.MusicOrigin);
                    Assert.Equal(expectedAuth, musicAuth);
                },
                iter: 100);
    }
}
