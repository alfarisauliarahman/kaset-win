using KasetWin.App.Auth;
using KasetWin.App.Hosting;
using KasetWin.App.ViewModels;
using KasetWin.App.Views;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Activation;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Auth;
using KasetWin.Core.Services.Localization;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Globalization;
using System.Linq;
using Windows.Foundation;
using Windows.System;

namespace KasetWin.App;

/// <summary>
/// Application shell window (Task 14.1, Req 1.4/1.5/16.1). Hosts the native Windows 11 Fluent
/// shell â€” Mica backdrop, a <see cref="NavigationView"/> sidebar + content <see cref="Frame"/>,
/// and the bottom <c>PlayerBar</c> â€” and owns the mount point for the hidden playback WebView2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hidden playback WebView2.</b> This window owns the mount for the app's single hidden
/// <see cref="PlaybackWebViewHost"/> element (Task 8.3). The host is resolved from DI and its
/// element inserted at child index 0 of <c>RootGrid</c> so its <c>CoreWebView2</c> can be created;
/// the element stays in the visual tree for the window's lifetime to keep background audio alive
/// (Req 1.1/1.4).
/// </para>
/// <para>
/// <b>Background audio (close â†’ hide).</b> The window intercepts <see cref="AppWindow.Closing"/>
/// and, unless the user explicitly quit, cancels the close and hides the window instead. The app
/// process and the hidden WebView2 stay alive so audio keeps playing in the background (Req 1.4).
/// An explicit Quit path (Ctrl+Q, see <see cref="QuitAsync"/>) releases the playback controller
/// (stops audio + tears down the WebView2) and then really closes the window (Req 1.5).
/// </para>
/// <para>
/// <b>Navigation.</b> A dedicated NavigationService (Task 14.2) does not exist yet, so the shell
/// navigates the content <see cref="Frame"/> directly. Concrete section pages (Tasks 14.3â€“14.10)
/// are built in parallel; the shell resolves them by full type name at runtime and falls back to
/// <see cref="PlaceholderPage"/> when a page is not present yet, so the build never breaks.
/// TODO (Task 14.2): route through the shared NavigationService once it lands.
/// </para>
/// <para>
/// <b>Login.</b> After the window is first activated, the shell evaluates
/// <see cref="IAuthService.CheckLoginStatusAsync"/> and presents <see cref="ILoginFlow"/> when the
/// session is logged-out or needs re-auth (Req 4.2/4.3/4.6).
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>Map of NavigationView item tag â†’ fully-qualified page type name (Tasks 14.3â€“14.10).</summary>
    private static readonly IReadOnlyDictionary<string, string> PageTypeNamesByTag = new Dictionary<string, string>
    {
        ["Home"] = "KasetWin.App.Views.HomePage",
        ["Explore"] = "KasetWin.App.Views.ExplorePage",
        ["Search"] = "KasetWin.App.Views.SearchPage",
        ["Library"] = "KasetWin.App.Views.LibraryPage",
        ["LikedSongs"] = "KasetWin.App.Views.LikedSongsPage",
        ["History"] = "KasetWin.App.Views.HistoryPage",
        ["Podcasts"] = "KasetWin.App.Views.PodcastsPage",
        ["Settings"] = "KasetWin.App.Views.SettingsPage",
    };

    private const int VolumeStep = 5;

    private readonly PlaybackWebViewHost _playbackHost;
    private readonly IPlayerService? _player;
    private readonly INetworkMonitor? _networkMonitor;
    private Controls.SidePanelController? _sidePanel;

    private bool _isQuitting;
    private bool _loginEvaluated;

    /// <summary>Set by the Playlists header's inline "+" so its click doesn't also toggle the expander.</summary>
    private bool _suppressPlaylistsToggleOnce;

    /// <summary>Startup retries for the sidebar playlists (the cookie jar races the first request).</summary>
    private int _sidebarPlaylistAttempts;

    /// <summary>Local search history (most recent first), persisted in app settings.</summary>
    private readonly System.Collections.Generic.List<string> _searchHistory = LoadSearchHistory();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _toastTimer;
    private TaskbarMediaControls? _taskbarControls;

    public MainWindow()
    {
        this.InitializeComponent();

        // Sidebar labels follow the app's content-language setting from the first frame.
        ApplySidebarLanguage();

        // TEMPORARY: route Core diagnostics to a log file so data-load issues can be traced.
        KasetWin.Core.Diag.Log = msg =>
        {
            try
            {
                var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "diag.log");
                System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never affect the app.
            }
        };
        KasetWin.Core.Diag.DumpSink = (name, content) =>
        {
            try
            {
                var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, name);
                System.IO.File.WriteAllText(path, content);
            }
            catch
            {
                // Diagnostics must never affect the app.
            }
        };


        // Mount the app-owned hidden playback WebView2 (Task 8.3). Resolving on this UI thread is
        // required because the host constructs a XAML WebView2 element. MUST be preserved. It is
        // placed in the content row (row 1) so it never affects the offline indicator's Auto row.
        _playbackHost = App.Current.Services.GetRequiredService<PlaybackWebViewHost>();
        RootGrid.Children.Insert(0, _playbackHost.Element);
        Grid.SetRow(_playbackHost.Element, 1);

        // Register the hidden mount with the floating-video controller (Task 19.1, Req 26.2) so it can
        // reparent the element into a VideoWindow on pop-out and return it here on pop-in. The element
        // is inserted at child index 0 / row 1 (mirrored in the call below).
        App.Current.Services.GetService<VideoWindowController>()?.AttachHomeMount(RootGrid, childIndex: 0, gridRow: 1);

        // Create the CoreWebView2 and attach it to the controller once the element is live.
        _ = _playbackHost.InitializeAsync();

        _player = App.Current.Services.GetService<IPlayerService>();

        // Subscribe to the app-wide in-app toast channel so any action (add to collection, like,
        // queue, …) shows a small pop near the sidebar that fades away.
        var notifier = App.Current.Services.GetService<Notifications.IInAppNotifier>();
        if (notifier is not null)
        {
            notifier.Shown += OnInAppNotification;
        }

        // Bind the shared navigation service to the content frame (Task 14.2, Req 16.1) so every
        // clickable affordance â€” including the bottom PlayerBar, which lives outside this frame â€”
        // can navigate to artist/album/playlist detail pages via NavigationHelper without taking a
        // direct Frame dependency.
        App.Current.Services.GetService<Navigation.INavigationService>()?.Initialize(ContentFrame);

        // Keep the NavigationView back button in sync with the content frame's back stack so detail
        // pages (album/artist/playlist/â€¦) can be backed out of. The button sits beside the pane
        // toggle (hamburger), which NavigationView renders by default, and BackRequested routes to
        // ContentFrame.GoBack().
        ContentFrame.Navigated += OnContentFrameNavigated;

        // Connectivity monitor backing the offline indicator (Req 35.2/35.3). Subscribe before the
        // monitor is started (in StartBackgroundControllers) so the initial publish is observed.
        _networkMonitor = App.Current.Services.GetService<INetworkMonitor>();
        if (_networkMonitor is not null)
        {
            _networkMonitor.ConnectivityChanged += OnConnectivityChanged;
        }

        // Right-hand now-playing side panel (queue / lyrics): observe the shared controller so the
        // player-bar buttons can slide the docked panel in and out.
        _sidePanel = App.Current.Services.GetService<Controls.SidePanelController>();
        if (_sidePanel is not null)
        {
            _sidePanel.Changed += OnSidePanelChanged;
        }

        RegisterKeyboardAccelerators();

        // Apply the UI language + flow direction once at construction (Req 19.1â€“19.3). Pure Core
        // policy picks the supported language (or English fallback); WinRT then overrides the app
        // language and the root flips to RTL for Arabic. Guarded so a missing/locked-down API never
        // blocks the shell from coming up.
        ApplyLanguageAndFlowDirection();

        // Primary-window sizing contract: pin a minimum size and open at the default (Req 37.8).
        MainWindowLayout.Configure(this);

        // Background-audio model: intercept the window close and hide instead of exiting (Req 1.4).
        AppWindow.Closing += OnAppWindowClosing;
        Activated += OnActivated;

        // Start in the Music source: hide the YouTube surfaces and select the music default. The
        // explicit SelectedItem below drives the toggle to Music; _sourceReady stays false until
        // after this so the startup selection only sets visibility (it must not navigate/override
        // the default page, which caused the shell to open on YouTube).
        ApplySourceVisibility(youtube: false);
        NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        MusicSourceItem.IsChecked = true;
        YouTubeSourceItem.IsChecked = false;
        AnimateSourceIndicator(youtube: false, animate: false);
        _sourceReady = true;
    }

    /// <summary>False until the shell finishes its initial source setup, so the source toggle's
    /// startup selection changes visibility without navigating over the default launch page.</summary>
    private bool _sourceReady;

    /// <summary>Whether the Podcasts tab has been revealed by the region-availability probe (Req 27).</summary>
    // Podcasts is shown by default (user request); the region check can still flip it off later.
    private bool _podcastsAvailable = true;

    /// <summary>Monotonic sequence for sidebar search suggestions, so stale results are dropped.</summary>
    private int _suggestSeq;

    // â”€â”€ Background audio: close â†’ hide; explicit Quit â†’ release + exit (Req 1.4/1.5) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isQuitting)
        {
            return; // genuine quit â€” let the window close.
        }

        // Keep the app + hidden WebView2 alive so audio continues in the background (Req 1.4).
        args.Cancel = true;
        AppWindow.Hide();
    }

    /// <summary>
    /// Explicit Quit (Req 1.5): releases the playback controller (stops audio + disposes the hidden
    /// WebView2 via the host) and then really closes the window so the process exits.
    /// </summary>
    private async Task QuitAsync()
    {
        _isQuitting = true;

        // Release the always-on background controllers (best-effort): stop track-change toasts and
        // unsubscribe from connectivity changes before tearing down playback.
        try
        {
            App.Current.Services.GetService<INotificationService>()?.Stop();
        }
        catch
        {
            // Never block shutdown on a notification teardown failure.
        }

        if (_networkMonitor is not null)
        {
            _networkMonitor.ConnectivityChanged -= OnConnectivityChanged;
        }

        try
        {
            await _playbackHost.DisposeAsync();
        }
        catch
        {
            // Best effort: never block shutdown on a teardown failure.
        }

        this.Close();
    }

    // â”€â”€ Login trigger after first activation (Req 4.2/4.3/4.6) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loginEvaluated || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _loginEvaluated = true;
        StartBackgroundControllers();
        _ = EvaluateLoginAsync();
        _ = RevealPodcastsTabIfAvailableAsync();
    }

    /// <summary>
    /// Resolves and starts the always-on background controllers once the shell is live: the system
    /// Now Playing / SMTC surface (Req 10), the network connectivity monitor (Req 35.2), and the
    /// track-change toast service (Req 35.1). All are optional and best-effort â€” a missing
    /// registration or platform failure must not break the shell. After the monitor starts, the
    /// offline indicator is seeded from its current state so an offline-at-launch device shows the
    /// indicator even when no change event fires.
    /// </summary>
    private void StartBackgroundControllers()
    {
        try
        {
            App.Current.Services.GetService<INowPlayingController>()?.Start();
        }
        catch
        {
            // Now Playing is non-essential to the shell; never block startup on an SMTC failure.
        }

        try
        {
            if (App.Current.Services.GetService<IPlayerService>() is { } player)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _taskbarControls = new TaskbarMediaControls(hwnd, player, DispatcherQueue);
                _taskbarControls.Start();
            }
        }
        catch
        {
            // Taskbar thumb-bar buttons are a nicety; never block startup on their failure.
        }

        try
        {
            if (_networkMonitor is not null)
            {
                _networkMonitor.Start();
                UpdateOfflineIndicator(_networkMonitor.IsConnected);
            }
        }
        catch
        {
            // Connectivity monitoring is best-effort; the app still works without it.
        }

        try
        {
            App.Current.Services.GetService<INotificationService>()?.Start();
        }
        catch
        {
            // Track-change toasts are non-essential; never block startup on a notification failure.
        }

        try
        {
            // Resolve the PlaybackArbiter so it constructs and begins enforcing a single audio
            // source between the music and YouTube video players (Req 32.3). Resolving (not just
            // registering) is required because the arbiter subscribes to both sources in its ctor.
            App.Current.Services.GetService<KasetWin.Core.Services.Player.PlaybackArbiter>();
        }
        catch
        {
            // Audio arbitration is part of YouTube mode; never block startup on it.
        }
    }

    /// <summary>
    /// Reflects the latest connectivity state into the offline indicator (Req 35.3). Raised by the
    /// monitor on a thread-pool thread, so the UI update is marshalled onto the dispatcher.
    /// </summary>
    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            UpdateOfflineIndicator(isConnected);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => UpdateOfflineIndicator(isConnected));
        }
    }

    /// <summary>Shows the offline InfoBar while disconnected and hides it when back online (Req 35.3).</summary>
    private void UpdateOfflineIndicator(bool isConnected) => OfflineInfoBar.IsOpen = !isConnected;

    /// <summary>
    /// Selects the UI language via the pure Core policy and applies it: overrides the WinRT primary
    /// language and flips the root <see cref="FrameworkElement.FlowDirection"/> to RTL for Arabic
    /// (Req 19.1â€“19.3). Wrapped in try/catch so an unavailable globalization API cannot prevent the
    /// window from showing.
    /// </summary>
    private void ApplyLanguageAndFlowDirection()
    {
        try
        {
            var lang = LanguageSelector.Select(CultureInfo.CurrentUICulture.Name, SupportedLanguages.All);
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
            RootGrid.FlowDirection = LayoutDirection.IsRtl(lang)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }
        catch
        {
            // Localization is best-effort; fall back to the default (English / LeftToRight).
        }
    }

    /// <summary>
    /// Region-aware Podcasts tab reveal (Req 27.1/27.2). Probes the <c>FEmusic_podcasts</c> surface
    /// once the shell is live; if the region supports it the hidden <c>PodcastsItem</c> nav item is
    /// shown, otherwise it stays collapsed. The probe is best-effort and fully guarded so a failure
    /// (network/auth/unavailable) never breaks the shell â€” a 404 region is mapped to "unavailable"
    /// by the client without throwing.
    /// </summary>
    private async Task RevealPodcastsTabIfAvailableAsync()
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null)
        {
            return;
        }

        try
        {
            var result = await client.GetPodcastsAsync();
            if (!result.IsAvailable)
            {
                return; // unsupported region (404): keep the tab hidden (Req 27.2).
            }

            _podcastsAvailable = true;

            // Only reveal it while the Music source is active; the source toggle re-applies this.
            void Reveal()
            {
                if (YouTubeSourceItem.IsChecked != true)
                {
                    PodcastsItem.Visibility = Visibility.Visible;
                }
            }

            if (DispatcherQueue.HasThreadAccess)
            {
                Reveal();
            }
            else
            {
                DispatcherQueue.TryEnqueue(Reveal);
            }
        }
        catch
        {
            // Best-effort: a probe failure (network/auth/transient) must never break the shell.
            // The Podcasts tab simply stays hidden until the next launch re-probes.
        }
    }

    private async Task EvaluateLoginAsync()
    {
        var auth = App.Current.Services.GetService<IAuthService>();
        if (auth is null)
        {
            return;
        }

        // Keep the footer Sign in / Account label in sync with the auth state.
        auth.PropertyChanged += (_, _) => UpdateSignInLabel();

        // Ensure the hidden playback WebView2 core exists before reading the session cookie: the
        // cookie source reads cookies from that core, so checking before it is created reports a
        // false "signed out" even when a valid session is persisted (the root cause of the sidebar
        // showing "Sign in" while Home already loads personalized content). InitializeAsync is
        // idempotent and gated, so awaiting it here is safe.
        try
        {
            await _playbackHost.InitializeAsync();
        }
        catch
        {
            // If the core cannot be created we still attempt the cookie read below (best-effort).
        }

        try
        {
            await auth.CheckLoginStatusAsync();
            if (auth.State == AuthState.LoggedOut || auth.NeedsReauth)
            {
                var login = App.Current.Services.GetService<ILoginFlow>();
                if (login is not null && Content?.XamlRoot is { } xamlRoot)
                {
                    await login.ShowAsync(xamlRoot);
                }
            }
        }
        catch
        {
            // A failed login evaluation must not crash the shell; public surfaces still work.
        }

        // Populate the footer with the account name + avatar when a session is present (also on a
        // persisted session at launch); otherwise this resets it to "Sign in".
        await RefreshAccountAsync();
    }
}
