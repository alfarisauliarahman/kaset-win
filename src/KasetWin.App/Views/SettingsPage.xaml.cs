using KasetWin.App.Hosting;
using KasetWin.App.ViewModels;
using KasetWin.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Settings surface (Task 14.10, Req 18.1–18.4, Req 7.3). Lets the user pick the default launch
/// page, preferred audio quality, whether playback settings (shuffle/repeat) are remembered, and
/// whether synced lyrics are preferred. Every change is written through to <see cref="ISettingsService"/>
/// immediately (Req 18.4) via <see cref="SettingsViewModel"/>.
/// </summary>
/// <remarks>
/// Created by the shell <see cref="Frame"/> (parameterless ctor, navigated by full type name from
/// the NavigationView "Settings" item), so it resolves its dependencies from
/// <c>((App)Application.Current).Services</c>.
/// <para>
/// <see cref="ISettingsService"/> is not yet registered in the DI container — its registration lands
/// with the final AppHost wiring. To keep this page functional in the meantime,
/// <see cref="IServiceProvider.GetService"/> is used and, when it returns <see langword="null"/>, a
/// local <see cref="SettingsService"/> over an <see cref="InMemorySettingsStore"/> is constructed as a
/// fallback. The fallback persists for the lifetime of the page only.
/// </para>
/// <para>
/// TODO (final wiring): register <c>LocalSettingsStore</c> + <c>SettingsService</c> as a singleton in
/// <c>AppHost</c> so preferences persist across launches; once registered this fallback is inert.
/// </para>
/// </remarks>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        UBlockStatusText = ExtensionsService.UBlockStatusText;
        this.InitializeComponent();
        ApplyLabels();

        var services = App.Current.Services;
        var settings = services.GetService<ISettingsService>()
            ?? new SettingsService(new InMemorySettingsStore());
        var playback = services.GetService<Core.Abstractions.IPlaybackController>();
        var lyrics = services.GetService<Core.Services.Lyrics.ILyricsService>();

        ViewModel = new SettingsViewModel(settings, playback, lyrics);

        // Must run after ViewModel exists. ApplyLabels() is called before it (it only touches XAML
        // elements), so anything that reads the ViewModel belongs here — reading it from inside
        // ApplyLabels crashed the page with a NullReferenceException.
        DiscordUnavailableBar.IsOpen = !ViewModel.IsDiscordAvailable;
    }

    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public SettingsViewModel ViewModel { get; }

    public string UBlockStatusText { get; }

    /// <summary>
    /// Opens the extensions folder in File Explorer so the user can drop an unpacked extension
    /// (e.g. uBlock Origin) into it. The folder is created if it does not exist.
    /// </summary>
    private async void OnOpenExtensionsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ExtensionsService.EnsureExtensionsFolder();
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch
        {
            // Opening Explorer is a convenience; a failure here is non-fatal.
        }
    }

    /// <summary>Restarts Kaset so newly-added extensions are loaded at the next launch.</summary>
    private void OnRestartApp(object sender, RoutedEventArgs e)
    {
        // AppInstance.Restart re-launches the packaged app; extensions load during WebView2 init.
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }

    /// <summary>Applies the app language to every label on the page (the XAML texts are fallbacks).</summary>
    private void ApplyLabels()
    {
        PageTitleText.Text = Localization.UiStrings.SettingsTitle;
        GeneralHeader.Text = Localization.UiStrings.SettingsGeneral;
        LaunchPageLabel.Text = Localization.UiStrings.SettingsLaunchPageLabel;
        LaunchPageCaption.Text = Localization.UiStrings.SettingsLaunchPageCaption;
        CloseBehaviorLabel.Text = Localization.UiStrings.SettingsCloseBehaviorLabel;
        CloseBehaviorCaption.Text = Localization.UiStrings.SettingsCloseBehaviorCaption;
        PreferSongVersionLabel.Text = Localization.UiStrings.SettingsPreferSongVersionLabel;
        PreferSongVersionCaption.Text = Localization.UiStrings.SettingsPreferSongVersionCaption;
        ThemeLabel.Text = Localization.UiStrings.SettingsThemeLabel;
        ThemeCaption.Text = Localization.UiStrings.SettingsThemeCaption;
        LanguageLabel.Text = Localization.UiStrings.SettingsLanguageLabel;
        LanguageCaption.Text = Localization.UiStrings.SettingsLanguageCaption;
        PlaybackHeader.Text = Localization.UiStrings.SettingsPlayback;
        AudioQualityLabel.Text = Localization.UiStrings.SettingsAudioQualityLabel;
        AudioQualityCaption.Text = Localization.UiStrings.SettingsAudioQualityCaption;
        RememberLabel.Text = Localization.UiStrings.SettingsRememberLabel;
        RememberCaption.Text = Localization.UiStrings.SettingsRememberCaption;
        LyricsHeader.Text = Localization.UiStrings.SettingsLyricsHeader;
        LyricsSourceLabel.Text = Localization.UiStrings.SettingsLyricsSourceLabel;
        LyricsSourceCaption.Text = Localization.UiStrings.SettingsLyricsSourceCaption;
        Accessibility.A11y.Name(LyricsSourceCombo, Localization.UiStrings.SettingsLyricsSourceLabel);
        SyncedLabel.Text = Localization.UiStrings.SettingsSyncedLabel;
        SyncedCaption.Text = Localization.UiStrings.SettingsSyncedCaption;
        EqualizerHeader.Text = Localization.UiStrings.SettingsEqualizerHeader;
        EqLinkCheck.Content = Localization.UiStrings.SettingsEqLink;
        ExtensionsHeader.Text = Localization.UiStrings.SettingsExtensionsHeader;
        ExtensionsCaption.Text = Localization.UiStrings.SettingsExtensionsCaption;
        OpenExtFolderButton.Content = Localization.UiStrings.SettingsOpenExtensionsFolder;
        RestartAppButton.Content = Localization.UiStrings.SettingsRestartKaset;
        HotkeysLabel.Text = Localization.UiStrings.SettingsHotkeysLabel;
        HotkeysCaption.Text = Localization.UiStrings.SettingsHotkeysCaption;
        DiscordHeader.Text = Localization.UiStrings.SettingsDiscordHeader;
        DiscordLabel.Text = Localization.UiStrings.SettingsDiscordLabel;
        DiscordCaption.Text = Localization.UiStrings.SettingsDiscordCaption;
        DiscordAdvancedExpander.Header = Localization.UiStrings.SettingsDiscordAdvanced;
        DiscordAdvancedCaption.Text = Localization.UiStrings.SettingsDiscordAdvancedCaption;
        DiscordAppIdBox.PlaceholderText = Localization.UiStrings.SettingsDiscordPlaceholder;
        Accessibility.A11y.Name(DiscordAppIdBox, Localization.UiStrings.SettingsDiscordPlaceholder);
        DiscordPortalLink.Content = Localization.UiStrings.SettingsDiscordPortalLink;
        DiscordUnavailableBar.Message = Localization.UiStrings.SettingsDiscordUnavailable;
        AboutHeader.Text = Localization.UiStrings.SettingsAboutHeader;
        VersionLabel.Text = Localization.UiStrings.SettingsVersionLabel;
        VersionCaption.Text = Localization.UiStrings.SettingsVersionCaption;
        VersionText.Text = GetAppVersionString();
    }

    /// <summary>
    /// Returns the running app version as "Major.Minor.Build.Revision". Reads the packaged
    /// identity first, falling back to the executing assembly version when unpackaged.
    /// </summary>
    private static string GetAppVersionString()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            // Show the familiar SemVer (Major.Minor.Build) — MSIX carries a 4th "revision" that is
            // always 0 here and just reads as noise (e.g. "0.2.0" not "0.2.0.0").
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            var v = typeof(SettingsPage).Assembly.GetName().Version;
            return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private bool _languageComboSeeded;

    /// <summary>
    /// Applies a language change instantly — no restart and no navigation: the ViewModel has
    /// already pinned the new <c>hl</c> (two-way binding runs before this event), so relabel the
    /// shell in place; content pages refetch in the new language when next opened (the cache
    /// keys include <c>hl</c>).
    /// </summary>
    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The first SelectionChanged is the combo picking up the persisted value — not a change.
        if (!_languageComboSeeded)
        {
            _languageComboSeeded = true;
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            // Belt-and-braces: make sure the VM saw the new index before anything refetches.
            if (sender is ComboBox combo && combo.SelectedIndex >= 0)
            {
                ViewModel.SelectedLanguageIndex = combo.SelectedIndex;
            }

            (App.Current.MainWindow as MainWindow)?.ApplySidebarLanguage();

            // Recreate this page in place (no back-stack growth) so every label and combo
            // option list rebuilds in the new language — the user stays on Settings.
            if (Frame is { } frame)
            {
                frame.Navigate(typeof(SettingsPage));
                if (frame.BackStack.Count > 0)
                {
                    frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
                }
            }
        });
    }
}
