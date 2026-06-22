using KasetWin.Core.Abstractions;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace KasetWin.Platform.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> implementation backed by
/// <see cref="ApplicationData.Current"/>'s <see cref="ApplicationDataContainer.Values"/>
/// (local app data settings) — the canonical per-user, per-machine preference store for a
/// packaged Windows app (Task 13.1, Req 18.4).
/// </summary>
/// <remarks>
/// <para>
/// <c>LocalSettings</c> writes are durable and synchronous: a value set here is immediately visible
/// to a fresh <c>SettingsService</c> constructed over a new <see cref="LocalSettingsStore"/> after a
/// relaunch, satisfying the persistence round-trip (Property 32). The store only holds opaque
/// strings produced by <c>SettingsService</c>; all typed serialization lives in Core so this adapter
/// stays a thin WinRT seam.
/// </para>
/// <para>
/// Requires a packaged process; <see cref="ApplicationData.Current"/> throws when run unpackaged.
/// Headless tests use <see cref="KasetWin.Core.Services.Settings.InMemorySettingsStore"/> instead.
/// </para>
/// </remarks>
public sealed class LocalSettingsStore : ISettingsStore
{
    private readonly IPropertySet _values;

    /// <summary>
    /// Creates a store over the application's roaming-free <c>LocalSettings</c> container values.
    /// </summary>
    public LocalSettingsStore()
        : this(ApplicationData.Current.LocalSettings.Values)
    {
    }

    /// <summary>
    /// Creates a store over an explicit <see cref="IPropertySet"/> (e.g. a named composite/container),
    /// allowing the backing container to be chosen by the caller.
    /// </summary>
    /// <param name="values">The property set that holds the persisted preference strings.</param>
    public LocalSettingsStore(IPropertySet values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }

    /// <inheritdoc />
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _values.TryGetValue(key, out var value) ? value as string : null;
    }

    /// <inheritdoc />
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        // Assigning a string to LocalSettings.Values persists synchronously.
        _values[key] = value;
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _values.Remove(key);
    }
}
