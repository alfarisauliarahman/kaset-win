using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Foundation.Collections;

namespace KasetWin.Platform.Storage;

/// <summary>
/// A tiny key-value settings bag that mirrors the shape of
/// <c>ApplicationData.Current.LocalSettings.Values</c> the app used when it only ran packaged, so
/// existing call sites keep working while also working when the app runs <b>unpackaged</b> (a
/// standalone .exe / Velopack install), where <c>ApplicationData.Current</c> is unavailable.
/// </summary>
public interface ISettingsBag
{
    /// <summary>Gets or sets a value; get returns <c>null</c> for an absent key (never throws).</summary>
    object? this[string key] { get; set; }

    /// <summary>Tries to read a value; <c>false</c> when the key is absent.</summary>
    bool TryGetValue(string key, out object? value);

    /// <summary>Removes a key if present.</summary>
    void Remove(string key);
}

/// <summary>
/// Storage locations + a persisted settings bag that work in <b>both</b> packaging modes.
/// <list type="bullet">
///   <item><b>Packaged (MSIX, the dev build).</b> <see cref="LocalFolder"/> is the package's
///   <c>ApplicationData.LocalFolder</c> and <see cref="Settings"/> wraps its <c>LocalSettings</c>,
///   so the existing WebView2 login profile and preferences are preserved.</item>
///   <item><b>Unpackaged (standalone .exe).</b> Everything lives under
///   <c>%LOCALAPPDATA%\Kaset</c>: files in that folder and preferences in <c>settings.json</c>.
///   <c>ApplicationData.Current</c> is never touched (it throws with no package identity).</item>
/// </list>
/// Detection happens once at startup.
/// </summary>
public static class AppData
{
    private static readonly bool _packaged = DetectPackaged();
    private static readonly Lazy<string> _localFolder = new(ResolveLocalFolder);
    private static readonly Lazy<ISettingsBag> _settings = new(ResolveSettings);

    /// <summary>Whether the process has Windows package identity (MSIX) rather than running standalone.</summary>
    public static bool IsPackaged => _packaged;

    /// <summary>The per-user data folder (login profile, covers, cache, diagnostics live under here).</summary>
    public static string LocalFolder => _localFolder.Value;

    /// <summary>The persisted preferences bag (theme, language, equalizer, search history, …).</summary>
    public static ISettingsBag Settings => _settings.Value;

    private static bool DetectPackaged()
    {
        try
        {
            // Package.Current throws when there is no package identity (unpackaged process).
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveLocalFolder()
    {
        if (_packaged)
        {
            try
            {
                return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                // Fall through to the unpackaged location if identity is somehow unavailable.
            }
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kaset");
        Directory.CreateDirectory(root);
        return root;
    }

    private static ISettingsBag ResolveSettings()
    {
        if (_packaged)
        {
            try
            {
                return new PackagedSettingsBag(Windows.Storage.ApplicationData.Current.LocalSettings.Values);
            }
            catch
            {
                // Fall through to the file-backed bag.
            }
        }

        return new FileSettingsBag(Path.Combine(LocalFolder, "settings.json"));
    }

    /// <summary>Packaged bag: a thin wrapper over the WinRT <c>LocalSettings.Values</c> property set.</summary>
    private sealed class PackagedSettingsBag(IPropertySet values) : ISettingsBag
    {
        private readonly IPropertySet _values = values;

        public object? this[string key]
        {
            get => _values.TryGetValue(key, out var value) ? value : null;
            set => _values[key] = value;
        }

        public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

        public void Remove(string key) => _values.Remove(key);
    }

    /// <summary>Unpackaged bag: an in-memory map persisted to a JSON file on every write.</summary>
    private sealed class FileSettingsBag : ISettingsBag
    {
        private readonly string _path;
        private readonly Dictionary<string, object?> _map;
        private readonly object _gate = new();

        public FileSettingsBag(string path)
        {
            _path = path;
            _map = Load(path);
        }

        public object? this[string key]
        {
            get
            {
                lock (_gate)
                {
                    return _map.TryGetValue(key, out var value) ? value : null;
                }
            }
            set
            {
                lock (_gate)
                {
                    _map[key] = value;
                    Save();
                }
            }
        }

        public bool TryGetValue(string key, out object? value)
        {
            lock (_gate)
            {
                return _map.TryGetValue(key, out value);
            }
        }

        public void Remove(string key)
        {
            lock (_gate)
            {
                if (_map.Remove(key))
                {
                    Save();
                }
            }
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(_map));
            }
            catch
            {
                // Preference persistence is best-effort.
            }
        }

        private static Dictionary<string, object?> Load(string path)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(path)
                    && JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path)) is { } raw)
                {
                    foreach (var (key, value) in raw)
                    {
                        map[key] = ToPrimitive(value);
                    }
                }
            }
            catch
            {
                // A corrupt file starts empty.
            }

            return map;
        }

        // Rehydrate to the primitive CLR types the call sites expect (string / bool / long / double),
        // so `bag[key] as string` and `bag[key] is bool` behave exactly like LocalSettings did.
        private static object? ToPrimitive(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }
}
