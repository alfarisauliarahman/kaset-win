using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Settings;
using KasetWin.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KasetWin.App.ViewModels;

/// <summary>A single equalizer band (centre-frequency label + its current gain in dB, -12..+12).</summary>
public sealed partial class EqBand : ObservableObject
{
    /// <summary>Human-readable centre frequency, e.g. "62 Hz" / "1 kHz".</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Screen-reader name for this band's slider, e.g. "1 kHz gain". The visible <see cref="Label"/>
    /// sits in a sibling TextBlock that a screen reader never associates with the slider, so the
    /// frequency has to be carried onto the control itself.
    /// </summary>
    public string AccessibleName => Localization.UiStrings.A11yEqBand(Label);

    /// <summary>Gain applied at this band in dB (-12..+12).</summary>
    [ObservableProperty]
    private double _gain;
}

/// <summary>
/// ViewModel for the Settings surface (Task 14.10, Req 18.1–18.4, Req 7.3). Surfaces the four
/// user-tunable preferences — default launch page, "remember playback", synced-lyrics toggle and
/// preferred audio quality — and writes every change straight through to <see cref="ISettingsService"/>
/// so it is persisted immediately (Req 18.4). The service raises its own <c>Changed</c> event and is
/// the single source of truth; this ViewModel only mirrors the values for two-way XAML binding.
/// </summary>
/// <remarks>
/// Backing fields are seeded from the service at construction. The <see cref="_isInitializing"/> guard
/// suppresses the generated change callbacks during that seeding so we never write the freshly-read
/// values back (the service setters are idempotent, but the guard keeps the intent explicit and avoids
/// redundant <c>Changed</c> churn). After construction, each <c>OnXChanged</c> partial persists the new
/// value through the service.
/// </remarks>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IPlaybackController? _playback;
    private readonly KasetWin.Core.Services.Lyrics.ILyricsService? _lyrics;
    private const string LyricsProviderKey = "lyrics.provider";

    /// <summary>Provider name of the YouTube Music lyrics source (matches <c>ILyricsProvider.Name</c>).</summary>
    private const string YouTubeMusicProviderName =
        KasetWin.Core.Services.Lyrics.YouTubeMusicLyricsProvider.ProviderName;
    private readonly bool _isInitializing;
    private bool _suppressBandApply;

    /// <summary>
    /// Creates the ViewModel over <paramref name="settings"/>, seeding the bound properties from the
    /// service's current values.
    /// </summary>
    /// <param name="settings">The preference store written through on every change (Req 18.4).</param>
    /// <param name="playback">Playback controller used to apply the equalizer live (optional).</param>
    public SettingsViewModel(ISettingsService settings, IPlaybackController? playback = null, KasetWin.Core.Services.Lyrics.ILyricsService? lyrics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _playback = playback;
        _lyrics = lyrics;

        _isInitializing = true;
        SelectedLaunchPage = settings.DefaultLaunchPage;
        RememberPlaybackSettings = settings.RememberPlaybackSettings;
        SyncedLyricsEnabled = settings.SyncedLyricsEnabled;
        SelectedAudioQuality = settings.PreferredAudioQuality;
        SelectedThemeIndex = ThemeManager.Current switch
        {
            Microsoft.UI.Xaml.ElementTheme.Light => 1,
            Microsoft.UI.Xaml.ElementTheme.Dark => 2,
            _ => 0,
        };

        var savedProvider = AppData.Settings[LyricsProviderKey] as string;
        SelectedLyricsProviderIndex = savedProvider switch
        {
            YouTubeMusicProviderName => 1,
            "LRCLib" => 2,
            "NetEase" => 3,
            _ => 0,
        };
        ApplyLyricsProvider(SelectedLyricsProviderIndex);

        SelectedLanguageIndex = LoadLanguageSetting() == "en" ? 1 : 0;

        // Close behaviour: default to hide-to-tray (background audio) unless the user turned it off.
        CloseToTray = AppData.Settings[CloseToTrayKey] is not bool closeToTray || closeToTray;
        PreferSongVersion = AppData.Settings[PreferSongVersionKey] is not false;

        Bands = new ObservableCollection<EqBand>(
            EqBandLabels.Select(l => new EqBand { Label = l }));
        LoadEqualizer();
        foreach (var band in Bands)
        {
            band.PropertyChanged += OnBandChanged;
        }

        _isInitializing = false;

        // Push the persisted equalizer to the player on startup (harmless when disabled/flat).
        ApplyEqualizer(persist: false);
    }

    /// <summary>Localised launch-page labels, index-aligned with <see cref="LaunchPage"/> (Req 18.1).</summary>
    public IReadOnlyList<string> LaunchPageOptions { get; } = Localization.UiStrings.LaunchPageOptions;

    /// <summary>Localised audio-quality labels, index-aligned with <see cref="AudioQuality"/> (Req 7.3).</summary>
    public IReadOnlyList<string> AudioQualityOptions { get; } = Localization.UiStrings.AudioQualityOptions;

    /// <summary>Launch page as a ComboBox index over <see cref="LaunchPageOptions"/>.</summary>
    public int SelectedLaunchPageIndex
    {
        get => (int)SelectedLaunchPage;
        set
        {
            if (value >= 0)
            {
                SelectedLaunchPage = (LaunchPage)value;
            }
        }
    }

    /// <summary>Audio quality as a ComboBox index over <see cref="AudioQualityOptions"/>.</summary>
    public int SelectedAudioQualityIndex
    {
        get => (int)SelectedAudioQuality;
        set
        {
            if (value >= 0)
            {
                SelectedAudioQuality = (AudioQuality)value;
            }
        }
    }

    /// <summary>Selected default launch page; persisted on change (Req 18.1, Req 18.4).</summary>
    [ObservableProperty]
    private LaunchPage _selectedLaunchPage;

    /// <summary>Whether shuffle/repeat are restored on next launch; persisted on change (Req 18.2, Req 18.4).</summary>
    [ObservableProperty]
    private bool _rememberPlaybackSettings;

    /// <summary>Whether synced lyrics are preferred over plain fallback; persisted on change (Req 18.3, Req 18.4).</summary>
    [ObservableProperty]
    private bool _syncedLyricsEnabled;

    /// <summary>Selected preferred streaming audio quality; persisted on change (Req 7.3, Req 18.4).</summary>
    [ObservableProperty]
    private AudioQuality _selectedAudioQuality;

    /// <summary>Theme choices bound to the Appearance ComboBox (index 0=system, 1=light, 2=dark).</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = Localization.UiStrings.ThemeOptions;

    /// <summary>Selected theme index; applied + persisted live via <see cref="ThemeManager"/>.</summary>
    [ObservableProperty]
    private int _selectedThemeIndex;

    /// <summary>Lyrics provider choices (0=Auto, 1=YouTube Music, 2=LRCLib, 3=NetEase).</summary>
    public IReadOnlyList<string> LyricsProviderOptions { get; } = Localization.UiStrings.LyricsProviderOptions;

    /// <summary>Selected lyrics provider; applied to the service + persisted.</summary>
    [ObservableProperty]
    private int _selectedLyricsProviderIndex;

    partial void OnSelectedLyricsProviderIndexChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        ApplyLyricsProvider(value);
    }

    private void ApplyLyricsProvider(int index)
    {
        var name = index switch
        {
            1 => YouTubeMusicProviderName,
            2 => "LRCLib",
            3 => "NetEase",
            _ => null,
        };
        if (_lyrics is not null)
        {
            _lyrics.PreferredProvider = name;
        }

        AppData.Settings[LyricsProviderKey] = name ?? string.Empty;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        ThemeManager.Set(value switch
        {
            1 => Microsoft.UI.Xaml.ElementTheme.Light,
            2 => Microsoft.UI.Xaml.ElementTheme.Dark,
            _ => Microsoft.UI.Xaml.ElementTheme.Default,
        });
    }

    // ── Content language (hl pin on every music request; server-localised strings) ────────────────

    private const string LanguageKey = "app.language";

    /// <summary>Content-language choices (0 = Indonesian, 1 = English). Always pinned — an
    /// unpinned request follows the account language and leaves the UI half-translated.</summary>
    public IReadOnlyList<string> LanguageOptions { get; } = ["Indonesia", "English"];

    /// <summary>
    /// Selected content language. Applies immediately to new requests (section titles, dates and
    /// descriptions come server-localised); already-open pages refresh as their caches expire.
    /// </summary>
    [ObservableProperty]
    private int _selectedLanguageIndex;

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        ApplyLanguage(value);
    }

    private static void ApplyLanguage(int index)
    {
        var hl = index == 1 ? "en" : "id";
        KasetWin.Core.Services.Api.InnerTubeSupport.LanguageOverride = hl;
        AppData.Settings[LanguageKey] = hl;
    }

    /// <summary>The persisted content-language code: <c>"en"</c>, or <c>"id"</c> (the default).</summary>
    public static string LoadLanguageSetting()
    {
        var saved = AppData.Settings[LanguageKey] as string;
        return saved == "en" ? "en" : "id";
    }

    // ── Prefer the song version (music videos play as their album track) ──────────────────────────

    private const string PreferSongVersionKey = "playback.preferSongVersion";

    /// <summary>
    /// When on (the default), a music video about to play is swapped for its album ("song")
    /// version when one can be found unambiguously. Read by the player per load, so flipping it
    /// applies from the next track.
    /// </summary>
    [ObservableProperty]
    private bool _preferSongVersion = true;

    partial void OnPreferSongVersionChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        AppData.Settings[PreferSongVersionKey] = value;
    }

    // ── Close behaviour (hide-to-tray vs quit) ────────────────────────────────────────────────────

    private const string CloseToTrayKey = "window.closeToTray";

    /// <summary>Close-behaviour choices (0 = minimize to tray, 1 = quit), index-aligned with the combo.</summary>
    public IReadOnlyList<string> CloseBehaviorOptions { get; } = Localization.UiStrings.CloseBehaviorOptions;

    /// <summary>Whether ✕ hides Kaset to the tray (audio keeps playing) rather than quitting.</summary>
    [ObservableProperty]
    private bool _closeToTray;

    /// <summary>Close behaviour as a ComboBox index: 0 = minimize to tray, 1 = quit.</summary>
    public int CloseBehaviorIndex
    {
        get => CloseToTray ? 0 : 1;
        set
        {
            if (value >= 0)
            {
                CloseToTray = value == 0;
            }
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        OnPropertyChanged(nameof(CloseBehaviorIndex));

        if (_isInitializing)
        {
            return;
        }

        AppData.Settings[CloseToTrayKey] = value;
    }

    // ── Global hotkeys ────────────────────────────────────────────────────────────────────────────

    private const string GlobalHotkeysKey = "input.globalHotkeys";

    /// <summary>
    /// Whether system-wide playback hotkeys are enabled. Off by default: they claim
    /// <c>Ctrl+Alt+Arrow</c> from every other app on the machine, which is not a decision to make on
    /// the user's behalf.
    /// </summary>
    public static bool LoadGlobalHotkeysEnabled() =>
        AppData.Settings[GlobalHotkeysKey] is bool enabled && enabled;

    /// <summary>Whether system-wide playback hotkeys are enabled (Ctrl+Alt+←/→/↑/↓).</summary>
    [ObservableProperty]
    private bool _globalHotkeysEnabled = LoadGlobalHotkeysEnabled();

    partial void OnGlobalHotkeysEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        AppData.Settings[GlobalHotkeysKey] = value;

        // Apply live so the toggle is verifiable immediately rather than after a restart.
        if (App.Current?.MainWindow is MainWindow window)
        {
            window.SetGlobalHotkeysEnabled(value);
        }
    }

    // ── Discord Rich Presence ─────────────────────────────────────────────────────────────────────

    private const string DiscordEnabledKey = "discord.richPresenceEnabled";
    private const string DiscordAppIdKey = "discord.applicationId";

    /// <summary>
    /// Whether the user has switched rich presence on. <b>Off by default</b>: publishing what you are
    /// listening to onto a public profile is a privacy choice, not something to enable on someone's
    /// behalf.
    /// </summary>
    public static bool LoadDiscordEnabled() =>
        AppData.Settings[DiscordEnabledKey] is bool enabled && enabled;

    /// <summary>
    /// The user's optional Application ID override — empty means "use the shared one Kaset ships".
    /// Static so <c>MainWindow</c> can read it at startup without constructing this ViewModel.
    /// </summary>
    public static string LoadDiscordApplicationId() =>
        AppData.Settings[DiscordAppIdKey] as string ?? string.Empty;

    /// <summary>Resolves the id actually used: the override when set, otherwise Kaset's shared id.</summary>
    public static string ResolveDiscordApplicationId() =>
        Hosting.DiscordRichPresenceOptions.Resolve(LoadDiscordApplicationId());

    /// <summary>Whether Discord rich presence is switched on.</summary>
    [ObservableProperty]
    private bool _discordRichPresenceEnabled = LoadDiscordEnabled();

    /// <summary>
    /// Optional Application ID override. Almost nobody needs this — it only changes which
    /// application name Discord displays. Blank uses the id Kaset ships with.
    /// </summary>
    [ObservableProperty]
    private string _discordApplicationId = LoadDiscordApplicationId();

    /// <summary>
    /// Whether the feature can actually run: either Kaset ships a shared id or the user supplied one.
    /// Surfaced so the Settings card can explain an unavailable toggle instead of doing nothing.
    /// </summary>
    public bool IsDiscordAvailable => !string.IsNullOrWhiteSpace(ResolveDiscordApplicationId());

    partial void OnDiscordRichPresenceEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        AppData.Settings[DiscordEnabledKey] = value;
        ApplyDiscordPresence();
    }

    partial void OnDiscordApplicationIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsDiscordAvailable));

        if (_isInitializing)
        {
            return;
        }

        AppData.Settings[DiscordAppIdKey] = (value ?? string.Empty).Trim();
        ApplyDiscordPresence();
    }

    /// <summary>
    /// Starts or stops presence right away. A setting that only takes effect after a restart reads
    /// as "it doesn't work", so both the toggle and the id apply live.
    /// </summary>
    private void ApplyDiscordPresence()
    {
        if (App.Current?.Services.GetService<Hosting.RichPresenceService>() is not { } presence)
        {
            return;
        }

        var appId = ResolveDiscordApplicationId();
        if (DiscordRichPresenceEnabled && !string.IsNullOrWhiteSpace(appId))
        {
            _ = presence.StartAsync(appId);
        }
        else
        {
            _ = presence.StopAsync();
        }
    }

    partial void OnSelectedLaunchPageChanged(LaunchPage value)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.DefaultLaunchPage = value;
    }

    partial void OnRememberPlaybackSettingsChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.RememberPlaybackSettings = value;
    }

    partial void OnSyncedLyricsEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.SyncedLyricsEnabled = value;
    }

    partial void OnSelectedAudioQualityChanged(AudioQuality value)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.PreferredAudioQuality = value;
    }

    // ── Equalizer (persisted in LocalSettings; applied live via IPlaybackController) ──────────────

    private static readonly string[] EqBandLabels =
        ["62 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz"];

    private const string EqEnabledKey = "eq.enabled";
    private const string EqPresetKey = "eq.preset";
    private const string EqGainsKey = "eq.gains";
    private const string CustomPreset = "Custom";

    /// <summary>Per-band gain sliders (62 Hz … 16 kHz), bound to the vertical slider strip.</summary>
    public ObservableCollection<EqBand> Bands { get; }

    /// <summary>Built-in presets; picking one fills every band, "Custom" is set when a band is nudged.</summary>
    public IReadOnlyList<string> EqPresets { get; } =
        ["Flat", "Bass Boost", "Treble Boost", "Vocal", "Rock", "Pop", "Electronic", "Custom"];

    /// <summary>Whether the equalizer is on. When off the bands are flattened (transparent).</summary>
    [ObservableProperty]
    private bool _isEqualizerEnabled;

    /// <summary>Selected preset name; drives the band values when changed by the user.</summary>
    [ObservableProperty]
    private string _selectedEqPreset = "Flat";

    /// <summary>When set, dragging one band nudges its immediate neighbours for a smoother curve.</summary>
    [ObservableProperty]
    private bool _linkNearbyBands;

    // Preset → 9 band gains (dB). "Custom" is user-defined and has no fixed curve.
    private static readonly Dictionary<string, double[]> PresetCurves = new()
    {
        ["Flat"] = [0, 0, 0, 0, 0, 0, 0, 0, 0],
        ["Bass Boost"] = [7, 6, 4, 2, 0, 0, 0, 0, 0],
        ["Treble Boost"] = [0, 0, 0, 0, 0, 2, 4, 6, 7],
        ["Vocal"] = [-2, -1, 0, 3, 4, 4, 2, 0, -1],
        ["Rock"] = [5, 3, -1, -2, 0, 2, 4, 5, 5],
        ["Pop"] = [-1, 1, 3, 4, 4, 2, 0, -1, -1],
        ["Electronic"] = [5, 4, 1, 0, -2, 1, 2, 4, 5],
    };

    partial void OnIsEqualizerEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        ApplyEqualizer(persist: true);
    }

    partial void OnLinkNearbyBandsChanged(bool value)
    {
        if (!_isInitializing)
        {
            PersistEqualizer();
        }
    }

    partial void OnSelectedEqPresetChanged(string value)
    {
        if (_isInitializing || value == CustomPreset)
        {
            return;
        }

        if (PresetCurves.TryGetValue(value, out var curve))
        {
            _suppressBandApply = true;
            for (var i = 0; i < Bands.Count && i < curve.Length; i++)
            {
                Bands[i].Gain = curve[i];
            }

            _suppressBandApply = false;
            ApplyEqualizer(persist: true);
        }
    }

    private void OnBandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isInitializing || _suppressBandApply || e.PropertyName != nameof(EqBand.Gain))
        {
            return;
        }

        // A manual band move optionally drags its neighbours, and always marks the preset "Custom".
        if (LinkNearbyBands && sender is EqBand moved)
        {
            NudgeNeighbours(moved);
        }

        if (SelectedEqPreset != CustomPreset)
        {
            _isInitializingPresetGuard(() => SelectedEqPreset = CustomPreset);
        }

        ApplyEqualizer(persist: true);
    }

    // Sets SelectedEqPreset to "Custom" without re-running the preset→bands fill.
    private void _isInitializingPresetGuard(Action set)
    {
        var wasSuppressing = _suppressBandApply;
        _suppressBandApply = true;
        set();
        _suppressBandApply = wasSuppressing;
    }

    private void NudgeNeighbours(EqBand moved)
    {
        var index = Bands.IndexOf(moved);
        if (index < 0)
        {
            return;
        }

        _suppressBandApply = true;
        void Pull(int i)
        {
            if (i >= 0 && i < Bands.Count)
            {
                // Move the neighbour a third of the way toward the dragged band.
                Bands[i].Gain = Math.Clamp(Bands[i].Gain + (moved.Gain - Bands[i].Gain) * 0.33, -12, 12);
            }
        }

        Pull(index - 1);
        Pull(index + 1);
        _suppressBandApply = false;
    }

    private void LoadEqualizer()
    {
        try
        {
            var values = AppData.Settings;
            IsEqualizerEnabled = values[EqEnabledKey] is bool b && b;
            LinkNearbyBands = values["eq.link"] is bool lb && lb;
            SelectedEqPreset = values[EqPresetKey] as string ?? "Flat";

            if (values[EqGainsKey] is string csv && !string.IsNullOrEmpty(csv))
            {
                var parts = csv.Split(',');
                for (var i = 0; i < Bands.Count && i < parts.Length; i++)
                {
                    if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var g))
                    {
                        Bands[i].Gain = Math.Clamp(g, -12, 12);
                    }
                }
            }
            else if (PresetCurves.TryGetValue(SelectedEqPreset, out var curve))
            {
                for (var i = 0; i < Bands.Count && i < curve.Length; i++)
                {
                    Bands[i].Gain = curve[i];
                }
            }
        }
        catch
        {
            // Corrupt/absent settings fall back to a flat, disabled equalizer.
        }
    }

    private void PersistEqualizer()
    {
        try
        {
            var values = AppData.Settings;
            values[EqEnabledKey] = IsEqualizerEnabled;
            values["eq.link"] = LinkNearbyBands;
            values[EqPresetKey] = SelectedEqPreset;
            values[EqGainsKey] = string.Join(
                ',', Bands.Select(b => b.Gain.ToString("0.##", CultureInfo.InvariantCulture)));
        }
        catch
        {
            // Persisting preferences is best-effort.
        }
    }

    private void ApplyEqualizer(bool persist)
    {
        if (persist)
        {
            PersistEqualizer();
        }

        var gains = Bands.Select(b => (int)Math.Round(b.Gain)).ToList();
        _ = _playback?.SetEqualizerAsync(IsEqualizerEnabled, gains);
    }
}
