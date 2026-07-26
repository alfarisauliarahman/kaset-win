using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Podcasts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Podcasts discovery surface (Task 20.1, Req 27.1â€“27.4). Renders the <c>FEmusic_podcasts</c>
/// shelves of shows and episodes; selecting an episode plays it and persists its progress, and the
/// card context menu subscribes / unsubscribes from a show.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies from <c>App.Services</c> and constructs the <see cref="PodcastsViewModel"/> directly
/// (ViewModels are not registered in the container). The initial load is kicked off from
/// <c>Loaded</c> and is single-flight-guarded by the ViewModel (Req 16.3). The shell only navigates
/// here after confirming region availability (Req 27.1).
/// </remarks>
public sealed partial class PodcastsPage : Page
{
    private const string PodcastShowPageTypeName = "KasetWin.App.Views.PodcastShowPage";

    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public PodcastsViewModel ViewModel { get; }

    private readonly EntityContextMenu _contextMenu = EntityContextMenu.FromServices(App.Current.Services);

    public PodcastsPage()
    {
        var services = App.Current.Services;
        ViewModel = new PodcastsViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetRequiredService<IEpisodeProgressStore>(),
            services.GetService<ISingleFlight>());

        this.InitializeComponent();
        PageTitleText.Text = Localization.UiStrings.PodcastsTitle;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadAsync();

    private async void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PodcastCardItem card })
        {
            return;
        }

        switch (card.Model)
        {
            case PodcastSectionItem.EpisodeItem episode:
                // THROWAWAY DIAGNOSTIC (Bug A): confirm a podcast episode click reaches the player.
                await ViewModel.PlayEpisodeAsync(episode.Episode);
                break;

            case PodcastSectionItem.ShowItem show:
                NavigateToShow(show.Show.Id);
                break;
        }
    }

    /// <summary>
    /// Right-click on a podcast card (tester #190): shows get Subscribe / Unsubscribe (the same
    /// ViewModel actions as before) plus Bagikan when the show has a navigable share URL; episodes
    /// get Putar (the same action as clicking the card).
    /// </summary>
    private void OnCardContextRequested(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PodcastCardItem card } element)
        {
            return;
        }

        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        switch (card.Model)
        {
            case PodcastSectionItem.ShowItem show:
                EntityContextMenu.AddItem(menu, Localization.UiStrings.MenuSubscribe, async () =>
                    await ViewModel.SubscribeAsync(show.Show));
                EntityContextMenu.AddItem(menu, Localization.UiStrings.MenuUnsubscribe, async () =>
                    await ViewModel.UnsubscribeAsync(show.Show));
                _contextMenu.AddShare(menu, KasetWin.Core.Services.Sharing.ShareUrlBuilder.TryCreate(show.Show));
                break;

            case PodcastSectionItem.EpisodeItem episode:
                EntityContextMenu.AddItem(menu, Localization.UiStrings.PlayLabel, async () =>
                    await ViewModel.PlayEpisodeAsync(episode.Episode));
                break;
        }

        if (menu.Items.Count > 0)
        {
            EntityContextMenu.Show(menu, element, e);
        }
    }

    /// <summary>
    /// Navigates to the podcast show detail page when it exists. Uses the same late-bind
    /// <see cref="Type.GetType(string)"/> guard the shell uses, so a not-yet-built page is a no-op
    /// rather than a crash.
    /// </summary>
    private void NavigateToShow(string showId)
    {
        if (string.IsNullOrEmpty(showId))
        {
            return;
        }

        if (Navigation.NavigationHelper.ResolvePageType(PodcastShowPageTypeName) is { } pageType)
        {
            this.Frame.Navigate(pageType, showId);
        }
    }
}
