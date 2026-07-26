using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Reusable Explore detail surface (Task 24.1, Req 31.1/31.2/31.3). Renders the shelves for one
/// Explore destination — New Releases, Charts, Moods &amp; Genres, or a selected mood/genre
/// category — by reusing the same <see cref="ViewModels.HomeSectionView"/> projection and
/// <c>HomeResponseParser</c> shape as Home/Explore. Card activation routes through
/// <see cref="FeedNavigation"/> to the correct detail page (Req 31.3).
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves dependencies from
/// <c>App.Services</c>; the navigation parameter (an <see cref="ExploreDestination"/>) arrives via
/// <see cref="OnNavigatedTo"/>, configures the ViewModel, and triggers the single-flight load.
/// </remarks>
public sealed partial class ExploreDetailPage : Page
{
    private readonly IPlayerService? _player;
    private readonly IYTMusicClient? _client;

    public ExploreDetailPage()
    {
        this.InitializeComponent();
        LoadMoreButton.Content = Localization.UiStrings.LoadMore;

        var services = App.Current.Services;
        _player = services.GetService<IPlayerService>();
        var client = services.GetRequiredService<IYTMusicClient>();
        _client = client;
        _notifier = services.GetService<Notifications.IInAppNotifier>();
        ViewModel = new ExploreDetailViewModel(
            client,
            services.GetService<ISingleFlight>(),
            services.GetService<IQueueService>(),
            _notifier);
    }

    private readonly Notifications.IInAppNotifier? _notifier;

    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public ExploreDetailViewModel ViewModel { get; }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ExploreDestination destination)
        {
            ViewModel.Configure(destination);
        }

        await ViewModel.LoadInitialAsync();
    }

    private void OnItemClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            FeedNavigation.Activate(this.Frame, card.Model, _player);
        }
    }

    /// <summary>
    /// Right-click on a card: only SONG cards get the YT-Music-style menu (play next / add to
    /// queue / go to album / go to artist / share). The card union is polymorphic, so the menu is
    /// built here instead of a static <c>ContextFlyout</c>; non-song cards simply show nothing.
    /// </summary>
    private void OnCardContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HomeCardItem card } element
            || card.Model is not HomeSectionItem.SongItem songItem)
        {
            return;
        }

        var song = songItem.Song;
        var menu = new MenuFlyout();

        var playNext = new MenuFlyoutItem { Text = Localization.UiStrings.MenuPlayNext };
        playNext.Click += (_, _) => ViewModel.PlayTrackNextCommand.Execute(song);
        menu.Items.Add(playNext);

        var addToQueue = new MenuFlyoutItem { Text = Localization.UiStrings.MenuAddToQueue };
        addToQueue.Click += (_, _) => ViewModel.AddTrackToQueueCommand.Execute(song);
        menu.Items.Add(addToQueue);

        // Contextual: only offered when the song actually carries a navigable id.
        if (song.HasAlbumLink)
        {
            var goToAlbum = new MenuFlyoutItem { Text = Localization.UiStrings.MenuGoToAlbum };
            goToAlbum.Click += (_, _) => Navigation.NavigationHelper.NavigateToSongAlbum(song);
            menu.Items.Add(goToAlbum);
        }

        if (song.HasArtistLink)
        {
            var goToArtist = new MenuFlyoutItem { Text = Localization.UiStrings.MenuGoToArtist };
            goToArtist.Click += (_, _) => Navigation.NavigationHelper.NavigateToSongArtist(song);
            menu.Items.Add(goToArtist);
        }

        var share = new MenuFlyoutItem { Text = Localization.UiStrings.MenuShare };
        share.Click += (_, _) =>
        {
            var target = KasetWin.Core.Services.Sharing.ShareUrlBuilder.TryCreate(song);
            if (!Sharing.ShareInvoker.TryShow(App.Current.MainWindow, target))
            {
                _notifier?.Show(Localization.UiStrings.ToastActionUnavailable(Localization.UiStrings.MenuShare));
            }
        };
        menu.Items.Add(share);

        if (e.TryGetPosition(element, out var point))
        {
            menu.ShowAt(element, point);
        }
        else
        {
            menu.ShowAt(element);
        }

        e.Handled = true;
    }

    private void OnChartCardEnter(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
        {
            g.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void OnChartCardExit(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
        {
            g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    /// <summary>"Lihat semua" on a video shelf opens the full list of the same items.</summary>
    private void OnChartSeeAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HomeSectionView section })
        {
            TrendingListPage.PendingSection = section;
            this.Frame?.Navigate(typeof(TrendingListPage), section.Title);
        }
    }

    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CardKeyboard.IsActivationKey(e.Key) && sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            FeedNavigation.Activate(this.Frame, card.Model, _player);
            e.Handled = true;
        }
    }

    // ── Card Play overlay (Feature A) ────────────────────────────────────────────────────────────

    private void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        CardPlayOverlay.OnPointerEntered(sender);

    private void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        CardPlayOverlay.OnPointerExited(sender);

    private void OnPlayOverlayGotFocus(object sender, RoutedEventArgs e) =>
        CardPlayOverlay.OnOverlayGotFocus(sender);

    private void OnPlayOverlayLostFocus(object sender, RoutedEventArgs e) =>
        CardPlayOverlay.OnOverlayLostFocus(sender);

    private async void OnCardPlay(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            await FeedNavigation.PlayItemAsync(card.Model, _player, _client);
        }
    }

    private async void OnLoadMoreClick(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadMoreAsync();
}
