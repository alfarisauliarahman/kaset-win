using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Player;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Routes activation of a Home / Explore card to the right destination (Task 14.3, Req 11.1/31.1):
/// a song plays immediately via <see cref="IPlayerService"/>; an album / playlist / artist navigates
/// to its detail page.
/// </summary>
/// <remarks>
/// The concrete detail pages (<c>AlbumPage</c>, <c>PlaylistPage</c>, <c>ArtistPage</c>) are built in
/// sibling tasks (14.6/14.7) and may not exist yet. To keep this surface compiling and functional in
/// isolation, navigation is <b>guarded</b> by resolving the page <see cref="Type"/> by name with
/// <see cref="Type.GetType(string)"/> — the same late-bind pattern the shell uses — and is simply a
/// no-op when the target page type is not present in the assembly. Once those pages land their types
/// resolve and navigation begins working with no change here.
/// </remarks>
internal static class FeedNavigation
{
    private const string AlbumPageTypeName = "KasetWin.App.Views.AlbumPage";
    private const string PlaylistPageTypeName = "KasetWin.App.Views.PlaylistPage";
    private const string ArtistPageTypeName = "KasetWin.App.Views.ArtistPage";

    /// <summary>
    /// Activates <paramref name="item"/> from within <paramref name="frame"/>: plays a song or
    /// navigates to the album/playlist/artist detail page (passing its id as the navigation
    /// parameter).
    /// </summary>
    public static void Activate(Frame? frame, HomeSectionItem item, IPlayerService? player)
    {
        ArgumentNullException.ThrowIfNull(item);

        switch (item)
        {
            case HomeSectionItem.SongItem song:
                _ = player?.PlaySongAsync(song.Song);
                break;

            case HomeSectionItem.AlbumItem album:
                NavigateToDetail(frame, AlbumPageTypeName, album.Album.Id);
                break;

            case HomeSectionItem.PlaylistItem playlist:
                // A moods/genres navigation button is surfaced as a playlist item but browses like
                // an Explore section, so route it to the reusable Explore detail page rather than
                // the playlist detail page (Req 31.2/31.3).
                if (MoodCategoryId.IsMoodCategory(playlist.Pl.Id))
                {
                    frame?.Navigate(
                        typeof(ExploreDetailPage),
                        new ExploreDestination(playlist.Pl.Id, playlist.Pl.Title));
                }
                else
                {
                    NavigateToDetail(frame, PlaylistPageTypeName, playlist.Pl.Id);
                }

                break;

            case HomeSectionItem.ArtistItem artist:
                NavigateToDetail(frame, ArtistPageTypeName, artist.Artist.Id);
                break;
        }
    }

    /// <summary>
    /// Activates a favorited item (Req 29.4 Favorites shelf): a song plays immediately via
    /// <see cref="IPlayerService.PlayAsync(string)"/>; an album / playlist / artist navigates to its
    /// detail page (passing its id as the navigation parameter).
    /// </summary>
    public static void ActivateFavorite(Frame? frame, FavoriteItem item, IPlayerService? player)
    {
        ArgumentNullException.ThrowIfNull(item);

        switch (item.Type)
        {
            case FavoriteItemType.Song:
                if (!string.IsNullOrEmpty(item.ContentId))
                {
                    _ = player?.PlayAsync(item.ContentId);
                }

                break;

            case FavoriteItemType.Album:
                NavigateToDetail(frame, AlbumPageTypeName, item.ContentId);
                break;

            case FavoriteItemType.Playlist:
                NavigateToDetail(frame, PlaylistPageTypeName, item.ContentId);
                break;

            case FavoriteItemType.Artist:
                NavigateToDetail(frame, ArtistPageTypeName, item.ContentId);
                break;
        }
    }

    /// <summary>
    /// Projects a Home card's underlying union item into a <see cref="FavoriteItem"/> suitable for
    /// pinning (Req 29.1), capturing its display fields at pin time.
    /// </summary>
    public static FavoriteItem ToFavorite(HomeSectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            HomeSectionItem.SongItem s => FavoriteItem.From(s.Song),
            HomeSectionItem.AlbumItem a => FavoriteItem.From(a.Album),
            HomeSectionItem.PlaylistItem p => FavoriteItem.From(p.Pl),
            HomeSectionItem.ArtistItem ar => FavoriteItem.From(ar.Artist),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unsupported home item kind."),
        };
    }

    private static void NavigateToDetail(Frame? frame, string pageTypeName, string id)
    {
        if (frame is null || string.IsNullOrEmpty(id))
        {
            return;
        }

        // Guard: the detail page type may not exist yet (built in a parallel task). Skip navigation
        // until it does rather than break the build / throw at runtime.
        if (Type.GetType(pageTypeName) is { } pageType)
        {
            frame.Navigate(pageType, id);
        }
    }
}