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

        var services = App.Current.Services;
        var settings = services.GetService<ISettingsService>()
            ?? new SettingsService(new InMemorySettingsStore());

        ViewModel = new SettingsViewModel(settings);
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
}
