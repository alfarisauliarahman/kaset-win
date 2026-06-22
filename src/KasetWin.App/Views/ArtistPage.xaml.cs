using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Artist detail page (Task 14.7, Req 15.1–15.4). Shows the artist header, top songs, albums and
/// singles/EPs, supports follow/unfollow and "See all", and plays content into the queue.
/// </summary>
/// <remarks>
/// <para>
/// Follows the page convention used across the shell: a parameterless constructor (so the
/// <see cref="Frame"/> can instantiate it) resolves its <see cref="ArtistDetailViewModel"/> from
/// <c>App.Services</c>, and the channelId is received via <see cref="OnNavigatedTo"/> as the
/// navigation parameter (a <c>UC…</c> id). The load is funnelled through
/// <see cref="ArtistDetailViewModel.LoadArtistAsync"/> which single-flights per id (Req 16.3).
/// </para>
/// <para>
/// Navigation to detail surfaces uses <c>this.Frame</c> directly, resolving the destination page
/// type by name with a <see cref="Type.GetType(string)"/> guard so the page keeps working while
/// <c>AlbumPage</c>/<c>PlaylistPage</c> are built in parallel tasks (Tasks 14.6): when the target
/// type is not present the click is simply ignored rather than breaking the build.
/// </para>
/// </remarks>
public sealed partial class ArtistPage : Page
{
    private const string AlbumPageTypeName = "KasetWin.App.Views.AlbumPage";
    private const string PlaylistPageTypeName = "KasetWin.App.Views.PlaylistPage";

    public ArtistPage()
    {
        this.InitializeComponent();

        // Resolve the ViewModel's collaborators from App.Services and compose it here. The
        // ViewModel itself is not registered in the DI container, so we build it from the
        // already-registered Core services (IYTMusicClient/IPlayerService, plus the optional
        // shared ISingleFlight) rather than via GetRequiredService<ArtistDetailViewModel>.
        var services = App.Current.Services;
        ViewModel = new ArtistDetailViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetService<ISingleFlight>());
    }

    /// <summary>The page ViewModel, resolved from DI and bound via <c>x:Bind</c>.</summary>
    public ArtistDetailViewModel ViewModel { get; }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string channelId && !string.IsNullOrWhiteSpace(channelId))
        {
            await ViewModel.LoadArtistAsync(channelId);
        }
    }

    // ── Play (Req 15.4) ──────────────────────────────────────────────────────────────────────────

    private void OnSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song && ViewModel.PlaySongCommand.CanExecute(song))
        {
            ViewModel.PlaySongCommand.Execute(song);
        }
    }

    // ── Navigation to Album/Playlist (Req 15.2) — Frame + Type.GetType guard ────────────────────

    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album)
        {
            NavigateByTypeName(AlbumPageTypeName, album.Id);
        }
    }

    private void OnSeeAllSongsClick(object sender, RoutedEventArgs e) =>
        NavigateToSeeAll(ViewModel.SongsSeeAllBrowseId);

    private void OnSeeAllAlbumsClick(object sender, RoutedEventArgs e) =>
        NavigateToSeeAll(ViewModel.AlbumsSeeAllBrowseId);

    private void OnSeeAllSinglesClick(object sender, RoutedEventArgs e) =>
        NavigateToSeeAll(ViewModel.SinglesSeeAllBrowseId);

    /// <summary>
    /// Navigates a "See all" rail to its full list. Artist discography "See all" targets are
    /// browse ids that render as a playlist-style collection, so we route them to
    /// <c>PlaylistPage</c> when that page exists. A dedicated artist see-all surface is future work;
    /// until <c>PlaylistPage</c> lands the link is a safe no-op (Req 15.2).
    /// </summary>
    private void NavigateToSeeAll(string? browseId)
    {
        if (!string.IsNullOrEmpty(browseId))
        {
            NavigateByTypeName(PlaylistPageTypeName, browseId);
        }
    }

    private void NavigateByTypeName(string typeName, string parameter)
    {
        if (Type.GetType(typeName) is { } pageType)
        {
            Frame?.Navigate(pageType, parameter);
        }

        // When the destination page does not exist yet, ignore the navigation rather than break the
        // shell. The concrete pages land in parallel tasks (14.6).
    }
}
