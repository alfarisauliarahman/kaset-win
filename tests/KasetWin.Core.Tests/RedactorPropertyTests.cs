using CsCheck;
using KasetWin.Core.Diagnostics;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Property-based tests for <see cref="Redactor"/> (Task 1.4).
/// </summary>
public class RedactorPropertyTests
{
    // Feature: kaset-winui3, Property 33: Redaksi menghapus nilai sensitif dari output
    // Validates: Requirements 21.3, 22.3
    [Fact]
    public void Property33_Redaction_removes_sensitive_values()
    {
        // For any randomly generated secret value embedded in a recognised sensitive
        // context (Authorization/Cookie headers, SAPISIDHASH, Bearer tokens, named
        // cookie/token pairs in header/JSON/query form), the redacted output must no
        // longer contain the original value.
        PbtGenerators.Token.Sample(
            secret =>
            {
                // Each entry places the same secret inside a different sensitive pattern.
                var inputs = new[]
                {
                    $"Authorization: SAPISIDHASH 1700000000_{secret}",
                    $"Authorization: Bearer {secret}",
                    $"Cookie: SAPISID={secret}; OTHER=plain",
                    $"Set-Cookie: __Secure-3PAPISID={secret}; Secure",
                    $"SAPISIDHASH 1700000000_{secret}",
                    $"Bearer {secret}",
                    $"\"SAPISID\":\"{secret}\"",
                    $"access_token={secret}&grant=code",
                    $"refresh_token: {secret}",
                    $"the cookie SAPISID={secret} appears mid-sentence",
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
