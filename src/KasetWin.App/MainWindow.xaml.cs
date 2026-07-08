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

    /// <summary>
    /// Renders an in-app notification in the InfoBar above the account footer. Plain messages
    /// auto-dismiss after a few seconds; actionable ones stay open (with an action button and the
    /// built-in close X) until the user acts or dismisses. Marshals to the UI thread.
    /// </summary>
    private void OnInAppNotification(Notifications.InAppNotification n)
    {
        var queue = this.DispatcherQueue;
        if (queue is null)
        {
            return;
        }

        queue.TryEnqueue(() =>
        {
            _toastTimer?.Stop();

            ToastBar.Title = n.Title ?? string.Empty;
            ToastBar.Message = n.Message;
            ToastBar.Severity = InfoBarSeverity.Informational;

            if (n.IsActionable)
            {
                var button = new Button { Content = n.ActionText };
                button.Click += (_, _) =>
                {
                    ToastBar.IsOpen = false;
                    n.OnAction!.Invoke();
                };
                ToastBar.ActionButton = button;
                ToastBar.IsOpen = true;
                // Actionable notifications persist until the user acts or closes them.
            }
            else
            {
                ToastBar.ActionButton = null;
                ToastBar.IsOpen = true;

                _toastTimer ??= queue.CreateTimer();
                _toastTimer.Interval = TimeSpan.FromSeconds(5);
                _toastTimer.IsRepeating = false;
                _toastTimer.Tick -= OnToastTimerTick;
                _toastTimer.Tick += OnToastTimerTick;
                _toastTimer.Start();
            }
        });
    }

    private void OnToastTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ToastBar.IsOpen = false;
    }

    /// <summary>Slides the docked now-playing side panel in/out as the shared controller mode changes.</summary>
    private void OnSidePanelChanged()
    {
        var open = _sidePanel?.Mode is not (null or Controls.SidePanelMode.None);
        SidePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SidePanelColumn.Width = open ? new GridLength(380) : new GridLength(0);
    }

    public MainWindow()
    {
        this.InitializeComponent();

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
        SourceSelector.SelectedItem = MusicSourceItem;
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

    // â”€â”€ Source toggle (Music â‡„ YouTube) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnSourceSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var youtube = ReferenceEquals(sender.SelectedItem, YouTubeSourceItem);
        ApplySourceVisibility(youtube);

        // Only a real user toggle navigates; the startup selection just sets visibility so it does
        // not override the default launch page.
        if (_sourceReady)
        {
            NavigateToTag(youtube ? "YouTube.Home" : "Home");
        }
    }

    /// <summary>
    /// Shows only the selected source's sidebar items: the YouTube header + <c>YouTube.*</c> items
    /// for the YouTube source, or the music surfaces otherwise. Podcasts (a music surface) stays
    /// hidden unless its region probe revealed it.
    /// </summary>
    private void ApplySourceVisibility(bool youtube)
    {
        foreach (var item in NavView.MenuItems)
        {
            switch (item)
            {
                case NavigationViewItemHeader:
                    // The only header is the "YouTube" one.
                    ((NavigationViewItemHeader)item).Visibility = youtube ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case NavigationViewItem { Tag: "Podcasts" } podcasts:
                    podcasts.Visibility = !youtube && _podcastsAvailable ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case NavigationViewItem navItem:
                    var isYouTube = navItem.Tag is string tag
                        && tag.StartsWith("YouTube.", StringComparison.Ordinal);
                    navItem.Visibility = isYouTube == youtube ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }
    }

    // â”€â”€ Navigation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    /// Routes the NavigationView back button (rendered beside the pane toggle) to the content
    /// frame's back stack (Req 16.1). Guarded on <see cref="Frame.CanGoBack"/> so a request with an
    /// empty stack is a no-op.
    /// </summary>
    private void OnNavigationBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    /// <summary>
    /// Keeps the back button enabled state in sync with the content frame's back stack after every
    /// navigation (forward navigations push detail pages onto the stack; GoBack pops them).
    /// </summary>
    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        NavView.IsBackEnabled = ContentFrame.CanGoBack;
    }

    /// <summary>
    /// Handles invocation of non-navigating footer actions â€” currently the Sign in / Account item
    /// (Req 4.2), which opens the interactive Google sign-in flow instead of navigating a page.
    /// </summary>
    /// <summary>The signed-in account (name + avatar) shown on the footer item, or <c>null</c> when signed out.</summary>
    private UserAccount? _currentAccount;

    /// <summary>The signed-in account, exposed for pages that fall back to the user's own avatar.</summary>
    internal UserAccount? CurrentAccount => _currentAccount;

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // ── Playlists tree ────────────────────────────────────────────────────────────────────
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "PlaylistsRoot" } root)
        {
            // NavigationView itself already toggles expansion for a click anywhere on the row;
            // toggling again here cancelled that out, so only the chevron seemed to work. Now the
            // handler only undoes the toggle caused by an inline "+" click.
            if (_suppressPlaylistsToggleOnce)
            {
                _suppressPlaylistsToggleOnce = false;
                root.IsExpanded = !root.IsExpanded;
            }

            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem { Tag: "AllPlaylists" })
        {
            if (Navigation.NavigationHelper.ResolvePageType(PageTypeNamesByTag["Library"]) is { } libraryType)
            {
                ContentFrame.Navigate(libraryType);
            }

            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem { Tag: string playlistTag }
            && playlistTag.StartsWith("Playlist:", StringComparison.Ordinal))
        {
            Navigation.NavigationHelper.NavigateToPlaylist(playlistTag["Playlist:".Length..]);
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem { Tag: "SignIn" } item)
        {
            var auth = App.Current.Services.GetService<IAuthService>();
            if (auth is { State: AuthState.LoggedIn })
            {
                // Already signed in: show the account card instead of re-opening the Google login
                // dialog. Re-opening it caused the flyout to flash openâ†’auto-close repeatedly (the
                // dialog detects the live session and closes itself immediately).
                ShowAccountFlyout(item);
            }
            else
            {
                _ = SignInAsync();
            }
        }
    }

    // ── Sidebar: search-as-you-type ───────────────────────────────────────────────────────────────

    private async void OnSidebarSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var text = sender.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            // Empty box: surface the local search history (deletable via the row's X).
            sender.ItemsSource = _searchHistory
                .Select(q => new SearchSuggestion(q, IsHistory: true))
                .ToList();
            return;
        }

        var seq = ++_suggestSeq;
        try
        {
            var client = App.Current.Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return;
            }

            var suggestions = await client.GetRichSearchSuggestionsAsync(text);
            if (seq == _suggestSeq)
            {
                sender.ItemsSource = suggestions;
            }
        }
        catch (Exception)
        {
            // Suggestions are best-effort; typing must never surface an error.
        }
    }

    private void OnSidebarSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var suggestion = args.ChosenSuggestion as SearchSuggestion;
        var query = suggestion?.Query ?? args.QueryText;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        RememberSearch(query);

        // Rich rows navigate straight to their entity (artist/album/playlist) or play the song;
        // plain completions land on the full search-results page.
        if (suggestion is { BrowseId: { } browseId })
        {
            var handled = suggestion.PageType switch
            {
                "MUSIC_PAGE_TYPE_ARTIST" => Navigation.NavigationHelper.NavigateToArtist(browseId),
                "MUSIC_PAGE_TYPE_ALBUM" => Navigation.NavigationHelper.NavigateToAlbum(browseId),
                "MUSIC_PAGE_TYPE_PLAYLIST" => Navigation.NavigationHelper.NavigateToPlaylist(browseId),
                _ => false,
            };
            if (handled)
            {
                return;
            }
        }

        if (suggestion is { VideoId: { } videoId })
        {
            _ = OpenSuggestedSongAsync(videoId);
            return;
        }

        if (Navigation.NavigationHelper.ResolvePageType(PageTypeNamesByTag["Search"]) is { } searchType)
        {
            ContentFrame.Navigate(searchType, query);
        }
    }

    /// <summary>A rich SONG suggestion goes to its album page when one is known; otherwise it plays.</summary>
    private async Task OpenSuggestedSongAsync(string videoId)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        try
        {
            if (client is not null)
            {
                var metadata = await client.GetSongMetadataAsync(videoId);
                if (Navigation.NavigationHelper.NavigateToSongAlbum(metadata.Song))
                {
                    return;
                }

                if (metadata.Song is { } song)
                {
                    await (_player?.PlayCollectionAsync([song], startIndex: 0) ?? Task.CompletedTask);
                }
            }
        }
        catch (Exception)
        {
            // Best-effort; a failed lookup simply does nothing.
        }
    }

    // ── Search history (local, persisted) ────────────────────────────────────────────────────────

    private const string SearchHistoryKey = "SearchHistory";
    private const int SearchHistoryLimit = 10;

    private static System.Collections.Generic.List<string> LoadSearchHistory()
    {
        try
        {
            var raw = Windows.Storage.ApplicationData.Current.LocalSettings.Values[SearchHistoryKey] as string;
            return string.IsNullOrEmpty(raw)
                ? []
                : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(raw) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void SaveSearchHistory()
    {
        try
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[SearchHistoryKey] =
                System.Text.Json.JsonSerializer.Serialize(_searchHistory);
        }
        catch (Exception)
        {
            // History is a convenience; persistence failures are ignored.
        }
    }

    private void RememberSearch(string query)
    {
        _searchHistory.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        _searchHistory.Insert(0, query);
        if (_searchHistory.Count > SearchHistoryLimit)
        {
            _searchHistory.RemoveRange(SearchHistoryLimit, _searchHistory.Count - SearchHistoryLimit);
        }

        SaveSearchHistory();
    }

    /// <summary>The X on a history row: remove the entry and refresh the open suggestion list.</summary>
    private void OnDeleteHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchSuggestion { IsHistory: true } entry })
        {
            _searchHistory.RemoveAll(q => string.Equals(q, entry.Query, StringComparison.OrdinalIgnoreCase));
            SaveSearchHistory();
            SidebarSearchBox.ItemsSource = _searchHistory
                .Select(q => new SearchSuggestion(q, IsHistory: true))
                .ToList();
        }
    }

    // ── Sidebar: playlists tree + create dialog ───────────────────────────────────────────────────

    /// <summary>Fills the Playlists nav item's children: All Playlists / new playlist / each playlist.</summary>
    private async Task LoadSidebarPlaylistsAsync()
    {
        System.Collections.Generic.IReadOnlyList<Playlist> playlists;
        try
        {
            var client = App.Current.Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return;
            }

            // Same landing source as the Library page (the dedicated liked_playlists browse parses
            // empty), but UNCACHED: an early anonymous-empty response must not poison the 5-minute
            // library cache — and retries must be able to see fresh data.
            if (client is not YTMusicClient concrete)
            {
                return;
            }

            var raw = await concrete.BrowseRawAsync("FEmusic_library_landing");
            playlists = Core.Services.Api.Parsers.LibraryContentParser.Parse(raw).Playlists;
        }
        catch (Exception ex)
        {
            KasetWin.Core.Diag.Write($"sidebar-playlists FAILED: {ex.GetType().Name}: {ex.Message}");
            return; // Signed out / offline: keep the static children only.
        }

        KasetWin.Core.Diag.Write($"sidebar-playlists count={playlists.Count}");

        // Startup races the WebView2 cookie jar: an early request comes back anonymous (empty
        // library) without failing. Retry a few times with a delay until real data arrives.
        if (playlists.Count == 0 && _sidebarPlaylistAttempts < 5)
        {
            _sidebarPlaylistAttempts++;
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                await LoadSidebarPlaylistsAsync();
            });
            return;
        }

        _sidebarPlaylistAttempts = 0;

        DispatcherQueue.TryEnqueue(() =>
        {
            PlaylistsNavItem.MenuItems.Clear();

            PlaylistsNavItem.MenuItems.Add(new NavigationViewItem
            {
                Content = "All Playlists",
                Tag = "AllPlaylists",
                SelectsOnInvoked = false,
                Icon = new FontIcon { Glyph = "" },
            });

            foreach (var playlist in playlists)
            {
                if (string.IsNullOrEmpty(playlist.Id))
                {
                    continue;
                }

                var coverPath = PlaylistCoverStore.Get(playlist.Id);
                // YT serves a generic gstatic placeholder for cover-less playlists; treat it as "no cover".
                var remote = playlist.ThumbnailUrl is { } t && !t.Host.Contains("gstatic", StringComparison.OrdinalIgnoreCase) ? t : null;
                var thumb = coverPath is not null ? new Uri(coverPath) : remote;
                PlaylistsNavItem.MenuItems.Add(new NavigationViewItem
                {
                    Content = playlist.Title,
                    Tag = $"Playlist:{playlist.Id}",
                    SelectsOnInvoked = false,
                    ContextFlyout = BuildPlaylistContextMenu(playlist),
                    // Prefer the playlist cover (local override first) over a generic glyph.
                    Icon = thumb is not null
                        ? new ImageIcon { Source = new BitmapImage(thumb) }
                        : new FontIcon { Glyph = "" },
                });
            }
        });
    }

    /// <summary>
    /// Apple-Music-style cover picker: an accent-outlined rounded square with a centred ⊕ that opens
    /// a file picker; the chosen image previews inside and its path is reported to the caller.
    /// </summary>
    private Button MakeCoverPicker(string? existingCoverPath, Action<string> onPicked)
    {
        var coverImage = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        if (existingCoverPath is not null)
        {
            coverImage.Source = new BitmapImage(new Uri(existingCoverPath));
        }

        // Plain "+" (no circle), accent-coloured, centred - matching the Apple Music mock.
        var plus = new FontIcon
        {
            Glyph = "",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        };

        var button = new Button
        {
            Width = 150,
            Height = 150,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            Content = new Grid { Children = { coverImage, plus } },
        };

        button.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                onPicked(file.Path);
                coverImage.Source = new BitmapImage(new Uri(file.Path));
            }
        };

        return button;
    }

    /// <summary>Right-click menu for a sidebar playlist (mirrors YT Music's playlist menu).</summary>
    private MenuFlyout BuildPlaylistContextMenu(Playlist playlist)
    {
        var menu = new MenuFlyout();

        void Add(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Putar acak", "", () => _ = PlaylistActionAsync(playlist.Id, shuffle: true));
        Add("Mulai mix", "", () => _ = PlaylistMixAsync(playlist.Id));
        Add("Edit playlist", "", () => _ = ShowEditPlaylistDialogAsync(playlist.Id));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Putar setelah ini", "", () => _ = PlaylistQueueAsync(playlist.Id, playNext: true));
        Add("Tambahkan ke antrean", "", () => _ = PlaylistQueueAsync(playlist.Id, playNext: false));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Bagikan", "", () =>
        {
            if (Core.Services.Sharing.ShareUrlBuilder.TryCreate(playlist) is { } target)
            {
                Sharing.ShareInvoker.TryShow(this, target);
            }
        });
        Add("Hapus playlist", "", () => _ = DeletePlaylistWithConfirmAsync(playlist));

        return menu;
    }

    private async Task PlaylistActionAsync(string playlistId, bool shuffle)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null || _player is null)
        {
            return;
        }

        try
        {
            var detail = await client.GetPlaylistAsync(playlistId);
            var tracks = detail.Tracks.ToList();
            if (shuffle)
            {
                // Deterministic-enough client shuffle for "Putar acak".
                var rng = new Random();
                tracks = [.. tracks.OrderBy(_ => rng.Next())];
            }

            if (tracks.Count > 0)
            {
                await _player.PlayCollectionAsync(tracks, startIndex: 0);
            }
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Gagal memutar: {ex.Message}");
        }
    }

    private async Task PlaylistMixAsync(string playlistId)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null || _player is null)
        {
            return;
        }

        try
        {
            var mix = await client.GetMixQueueAsync(playlistId);
            if (mix.Songs.Count > 0)
            {
                await _player.PlayCollectionAsync(mix.Songs, startIndex: 0);
            }
            else
            {
                App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show("Mix belum tersedia untuk playlist ini.");
            }
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Gagal memulai mix: {ex.Message}");
        }
    }

    private async Task PlaylistQueueAsync(string playlistId, bool playNext)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        var queue = App.Current.Services.GetService<Core.Services.Player.IQueueService>();
        if (client is null || queue is null)
        {
            return;
        }

        try
        {
            var detail = await client.GetPlaylistAsync(playlistId);
            var added = playNext ? queue.InsertNext(detail.Tracks) : queue.AppendDeduplicated(detail.Tracks);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(
                added == 0 ? "Semua lagu sudah ada di antrean." : $"{added} lagu {(playNext ? "diputar setelah ini" : "ditambahkan ke antrean")}.");
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Gagal: {ex.Message}");
        }
    }

    private async Task DeletePlaylistWithConfirmAsync(Playlist playlist)
    {
        var confirm = new ContentDialog
        {
            Title = "Hapus playlist",
            Content = $"Hapus \"{playlist.Title}\" secara permanen?",
            PrimaryButtonText = "Hapus",
            CloseButtonText = "Batal",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var client = App.Current.Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return;
            }

            await client.DeletePlaylistAsync(playlist.Id);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"\"{playlist.Title}\" dihapus.");
            _ = LoadSidebarPlaylistsAsync();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Gagal menghapus: {ex.Message}");
        }
    }

    /// <summary>
    /// "Edit playlist" dialog with UMUM / KOLABORASI tabs (mirrors YT Music): title, description,
    /// privacy (persisted via the edit API); voting + collaboration are UI-only until an API exists.
    /// </summary>
    internal async Task ShowEditPlaylistDialogAsync(string playlistId)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null)
        {
            return;
        }

        string currentTitle = "";
        string currentDescription = "";
        try
        {
            var detail = await client.GetPlaylistAsync(playlistId);
            currentTitle = detail.Playlist.Title;
            currentDescription = detail.Playlist.Description ?? "";
        }
        catch (Exception)
        {
            // Editable fields simply start blank when the lookup fails.
        }

        var titleBox = new TextBox { Header = "Judul", Text = currentTitle };
        var descriptionBox = new TextBox { Header = "Deskripsi", Text = currentDescription, AcceptsReturn = true };

        static ComboBoxItem TwoLine(string name, string description, object? tag = null)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7 });
            return new ComboBoxItem { Content = panel, Tag = tag };
        }

        var privacyBox = new ComboBox { Header = "Privasi", HorizontalAlignment = HorizontalAlignment.Stretch };
        privacyBox.Items.Add(TwoLine("Publik", "Dapat ditemukan dan dilihat siapa pun", PlaylistPrivacy.Public));
        privacyBox.Items.Add(TwoLine("Tidak publik", "Hanya dapat dilihat orang yang tahu linknya", PlaylistPrivacy.Unlisted));
        privacyBox.Items.Add(TwoLine("Pribadi", "Hanya dapat dilihat oleh Anda", PlaylistPrivacy.Private));

        var votingBox = new ComboBox { Header = "Pemungutan suara", HorizontalAlignment = HorizontalAlignment.Stretch };
        votingBox.Items.Add(TwoLine("Semua orang", "Semua orang dapat memberikan suara"));
        votingBox.Items.Add(TwoLine("Khusus kolaborator", "Hanya kolaborator yang dapat memberikan suara"));
        votingBox.Items.Add(TwoLine("Pemungutan suara nonaktif", "Tidak ada yang dapat memberikan suara"));
        votingBox.SelectedIndex = 2;

        string? pickedCoverPath = null;
        var editCoverButton = MakeCoverPicker(PlaylistCoverStore.Get(playlistId), path => pickedCoverPath = path);

        var umum = new StackPanel { Spacing = 12 };
        umum.Children.Add(editCoverButton);
        umum.Children.Add(titleBox);
        umum.Children.Add(descriptionBox);
        umum.Children.Add(privacyBox);
        umum.Children.Add(votingBox);

        var collabSwitch = new ToggleSwitch { Header = "Kolaborasi", OffContent = "Nonaktif", OnContent = "Kolaborator dapat menambahkan video" };
        var copyLink = new Button { Content = "Salin link undangan" };
        copyLink.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText($"https://music.youtube.com/playlist?list={playlistId.TrimStart('V', 'L')}&feature=share");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show("Link undangan disalin.");
        };
        var kolaborasi = new StackPanel { Spacing = 12 };
        kolaborasi.Children.Add(collabSwitch);
        kolaborasi.Children.Add(copyLink);

        // Pivot headers default to a huge font; use small semibold tab labels instead.
        static TextBlock Tab(string text) => new()
        {
            Text = text,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };

        var pivot = new Pivot { MinWidth = 400 };
        pivot.Items.Add(new PivotItem { Header = Tab("UMUM"), Content = umum });
        pivot.Items.Add(new PivotItem { Header = Tab("KOLABORASI"), Content = kolaborasi });

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(currentTitle) ? "Edit playlist" : currentTitle,
            Content = pivot,
            PrimaryButtonText = "Simpan",
            CloseButtonText = "Batal",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };

        titleBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(titleBox.Text);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(titleBox.Text))
        {
            return;
        }

        try
        {
            var privacy = (privacyBox.SelectedItem as ComboBoxItem)?.Tag as PlaylistPrivacy?;
            await client.EditPlaylistMetadataAsync(playlistId, titleBox.Text.Trim(), descriptionBox.Text, privacy);
            if (pickedCoverPath is not null)
            {
                PlaylistCoverStore.Set(playlistId, pickedCoverPath);
            }

            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show("Playlist diperbarui.");
            _ = LoadSidebarPlaylistsAsync();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Gagal menyimpan: {ex.Message}");
        }
    }

    /// <summary>The inline "+" on the Playlists header row opens the create dialog (and suppresses
    /// the row's expand toggle for this click, which also fires ItemInvoked).</summary>
    private void OnNewPlaylistClick(object sender, RoutedEventArgs e)
    {
        _suppressPlaylistsToggleOnce = true;
        _ = ShowCreatePlaylistDialogAsync();
    }

    /// <summary>
    /// "Playlist baru" dialog (judul + deskripsi + privasi + kolaborasi), creating via the API and
    /// refreshing the sidebar tree; when collaboration is on the Kolaborasi dialog follows.
    /// </summary>
    private async Task ShowCreatePlaylistDialogAsync()
    {
        var titleBox = new TextBox { Header = "Judul", PlaceholderText = "Judul playlist" };
        var descriptionBox = new TextBox { Header = "Deskripsi", PlaceholderText = "Deskripsi (opsional)", AcceptsReturn = true };

        // Cover picker (stored locally — no cover-upload endpoint has been found in InnerTube).
        string? pickedCoverPath = null;
        var coverButton = MakeCoverPicker(existingCoverPath: null, path => pickedCoverPath = path);

        static ComboBoxItem PrivacyOption(string name, string description, PlaylistPrivacy value)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7 });
            return new ComboBoxItem { Content = panel, Tag = value };
        }

        var privacyBox = new ComboBox
        {
            Header = "Privasi",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        privacyBox.Items.Add(PrivacyOption("Publik", "Dapat ditemukan dan dilihat siapa pun", PlaylistPrivacy.Public));
        privacyBox.Items.Add(PrivacyOption("Tidak publik", "Hanya dapat dilihat orang yang tahu linknya", PlaylistPrivacy.Unlisted));
        privacyBox.Items.Add(PrivacyOption("Pribadi", "Hanya dapat dilihat oleh Anda", PlaylistPrivacy.Private));
        privacyBox.SelectedIndex = 2;

        var collabSwitch = new ToggleSwitch { Header = "Kolaborasi", OffContent = "Nonaktif", OnContent = "Aktif" };

        var panelRoot = new StackPanel { MinWidth = 380, Spacing = 12 };
        panelRoot.Children.Add(coverButton);
        panelRoot.Children.Add(titleBox);
        panelRoot.Children.Add(descriptionBox);
        panelRoot.Children.Add(privacyBox);
        panelRoot.Children.Add(collabSwitch);

        var dialog = new ContentDialog
        {
            Title = "Playlist baru",
            Content = panelRoot,
            PrimaryButtonText = "Buat",
            CloseButtonText = "Batal",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false, // judul wajib diisi
            XamlRoot = RootGrid.XamlRoot,
        };

        titleBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(titleBox.Text);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(titleBox.Text))
        {
            return;
        }

        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null)
        {
            return;
        }

        try
        {
            var privacy = (privacyBox.SelectedItem as ComboBoxItem)?.Tag is PlaylistPrivacy p ? p : PlaylistPrivacy.Private;
            var playlistId = await client.CreatePlaylistAsync(
                titleBox.Text.Trim(),
                string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim(),
                privacy,
                videoIds: null);

            if (pickedCoverPath is not null)
            {
                PlaylistCoverStore.Set(playlistId, pickedCoverPath);
            }

            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Playlist \"{titleBox.Text.Trim()}\" dibuat.");
            _ = LoadSidebarPlaylistsAsync();

            if (collabSwitch.IsOn)
            {
                await ShowCollaborationDialogAsync(playlistId);
            }
        }
        catch (Core.Errors.KasetError ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show($"Gagal membuat playlist: {ex.Message}");
        }
    }

    /// <summary>Kolaborasi dialog: collaboration switches + invite-link copy + the owner row.</summary>
    private async Task ShowCollaborationDialogAsync(string playlistId)
    {
        var collabSwitch = new ToggleSwitch { Header = "Kolaborasi", IsOn = true, OnContent = "Kolaborator dapat menambahkan video", OffContent = "Nonaktif" };
        var newCollabSwitch = new ToggleSwitch { Header = "Izinkan kolaborator baru", IsOn = true, OnContent = "Aktif", OffContent = "Nonaktif" };

        var copyButton = new Button { Content = "Salin link undangan" };
        copyButton.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText($"https://music.youtube.com/playlist?list={playlistId.TrimStart('V', 'L')}&feature=share");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show("Link undangan disalin.");
        };

        var owner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        owner.Children.Add(new PersonPicture
        {
            Width = 36,
            Height = 36,
            DisplayName = _currentAccount?.Name,
            ProfilePicture = _currentAccount?.AvatarUrl is { } avatarUrl ? new BitmapImage(avatarUrl) : null,
        });
        var ownerText = new StackPanel();
        ownerText.Children.Add(new TextBlock { Text = _currentAccount?.Name ?? "Akun", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        ownerText.Children.Add(new TextBlock { Text = "Pemilik", FontSize = 12, Opacity = 0.7 });
        owner.Children.Add(ownerText);

        var panel = new StackPanel { MinWidth = 380, Spacing = 12 };
        panel.Children.Add(collabSwitch);
        panel.Children.Add(newCollabSwitch);
        panel.Children.Add(copyButton);
        panel.Children.Add(owner);
        panel.Children.Add(new TextBlock
        {
            Text = "Catatan: sinkronisasi setelan kolaborasi ke server belum tersedia; link undangan sudah bisa dibagikan.",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title = "Kolaborasi",
            Content = panel,
            CloseButtonText = "Tutup",
            XamlRoot = RootGrid.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    /// <summary>
    /// Presents the Google sign-in dialog via <see cref="ILoginFlow"/>, then refreshes the account
    /// footer (name + avatar) and posts a small success/failure toast (Req 4.2/4.3).
    /// </summary>
    private async Task SignInAsync()
    {
        var login = App.Current.Services.GetService<ILoginFlow>();
        if (login is null || Content?.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        bool loggedIn = false;
        bool errored = false;
        try
        {
            loggedIn = await login.ShowAsync(xamlRoot);
        }
        catch
        {
            // A failed sign-in attempt must not crash the shell.
            errored = true;
        }

        if (loggedIn)
        {
            await RefreshAccountAsync();
            ShowLoginTip(success: true);
        }
        else
        {
            UpdateSignInLabel();

            // Only surface a failure notification on an actual error; a plain user cancel is silent.
            if (errored)
            {
                ShowLoginTip(success: false);
            }
        }
    }

    /// <summary>
    /// Shows a small in-app sign-in result notification anchored above the account footer item
    /// (Req 4.3) â€” the in-app version the user asked for instead of a system toast. Auto-dismisses.
    /// </summary>
    private void ShowLoginTip(bool success)
    {
        void Apply()
        {
            var name = _currentAccount?.Name;
            LoginTip.Target = SignInItem;
            LoginTip.Title = success ? "Signed in" : "Sign-in failed";
            LoginTip.Subtitle = success
                ? (string.IsNullOrEmpty(name) ? "You're signed in to YouTube Music." : $"Signed in as {name}.")
                : "Couldn't sign you in. Please try again.";
            LoginTip.IconSource = new SymbolIconSource
            {
                Symbol = success ? Symbol.Accept : Symbol.Important,
            };
            LoginTip.IsOpen = true;

            // Auto-dismiss after a few seconds so it behaves like a transient notification.
            _loginTipTimer ??= CreateLoginTipTimer();
            _loginTipTimer.Stop();
            _loginTipTimer.Start();
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Apply);
        }
    }

    private DispatcherTimer? _loginTipTimer;

    private DispatcherTimer CreateLoginTipTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LoginTip.IsOpen = false;
        };
        return timer;
    }

    /// <summary>
    /// Fetches the currently-selected account (name + avatar) via <c>account/accounts_list</c> and
    /// refreshes the footer item. Best-effort: a network/auth failure leaves the footer unchanged.
    /// </summary>
    private async Task RefreshAccountAsync()
    {
        var auth = App.Current.Services.GetService<IAuthService>();
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (auth is not { State: AuthState.LoggedIn } || client is null)
        {
            _currentAccount = null;
            UpdateSignInLabel();
            return;
        }

        try
        {
            // Prefer account/account_menu â€” it returns the active account's name + avatar directly.
            // accounts_list is a brand-account switcher and can be empty for a personal account.
            _currentAccount = await client.GetAccountInfoAsync();

            if (_currentAccount is null)
            {
                var accounts = await client.GetAccountsListAsync();
                _currentAccount = accounts.FirstOrDefault(a => a.IsCurrent)
                    ?? accounts.FirstOrDefault(a => a.IsPrimary)
                    ?? accounts.FirstOrDefault();
            }
        }
        catch
        {
            // Keep whatever we had; the footer still shows a generic "Account" label if unknown.
        }

        UpdateSignInLabel();
    }

    /// <summary>Reflects the current auth state on the footer item (Sign in â‡„ account name + avatar).</summary>
    private void UpdateSignInLabel()
    {
        var auth = App.Current.Services.GetService<IAuthService>();
        var signedIn = auth is { State: AuthState.LoggedIn };

        // The sidebar playlists tree needs an authenticated session; (re)load it whenever the auth
        // state resolves to signed-in (the constructor-time attempt runs before auth is ready).
        // Marshalled to the UI thread: the API client's WebView2 cookie source is thread-affine.
        if (signedIn)
        {
            DispatcherQueue.TryEnqueue(() => _ = LoadSidebarPlaylistsAsync());
        }

        void Apply()
        {
            if (signedIn && _currentAccount?.AvatarUrl is { } avatar)
            {
                // Circular profile photo (PersonPicture is round by design), enlarged, + name. The
                // Icon slot is cleared because the avatar lives in the content row.
                SignInItem.Icon = null;
                SignInItem.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new PersonPicture
                        {
                            Width = 28,
                            Height = 28,
                            ProfilePicture = new BitmapImage(avatar),
                            DisplayName = _currentAccount.Name,
                        },
                        new TextBlock
                        {
                            Text = _currentAccount.Name,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                };
            }
            else if (signedIn)
            {
                SignInItem.Content = _currentAccount?.Name ?? "Account";
                SignInItem.Icon = _currentAccount?.AvatarUrl is { } avatarIcon
                    ? new ImageIcon { Source = new BitmapImage(avatarIcon) }
                    : new FontIcon { Glyph = "î»" };
            }
            else
            {
                SignInItem.Content = "Sign in";
                SignInItem.Icon = new FontIcon { Glyph = "î»" };
            }
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Apply);
        }
    }

    /// <summary>
    /// Shows a small account card (avatar + name + handle) anchored to the footer item, so a
    /// signed-in user can confirm who they are signed in as without re-triggering the login flow.
    /// </summary>
    private void ShowAccountFlyout(FrameworkElement anchor)
    {
        var account = _currentAccount;

        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(4), MinWidth = 200 };

        if (account?.AvatarUrl is { } avatar)
        {
            panel.Children.Add(new PersonPicture
            {
                Width = 72,
                Height = 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                ProfilePicture = new BitmapImage(avatar),
                DisplayName = account.Name,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = account?.Name ?? "Signed in",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        if (!string.IsNullOrEmpty(account?.Handle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = account!.Handle,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var flyout = new Flyout { Content = panel };

        var signOut = new Button
        {
            Content = "Sign out",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        signOut.Click += async (_, _) =>
        {
            flyout.Hide();
            await SignOutAsync();
        };
        panel.Children.Add(signOut);

        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Signs the user out: clears the session (cookies) via the playback host, re-evaluates the
    /// auth state, and resets the footer to "Sign in". Best-effort; a failure never crashes the shell.
    /// </summary>
    private async Task SignOutAsync()
    {
        try
        {
            await _playbackHost.SignOutAsync();
        }
        catch
        {
            // Clearing the session is best-effort.
        }

        try
        {
            if (App.Current.Services.GetService<IAuthService>() is { } auth)
            {
                await auth.CheckLoginStatusAsync();
            }
        }
        catch
        {
            // A failed re-check must not block the UI reset below.
        }

        _currentAccount = null;
        UpdateSignInLabel();
    }

    /// <summary>Posts a small system toast for the sign-in outcome. Best-effort; never throws.</summary>
    private void PostLoginToast(bool success)
    {
        try
        {
            var name = _currentAccount?.Name;
            var text = success
                ? (string.IsNullOrEmpty(name) ? "Signed in to YouTube Music" : $"Signed in as {name}")
                : "Sign-in failed";

            var builder = new AppNotificationBuilder().AddText("Kaset").AddText(text);
            if (success && _currentAccount?.AvatarUrl is { } avatar)
            {
                builder.SetAppLogoOverride(avatar, AppNotificationImageCrop.Circle);
            }

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Toast is a nicety; a notification-platform failure must not affect sign-in.
        }
    }

    /// <summary>
    /// Navigates the content frame to the real page registered for <paramref name="tag"/> when its
    /// type exists in the loaded assembly; otherwise shows the <see cref="PlaceholderPage"/> for the
    /// section so the shell keeps working while the page is built in parallel (Tasks 14.3â€“14.10).
    /// </summary>
    private void NavigateToTag(string tag)
    {
        // YouTube (full mode) surfaces (Req 32.1/32.4): the feed pages need a navigation parameter
        // describing which feed to render, so they are routed here rather than via the simple
        // tagâ†’type map. The music surfaces below are untouched.
        if (tag.StartsWith("YouTube.", StringComparison.Ordinal))
        {
            NavigateYouTube(tag);
            return;
        }

        if (PageTypeNamesByTag.TryGetValue(tag, out var typeName)
            && Navigation.NavigationHelper.ResolvePageType(typeName) is { } pageType
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
        if (Navigation.NavigationHelper.ResolvePageType(pageTypeName) is { } pageType)
        {
            ContentFrame.Navigate(pageType, parameter);
        }
    }

    // â”€â”€ Protocol activation dispatch (Task 26.1, Req 33.1â€“33.5) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private const string PlaylistPageTypeName = "KasetWin.App.Views.PlaylistPage";
    private const string AlbumPageTypeName = "KasetWin.App.Views.AlbumPage";
    private const string ArtistPageTypeName = "KasetWin.App.Views.ArtistPage";

    /// <summary>
    /// Acts on a parsed <c>kaset://</c> command (Req 33.1â€“33.4): a song plays immediately via
    /// <see cref="IPlayerService"/>; a playlist/album/artist navigates the shell to its detail page.
    /// Safe to call from any thread and at any time â€” work is marshalled onto the UI thread, and the
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
        // Surface the window in case it was hidden (background-audio close â†’ hide, Req 1.4).
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

        if (Navigation.NavigationHelper.ResolvePageType(pageTypeName) is { } pageType)
        {
            ContentFrame.Navigate(pageType, id);
        }
    }

    // â”€â”€ Keyboard accelerators (Req 16.1 shell UX) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                if (!ReferenceEquals(SourceSelector.SelectedItem, YouTubeSourceItem))
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
