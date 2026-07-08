using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Playlist detail page (Task 14.6, Req 14.1/14.3/14.4). Shows the playlist header and its track
/// list, plays the collection into the queue, pages in additional tracks, and â€” for a playlist owned
/// by the user â€” offers a delete affordance that navigates back once the playlist is removed.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies from <c>App.Services</c>; the navigation parameter (a playlist browseId) arrives via
/// <see cref="OnNavigatedTo"/> and triggers the single-flight load.
/// </remarks>
public sealed partial class PlaylistPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public PlaylistDetailViewModel ViewModel { get; }

    private readonly ILikeStateStore? _likeStore;

    public PlaylistPage()
    {
        var services = ((App)Application.Current).Services;
        _likeStore = services.GetService<ILikeStateStore>();
        ViewModel = new PlaylistDetailViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetService<IQueueService>(),
            services.GetService<Notifications.IInAppNotifier>(),
            _likeStore);

        this.InitializeComponent();
        ViewModel.Deleted += OnDeleted;

        // Alternating row backgrounds (zebra striping) for the track list; containers are recycled,
        // so restripe on every content change.
        TracksList.ContainerContentChanging += (_, a) =>
        {
            if (!a.InRecycleQueue)
            {
                a.ItemContainer.Background = a.ItemIndex % 2 == 1
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        };

        if (_likeStore is not null)
        {
            _likeStore.Changed += OnLikeStoreChanged;
            Unloaded += (_, _) => _likeStore.Changed -= OnLikeStoreChanged;
        }
    }

    private void OnLikeStoreChanged(string videoId) =>
        DispatcherQueue.TryEnqueue(() => ViewModel.RefreshLikeOverlay());

    /// <summary>Shares the playlist's public URL via the native share sheet.</summary>
    private void OnSharePlaylistClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ShareTarget is { } target)
        {
            Sharing.ShareInvoker.TryShow(((App)Application.Current).MainWindow, target);
        }
    }

    private string? _playlistId;

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string playlistId && !string.IsNullOrWhiteSpace(playlistId))
        {
            _playlistId = playlistId;
            await ViewModel.LoadPlaylistAsync(playlistId);

            // Local custom cover override (YT Music has no cover-upload API).
            if (PlaylistCoverStore.Get(playlistId) is { } coverPath)
            {
                CoverImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(coverPath));
            }

            // Own playlists carry no creator avatar in the API response; fall back to the signed-in
            // account's photo when the creator is the current user.
            if (!ViewModel.HasArtistThumbnail
                && ((App)Application.Current).MainWindow is MainWindow main
                && main.CurrentAccount is { AvatarUrl: { } avatar } account
                && string.Equals(ViewModel.AuthorDisplay, account.Name, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.ArtistThumbnailUrl = avatar;
            }
        }
    }

    /// <summary>Deletes the playlist only after an explicit confirmation.</summary>
    private async void OnDeletePlaylistClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Hapus playlist",
            Content = $"Hapus \"{ViewModel.Title}\" secara permanen?",
            PrimaryButtonText = "Hapus",
            CloseButtonText = "Batal",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.DeleteCommand.Execute(null);
        }
    }

    /// <summary>Opens the shell's Edit-playlist dialog (UMUM / KOLABORASI) for this playlist.</summary>
    private async void OnEditPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (_playlistId is not null && ((App)Application.Current).MainWindow is MainWindow main)
        {
            await main.ShowEditPlaylistDialogAsync(_playlistId);
            await ViewModel.LoadPlaylistAsync(_playlistId); // reflect the edited metadata
        }
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            // THROWAWAY DIAGNOSTIC (Bug A): confirm a playlist track-row click reaches the player.
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }

    private void OnDeleted(object? sender, EventArgs e)
    {
        if (Frame is { CanGoBack: true })
        {
            Frame.GoBack();
        }
    }
}
