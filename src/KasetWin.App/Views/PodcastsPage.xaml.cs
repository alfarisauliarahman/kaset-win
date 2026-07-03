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
/// Podcasts discovery surface (Task 20.1, Req 27.1–27.4). Renders the <c>FEmusic_podcasts</c>
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

    public PodcastsPage()
    {
        var services = App.Current.Services;
        ViewModel = new PodcastsViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetRequiredService<IEpisodeProgressStore>(),
            services.GetService<ISingleFlight>());

        this.InitializeComponent();
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
                KasetWin.Core.Diagnostics.KasetTrace.Log("Play:Podcasts.EpisodeClick");
                await ViewModel.PlayEpisodeAsync(episode.Episode);
                break;

            case PodcastSectionItem.ShowItem show:
                NavigateToShow(show.Show.Id);
                break;
        }
    }

    private async void OnSubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PodcastCardItem { Model: PodcastSectionItem.ShowItem show } })
        {
            await ViewModel.SubscribeAsync(show.Show);
        }
    }

    private async void OnUnsubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PodcastCardItem { Model: PodcastSectionItem.ShowItem show } })
        {
            await ViewModel.UnsubscribeAsync(show.Show);
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
