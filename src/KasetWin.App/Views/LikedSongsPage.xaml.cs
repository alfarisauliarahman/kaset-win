using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Dedicated Liked Songs page: the user's Liked Music playlist (VLLM), a separate dataset from the
/// Library saved songs. Mirrors the History page's columnar layout and plays a clicked track into
/// the queue.
/// </summary>
public sealed partial class LikedSongsPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public LikedSongsViewModel ViewModel { get; }

    private readonly Notifications.IInAppNotifier? _notifier;

    public LikedSongsPage()
    {
        var services = App.Current.Services;
        _notifier = services.GetService<Notifications.IInAppNotifier>();
        ViewModel = new LikedSongsViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetService<ISingleFlight>(),
            services.GetService<IQueueService>(),
            _notifier);

        this.InitializeComponent();
        PageTitleText.Text = Localization.UiStrings.LikedSongsTitle;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadLikedAsync();

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }

    // ── Row context menu (right-click, YT-Music-style) ───────────────────────────────────────────

    private void OnTrackPlayNextClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.PlayTrackNextCommand.Execute(song);
        }
    }

    private void OnTrackAddToQueueClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.AddTrackToQueueCommand.Execute(song);
        }
    }

    private void OnTrackOpenAlbumClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            Navigation.NavigationHelper.NavigateToSongAlbum(song);
        }
    }

    private void OnTrackOpenArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            Navigation.NavigationHelper.NavigateToSongArtist(song);
        }
    }

    private void OnTrackShareClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Song song })
        {
            return;
        }

        var target = Core.Services.Sharing.ShareUrlBuilder.TryCreate(song);
        if (!Sharing.ShareInvoker.TryShow(App.Current.MainWindow, target))
        {
            _notifier?.Show(Localization.UiStrings.ToastActionUnavailable(Localization.UiStrings.MenuShare));
        }
    }
}
