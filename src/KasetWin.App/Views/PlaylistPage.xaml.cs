using KasetWin.App.Sharing;
using KasetWin.App.Sharing;
using KasetWin.App.ViewModels;
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
        PlayButtonLabel.Text = Localization.UiStrings.PlayLabel;
        DeleteButtonLabel.Text = Localization.UiStrings.DeleteLabel;
        LoadMoreButton.Content = Localization.UiStrings.LoadMore;
        ViewModel.Deleted += OnDeleted;

        // Alternating row backgrounds (zebra striping) for the track list; containers are recycled,
        // so restripe on every content change.
        TracksList.ContainerContentChanging += (_, a) =>
        {
            if (a.InRecycleQueue)
            {
                return;
            }

            ZebraStriping.StripeRow(a.ItemContainer, a.ItemIndex);
            // A brand-new container has no template root yet in phase 0 — restripe once it does.
            if (a.ItemContainer.ContentTemplateRoot is null)
            {
                a.RegisterUpdateCallback(static (_, b) => ZebraStriping.StripeRow(b.ItemContainer, b.ItemIndex));
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

            // A podcast playlist carries episode rows the track parser cannot represent, so this
            // page would render an empty list — reroute to the podcast show surface (which parses
            // episodes) and drop this blank entry from the back stack.
            if (ViewModel.IsPodcastPlaylist
                && Navigation.NavigationHelper.ResolvePageType("KasetWin.App.Views.PodcastShowPage") is { } podcastPage)
            {
                var browseId = playlistId.StartsWith("VL", StringComparison.Ordinal)
                    ? playlistId
                    : "VL" + playlistId;
                Frame.Navigate(podcastPage, browseId);
                if (Frame.BackStack.Count > 0)
                {
                    Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
                }

                return;
            }

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
            Title = Localization.UiStrings.MenuDeletePlaylist,
            Content = Localization.UiStrings.DeletePlaylistConfirm(ViewModel.Title ?? string.Empty),
            PrimaryButtonText = Localization.UiStrings.DialogDelete,
            CloseButtonText = Localization.UiStrings.DialogCancel,
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

    // ---- Per-track context menu (right-click). Mirrors AlbumPage's three-dot menu; both pages
    // ---- share PlaylistDetailViewModel, so the handlers delegate to the same commands.

    private void OnTrackStartMixClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }

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

    private void OnTrackToggleCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.ToggleTrackCollectionCommand.Execute(song);
        }
    }

    private void OnTrackOpenArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            ViewModel.OpenTrackArtistCommand.Execute(song);
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
            ViewModel.ShowUnavailableCommand.Execute(Localization.UiStrings.MenuShare);
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
                    Title = Localization.UiStrings.DialogAlreadyInPlaylistTitle,
                    Content = Localization.UiStrings.AlreadyInPlaylistBody(song.Title, playlist.Title),
                    PrimaryButtonText = Localization.UiStrings.DialogKeepAdd,
                    CloseButtonText = Localization.UiStrings.DialogCancel,
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
            Content = Localization.UiStrings.DialogNewPlaylistButton,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var panel = new StackPanel { MinWidth = 460 };
        panel.Children.Add(list);
        panel.Children.Add(newButton);

        var dialog = new ContentDialog
        {
            Title = Localization.UiStrings.MenuSaveToPlaylist,
            Content = panel,
            PrimaryButtonText = Localization.UiStrings.DialogSave,
            CloseButtonText = Localization.UiStrings.DialogCancel,
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
        var box = new TextBox { PlaceholderText = Localization.UiStrings.DialogPlaylistNamePlaceholder };
        var dialog = new ContentDialog
        {
            Title = Localization.UiStrings.DialogNewPlaylistTitle,
            Content = box,
            PrimaryButtonText = Localization.UiStrings.DialogCreate,
            CloseButtonText = Localization.UiStrings.DialogCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text.Trim()
            : null;
    }
}
