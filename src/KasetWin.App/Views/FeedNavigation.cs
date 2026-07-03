using KasetWin.App.Navigation;
using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
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
/// <see cref="Type.GetType(string)"/> â€” the same late-bind pattern the shell uses â€” and is simply a
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
                // THROWAWAY DIAGNOSTIC (Bug A): confirm a Home/Explore song activation reaches the
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
                    // THROWAWAY DIAGNOSTIC (Bug A): confirm a Favorites-shelf song reaches the player.
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

    /// <summary>
    /// Plays a Home/Explore card's underlying item without navigating â€” backs the hover/keyboard
    /// Play overlay on card shelves (Feature A). A song plays immediately; an album or (non-mood)
    /// playlist is fetched and its tracks are loaded into the queue. Artist and moods/genres cards
    /// have no cheap play action, so they are a no-op here (the card body still navigates).
    /// </summary>
    public static async Task PlayItemAsync(HomeSectionItem item, IPlayerService? player, IYTMusicClient? client)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (player is null)
        {
            return;
        }

        switch (item)
        {
            case HomeSectionItem.SongItem song:
                await player.PlaySongAsync(song.Song).ConfigureAwait(true);
                break;

            case HomeSectionItem.AlbumItem album:
                await PlayBrowseCollectionAsync(album.Album.Id, player, client).ConfigureAwait(true);
                break;

            case HomeSectionItem.PlaylistItem playlist when !MoodCategoryId.IsMoodCategory(playlist.Pl.Id):
                await PlayBrowseCollectionAsync(playlist.Pl.Id, player, client).ConfigureAwait(true);
                break;

            // Artist cards / moods/genres entry points: no cheap play action â€” navigation only.
        }
    }

    /// <summary>
    /// Fetches the album/playlist identified by <paramref name="browseId"/> and plays its tracks as
    /// the queue (Feature A). Best-effort: a fetch failure or an empty track list is a no-op so a
    /// card Play tap never throws. Reused by the Home/Explore shelves and the Artist page cards.
    /// </summary>
    public static async Task PlayBrowseCollectionAsync(string? browseId, IPlayerService? player, IYTMusicClient? client)
    {
        if (player is null || client is null || string.IsNullOrEmpty(browseId))
        {
            return;
        }

        try
        {
            var detail = await client.GetPlaylistAsync(browseId).ConfigureAwait(true);
            if (detail.Tracks.Count > 0)
            {
                await player.PlayCollectionAsync([.. detail.Tracks], startIndex: 0).ConfigureAwait(true);
            }
        }
        catch
        {
            // A card Play affordance must never crash the shell on a transient fetch failure.
        }
    }

    private static void NavigateToDetail(Frame? frame, string pageTypeName, string id)
    {
        // Route through the shared NavigationHelper (which resolves the shell frame via the
        // navigation service) so card activation uses the same path as every other clickable
        // affordance. The frame parameter is retained for API compatibility with callers but the
        // helper owns the late-bind Type.GetType guard.
        _ = frame;
        NavigationHelper.NavigateToDetail(pageTypeName, id);
    }
}
