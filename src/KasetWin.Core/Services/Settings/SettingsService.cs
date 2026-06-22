using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Settings;

/// <summary>
/// Default <see cref="ISettingsService"/> implementation that persists preferences through an
/// <see cref="ISettingsStore"/> (Task 13.1, Req 18.1–18.4, Req 7.3).
/// </summary>
/// <remarks>
/// <para>
/// Typed preferences are serialized to/from store strings with <see cref="KasetJson"/>
/// (<see cref="System.Text.Json"/>): enums persist as their stable <em>names</em> (never ordinals)
/// and bools as <c>true</c>/<c>false</c>. This makes the persisted set round-trip exactly — for any
/// state, <c>Apply(state)</c> followed by <c>Load()</c> reproduces an equal state (Property 32) — and
/// keeps stored values resilient to enum reordering.
/// </para>
/// <para>
/// Backing fields are hydrated from the store at construction. Each setter writes through to the
/// store immediately (Req 18.4) and raises <see cref="Changed"/> only when the value actually
/// changes, avoiding redundant persistence and event churn.
/// </para>
/// </remarks>
public sealed class SettingsService : ISettingsService
{
    private static class Keys
    {
        public const string DefaultLaunchPage = "settings.defaultLaunchPage";
        public const string RememberPlaybackSettings = "settings.rememberPlaybackSettings";
        public const string SyncedLyricsEnabled = "settings.syncedLyricsEnabled";
        public const string PreferredAudioQuality = "settings.preferredAudioQuality";
        public const string SavedRepeatMode = "settings.savedRepeatMode";
        public const string SavedShuffle = "settings.savedShuffle";
    }

    private readonly ISettingsStore _store;
    private readonly ILogger<SettingsService> _logger;

    private LaunchPage _defaultLaunchPage;
    private bool _rememberPlaybackSettings;
    private bool _syncedLyricsEnabled;
    private AudioQuality _preferredAudioQuality;
    private RepeatMode _savedRepeatMode;
    private bool _savedShuffle;

    /// <summary>
    /// Creates the service over <paramref name="store"/> and immediately hydrates all preferences
    /// from it (defaults applied for absent keys).
    /// </summary>
    /// <param name="store">Backing persistence store (in-memory for tests, LocalSettings in the app).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public SettingsService(ISettingsStore store, ILogger<SettingsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
        Load();
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LaunchPage DefaultLaunchPage
    {
        get => _defaultLaunchPage;
        set => SetEnum(ref _defaultLaunchPage, value, Keys.DefaultLaunchPage, nameof(DefaultLaunchPage));
    }

    /// <inheritdoc />
    public bool RememberPlaybackSettings
    {
        get => _rememberPlaybackSettings;
        set => SetBool(ref _rememberPlaybackSettings, value, Keys.RememberPlaybackSettings, nameof(RememberPlaybackSettings));
    }

    /// <inheritdoc />
    public bool SyncedLyricsEnabled
    {
        get => _syncedLyricsEnabled;
        set => SetBool(ref _syncedLyricsEnabled, value, Keys.SyncedLyricsEnabled, nameof(SyncedLyricsEnabled));
    }

    /// <inheritdoc />
    public AudioQuality PreferredAudioQuality
    {
        get => _preferredAudioQuality;
        set => SetEnum(ref _preferredAudioQuality, value, Keys.PreferredAudioQuality, nameof(PreferredAudioQuality));
    }

    /// <inheritdoc />
    public RepeatMode SavedRepeatMode
    {
        get => _savedRepeatMode;
        set => SetEnum(ref _savedRepeatMode, value, Keys.SavedRepeatMode, nameof(SavedRepeatMode));
    }

    /// <inheritdoc />
    public bool SavedShuffle
    {
        get => _savedShuffle;
        set => SetBool(ref _savedShuffle, value, Keys.SavedShuffle, nameof(SavedShuffle));
    }

