using System.Threading.Tasks;
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
/// Library page (Task 14.5, Req 13). Shows the user's playlists, liked/uploaded songs, followed
/// artists, and albums with filter chips (Req 13.1/13.5) and performs create/add-song/delete
/// mutations optimistically through <see cref="LibraryViewModel"/> (Req 13.2/13.3/13.4/13.6/13.7).
/// </summary>
/// <remarks>
/// Follows the shell's page contract: a parameterless constructor (so the shell can resolve the
/// page by type name) that builds its <see cref="LibraryViewModel"/> from the app DI container, sets
/// it as <see cref="FrameworkElement.DataContext"/>, and kicks off the load on navigation. Detail
/// navigation is guarded with <see cref="Type.GetType(string)"/> so clicking an item is a no-op
/// until the corresponding detail page (Tasks 14.6/14.7) lands.
/// </remarks>
public sealed partial class LibraryPage : Page
{
    private readonly LibraryViewModel _viewModel;
    private readonly EntityContextMenu _contextMenu;

    public LibraryPage()
    {
        this.InitializeComponent();
        PageTitleText.Text = Localization.UiStrings.LibraryTitle;
        LibPlaylistsHeader.Text = Localization.UiStrings.LibraryPlaylists;
        LibSongsHeader.Text = Localization.UiStrings.LibrarySongs;
        LibUploadsHeader.Text = Localization.UiStrings.LibraryUploads;
        LibArtistsHeader.Text = Localization.UiStrings.LibraryArtists;
        LibAlbumsHeader.Text = Localization.UiStrings.LibraryAlbums;

        var services = App.Current.Services;
        _viewModel = new LibraryViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetService<IPlayerService>());
        _contextMenu = EntityContextMenu.FromServices(services);

        DataContext = _viewModel;

        // No per-page wheel forwarding here anymore. This page used to route the wheel to its own
        // scroller (the section lists' disabled inner ScrollViewer swallowed it), and that local
        // forwarder DOUBLE-scrolled once the shell gained its central wheel policy
        // (MainWindow.Scroll.cs) — the same spin was applied by both, and the local one was also
        // un-smoothed. The central policy walks up from whatever the wheel hit, so it covers these
        // lists without any wiring.
    }

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = _viewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void OnPlaylistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist)
        {
            // Route via the shared helper: a saved podcast show/playlist (MPSP… id) must open the
            // podcast page — navigating straight to PlaylistPage rendered it blank.
            Navigation.NavigationHelper.NavigateToPlaylist(playlist.Id);
        }
    }

    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album)
        {
            NavigateByName("KasetWin.App.Views.AlbumPage", album.Id);
        }
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Artist artist)
        {
            NavigateByName("KasetWin.App.Views.ArtistPage", artist.Id);
        }
    }

    /// <summary>
    /// Row click on a liked/uploaded song plays it — the same action as the row's Play button and
    /// the same row-click behaviour as LikedSongsPage/HistoryPage. (IsItemClickEnabled is also what
    /// re-enables the ListViewItem hover visuals; see the XAML note.)
    /// </summary>
    private void OnLibrarySongItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            _viewModel.PlaySongCommand.Execute(song);
        }
    }

    // ── Right-click menus (tester #190: context menus on every card/row, YT-Music style) ─────────

    /// <summary>Album / artist cards and upload rows: the shared context-appropriate menu.</summary>
    private void OnEntityCardContextRequested(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e) =>
        _contextMenu.TryShow(sender, e);

    /// <summary>
    /// Playlist cards: play/share plus "Hapus playlist" for playlists the user owns (the same
    /// <see cref="LibraryViewModel.DeletePlaylistCommand"/> the card's trash button invokes).
    /// </summary>
    private void OnPlaylistCardContextRequested(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Playlist playlist } element)
        {
            return;
        }

        var menu = _contextMenu.BuildForPlaylist(playlist) ?? new MenuFlyout();
        if (playlist.IsOwnedByUser)
        {
            EntityContextMenu.AddItem(menu, Localization.UiStrings.MenuDeletePlaylist, () =>
                _viewModel.DeletePlaylistCommand.Execute(playlist));
        }

        if (menu.Items.Count > 0)
        {
            EntityContextMenu.Show(menu, element, e);
        }
    }

    /// <summary>
    /// Library song rows: the standard song menu plus "Simpan ke playlist" (same picker as the row's
    /// "+" button).
    /// </summary>
    private void OnLibrarySongContextRequested(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Song song } element)
        {
            return;
        }

        var menu = _contextMenu.BuildForSong(song);
        EntityContextMenu.AddItem(menu, Localization.UiStrings.MenuSaveToPlaylist, async () =>
            await PickPlaylistAndAddAsync(song));
        EntityContextMenu.Show(menu, element, e);
    }

    /// <summary>Opens a playlist picker for the song, then adds it to the chosen playlist.</summary>
    private async void OnAddLibrarySongToPlaylist(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Song song })
        {
            await PickPlaylistAndAddAsync(song);
        }
    }

    /// <summary>Shows the "Simpan ke playlist" picker and performs the add (Req 13.3).</summary>
    private async Task PickPlaylistAndAddAsync(Song song)
    {
        var playlists = _viewModel.Playlists;
        if (playlists.Count == 0)
        {
            return;
        }

        var list = new ListView
        {
            ItemsSource = playlists,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)Resources["PlaylistPickerItemTemplate"],
            Height = 440,
            MinWidth = 460,
            SelectedIndex = 0,
        };

        var dialog = new ContentDialog
        {
            Title = Localization.UiStrings.MenuSaveToPlaylist,
            Content = list,
            PrimaryButtonText = Localization.UiStrings.DialogSave,
            CloseButtonText = Localization.UiStrings.DialogCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is Playlist target)
        {
            await _viewModel.AddSongToPlaylistAsync(song, target.Id);
        }
    }

    /// <summary>
    /// Navigates the hosting <see cref="Frame"/> to <paramref name="typeName"/> when its type exists
    /// in the loaded assembly; otherwise does nothing so the page keeps working while detail pages
    /// are built in parallel (Tasks 14.6/14.7).
    /// </summary>
    private void NavigateByName(string typeName, string parameter)
    {
        if (Navigation.NavigationHelper.ResolvePageType(typeName) is { } pageType)
        {
            this.Frame?.Navigate(pageType, parameter);
        }
    }
}
