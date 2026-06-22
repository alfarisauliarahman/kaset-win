using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// In-memory cache for InnerTube API responses (Task 4.1, Requirements 23.1).
/// </summary>
/// <remarks>
/// Mirrors the macOS <c>APICache</c> contract: time-to-live (TTL) expiry plus
/// least-recently-used (LRU) eviction, keyed by a stable hash of the request so
/// equivalent request bodies always map to the same key. Keys are namespaced by
/// endpoint (<c>"{endpoint}:{hash}"</c>) so mutation-driven invalidation can target
/// whole endpoint families by prefix. The cache is independent of any HTTP cache.
/// </remarks>
public interface IApiCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> when present and not expired.
    /// </summary>
    /// <typeparam name="T">Expected value type.</typeparam>
    /// <param name="key">Cache key, typically produced by <see cref="ComputeKey"/>.</param>
    /// <param name="value">The cached value when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> on a live hit; otherwise <c>false</c>.</returns>
    bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="ttl"/>.</summary>
    /// <remarks>Triggers periodic expired-entry sweeping and LRU eviction when at capacity.</remarks>
    void Set<T>(string key, T value, TimeSpan ttl);

    /// <summary>
    /// Removes every entry written by mutating operations: prefixes
    /// <c>browse:</c>, <c>next:</c>, <c>like:</c>, and <c>playlist/get_add_to_playlist:</c>.
    /// Long-lived caches such as lyrics are intentionally preserved.
    /// </summary>
    void InvalidateMutationCaches();

    /// <summary>Removes all entries (e.g. on account switch, sign-out, or session expiry).</summary>
    void InvalidateAll();

    /// <summary>Removes every entry whose key starts with <paramref name="prefix"/>.</summary>
    void Invalidate(string prefix);

    /// <summary>
    /// Builds a stable, deterministic cache key as <c>"{endpoint}:{sha256hex}"</c> where the
    /// hash is computed over the canonical (recursively key-sorted) JSON of <paramref name="body"/>,
    /// optionally scoped by <paramref name="authUser"/> and <paramref name="brand"/> to isolate
    /// cache entries between accounts. Equivalent bodies always yield the same key.
    /// </summary>
    string ComputeKey(string endpoint, JsonObject body, string? authUser = null, string? brand = null);
}