    /// <inheritdoc />
    public void Load()
    {
        var defaults = SettingsState.Defaults;
        _defaultLaunchPage = ReadEnum(Keys.DefaultLaunchPage, defaults.DefaultLaunchPage);
        _rememberPlaybackSettings = ReadBool(Keys.RememberPlaybackSettings, defaults.RememberPlaybackSettings);
        _syncedLyricsEnabled = ReadBool(Keys.SyncedLyricsEnabled, defaults.SyncedLyricsEnabled);
        _preferredAudioQuality = ReadEnum(Keys.PreferredAudioQuality, defaults.PreferredAudioQuality);
        _savedRepeatMode = ReadEnum(Keys.SavedRepeatMode, defaults.SavedRepeatMode);
        _savedShuffle = ReadBool(Keys.SavedShuffle, defaults.SavedShuffle);
    }

    /// <inheritdoc />
    public SettingsState Snapshot() => new(
        DefaultLaunchPage: _defaultLaunchPage,
        RememberPlaybackSettings: _rememberPlaybackSettings,
        SyncedLyricsEnabled: _syncedLyricsEnabled,
        PreferredAudioQuality: _preferredAudioQuality,
        SavedRepeatMode: _savedRepeatMode,
        SavedShuffle: _savedShuffle);

    /// <inheritdoc />
    public void Apply(SettingsState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Persist every field unconditionally, then update backing fields, so the store and the
        // in-memory snapshot match exactly regardless of prior values.
        _store.Set(Keys.DefaultLaunchPage, Serialize(state.DefaultLaunchPage));
        _store.Set(Keys.RememberPlaybackSettings, Serialize(state.RememberPlaybackSettings));
        _store.Set(Keys.SyncedLyricsEnabled, Serialize(state.SyncedLyricsEnabled));
        _store.Set(Keys.PreferredAudioQuality, Serialize(state.PreferredAudioQuality));
        _store.Set(Keys.SavedRepeatMode, Serialize(state.SavedRepeatMode));
        _store.Set(Keys.SavedShuffle, Serialize(state.SavedShuffle));

        _defaultLaunchPage = state.DefaultLaunchPage;
        _rememberPlaybackSettings = state.RememberPlaybackSettings;
        _syncedLyricsEnabled = state.SyncedLyricsEnabled;
        _preferredAudioQuality = state.PreferredAudioQuality;
        _savedRepeatMode = state.SavedRepeatMode;
        _savedShuffle = state.SavedShuffle;

        RaiseChanged(propertyName: null);
    }

    private void SetEnum<TEnum>(ref TEnum field, TEnum value, string key, string propertyName)
        where TEnum : struct, Enum
    {
        if (EqualityComparer<TEnum>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        _store.Set(key, Serialize(value));
        RaiseChanged(propertyName);
    }

    private void SetBool(ref bool field, bool value, string key, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        _store.Set(key, Serialize(value));
        RaiseChanged(propertyName);
    }

    private TEnum ReadEnum<TEnum>(string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        var raw = _store.Get(key);
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        try
        {
            return KasetJson.Deserialize<TEnum>(raw);
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt/unknown stored value: fall back to the default rather than throwing so a
            // single bad key cannot prevent the app from starting.
            _logger.LogWarning("Stored setting '{Key}' is unreadable; using default.", key);
            return fallback;
        }
    }

    private bool ReadBool(string key, bool fallback)
    {
        var raw = _store.Get(key);
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        try
        {
            return KasetJson.Deserialize<bool>(raw);
        }
        catch (System.Text.Json.JsonException)
        {
            _logger.LogWarning("Stored setting '{Key}' is unreadable; using default.", key);
            return fallback;
        }
    }

    private static string Serialize<T>(T value) => KasetJson.Serialize(value);

    private void RaiseChanged(string? propertyName) =>
        Changed?.Invoke(this, new SettingsChangedEventArgs(propertyName, Snapshot()));
}
