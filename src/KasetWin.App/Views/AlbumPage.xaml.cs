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

    public AlbumPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = new PlaylistDetailViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>());

        this.InitializeComponent();
    }

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
}
