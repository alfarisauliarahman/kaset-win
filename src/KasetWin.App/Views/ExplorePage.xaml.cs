using KasetWin.App.ViewModels;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Views;

/// <summary>
/// Explore surface (Task 14.3, Req 31.1). Renders the <c>FEmusic_explore</c> shelves (New Releases /
/// Charts / Moods &amp; Genres shortcuts) and routes card activation the same way as Home: songs
/// play immediately, albums/playlists/artists navigate to their detail page.
/// </summary>
/// <remarks>
/// Created by <see cref="Frame.Navigate(Type)"/> (parameterless ctor); resolves
/// <see cref="IYTMusicClient"/>, <see cref="IPlayerService"/> and the optional shared
/// <see cref="ISingleFlight"/> from the DI container and constructs its <see cref="ExploreViewModel"/>
/// directly. The initial load is single-flight-guarded so a re-entrant <c>Loaded</c> joins the
/// in-flight load (Req 16.3).
/// </remarks>
public sealed partial class ExplorePage : Page
{
    private readonly IPlayerService? _player;
    private readonly IYTMusicClient? _client;

    public ExplorePage()
    {
        this.InitializeComponent();

        var services = App.Current.Services;
        _player = services.GetService<IPlayerService>();
        var client = services.GetRequiredService<IYTMusicClient>();
        _client = client;
        ViewModel = new ExploreViewModel(client, services.GetService<ISingleFlight>());

        Loaded += OnLoaded;
    }

    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public ExploreViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadInitialAsync();

    private void OnItemClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            FeedNavigation.Activate(this.Frame, card.Model, _player);
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

    private void OnEntryPointClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string browseId } || string.IsNullOrEmpty(browseId))
        {
            return;
        }

        var title = browseId switch
        {
            "FEmusic_new_releases" => "New Releases",
            "FEmusic_charts" => "Charts",
            "FEmusic_moods_and_genres" => "Moods & Genres",
            _ => "Explore",
        };

        this.Frame?.Navigate(typeof(ExploreDetailPage), new ExploreDestination(browseId, title));
    }

    private async void OnLoadMoreClick(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadMoreAsync();
}
