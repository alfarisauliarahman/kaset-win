using System.Collections.Concurrent;
using KasetWin.Core.Abstractions;

namespace KasetWin.Core.Services.Settings;

/// <summary>
/// Volatile, thread-safe <see cref="ISettingsStore"/> backed by an in-memory dictionary. It is the
/// default Core implementation: it lets <c>SettingsService</c> be exercised headless (e.g. by the
/// round-trip Property 32) without the WinRT <c>ApplicationData</c> dependency. The production app
/// substitutes <c>LocalSettingsStore</c> (KasetWin.Platform) for true on-disk persistence.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    /// <inheritdoc />
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        _values[key] = value;
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _values.TryRemove(key, out _);
    }
}
