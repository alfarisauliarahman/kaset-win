using KasetWin.Core.Services.Api;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ApiCache"/> (Task 4.4, Req 23.1): TTL expiry, LRU eviction, and
/// mutation-prefix invalidation. A manual <see cref="TimeProvider"/> drives the clock so the
/// behaviour is exercised deterministically without sleeping.
/// </summary>
public class ApiCacheTests
{
    /// <summary>A controllable clock for deterministic TTL/LRU tests.</summary>
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryGet_returns_live_entry_then_misses_after_ttl_expiry()
    {
        var clock = new TestClock(Epoch);
        var cache = new ApiCache(clock);

        cache.Set("k", "value", TimeSpan.FromMinutes(1));

        Assert.True(cache.TryGet<string>("k", out var hit));
        Assert.Equal("value", hit);

        // Advance past the TTL — the entry must now be treated as a miss and removed.
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.False(cache.TryGet<string>("k", out var expired));
        Assert.Null(expired);
    }

    [Fact]
    public void Set_evicts_least_recently_used_entry_at_capacity()
    {
        var clock = new TestClock(Epoch);
        var cache = new ApiCache(clock, maxEntries: 2);
        var ttl = TimeSpan.FromHours(1);

        cache.Set("a", "A", ttl);
        clock.Advance(TimeSpan.FromSeconds(1));
        cache.Set("b", "B", ttl);
        clock.Advance(TimeSpan.FromSeconds(1));

        // Touch "a" so "b" becomes the least-recently-used entry.
        Assert.True(cache.TryGet<string>("a", out _));
        clock.Advance(TimeSpan.FromSeconds(1));

        // Inserting "c" is at capacity → evicts the LRU entry ("b").
        cache.Set("c", "C", ttl);

        Assert.True(cache.TryGet<string>("a", out _));
        Assert.False(cache.TryGet<string>("b", out _));
        Assert.True(cache.TryGet<string>("c", out _));
    }

    [Fact]
    public void InvalidateMutationCaches_removes_only_mutation_prefixed_entries()
    {
        var clock = new TestClock(Epoch);
        var cache = new ApiCache(clock);
        var ttl = TimeSpan.FromHours(1);

        cache.Set("browse:home", "1", ttl);
        cache.Set("next:radio", "2", ttl);
        cache.Set("like:song", "3", ttl);
        cache.Set("playlist/get_add_to_playlist:vid", "4", ttl);
        cache.Set("lyrics:song", "keep", ttl);

        cache.InvalidateMutationCaches();

        Assert.False(cache.TryGet<string>("browse:home", out _));
        Assert.False(cache.TryGet<string>("next:radio", out _));
        Assert.False(cache.TryGet<string>("like:song", out _));
        Assert.False(cache.TryGet<string>("playlist/get_add_to_playlist:vid", out _));

        // Long-lived caches such as lyrics are intentionally preserved.
        Assert.True(cache.TryGet<string>("lyrics:song", out var kept));
        Assert.Equal("keep", kept);
    }

    [Fact]
    public void Invalidate_removes_entries_by_prefix()
    {
        var clock = new TestClock(Epoch);
        var cache = new ApiCache(clock);
        var ttl = TimeSpan.FromHours(1);

        cache.Set("artist:1", "a", ttl);
        cache.Set("artist:2", "b", ttl);
        cache.Set("album:1", "c", ttl);

        cache.Invalidate("artist:");

        Assert.False(cache.TryGet<string>("artist:1", out _));
        Assert.False(cache.TryGet<string>("artist:2", out _));
        Assert.True(cache.TryGet<string>("album:1", out _));
    }
}
