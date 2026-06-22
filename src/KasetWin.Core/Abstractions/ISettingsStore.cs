namespace KasetWin.Core.Abstractions;

/// <summary>
/// Cross-layer abstraction over a simple, persisted string key/value store used by
/// <c>SettingsService</c> to hold user preferences (Req 18.4). Keeping the backing store
/// behind an interface lets <c>KasetWin.Core</c> stay free of WinUI/WinRT: the production
/// implementation (<c>LocalSettingsStore</c> in <c>KasetWin.Platform</c>) is backed by
/// <c>Windows.Storage.ApplicationData.Current.LocalSettings</c>, while
/// <see cref="KasetWin.Core.Services.Settings.InMemorySettingsStore"/> backs headless tests.
/// </summary>
/// <remarks>
/// Values are opaque strings; <c>SettingsService</c> is responsible for serializing typed
/// preferences (enums/bools) to and from these strings via <see cref="KasetWin.Core.Models.KasetJson"/>
/// so round-trips are exact (Property 32). Implementations must persist writes immediately so a
/// fresh service instance constructed over the same store observes previously stored values.
/// </remarks>
public interface ISettingsStore
{
    /// <summary>
    /// Returns the stored string for <paramref name="key"/>, or <see langword="null"/> when no
    /// value has been stored (in which case the caller applies its default).
    /// </summary>
    /// <param name="key">A non-empty logical preference key.</param>
    string? Get(string key);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>, replacing any existing value,
    /// and persists it immediately.
    /// </summary>
    /// <param name="key">A non-empty logical preference key.</param>
    /// <param name="value">The string value to persist.</param>
    void Set(string key, string value);

    /// <summary>
    /// Removes any value stored under <paramref name="key"/>. A no-op when the key is absent.
    /// </summary>
    /// <param name="key">A non-empty logical preference key.</param>
    void Remove(string key);
}
