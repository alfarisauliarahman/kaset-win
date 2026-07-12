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

        DataContext = _viewModel;

        // Wheel forwarding must observe HANDLED events too: the lists' inner (disabled) ScrollViewer
        // marks the wheel handled before a plain XAML handler would fire, which is why hovering a
        // grid still blocked the page scroll. AddHandler(..., handledEventsToo: true) always fires.
        var wheel = new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSectionWheel);
        PlaylistsGrid.AddHandler(PointerWheelChangedEvent, wheel, handledEventsToo: true);
        LikedList.AddHandler(PointerWheelChangedEvent, wheel, handledEventsToo: true);
        UploadsList.AddHandler(PointerWheelChangedEvent, wheel, handledEventsToo: true);
        ArtistsGrid.AddHandler(PointerWheelChangedEvent, wheel, handledEventsToo: true);
        AlbumsGrid.AddHandler(PointerWheelChangedEvent, wheel, handledEventsToo: true);
    }

    /// <summary>
    /// Forwards the mouse wheel from an inner section list to the page's outer scroll viewer. The
    /// wrapped GridViews/ListViews have their own (disabled) scroll viewer that otherwise swallows
    /// the wheel, so hovering over a grid stopped the page from scrolling and felt janky.
    /// </summary>
    private void OnSectionWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        LibraryScroller.ChangeView(null, LibraryScroller.VerticalOffset - delta, null, disableAnimation: true);
        e.Handled = true;
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

    /// <summary>Opens a playlist picker for the song, then adds it to the chosen playlist.</summary>
    private async void OnAddLibrarySongToPlaylist(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Song song })
        {
            return;
        }

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
