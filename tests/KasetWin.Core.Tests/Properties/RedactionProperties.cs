using CsCheck;
using KasetWin.Core.Diagnostics;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test covering secret redaction for the kaset-winui3 feature (Property 33).
///
/// The single [Fact] runs a minimum of 100 CsCheck iterations.
///
/// SECURITY: the embedded "secret" is a randomly generated placeholder — never a real
/// cookie/token/SAPISID value.
/// </summary>
public class RedactionProperties
{
    // Feature: kaset-winui3, Property 33: Redaksi menghapus nilai sensitif dari output
    // Validates: Requirements 21.3, 22.3
    [Fact]
    public void Property33_Redaction_removes_sensitive_values()
    {
        // For any randomly generated secret embedded in a recognised sensitive pattern
        // (SAPISIDHASH tokens, __Secure-3PAPISID cookie values, Authorization headers,
        // Bearer tokens, access_token=... pairs in header/JSON/query form), the redacted
        // output must no longer contain the original secret value.
        PbtGenerators.Token.Sample(
            secret =>
            {
                // Each entry places the same secret inside a different sensitive pattern.
                var inputs = new[]
                {
                    $"Authorization: SAPISIDHASH 1700000000_{secret}",
                    $"Authorization: Bearer {secret}",
                    $"Cookie: __Secure-3PAPISID={secret}; OTHER=plain",
                    $"Set-Cookie: __Secure-3PAPISID={secret}; Secure",
                    $"SAPISIDHASH 1700000000_{secret}",
                    $"Bearer {secret}",
                    $"\"SAPISID\":\"{secret}\"",
                    $"access_token={secret}&grant=code",
                    $"the cookie __Secure-3PAPISID={secret} appears mid-sentence",
                };

                foreach (var input in inputs)
                {
                    var redacted = Redactor.Redact(input);

                    Assert.DoesNotContain(secret, redacted, System.StringComparison.Ordinal);
                    Assert.Contains(Redactor.Placeholder, redacted, System.StringComparison.Ordinal);
                }
            },
            iter: 100);
    }
}
