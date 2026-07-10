using KasetWin.App.ViewModels;
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

        var services = App.Current.Services;
        _player = services.GetService<IPlayerService>();
        var client = services.GetRequiredService<IYTMusicClient>();
        _client = client;
        ViewModel = new ExploreDetailViewModel(client, services.GetService<ISingleFlight>());
    }

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
