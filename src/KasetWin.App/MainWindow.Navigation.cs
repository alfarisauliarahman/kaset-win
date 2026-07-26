using KasetWin.App.Accessibility;
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

public sealed partial class MainWindow
{
    // â”€â”€ Source toggle (Music â‡„ YouTube) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Single source of truth for the active source (true = YouTube). The two segments' IsChecked
    /// cannot serve as that state inside a Click handler: ToggleButton has already flipped the
    /// clicked segment by the time the handler runs, so "was YouTube active?" is unreadable there.
    /// </summary>
    private bool _youtubeSource;

    private void OnSourceToggleClick(object sender, RoutedEventArgs e)
    {
        var youtube = ReferenceEquals(sender, YouTubeSourceItem);

        // Segmented behaviour: exactly one segment stays active, and clicking the segment that is
        // ALREADY active does nothing at all - no navigation, no re-selection. ToggleButton has
        // just un-checked itself, so put the checked state back and leave.
        if (youtube == _youtubeSource)
        {
            MusicSourceItem.IsChecked = !_youtubeSource;
            YouTubeSourceItem.IsChecked = _youtubeSource;
            return;
        }

        SelectSource(youtube, animate: true);
    }

    /// <summary>
    /// Compact (pane-collapsed) source toggle: flips to the other source. Mirrors
    /// <see cref="OnSourceToggleClick"/> but is icon-only, shown when the sidebar is shrunk for a panel.
    /// It is a plain Button with no "already active" case - a click always means "switch".
    /// </summary>
    private void OnSourceToggleCompactClick(object sender, RoutedEventArgs e)
        => SelectSource(!_youtubeSource, animate: false);

    /// <summary>
    /// Makes <paramref name="youtube"/> the active source: checked state, sliding indicator, sidebar
    /// item visibility, compact icon, and the source's home page. Only ever called for a real change.
    /// </summary>
    private void SelectSource(bool youtube, bool animate)
    {
        _youtubeSource = youtube;
        MusicSourceItem.IsChecked = !youtube;
        YouTubeSourceItem.IsChecked = youtube;
        AnimateSourceIndicator(youtube, animate);
        ApplySourceVisibility(youtube);
        UpdateSourceCompactIcon();

        // Only a real user toggle navigates; the startup selection just sets visibility so it does
        // not override the default launch page.
        if (_sourceReady)
        {
            NavigateToTag(youtube ? "YouTube.Home" : "Home");
        }
    }

    /// <summary>Keeps the compact source button's icon/tooltip in sync with the active source.</summary>
    private void UpdateSourceCompactIcon()
    {
        var youtube = YouTubeSourceItem.IsChecked == true;
        SourceCompactIcon.Glyph = youtube ? "" : "";
        // The compact button is a single icon that both *shows* the active source and *switches*
        // it, so the label has to say which is active — an icon alone leaves a screen-reader user
        // with no way to know which of the two they are on.
        A11y.Label(
            SourceToggleCompact,
            youtube ? Localization.UiStrings.A11ySourceActiveYouTube : Localization.UiStrings.A11ySourceActiveMusic);
    }

    /// <summary>Glides the segmented source indicator to the active segment (Music = 0, YouTube = 96).</summary>
    private void AnimateSourceIndicator(bool youtube, bool animate)
    {
        const double segmentWidth = 96;
        var target = youtube ? segmentWidth : 0;

        if (!animate)
        {
            SourceIndicatorTransform.X = target;
            return;
        }

        var slide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EnableDependentAnimation = true,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, SourceIndicatorTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "X");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Begin();
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
            // Playlist children are selected PROGRAMMATICALLY by SyncSidebarPlaylistSelection
            // (after their id-based navigation already happened in ItemInvoked). Their tag is not
            // in the page map, so letting it through would drop the shell onto the placeholder.
            if (tag.StartsWith("Playlist:", StringComparison.Ordinal))
            {
                return;
            }

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

    /// <summary>Title-bar back button (Media Player-style): pops the content frame's back stack.</summary>
    private void OnTitleBarBackClick(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    /// <summary>
    /// The navigation parameter of the page currently in the content frame (null for parameterless
    /// pages). <see cref="Frame.CurrentSourcePageType"/> alone cannot distinguish two instances of a
    /// shared page (PlaylistPage per playlist id, YouTubeFeedPage per feed), so the "already showing
    /// this page" no-op guards need the parameter too.
    /// </summary>
    private object? _currentNavParameter;

    /// <summary>
    /// Keeps the back button enabled state in sync with the content frame's back stack after every
    /// navigation (forward navigations push detail pages onto the stack; GoBack pops them), tracks
    /// the current page's navigation parameter, and highlights the sidebar playlist when a playlist
    /// page loads.
    /// </summary>
    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _currentNavParameter = e.Parameter;
        NavView.IsBackEnabled = ContentFrame.CanGoBack;
        // The title-bar back button only shows once there is somewhere to go back to (starts hidden,
        // like Media Player: no back on the landing page, appears after navigating into a detail).
        TitleBarBackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        SyncSidebarPlaylistSelection();
        HookPageScrolling();
    }

