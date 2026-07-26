using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Podcast creator channel page (browseId <c>UC…</c>), modelled on the YT Music web layout:
/// round avatar + red Subscribe button over "Episode terbaru" and the channel's show shelves.
/// </summary>
public sealed partial class PodcastChannelPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public PodcastChannelViewModel ViewModel { get; }

    public PodcastChannelPage()
    {
        var services = App.Current.Services;
        ViewModel = new PodcastChannelViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetService<ISingleFlight>(),
            services.GetService<KasetWin.Core.Services.Podcasts.IEpisodeProgressStore>(),
            services.GetService<Notifications.IInAppNotifier>());

        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string channelId && !string.IsNullOrWhiteSpace(channelId))
        {
            await ViewModel.LoadChannelAsync(channelId);
        }
    }

    private void OnSubscribeClick(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleSubscribeCommand.Execute(null);

    private void OnEpisodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PodcastEpisode episode })
        {
            ViewModel.PlayEpisodeCommand.Execute(episode);
        }
    }

    private void OnShowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PodcastShow show })
        {
            Navigation.NavigationHelper.NavigateToPodcastShow(show.Id);
        }
    }

    // ── Right-click menus (tester #190) ──────────────────────────────────────────────────────────

    private readonly EntityContextMenu _contextMenu = EntityContextMenu.FromServices(App.Current.Services);

    /// <summary>Show cards: Bagikan (only when the show has a navigable MPSPP… share URL).</summary>
    private void OnShowContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e) =>
        _contextMenu.TryShow(sender, e);

    /// <summary>Episode cards: Putar — the same ViewModel command as clicking the card.</summary>
    private void OnEpisodeContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PodcastEpisode episode } element)
        {
            return;
        }

        var menu = new MenuFlyout();
        EntityContextMenu.AddItem(menu, Localization.UiStrings.PlayLabel, () =>
            ViewModel.PlayEpisodeCommand.Execute(episode));
        EntityContextMenu.Show(menu, element, e);
    }
}
