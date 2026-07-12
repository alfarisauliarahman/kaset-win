using System.Linq;
using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Podcast show detail page (Req 27): a show header (cover + title + author) over the show's
/// episode list. Selecting an episode plays it through the shared player.
/// </summary>
public sealed partial class PodcastShowPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public PodcastShowViewModel ViewModel { get; }

    public PodcastShowPage()
    {
        var services = App.Current.Services;
        ViewModel = new PodcastShowViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetService<ISingleFlight>(),
            services.GetService<KasetWin.Core.Services.Player.IQueueService>(),
            services.GetService<KasetWin.Core.Services.Podcasts.IEpisodeProgressStore>(),
            services.GetService<Notifications.IInAppNotifier>());

        this.InitializeComponent();
    }

    /// <summary>Shares the show's public URL via the native share sheet.</summary>
    private void OnShareShowClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.ShareTarget is { } target)
        {
            Sharing.ShareInvoker.TryShow(((App)Microsoft.UI.Xaml.Application.Current).MainWindow, target);
        }
    }

    /// <summary>Shares one episode's watch URL (row ⋯ menu).</summary>
    private void OnShareEpisodeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: PodcastEpisode episode }
            && ViewModel.ShareEpisode(episode) is { } target)
        {
            Sharing.ShareInvoker.TryShow(((App)Microsoft.UI.Xaml.Application.Current).MainWindow, target);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string showId && !string.IsNullOrWhiteSpace(showId))
        {
            await ViewModel.LoadShowAsync(showId);
        }
    }

    /// <summary>"Simpan ke playlist": pick one of the user's playlists, then add the episode.</summary>
    private async void OnSaveToPlaylistClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.FrameworkElement { DataContext: PodcastEpisode episode })
        {
            return;
        }

        var services = App.Current.Services;
        var client = services.GetRequiredService<IYTMusicClient>();
        var notifier = services.GetService<Notifications.IInAppNotifier>();
        try
        {
            var menu = await client.GetAddToPlaylistOptionsAsync(episode.Id);
            var list = new ListView
            {
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.None,
                ItemsSource = menu.Playlists.Select(p => new TextBlock { Text = p.Title, Tag = p.Id, Padding = new Microsoft.UI.Xaml.Thickness(4, 8, 4, 8) }).ToList(),
            };
            var dialog = new ContentDialog
            {
                Title = Localization.UiStrings.MenuSaveToPlaylist,
                Content = new ScrollViewer { Content = list, MaxHeight = 420 },
                CloseButtonText = Localization.UiStrings.DialogCancel,
                XamlRoot = XamlRoot,
            };
            list.ItemClick += async (_, args) =>
            {
                dialog.Hide();
                if (args.ClickedItem is TextBlock { Tag: string playlistId })
                {
                    try
                    {
                        await client.AddSongToPlaylistAsync(episode.Id, playlistId);
                        notifier?.Show(Localization.UiStrings.ToastSavedToPlaylist);
                    }
                    catch (Exception)
                    {
                        notifier?.Show(Localization.UiStrings.ToastSaveToPlaylistFailed);
                    }
                }
            };
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            notifier?.Show(Localization.UiStrings.ToastLoadPlaylistsFailed);
        }
    }

    /// <summary>Header "Simpan": saves the show/playlist to the library.</summary>
    private void OnSaveShowClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ViewModel.SaveShowCommand.Execute(null);

    /// <summary>Header author link → the creator's artist/channel page.</summary>
    private void OnAuthorClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Navigation.NavigationHelper.NavigateToPodcastChannel(ViewModel.AuthorChannelId);

    private void OnSaveForLaterClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: PodcastEpisode episode })
        {
            ViewModel.SaveForLaterCommand.Execute(episode);
        }
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            ViewModel.FindQuery = box.Text;
        }
    }

    private void OnPlayNextClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: PodcastEpisode episode })
        {
            ViewModel.PlayEpisodeNextCommand.Execute(episode);
        }
    }

    private void OnEnqueueClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: PodcastEpisode episode })
        {
            ViewModel.EnqueueEpisodeCommand.Execute(episode);
        }
    }

    private void OnTogglePlayedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: PodcastEpisode episode })
        {
            ViewModel.TogglePlayedCommand.Execute(episode);
        }
    }

    private void OnEpisodeClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PodcastEpisode episode)
        {
            ViewModel.PlayEpisodeCommand.Execute(episode);
        }
    }
}
