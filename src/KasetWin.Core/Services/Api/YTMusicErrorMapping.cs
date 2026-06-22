using System.Net;
using KasetWin.Core.Errors;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Pure mapping from transport-level failures onto the unified <see cref="KasetError"/>
/// taxonomy (Req 3.6 / 20.2). Kept <c>static</c> and side-effect-free so the status mapping
/// can be exercised directly by property-based tests (Property 4).
/// </summary>
public static class YTMusicErrorMapping
{
    /// <summary>
    /// Maps an HTTP status code onto a <see cref="KasetError"/>. Authentication failures
    /// (<see cref="HttpStatusCode.Unauthorized"/> / <see cref="HttpStatusCode.Forbidden"/>) map
    /// to <see cref="KasetErrorKind.AuthExpired"/> (not retryable, triggers re-auth); every other
    /// non-success status maps to <see cref="KasetErrorKind.ApiError"/> carrying the status code.
    /// The function is total: it returns a value for any input.
    /// </summary>
    /// <param name="statusCode">The HTTP status code returned by the API.</param>
    /// <returns>A classified <see cref="KasetError"/>.</returns>
    public static KasetError MapHttpError(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new KasetError(
                KasetErrorKind.AuthExpired,
                $"Authentication expired (HTTP {code}).",
                statusCode: code);
        }

        return new KasetError(
            KasetErrorKind.ApiError,
            $"API request failed (HTTP {code}).",
            statusCode: code);
    }

    /// <summary>
    /// Wraps a transport-level network failure (no HTTP response, DNS failure, timeout, socket
    /// reset) as a retryable <see cref="KasetErrorKind.NetworkError"/> (Req 20.2).
    /// </summary>
    /// <param name="inner">The underlying transport exception, if any.</param>
    /// <returns>A <see cref="KasetError"/> classified as a (retryable) network error.</returns>
    public static KasetError MapNetworkError(Exception? inner = null) =>
        new(KasetErrorKind.NetworkError, "Network request failed.", inner);
}
