using System.Security.Cryptography;
using System.Text;

namespace KasetWin.Core.Services.Imaging;

/// <summary>
/// Pure, deterministic cache-key derivation for artwork (Req 16.2). Keys hash the source URL so
/// arbitrarily long/odd URLs map to a fixed, filesystem-safe identifier, and fold in the requested
/// display size so different render sizes are cached independently.
/// </summary>
/// <remarks>
/// Kept in <c>Core</c> (no WinRT) so keying is headless-testable and shared by both the in-memory
/// and on-disk cache tiers. SHA-256 is used purely as a stable content hash (not for security).
/// </remarks>
public static class ImageCacheKey
{
    /// <summary>
    /// Returns a stable key for <paramref name="url"/> at <paramref name="targetSize"/>. The result
    /// is lowercase hex of <c>SHA-256(url)</c> suffixed with <c>"_{size}"</c> (size normalized so any
    /// non-positive value collapses to <c>0</c> = "original").
    /// </summary>
    public static string For(Uri url, int targetSize)
    {
        ArgumentNullException.ThrowIfNull(url);
        var normalizedSize = targetSize > 0 ? targetSize : 0;
        return $"{Hash(url)}_{normalizedSize}";
    }

    /// <summary>Returns lowercase hex of <c>SHA-256</c> over the URL's absolute form.</summary>
    public static string Hash(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        // AbsoluteUri for absolute URLs; OriginalString as a fallback for relative inputs.
        var canonical = url.IsAbsoluteUri ? url.AbsoluteUri : url.OriginalString;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
