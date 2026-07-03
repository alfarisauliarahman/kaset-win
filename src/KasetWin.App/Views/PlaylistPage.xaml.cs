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
/// list, plays the collection into the queue, pages in additional tracks, and — for a playlist owned
/// by the user — offers a delete affordance that navigates back once the playlist is removed.
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

    public PlaylistPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = new PlaylistDetailViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>());

        this.InitializeComponent();
        ViewModel.Deleted += OnDeleted;
    }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string playlistId && !string.IsNullOrWhiteSpace(playlistId))
        {
            await ViewModel.LoadPlaylistAsync(playlistId);
        }
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            // THROWAWAY DIAGNOSTIC (Bug A): confirm a playlist track-row click reaches the player.
            KasetWin.Core.Diagnostics.KasetTrace.Log("Play:PlaylistPage.SongClick", $"tracks={ViewModel.Tracks.Count}");
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
