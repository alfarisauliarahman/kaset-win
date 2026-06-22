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
using System.Globalization;
using Windows.Foundation;
using Windows.System;

namespace KasetWin.App;

/// <summary>
/// Application shell window (Task 14.1, Req 1.4/1.5/16.1). Hosts the native Windows 11 Fluent
/// shell — Mica backdrop, a <see cref="NavigationView"/> sidebar + content <see cref="Frame"/>,
/// and the bottom <c>PlayerBar</c> — and owns the mount point for the hidden playback WebView2.
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
/// <b>Background audio (close → hide).</b> The window intercepts <see cref="AppWindow.Closing"/>
/// and, unless the user explicitly quit, cancels the close and hides the window instead. The app
/// process and the hidden WebView2 stay alive so audio keeps playing in the background (Req 1.4).
/// An explicit Quit path (Ctrl+Q, see <see cref="QuitAsync"/>) releases the playback controller
/// (stops audio + tears down the WebView2) and then really closes the window (Req 1.5).
/// </para>
/// <para>
/// <b>Navigation.</b> A dedicated NavigationService (Task 14.2) does not exist yet, so the shell
/// navigates the content <see cref="Frame"/> directly. Concrete section pages (Tasks 14.3–14.10)
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
    /// <summary>Map of NavigationView item tag → fully-qualified page type name (Tasks 14.3–14.10).</summary>
    private static readonly IReadOnlyDictionary<string, string> PageTypeNamesByTag = new Dictionary<string, string>
    {
        ["Home"] = "KasetWin.App.Views.HomePage",
        ["Explore"] = "KasetWin.App.Views.ExplorePage",
        ["Search"] = "KasetWin.App.Views.SearchPage",
        ["Library"] = "KasetWin.App.Views.LibraryPage",
        ["History"] = "KasetWin.App.Views.HistoryPage",
        ["Podcasts"] = "KasetWin.App.Views.PodcastsPage",
        ["Settings"] = "KasetWin.App.Views.SettingsPage",
    };

    private const int VolumeStep = 5;

    private readonly PlaybackWebViewHost _playbackHost;
    private readonly IPlayerService? _player;
    private readonly INetworkMonitor? _networkMonitor;

    private bool _isQuitting;
    private bool _loginEvaluated;

    public MainWindow()
    {
        this.InitializeComponent();

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

        // Connectivity monitor backing the offline indicator (Req 35.2/35.3). Subscribe before the
        // monitor is started (in StartBackgroundControllers) so the initial publish is observed.
        _networkMonitor = App.Current.Services.GetService<INetworkMonitor>();
        if (_networkMonitor is not null)
        {
            _networkMonitor.ConnectivityChanged += OnConnectivityChanged;
        }

        RegisterKeyboardAccelerators();

        // Apply the UI language + flow direction once at construction (Req 19.1–19.3). Pure Core
        // policy picks the supported language (or English fallback); WinRT then overrides the app
        // language and the root flips to RTL for Arabic. Guarded so a missing/locked-down API never
        // blocks the shell from coming up.
        ApplyLanguageAndFlowDirection();

        // Background-audio model: intercept the window close and hide instead of exiting (Req 1.4).
        AppWindow.Closing += OnAppWindowClosing;
        Activated += OnActivated;

        // Select Home on first show.
        NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
    }

    // ── Navigation ────────────────────────────────────────────────────────────────────────────

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateToTag("Settings");
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            NavigateToTag(tag);
        }
    }

    /// <summary>
    /// Navigates the content frame to the real page registered for <paramref name="tag"/> when its
    /// type exists in the loaded assembly; otherwise shows the <see cref="PlaceholderPage"/> for the
    /// section so the shell keeps working while the page is built in parallel (Tasks 14.3–14.10).
    /// </summary>
    private void NavigateToTag(string tag)
    {
        // YouTube (full mode) surfaces (Req 32.1/32.4): the feed pages need a navigation parameter
        // describing which feed to render, so they are routed here rather than via the simple
        // tag→type map. The music surfaces below are untouched.
        if (tag.StartsWith("YouTube.", StringComparison.Ordinal))
        {
            NavigateYouTube(tag);
            return;
        }

        if (PageTypeNamesByTag.TryGetValue(tag, out var typeName)
            && Type.GetType(typeName) is { } pageType
            && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
            return;
        }

        // Avoid re-navigating to the same placeholder section.
        if (ContentFrame.Content is PlaceholderPage existing && (string?)existing.Tag == tag)
        {
            return;
        }

        ContentFrame.Navigate(typeof(PlaceholderPage), tag);
        if (ContentFrame.Content is PlaceholderPage page)
        {
            page.Tag = tag;
        }
    }

    /// <summary>
    /// Routes a YouTube (full mode) nav tag to its page (Req 32.1/32.4). Home and Shorts are
    /// parameterless pages; Subscriptions, History, and Explore share the reusable
    /// <c>YouTubeFeedPage</c> and receive a <see cref="YouTubeFeedRequest"/> describing the feed.
    /// </summary>
    private void NavigateYouTube(string tag)
    {
        switch (tag)
        {
            case "YouTube.Home":
                NavigateYouTubePage("KasetWin.App.Views.YouTubeHomePage", parameter: null);
                break;

            case "YouTube.Shorts":
                NavigateYouTubePage("KasetWin.App.Views.YouTubeShortsPage", parameter: null);
                break;

            case "YouTube.Subscriptions":
                NavigateYouTubePage(
                    "KasetWin.App.Views.YouTubeFeedPage",
                    new YouTubeFeedRequest(YouTubeFeedKind.Subscriptions));
                break;

            case "YouTube.History":
                NavigateYouTubePage(
                    "KasetWin.App.Views.YouTubeFeedPage",
                    new YouTubeFeedRequest(YouTubeFeedKind.History));
                break;

            case "YouTube.Explore":
                NavigateYouTubePage(
                    "KasetWin.App.Views.YouTubeFeedPage",
                    new YouTubeFeedRequest(YouTubeFeedKind.Destination, YouTubeDestination.Gaming));
                break;
        }
    }

    private void NavigateYouTubePage(string pageTypeName, object? parameter)
    {
        if (Type.GetType(pageTypeName) is { } pageType)
        {
            ContentFrame.Navigate(pageType, parameter);
        }
    }

    // ── Protocol activation dispatch (Task 26.1, Req 33.1–33.5) ─────────────────────────────────

    private const string PlaylistPageTypeName = "KasetWin.App.Views.PlaylistPage";
    private const string AlbumPageTypeName = "KasetWin.App.Views.AlbumPage";
    private const string ArtistPageTypeName = "KasetWin.App.Views.ArtistPage";

    /// <summary>
    /// Acts on a parsed <c>kaset://</c> command (Req 33.1–33.4): a song plays immediately via
    /// <see cref="IPlayerService"/>; a playlist/album/artist navigates the shell to its detail page.
    /// Safe to call from any thread and at any time — work is marshalled onto the UI thread, and the
    /// window is brought to the foreground first (it may be hidden for background audio, Req 1.4).
    /// Invalid commands never reach here; the parser already dropped them (Req 33.5).
    /// </summary>
    public void HandleProtocolCommand(KasetUriCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (DispatcherQueue.HasThreadAccess)
        {
            DispatchProtocolCommand(command);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => DispatchProtocolCommand(command));
        }
    }

    private void DispatchProtocolCommand(KasetUriCommand command)
    {
        // Surface the window in case it was hidden (background-audio close → hide, Req 1.4).
        try
        {
            AppWindow.Show();
            this.Activate();
        }
        catch
        {
            // Foregrounding is best-effort; still perform the requested action below.
        }

        switch (command.Kind)
        {
            case KasetUriKind.Play:
                _ = _player?.PlayAsync(command.Id);
                break;

            case KasetUriKind.Playlist:
                NavigateToDetail(PlaylistPageTypeName, command.Id);
                break;

            case KasetUriKind.Album:
                NavigateToDetail(AlbumPageTypeName, command.Id);
                break;

            case KasetUriKind.Artist:
                NavigateToDetail(ArtistPageTypeName, command.Id);
                break;
        }
    }

    /// <summary>
    /// Navigates the content frame to a detail page resolved by full type name, passing
    /// <paramref name="id"/> as the navigation parameter. Uses the same late-bind
    /// <see cref="Type.GetType(string)"/> guard the shell/<c>FeedNavigation</c> use, so a not-yet-built
    /// page is a no-op rather than a crash.
    /// </summary>
    private void NavigateToDetail(string pageTypeName, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (Type.GetType(pageTypeName) is { } pageType)
        {
            ContentFrame.Navigate(pageType, id);
        }
    }

    // ── Keyboard accelerators (Req 16.1 shell UX) ───────────────────────────────────────────────

    /// <summary>
    /// Registers Ctrl-based accelerators that avoid clobbering standard Windows shortcuts:
    /// Ctrl+F (Search), Space (play/pause), Ctrl+Right/Left (next/prev), Ctrl+Up/Down (volume),
    /// Ctrl+, (Settings) and Ctrl+Q (explicit Quit).
    /// </summary>
    private void RegisterKeyboardAccelerators()
    {
        void Add(VirtualKeyModifiers modifiers, VirtualKey key, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
        {
            var accelerator = new KeyboardAccelerator { Modifiers = modifiers, Key = key };
            accelerator.Invoked += handler;
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }

        Add(VirtualKeyModifiers.Control, VirtualKey.F, OnSearchAccelerator);
        Add(VirtualKeyModifiers.None, VirtualKey.Space, OnPlayPauseAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Right, OnNextAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Left, OnPreviousAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Up, OnVolumeUpAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Down, OnVolumeDownAccelerator);
        // OEM comma (',') has no named VirtualKey member; its virtual-key code is 188.
        Add(VirtualKeyModifiers.Control, (VirtualKey)188, OnSettingsAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Q, OnQuitAccelerator);
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectMenuItemByTag("Search");
        args.Handled = true;
    }

    private void OnPlayPauseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Do not hijack Space while the user is typing or focused on an activatable control.
        if (IsTextInputFocused())
        {
            args.Handled = false;
            return;
        }

        _ = _player?.TogglePlayPauseAsync();
        args.Handled = true;
    }

    private void OnNextAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = _player?.NextAsync();
        args.Handled = true;
    }

    private void OnPreviousAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = _player?.PreviousAsync();
        args.Handled = true;
    }

    private void OnVolumeUpAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_player is not null)
        {
            _player.SetVolume(_player.Volume + VolumeStep);
        }

        args.Handled = true;
    }

    private void OnVolumeDownAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_player is not null)
        {
            _player.SetVolume(_player.Volume - VolumeStep);
        }

        args.Handled = true;
    }

    private void OnSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (NavView.SettingsItem is NavigationViewItem settings)
        {
            NavView.SelectedItem = settings;
        }
        else
        {
            NavigateToTag("Settings");
        }

        args.Handled = true;
    }

    private void OnQuitAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = QuitAsync();
    }

    private void SelectMenuItemByTag(string tag)
    {
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        return focused is TextBox or AutoSuggestBox or PasswordBox or RichEditBox or ButtonBase;
    }

    // ── Background audio: close → hide; explicit Quit → release + exit (Req 1.4/1.5) ────────────

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isQuitting)
        {
            return; // genuine quit — let the window close.
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

    // ── Login trigger after first activation (Req 4.2/4.3/4.6) ──────────────────────────────────

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
    /// track-change toast service (Req 35.1). All are optional and best-effort — a missing
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
    /// (Req 19.1–19.3). Wrapped in try/catch so an unavailable globalization API cannot prevent the
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
    /// (network/auth/unavailable) never breaks the shell — a 404 region is mapped to "unavailable"
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

            if (DispatcherQueue.HasThreadAccess)
            {
                PodcastsItem.Visibility = Visibility.Visible;
            }
            else
            {
                DispatcherQueue.TryEnqueue(() => PodcastsItem.Visibility = Visibility.Visible);
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
    }
}
