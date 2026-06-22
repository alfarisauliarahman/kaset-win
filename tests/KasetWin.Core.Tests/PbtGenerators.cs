using CsCheck;

namespace KasetWin.Core.Tests;

/// <summary>
/// Shared CsCheck generators for the property-based tests (Feature: kaset-winui3).
/// Generators are deliberately constrained to the relevant input space so shrinking
/// stays meaningful and counterexamples are easy to read.
///
/// SECURITY: every "secret-like" value produced here is a randomly generated
/// placeholder — never a real cookie/token/SAPISID value.
/// </summary>
internal static class PbtGenerators
{
    /// <summary>URL/cookie-safe alphabet used for synthetic ids, tokens, and secrets.</summary>
    private const string TokenAlphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// A non-empty alphanumeric token (length 12–32). Long enough that a value can be
    /// asserted "absent" from redacted output without coincidental collisions.
    /// </summary>
    public static readonly Gen<string> Token =
        Gen.Char[TokenAlphabet].Array[12, 32].Select(chars => new string(chars));

    /// <summary>A shorter alphanumeric token (length 6–16) for ids and cookie names.</summary>
    public static readonly Gen<string> ShortToken =
        Gen.Char[TokenAlphabet].Array[6, 16].Select(chars => new string(chars));

    /// <summary>Unix timestamp in seconds across a wide but realistic range.</summary>
    public static readonly Gen<long> UnixSeconds = Gen.Long[0L, 4_102_444_800L];
}
