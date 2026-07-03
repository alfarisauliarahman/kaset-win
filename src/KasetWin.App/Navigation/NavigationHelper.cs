using KasetWin.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace KasetWin.App.Navigation;

/// <summary>
/// Shared, DRY entry point for "navigate to artist / album / playlist by id" used by every
/// clickable affordance across the shell — Home/Explore card subtitles, detail-page track rows,
/// Search results, the Lyrics header, and the bottom <c>PlayerBar</c> (which lives outside the
/// content <see cref="Microsoft.UI.Xaml.Controls.Frame"/>).
/// </summary>
/// <remarks>
/// <para>
/// Navigation routes through the singleton <see cref="INavigationService"/>, which the shell
/// (<c>MainWindow</c>) initialises once with its content frame. Resolving the service from
/// <c>App.Services</c> here means callers do not need a <see cref="Microsoft.UI.Xaml.Controls.Frame"/>
/// reference of their own — important for the <c>PlayerBar</c>, which is hosted beside the frame
/// rather than inside it.
/// </para>
/// <para>
/// Target page types are resolved by full name with the same late-bind
/// <see cref="Type.GetType(string)"/> guard the rest of the shell uses, so a not-yet-present page is
/// a safe no-op. Every method is a no-op for a null/empty id, so callers can wire links
/// unconditionally and the affordance is only meaningful when a real id exists (ids are never
/// fabricated).
/// </para>
/// </remarks>
internal static class NavigationHelper
{
    private const string AlbumPageTypeName = "KasetWin.App.Views.AlbumPage";
    private const string PlaylistPageTypeName = "KasetWin.App.Views.PlaylistPage";
    private const string ArtistPageTypeName = "KasetWin.App.Views.ArtistPage";

    /// <summary>
    /// Resolves a shell page <see cref="Type"/> from its full name. <see cref="Type.GetType(string)"/>
    /// with a non-assembly-qualified name is unreliable in WinUI (it often returns <c>null</c> for
    /// app types), which silently turned every album/playlist/artist navigation into a no-op. This
    /// falls back to an explicit lookup in this assembly, where all detail pages live. Shared by every
    /// navigation surface (Home/Explore/Search/Library/Artist/Podcasts/YouTube) so the fix is central.
    /// </summary>
    public static Type? ResolvePageType(string typeName) =>
        Type.GetType(typeName) ?? typeof(NavigationHelper).Assembly.GetType(typeName);

    /// <summary>Navigates to the artist page for <paramref name="artistId"/> (a <c>UC…</c> channel id).</summary>
    public static bool NavigateToArtist(string? artistId) => NavigateToDetail(ArtistPageTypeName, artistId);

    /// <summary>Navigates to the album page for <paramref name="albumBrowseId"/> (an <c>MPRE…/OLAK…</c> id).</summary>
    public static bool NavigateToAlbum(string? albumBrowseId) => NavigateToDetail(AlbumPageTypeName, albumBrowseId);

    /// <summary>Navigates to the playlist page for <paramref name="playlistId"/> (a <c>VL…/PL…</c> id).</summary>
    public static bool NavigateToPlaylist(string? playlistId) => NavigateToDetail(PlaylistPageTypeName, playlistId);

    /// <summary>
    /// Navigates the shell's content frame to the Lyrics panel for whatever is currently playing.
    /// Used by the <c>PlayerBar</c> Lyrics affordance (Feature/Bug 3); the page itself reads the
    /// current track from <see cref="KasetWin.Core.Services.Player.IPlayerService"/> and resolves
    /// lyrics via <see cref="KasetWin.Core.Services.Lyrics.ILyricsService"/>, so no parameter is
    /// needed. <see cref="Views.LyricsPage"/> lives in this assembly, so it is referenced directly
    /// rather than late-bound. No-op when the navigation service is unavailable.
    /// </summary>
    public static bool NavigateToLyrics()
    {
        var nav = (Application.Current as App)?.Services.GetService<INavigationService>();
        return nav?.NavigateTo(typeof(Views.LyricsPage)) ?? false;
    }

    /// <summary>Navigates to the first artist on <paramref name="song"/> that carries a real channel id.</summary>
    public static bool NavigateToSongArtist(Song? song) => NavigateToArtist(song?.PrimaryArtistId);

    /// <summary>Navigates to the album/single page <paramref name="song"/> belongs to, when one is known.</summary>
    public static bool NavigateToSongAlbum(Song? song) => NavigateToAlbum(song?.AlbumBrowseId);

    /// <summary>
    /// Navigates the shell's content frame to <paramref name="pageTypeName"/>, passing
    /// <paramref name="id"/> as the navigation parameter. No-op when the id is empty, the navigation
    /// service is unavailable, or the page type is not present in the loaded assembly.
    /// </summary>
    public static bool NavigateToDetail(string pageTypeName, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var nav = (Application.Current as App)?.Services.GetService<INavigationService>();
        if (nav is null || ResolvePageType(pageTypeName) is not { } pageType)
        {
            return false;
        }

        return nav.NavigateTo(pageType, id);
    }
}
