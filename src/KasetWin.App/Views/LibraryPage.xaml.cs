using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
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

        var services = App.Current.Services;
        _viewModel = new LibraryViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetService<IPlayerService>());

        DataContext = _viewModel;
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
            NavigateByName("KasetWin.App.Views.PlaylistPage", playlist.Id);
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
