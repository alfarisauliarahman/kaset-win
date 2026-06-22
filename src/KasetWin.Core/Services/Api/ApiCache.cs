using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Thread-safe, in-memory <see cref="IApiCache"/> with TTL expiry and LRU eviction
/// (Task 4.1, Requirements 23.1). Faithful port of the macOS <c>APICache</c>.
/// </summary>
/// <remarks>
/// All state is guarded by a single lock, so the cache is safe to share across the
/// concurrent fetches issued by <c>YTMusicClient</c>. The clock is injectable via
/// <see cref="TimeProvider"/> so TTL and LRU behaviour can be exercised deterministically
/// in tests without sleeping.
/// </remarks>
public sealed class ApiCache : IApiCache
{
    /// <summary>Default maximum number of entries before LRU eviction kicks in.</summary>
    public const int DefaultMaxEntries = 50;

    /// <summary>Minimum interval between automatic expired-entry sweeps.</summary>
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache;
    private readonly TimeProvider _clock;
    private readonly int _maxEntries;

    private DateTimeOffset _lastEvictionTime = DateTimeOffset.MinValue;

    /// <summary>
    /// Creates a cache.
    /// </summary>
    /// <param name="timeProvider">Clock source; defaults to <see cref="TimeProvider.System"/>. Inject a fake in tests.</param>
    /// <param name="maxEntries">Capacity before LRU eviction; defaults to <see cref="DefaultMaxEntries"/>.</param>
    public ApiCache(TimeProvider? timeProvider = null, int maxEntries = DefaultMaxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        this._clock = timeProvider ?? TimeProvider.System;
        this._maxEntries = maxEntries;
        this._cache = new Dictionary<string, CacheEntry>(capacity: maxEntries, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (this._gate)
        {
            if (!this._cache.TryGetValue(key, out var entry))
            {
                value = default;
                return false;
            }

            var now = this._clock.GetUtcNow();
            if (entry.IsExpired(now))
            {
                this._cache.Remove(key);
                value = default;
                return false;
            }

            // Touch for LRU ordering.
            entry.LastAccessed = now;

            switch (entry.Value)
            {
                case null:
                    // A null was deliberately cached; surface it for reference/nullable T.
                    value = default;
                    return true;
                case T typed:
                    value = typed;
                    return true;
                default:
                    // Type mismatch on the same key — treat as a miss rather than throw.
                    value = default;
                    return false;
            }
        }
    }

    /// <inheritdoc />
    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (this._gate)
        {
            var now = this._clock.GetUtcNow();

            // Sweep expired entries periodically rather than on every write.
            if (now - this._lastEvictionTime > EvictionInterval)
            {
                this.EvictExpiredEntries(now);
                this._lastEvictionTime = now;
            }

            // Evict LRU entries while at capacity (unless we are overwriting an existing key).
            while (this._cache.Count >= this._maxEntries && !this._cache.ContainsKey(key))
            {
                if (!this.EvictLeastRecentlyUsed())
                {
                    break;
                }
            }

            this._cache[key] = new CacheEntry(value, now, ttl);
        }
    }

    /// <inheritdoc />
    public void InvalidateMutationCaches()
    {
        ReadOnlySpan<string> mutationPrefixes =
        [
            "browse:",
            "next:",
            "like:",
            "playlist/get_add_to_playlist:",
        ];

        lock (this._gate)
        {
            // Materialize keys first to avoid mutating the dictionary during enumeration.
            foreach (var key in this._cache.Keys.ToArray())
            {
                foreach (var prefix in mutationPrefixes)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        this._cache.Remove(key);
                        break;
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        lock (this._gate)
        {
            this._cache.Clear();
        }
    }

    /// <inheritdoc />
    public void Invalidate(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        lock (this._gate)
        {
            foreach (var key in this._cache.Keys.ToArray())
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    this._cache.Remove(key);
                }
            }
        }
    }

    /// <inheritdoc />
    public string ComputeKey(string endpoint, JsonObject body, string? authUser = null, string? brand = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(body);

        // Canonical, recursively key-sorted JSON guarantees equivalent bodies hash identically.
        var canonical = new StringBuilder(256);
        WriteCanonical(canonical, body);

        byte[] input;
        if (string.IsNullOrEmpty(authUser) && string.IsNullOrEmpty(brand))
        {
            input = Encoding.UTF8.GetBytes(canonical.ToString());
        }
        else
        {
            using var buffer = new MemoryStream();
            buffer.Write(Encoding.UTF8.GetBytes(canonical.ToString()));

            // NUL-byte separated scope so account/brand bytes can never collide with JSON bytes.
            if (!string.IsNullOrEmpty(authUser))
            {
                buffer.WriteByte(0);
                buffer.Write(Encoding.UTF8.GetBytes(authUser));
            }

            if (!string.IsNullOrEmpty(brand))
            {
                buffer.WriteByte(0);
                buffer.Write(Encoding.UTF8.GetBytes(brand));
            }

            input = buffer.ToArray();
        }

        var hash = SHA256.HashData(input);
        return $"{endpoint}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Number of entries currently held (including any not-yet-swept expired ones).</summary>
    public int Count
    {
        get
        {
            lock (this._gate)
            {
                return this._cache.Count;
            }
        }
    }

    private void EvictExpiredEntries(DateTimeOffset now)
    {
        foreach (var pair in this._cache.ToArray())
        {
            if (pair.Value.IsExpired(now))
            {
                this._cache.Remove(pair.Key);
            }
        }
    }

    private bool EvictLeastRecentlyUsed()
    {
        string? lruKey = null;
        var oldest = DateTimeOffset.MaxValue;

        foreach (var pair in this._cache)
        {
            if (pair.Value.LastAccessed < oldest)
            {
                oldest = pair.Value.LastAccessed;
                lruKey = pair.Key;
            }
        }

        if (lruKey is null)
        {
            return false;
        }

        this._cache.Remove(lruKey);
        return true;
    }

    /// <summary>Writes a deterministic JSON encoding: object keys sorted ordinally, arrays in order.</summary>
    private static void WriteCanonical(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;

            case JsonObject obj:
                sb.Append('{');
                var first = true;
                foreach (var property in obj.OrderBy(static p => p.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append(JsonSerializer.Serialize(property.Key));
                    sb.Append(':');
                    WriteCanonical(sb, property.Value);
                }

                sb.Append('}');
                break;

            case JsonArray array:
                sb.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    WriteCanonical(sb, array[i]);
                }

                sb.Append(']');
                break;

            default:
                // JsonValue (string/number/bool) — ToJsonString yields canonical primitive text.
                sb.Append(node.ToJsonString());
                break;
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(object? value, DateTimeOffset timestamp, TimeSpan ttl)
        {
            this.Value = value;
            this.Timestamp = timestamp;
            this.Ttl = ttl;
            this.LastAccessed = timestamp;
        }

        public object? Value { get; }

        public DateTimeOffset Timestamp { get; }

        public TimeSpan Ttl { get; }

        public DateTimeOffset LastAccessed { get; set; }

        public bool IsExpired(DateTimeOffset now) => now - this.Timestamp > this.Ttl;
    }
}