    /// <summary>
    /// Highlights the sidebar playlist matching a playlist page that just loaded. The dynamic
    /// playlist children are built with <c>SelectsOnInvoked=false</c> (their click navigates by id
    /// from ItemInvoked, not via the tag→page map), so NavigationView never marks them selected on
    /// its own; this post-navigation sync does — and, running on every navigation, it also covers
    /// arrivals from elsewhere (Library tiles, kaset:// protocol) and Back into a playlist page.
    /// The programmatic selection re-raises SelectionChanged, which ignores <c>Playlist:</c> tags,
    /// so it can never re-navigate or fall through to the placeholder. Reads the CURRENT frame
    /// state (not event args) so the sidebar rebuild in <c>LoadSidebarPlaylistsAsync</c> — which
    /// clears and re-creates the children, losing any selection — can call it too.
    /// </summary>
    private void SyncSidebarPlaylistSelection()
    {
        if (ContentFrame.CurrentSourcePageType?.FullName != PlaylistPageTypeName
            || _currentNavParameter is not string playlistId)
        {
            return;
        }

        var tag = $"Playlist:{playlistId}";
        foreach (var item in PlaylistsNavItem.MenuItems)
        {
            if (item is NavigationViewItem child && child.Tag as string == tag)
            {
                if (!ReferenceEquals(NavView.SelectedItem, child))
                {
                    NavView.SelectedItem = child;
                }

                return;
            }
        }
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
            // Same tag→page route as the sidebar's own Library item, including its "already
            // showing" no-op guard (a second click must not stack Library onto the back stack).
            NavigateToTag("Library");
            CloseOverlayPaneAfterInvoke();
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem { Tag: string playlistTag }
            && playlistTag.StartsWith("Playlist:", StringComparison.Ordinal))
        {
            var playlistId = playlistTag["Playlist:".Length..];

            // Clicking the playlist that is already open is a no-op (mirrors behaviour (b) for the
            // tag pages): PlaylistPage is one shared type, so the id parameter — tracked by
            // OnContentFrameNavigated — is what identifies "this playlist is already showing".
            if (Navigation.NavigationHelper.ResolvePageType(PlaylistPageTypeName) is not { } playlistPage
                || ContentFrame.CurrentSourcePageType != playlistPage
                || !Equals(_currentNavParameter, playlistId))
            {
                Navigation.NavigationHelper.NavigateToPlaylist(playlistId);
            }

            CloseOverlayPaneAfterInvoke();
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
                // The pane is deliberately NOT closed here: the account flyout anchors to this item.
                ShowAccountFlyout(item);
            }
            else
            {
                _ = SignInAsync();
            }

            return;
        }

        // Defensive: the built-in Settings item is hidden (IsSettingsVisible=False), but ItemInvoked
        // would report it here rather than as a tagged container if it ever comes back.
        if (args.IsSettingsInvoked)
        {
            NavigateToTag("Settings");
            CloseOverlayPaneAfterInvoke();
            return;
        }

        // Regular nav items. SelectionChanged does not fire for an item that is ALREADY selected,
        // so this is what makes "click the active section → return to its root page" work (e.g.
        // Home → AlbumPage → click Home again). For a click on a DIFFERENT item both events fire;
        // NavigateToTag is safe to run twice because every route in it no-ops when the target page
        // (and, for YouTube feeds, its parameter) is already showing — the second call from
        // SelectionChanged lands after this one has navigated and does nothing.
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag })
        {
            NavigateToTag(tag);
            CloseOverlayPaneAfterInvoke();
        }
    }

    /// <summary>
    /// Closes the pane after an item invoke, but only while it is OVERLAYING the content — compact
    /// or minimal display mode, where the hamburger opened it and leaving it open covers the page
    /// (standard Windows 11 behaviour: pick, then the flyout pane goes away). An expanded pane is
    /// inline and always open, so it is left alone; the display-mode logic itself
    /// (Auto / side-panel pinning / UpdatePaneChrome) is untouched because this only flips
    /// <see cref="NavigationView.IsPaneOpen"/>, never <see cref="NavigationView.PaneDisplayMode"/>.
    /// </summary>
    private void CloseOverlayPaneAfterInvoke()
    {
        if (NavView.DisplayMode != NavigationViewDisplayMode.Expanded && NavView.IsPaneOpen)
        {
            NavView.IsPaneOpen = false;
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
            && Navigation.NavigationHelper.ResolvePageType(typeName) is { } pageType)
        {
            // A tag with a real page NEVER falls through to the placeholder. The "already on this
            // page" case used to be folded into the condition above, so re-navigating to a section
            // that was already showing (e.g. clicking the active source segment while Home was up)
            // dropped the shell onto "Home - Coming soon" instead of doing nothing.
            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }

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
            // Already showing this exact page? No-op — same rule the music tags get in
            // NavigateToTag. The parameter must be part of the check because YouTubeFeedPage is one
            // shared type serving three feeds; YouTubeFeedRequest is a record, so Equals compares
            // by value (and null==null covers the parameterless Home/Shorts pages). Without this,
            // a sidebar click routed through BOTH ItemInvoked and SelectionChanged would navigate
            // the same feed twice and stack a duplicate on the back stack.
            if (ContentFrame.CurrentSourcePageType == pageType && Equals(_currentNavParameter, parameter))
            {
                return;
            }

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
}
