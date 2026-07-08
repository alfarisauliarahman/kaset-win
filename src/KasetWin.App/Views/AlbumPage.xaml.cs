using KasetWin.App.ViewModels;
using KasetWin.App.Sharing;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Album detail page (Task 14.6, Req 14.2/14.4). An album is treated as a playlist-detail surface:
/// it is fetched through <c>GetPlaylistAsync</c> with the album browseId (<c>MPRE.../OLAK...</c>) and
/// shares <see cref="PlaylistDetailViewModel"/> with the playlist page. Playback loads the album
/// tracks into the queue (Req 14.4). No delete affordance is shown for albums (Req 14.3).
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies from <c>App.Services</c>; the navigation parameter (an album browseId) arrives via
/// <see cref="OnNavigatedTo"/> and triggers the single-flight load.
/// </remarks>
public sealed partial class AlbumPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public PlaylistDetailViewModel ViewModel { get; }

    private readonly ILikeStateStore? _likeStore;

    public AlbumPage()
    {
        var services = ((App)Application.Current).Services;
        _likeStore = services.GetService<ILikeStateStore>();
        ViewModel = new PlaylistDetailViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetRequiredService<IQueueService>(),
            services.GetService<Notifications.IInAppNotifier>(),
            _likeStore);

        this.InitializeComponent();

        // Alternating row backgrounds (zebra striping) for the track list; containers are recycled,
        // so restripe on every content change.
        TracksList.ContainerContentChanging += (_, a) =>
        {
            if (!a.InRecycleQueue)
            {
                StripeRow(a.ItemContainer, a.ItemIndex);
            }
        };

        // Live like sync: when a like changes anywhere (player bar, another row), re-apply it here.
        if (_likeStore is not null)
        {
            _likeStore.Changed += OnLikeStoreChanged;
            Unloaded += (_, _) => _likeStore.Changed -= OnLikeStoreChanged;
        }
    }

    private void OnLikeStoreChanged(string videoId) =>
        DispatcherQueue.TryEnqueue(() => ViewModel.RefreshLikeOverlay());

    /// <summary>Applies the zebra background to a track row: odd rows get a subtle fill.</summary>
    private static void StripeRow(Control container, int index) =>
        container.Background = index % 2 == 1
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string albumBrowseId && !string.IsNullOrWhiteSpace(albumBrowseId))
        {
            await ViewModel.LoadAlbumAsync(albumBrowseId);
        }
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }

    private void OnLikeTrackClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            // Toggle: a second tap on an already-liked track clears the like (un-like).
            var rating = song.LikeStatus == LikeStatus.Like ? LikeStatus.Indifferent : LikeStatus.Like;
            ViewModel.RateTrackCommand.Execute(new SongRatingRequest(song, rating));
        }
    }

    private void OnDislikeTrackClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            // Toggle: a second tap on an already-disliked track clears the dislike.
            var rating = song.LikeStatus == LikeStatus.Dislike ? LikeStatus.Indifferent : LikeStatus.Dislike;
            ViewModel.RateTrackCommand.Execute(new SongRatingRequest(song, rating));
        }
    }

    private void OnTrackStartMixClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }

    private void OnTrackOpenArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.OpenTrackArtistCommand.Execute(song);
        }
    }

    private void OnAlbumDescriptionTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.IsTextTrimmed)
        {
            ViewModel.CanExpandDescription = true;
        }
    }

    private async void OnAlbumDescriptionMoreClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ViewModel.Title,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = ViewModel.Description,
                    TextWrapping = TextWrapping.Wrap,
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420,
            },
            CloseButtonText = "Tutup",
            XamlRoot = this.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private void OnTrackRowPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ToggleTrackPlayGlyph(sender, hovering: true);

    private void OnTrackRowPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ToggleTrackPlayGlyph(sender, hovering: false);

    /// <summary>On hover, swaps the track-number for a play glyph (and back on exit).</summary>
    private static void ToggleTrackPlayGlyph(object sender, bool hovering)
    {
        if (sender is not Grid row)
        {
            return;
        }

        foreach (var child in row.Children)
        {
            if (child is Grid { Tag: "numcell" } cell)
            {
                foreach (var inner in cell.Children)
                {
                    if (inner is FrameworkElement { Tag: "num" } num)
                    {
                        num.Opacity = hovering ? 0 : 0.55;
                    }
                    else if (inner is FrameworkElement { Tag: "play" } play)
                    {
                        play.Opacity = hovering ? 1 : 0;
                    }
                }

                break;
            }
        }
    }

    private void OnShareAlbumClick(object sender, RoutedEventArgs e)
    {
        var target = ShareUrlBuilder.TryCreate(ViewModel.CurrentAlbum);
        if (!ShareInvoker.TryShow(App.Current.MainWindow, target))
        {
            ViewModel.ShowUnavailableCommand.Execute("Bagikan");
        }
    }

    private void OnTrackAddToQueueClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.AddTrackToQueueCommand.Execute(song);
        }
    }

    private void OnTrackPlayNextClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.PlayTrackNextCommand.Execute(song);
        }
    }

    private void OnTrackToggleCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.ToggleTrackCollectionCommand.Execute(song);
        }
    }

    private void OnTrackShareClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Song song })
        {
            return;
        }

        var target = ShareUrlBuilder.TryCreate(song);
        if (!ShareInvoker.TryShow(App.Current.MainWindow, target))
        {
            ViewModel.ShowUnavailableCommand.Execute("Bagikan");
        }
    }

    private async void OnTrackSaveToPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Song song })
        {
            return;
        }

        var (action, playlist) = await PickPlaylistAsync(song.VideoId);
        if (action == PickAction.CreateNew)
        {
            var name = await PromptPlaylistNameAsync();
            if (name is not null)
            {
                await ViewModel.CreatePlaylistWithTracksAsync(name, new[] { song.VideoId });
            }
        }
        else if (action == PickAction.Chosen && playlist is not null)
        {
            if (await ViewModel.IsTrackInPlaylistAsync(song.VideoId, playlist.Id))
            {
                var confirm = new ContentDialog
                {
                    Title = "Sudah ada di playlist",
                    Content = $"\"{song.Title}\" sudah ada di \"{playlist.Title}\". Tetap tambahkan?",
                    PrimaryButtonText = "Tetap Tambahkan",
                    CloseButtonText = "Batal",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                };

                if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                {
                    await ViewModel.SaveTrackToPlaylistAsync(song, playlist.Id, allowDuplicates: true);
                }
            }
            else
            {
                await ViewModel.SaveTrackToPlaylistAsync(song, playlist.Id);
            }
        }
    }

    private async void OnSaveAlbumToPlaylistClick(object sender, RoutedEventArgs e)
    {
        var (action, playlist) = await PickPlaylistAsync(ViewModel.FirstTrackVideoId);
        if (action == PickAction.CreateNew)
        {
            var name = await PromptPlaylistNameAsync();
            if (name is not null)
            {
                await ViewModel.CreatePlaylistWithTracksAsync(name, ViewModel.AllTrackVideoIds);
            }
        }
        else if (action == PickAction.Chosen && playlist is not null)
        {
            await ViewModel.SaveAlbumToPlaylistAsync(playlist.Id);
        }
    }

    private enum PickAction
    {
        Cancel,
        Chosen,
        CreateNew,
    }

    /// <summary>
    /// Shows the "Save to playlist" picker (covers + titles, "+ Playlist Baru", Save, Cancel) and
    /// reports the user's choice. Shown even when the library is empty so the user can create one.
    /// </summary>
    private async Task<(PickAction Action, Playlist? Playlist)> PickPlaylistAsync(string? sampleVideoId)
    {
        var targets = await ViewModel.GetSaveTargetsAsync(sampleVideoId);

        var list = new ListView
        {
            ItemsSource = targets,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)Resources["PlaylistPickerItemTemplate"],
            Height = 440,
        };
        if (targets.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        var newButton = new Button
        {
            Content = "+ Playlist Baru",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var panel = new StackPanel { MinWidth = 460 };
        panel.Children.Add(list);
        panel.Children.Add(newButton);

        var dialog = new ContentDialog
        {
            Title = "Simpan ke playlist",
            Content = panel,
            PrimaryButtonText = "Simpan",
            CloseButtonText = "Batal",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        // "+ Playlist Baru" sits inside the content (above the Simpan/Batal row); it closes the
        // dialog and signals the create-new path.
        var createNew = false;
        newButton.Click += (_, _) => { createNew = true; dialog.Hide(); };

        var result = await dialog.ShowAsync();
        if (createNew)
        {
            return (PickAction.CreateNew, null);
        }

        return result == ContentDialogResult.Primary
            ? (PickAction.Chosen, list.SelectedItem as Playlist)
            : (PickAction.Cancel, null);
    }

    /// <summary>Prompts for a new playlist name, or <c>null</c> when cancelled/empty.</summary>
    private async Task<string?> PromptPlaylistNameAsync()
    {
        var box = new TextBox { PlaceholderText = "Nama playlist" };
        var dialog = new ContentDialog
        {
            Title = "Playlist baru",
            Content = box,
            PrimaryButtonText = "Buat",
            CloseButtonText = "Batal",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text.Trim()
            : null;
    }
}
