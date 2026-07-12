using KasetWin.App.Composition;
using KasetWin.Core.Services.Activation;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace KasetWin.App;

/// <summary>
/// Application entry point. Builds the Generic Host + DI container (Task 1.3, Req 23.1) at
/// startup and exposes <see cref="Services"/> so views/ViewModels are resolved via constructor
/// injection. The hidden WebView2 playback host (Task 8.3) is wired in a later task.
/// </summary>
/// <remarks>
/// <b>Protocol activation (Task 26.1, Req 33).</b> The app registers the custom <c>kaset://</c>
/// scheme in <c>Package.appxmanifest</c>. On launch the activation arguments are inspected: when the
/// activation kind is <see cref="ExtendedActivationKind.Protocol"/>, the URI is parsed by the pure
/// <see cref="KasetUriParser"/> and dispatched to the shell (<see cref="MainWindow"/>) — a song plays,
/// a playlist/album/artist navigates to its detail page. Invalid or unknown URIs are ignored without
/// changing playback state (Req 33.5). Both the <em>cold-start</em> path
/// (<see cref="AppInstance.GetActivatedEventArgs"/> read once the shell exists) and the
/// <em>already-running</em> path (the <see cref="AppInstance.Activated"/> event, marshalled onto the
/// UI thread by the shell) are handled.
/// </remarks>
public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

    public App()
    {
        this.InitializeComponent();

        // The content-language pin must be in place before the first API request goes out,
        // otherwise the first page load (and its cache entries) use the account language.
        try
        {
            KasetWin.Core.Services.Api.InnerTubeSupport.LanguageOverride =
                ViewModels.SettingsViewModel.LoadLanguageSetting();
        }
        catch (Exception)
        {
            // Settings store unavailable: fall back to the account language.
        }

        // Compose the application host (logging + service registrations) before any UI is shown
        // so the container is ready to resolve the main window's dependencies.
        _host = AppHost.Build();
    }

    /// <summary>
    /// The application-wide dependency injection container. Resolve ViewModels and services from
    /// here (e.g. <c>App.Current.Services.GetRequiredService&lt;HomeViewModel&gt;()</c>).
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>The running <see cref="App"/> instance, for convenient access to <see cref="Services"/>.</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>
    /// The application shell window, once created. Used by features that need the window handle
    /// (HWND) for WinRT interop — e.g. the native share UI (<c>ShareInvoker</c>, Req 34.1).
    /// <see langword="null"/> before <see cref="OnLaunched"/> has run.
    /// </summary>
    internal Window? MainWindow => _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        // Apply the saved light/dark theme before showing the window (no flash of the wrong theme).
        ThemeManager.Apply();

        // Apply the saved preferred lyrics provider at startup (else it only takes effect once the
        // Settings page is opened).
        if (Services.GetService(typeof(Core.Services.Lyrics.ILyricsService)) is Core.Services.Lyrics.ILyricsService lyrics
            && KasetWin.Platform.Storage.AppData.Settings["lyrics.provider"] as string is { Length: > 0 } provider)
        {
            lyrics.PreferredProvider = provider;
        }

        _window.Activate();

        // Handle protocol activations that arrive while this instance is already running
        // (redirected kaset:// launches). Best-effort: a missing/locked-down lifecycle API must
        // never block the shell from coming up.
        try
        {
            AppInstance.GetCurrent().Activated += OnAppInstanceActivated;
        }
        catch
        {
            // App lifecycle activation events are unavailable; cold-start dispatch below still works.
        }

        // Cold-start protocol activation: the shell window now exists, so dispatch the launch URI.
        try
        {
            DispatchActivation(AppInstance.GetCurrent().GetActivatedEventArgs());
        }
        catch
        {
            // Never fail launch because the activation arguments could not be read/parsed.
        }
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments args)
    {
        // A second launch was redirected to us (see Program.RedirectToPrimaryInstance). Surface the
        // shell first — it may be hidden in the tray for background audio — then handle any kaset://
        // command the launch carried. This event arrives on a background thread; BringToForeground
        // marshals itself onto the UI thread.
        (_window as MainWindow)?.BringToForeground();
        DispatchActivation(args);
    }

    /// <summary>
    /// Parses a protocol activation and forwards the resulting command to the shell. Non-protocol
    /// activations and invalid/unknown <c>kaset://</c> URIs are ignored (no state change, Req 33.5).
    /// </summary>
    private void DispatchActivation(AppActivationArguments? args)
    {
        if (args is null || args.Kind != ExtendedActivationKind.Protocol)
        {
            return;
        }

        if (args.Data is not Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocolArgs)
        {
            return;
        }

        var command = KasetUriParser.Parse(protocolArgs.Uri?.OriginalString);
        if (command is null)
        {
            return; // Invalid/unknown URI — ignore without changing state (Req 33.5).
        }

        (_window as MainWindow)?.HandleProtocolCommand(command);
    }
}
