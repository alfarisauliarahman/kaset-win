using System;
using System.Linq;
using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Artist detail page (Task 14.7, Req 15.1â€“15.4). Shows the artist header, top songs, albums and
/// singles/EPs, supports follow/unfollow and "See all", and plays content into the queue.
/// </summary>
/// <remarks>
/// <para>
/// Follows the page convention used across the shell: a parameterless constructor (so the
/// <see cref="Frame"/> can instantiate it) resolves its <see cref="ArtistDetailViewModel"/> from
/// <c>App.Services</c>, and the channelId is received via <see cref="OnNavigatedTo"/> as the
/// navigation parameter (a <c>UCâ€¦</c> id). The load is funnelled through
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

    private readonly IPlayerService? _player;
    private readonly IYTMusicClient? _client;

    public ArtistPage()
    {
        this.InitializeComponent();

        // Resolve the ViewModel's collaborators from App.Services and compose it here. The
        // ViewModel itself is not registered in the DI container, so we build it from the
        // already-registered Core services (IYTMusicClient/IPlayerService, plus the optional
        // shared ISingleFlight) rather than via GetRequiredService<ArtistDetailViewModel>.
        var services = App.Current.Services;
        _player = services.GetService<IPlayerService>();
        _client = services.GetService<IYTMusicClient>();
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

    // â”€â”€ Play (Req 15.4) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song && ViewModel.PlaySongCommand.CanExecute(song))
        {
            // THROWAWAY DIAGNOSTIC (Bug A): confirm an artist top-song click reaches the player.
            ViewModel.PlaySongCommand.Execute(song);
        }
    }

    // â”€â”€ Navigation to Album/Playlist/Artist (Req 15.2) â€” Frame + Type.GetType guard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnAlbumCardClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Album album })
        {
            NavigateByTypeName(AlbumPageTypeName, album.Id);
        }
    }

    private void OnVideoCardClick(object sender, TappedRoutedEventArgs e)
    {
        // A video plays like any other track, loading the full rail into the queue so the user can
        // continue past the clicked item (Req 15.4). The Videos and Live-performances shelves share
        // this template, so route the click to whichever rail actually holds the item.
        if (sender is FrameworkElement { DataContext: Song video })
        {
            PlayVideoOrLive(video);
        }
    }

    private void PlayVideoOrLive(Song video)
    {
        var isLive = ViewModel.LivePerformances.Any(v => string.Equals(v.Id, video.Id, StringComparison.Ordinal))
            && !ViewModel.Videos.Any(v => string.Equals(v.Id, video.Id, StringComparison.Ordinal));

        if (isLive)
        {
            if (ViewModel.PlayLivePerformanceCommand.CanExecute(video))
            {
                ViewModel.PlayLivePerformanceCommand.Execute(video);
            }
        }
        else if (ViewModel.PlayVideoCommand.CanExecute(video))
        {
            ViewModel.PlayVideoCommand.Execute(video);
        }
    }

    /// <summary>
    /// Keeps the header banner at the YouTube-Music cover ratio (1920×800 ≈ 2.4:1) as the window
    /// resizes, so the art fills the width without being cropped by a fixed height.
    /// </summary>
    private void OnBannerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double BannerAspectRatio = 1920.0 / 800.0;
        if (sender is FrameworkElement banner && e.NewSize.Width > 0)
        {
            var height = e.NewSize.Width / BannerAspectRatio;
            // Clamp so a very wide window doesn't push the whole page below the fold.
            banner.Height = Math.Clamp(height, 220, 520);
        }
    }

    private void OnSeeAllLiveClick(object sender, RoutedEventArgs e) =>
        Frame?.Navigate(typeof(ArtistVideosPage), new ArtistSeeAllRequest("Pertunjukan langsung", Videos: [.. ViewModel.LivePerformances]));

    /// <summary>
    /// Shows the "Show more" toggle only when the collapsed description is actually clipped. Fires
    /// as the text's trim state changes; once clipping is seen the toggle stays available so the user
    /// can also collapse again. A description that fits within the line cap never enables it.
    /// </summary>
    private void OnDescriptionTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.IsTextTrimmed)
        {
            ViewModel.CanExpandDescription = true;
        }
    }

    private void OnPlaylistCardClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist playlist })
        {
            NavigateByTypeName(PlaylistPageTypeName, playlist.Id);
        }
    }

    private void OnArtistCardClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Artist artist })
        {
            // Re-navigate the same page type to the related artist.
            Frame?.Navigate(typeof(ArtistPage), artist.Id);
        }
    }

    /// <summary>
    /// Keyboard activation for the Grid-based cards (album/single/video/featured/related). The card
    /// root is a Grid (not a Button â€” a nested Play Button would break a Button root's hit-testing),
    /// so it does not raise Click for Enter/Space; this handler dispatches on the bound model type
    /// to mirror the matching Tapped handler.
    /// </summary>
    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!CardKeyboard.IsActivationKey(e.Key) || sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.DataContext)
        {
            case Album album:
                NavigateByTypeName(AlbumPageTypeName, album.Id);
                e.Handled = true;
                break;
            case Playlist playlist:
                NavigateByTypeName(PlaylistPageTypeName, playlist.Id);
                e.Handled = true;
                break;
            case Artist artist:
                Frame?.Navigate(typeof(ArtistPage), artist.Id);
                e.Handled = true;
                break;
            case Song video:
                PlayVideoOrLive(video);
                e.Handled = true;
                break;
        }
    }

    // â”€â”€ Card Play overlay (Feature A) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        CardPlayOverlay.OnPointerEntered(sender);

    private void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        CardPlayOverlay.OnPointerExited(sender);

    private void OnPlayOverlayGotFocus(object sender, RoutedEventArgs e) =>
        CardPlayOverlay.OnOverlayGotFocus(sender);

    private void OnPlayOverlayLostFocus(object sender, RoutedEventArgs e) =>
        CardPlayOverlay.OnOverlayLostFocus(sender);

    private async void OnAlbumCardPlay(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Album album })
        {
            await FeedNavigation.PlayBrowseCollectionAsync(album.Id, _player, _client);
        }
    }

    private async void OnPlaylistCardPlay(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist playlist })
        {
            await FeedNavigation.PlayBrowseCollectionAsync(playlist.Id, _player, _client);
        }
    }

    private void OnSeeAllSongsClick(object sender, RoutedEventArgs e) =>
        NavigateToSeeAll(ViewModel.SongsSeeAllBrowseId);

    private void OnSeeAllAlbumsClick(object sender, RoutedEventArgs e) =>
        Frame?.Navigate(typeof(ArtistVideosPage), new ArtistSeeAllRequest("Album", Albums: [.. ViewModel.Albums]));

    private void OnSeeAllSinglesClick(object sender, RoutedEventArgs e) =>
        Frame?.Navigate(typeof(ArtistVideosPage), new ArtistSeeAllRequest("Single & EP", Albums: [.. ViewModel.SinglesAndEps]));

    private void OnSeeAllVideosClick(object sender, RoutedEventArgs e) =>
        Frame?.Navigate(typeof(ArtistVideosPage), new ArtistSeeAllRequest("Video", Videos: [.. ViewModel.Videos]));

    private void OnSeeAllFeaturedClick(object sender, RoutedEventArgs e) =>
        Frame?.Navigate(typeof(ArtistVideosPage), new ArtistSeeAllRequest("Tampil di", Playlists: [.. ViewModel.FeaturedPlaylists]));

    private void OnSeeAllRelatedClick(object sender, RoutedEventArgs e) =>
        Frame?.Navigate(typeof(ArtistVideosPage), new ArtistSeeAllRequest("Penggemar mungkin juga suka", Artists: [.. ViewModel.RelatedArtists]));

    /// <summary>
    /// Navigates a "See all" rail to its full list. Artist discography "See all" targets are
    /// browse ids that render as a playlist-style collection, so we route them to
    /// <c>PlaylistPage</c> when that page exists. A dedicated artist see-all surface is future work;
    /// until then the link is a safe no-op when the target page is absent (Req 15.2).
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
        if (Navigation.NavigationHelper.ResolvePageType(typeName) is { } pageType)
        {
            Frame?.Navigate(pageType, parameter);
        }

        // When the destination page does not exist yet, ignore the navigation rather than break the
        // shell. The concrete pages land in parallel tasks (14.6).
    }
}
