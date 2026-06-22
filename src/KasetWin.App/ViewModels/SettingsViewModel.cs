using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Settings;

namespace KasetWin.App.ViewModels;

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
    private readonly bool _isInitializing;

    /// <summary>
    /// Creates the ViewModel over <paramref name="settings"/>, seeding the bound properties from the
    /// service's current values.
    /// </summary>
    /// <param name="settings">The preference store written through on every change (Req 18.4).</param>
    public SettingsViewModel(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        _isInitializing = true;
        SelectedLaunchPage = settings.DefaultLaunchPage;
        RememberPlaybackSettings = settings.RememberPlaybackSettings;
        SyncedLyricsEnabled = settings.SyncedLyricsEnabled;
        SelectedAudioQuality = settings.PreferredAudioQuality;
        _isInitializing = false;
    }

    /// <summary>All launch-page choices, bound to the launch-page ComboBox (Req 18.1).</summary>
    public IReadOnlyList<LaunchPage> LaunchPages { get; } = Enum.GetValues<LaunchPage>();

    /// <summary>All audio-quality choices (Low/Medium/High), bound to the quality ComboBox (Req 7.3).</summary>
    public IReadOnlyList<AudioQuality> AudioQualities { get; } = Enum.GetValues<AudioQuality>();

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
}
