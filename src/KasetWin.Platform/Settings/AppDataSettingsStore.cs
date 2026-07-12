using KasetWin.Core.Abstractions;
using KasetWin.Platform.Storage;

namespace KasetWin.Platform.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> backed by <see cref="AppData.Settings"/>, so preferences persist in
/// <b>both</b> packaging modes: the package's <c>LocalSettings</c> when packaged, and
/// <c>%LOCALAPPDATA%\Kaset\settings.json</c> when the app runs as a standalone .exe. Supersedes the
/// packaged-only <see cref="LocalSettingsStore"/> (which threw unpackaged, forcing a volatile
/// in-memory fallback that lost settings on exit).
/// </summary>
public sealed class AppDataSettingsStore : ISettingsStore
{
    /// <inheritdoc />
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return AppData.Settings[key] as string;
    }

    /// <inheritdoc />
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        AppData.Settings[key] = value;
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        AppData.Settings.Remove(key);
    }
}
