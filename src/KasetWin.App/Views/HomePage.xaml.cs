using KasetWin.App.Sharing;
using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Favorites;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Views;

/// <summary>
/// Home surface (Task 14.3, Req 11.1/11.2/11.3). Renders the <c>FEmusic_home</c> shelves and routes
/// card activation: songs play immediately, albums/playlists/artists navigate to their detail page.
/// </summary>
/// <remarks>
/// <para>
/// The page is created by <see cref="Frame.Navigate(Type)"/> (parameterless ctor), so it resolves
/// its collaborators from the application DI container — <see cref="IYTMusicClient"/>,
/// <see cref="IPlayerService"/> and the optional shared <see cref="ISingleFlight"/> — and constructs
/// its <see cref="HomeViewModel"/> directly (the ViewModels are not registered in the container).
/// </para>
/// <para>
/// The initial load is kicked off from <c>Loaded</c> and is single-flight-guarded by the ViewModel,
/// so a re-entrant <c>Loaded</c> (e.g. back/forward navigation) joins the in-flight load (Req 16.3).
/// </para>
/// </remarks>
public sealed partial class HomePage : Page
{
    private readonly IPlayerService? _player;
    private readonly IYTMusicClient? _client;

    public HomePage()
    {
        this.InitializeComponent();

        var services = App.Current.Services;
        _player = services.GetService<IPlayerService>();
        var client = services.GetRequiredService<IYTMusicClient>();
        _client = client;
        ViewModel = new HomeViewModel(
            client,
            services.GetService<ISingleFlight>(),
            services.GetService<IFavoritesService>());

        Loaded += OnLoaded;
    }

    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public HomeViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadAllAsync();

    private void OnItemClick(object sender, TappedRoutedEventArgs e)
    {
        // A tap on the clickable artist subtitle bubbles up to the card Grid; ignore it here so the
        // subtitle's own Click (navigate to artist) wins instead of activating the card (Bug: klik
        // nama artis di kartu Home).
        if (OriginatesFromHyperlink(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            FeedNavigation.Activate(this.Frame, card.Model, _player);
        }
    }

    /// <summary>Walks up from the tapped element to see whether it sits inside a HyperlinkButton.</summary>
    private static bool OriginatesFromHyperlink(DependencyObject? source)
    {
        for (var node = source; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is HyperlinkButton)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Navigates to the artist named in a card's subtitle (without triggering the card tap).</summary>
    private void OnSubtitleArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string artistId } && !string.IsNullOrEmpty(artistId))
        {
            Navigation.NavigationHelper.NavigateToArtist(artistId);
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

    private void OnChartCardEnter(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
        {
            g.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void OnChartCardExit(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
        {
            g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

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

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FavoriteCardItem card })
        {
            FeedNavigation.ActivateFavorite(this.Frame, card.Model, _player);
        }
    }

    private void OnAddToFavorites(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            ViewModel.ToggleFavorite(FeedNavigation.ToFavorite(card.Model));
        }
    }

    private void OnRemoveFromFavorites(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FavoriteCardItem card })
        {
            ViewModel.RemoveFavorite(card.Model.ContentId);
        }
    }

    private void OnShareItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            var target = ShareUrlBuilder.TryCreate(card.Model);
            ShareInvoker.TryShow(App.Current.MainWindow, target);
        }
    }

    private void OnShareFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FavoriteCardItem card })
        {
            var target = ShareUrlBuilder.TryCreate(card.Model);
            ShareInvoker.TryShow(App.Current.MainWindow, target);
        }
    }

    private async void OnLoadMoreClick(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadMoreAsync();
}
