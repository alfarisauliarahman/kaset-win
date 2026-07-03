using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Search page (Task 14.4, Req 12.1â€“12.4). Hosts an <see cref="AutoSuggestBox"/> whose typing is
/// <b>debounced</b> by <see cref="SearchViewModel"/> (Req 12.2), shows live suggestions while the
/// user types (Req 12.3), and renders grouped results â€” Top Result, Songs, Albums, Artists,
/// Playlists and Podcasts (Req 12.1). Selecting a song plays it; selecting any other result
/// navigates to the matching detail page (Req 12.4).
/// </summary>
/// <remarks>
/// Follows the shell's resolution pattern: the page has a parameterless constructor (required by
/// frame navigation) and resolves its collaborators from <c>App.Services</c> to build the
/// <see cref="SearchViewModel"/>. Detail navigation uses late-bound page types
/// (<see cref="Type.GetType(string)"/>) so the page still builds and runs while sibling detail
/// pages (Album/Artist/Playlist/Podcast) are implemented in parallel tasks â€” a missing target is a
/// no-op rather than a crash.
/// </remarks>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        this.InitializeComponent();

        var services = ((App)Application.Current).Services;
        ViewModel = new SearchViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>());
    }

    /// <summary>The page ViewModel; bound from XAML via <c>x:Bind</c>.</summary>
    public SearchViewModel ViewModel { get; }

    // â”€â”€ Query box (Req 12.2 / 12.3) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Only react to the user typing; programmatic / suggestion-driven changes are ignored so we
        // do not loop or double-search.
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.UpdateQuery(sender.Text);
        }
    }

    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Enter or a chosen suggestion commits the query: run immediately, bypassing the debounce.
        var query = args.ChosenSuggestion as string ?? args.QueryText;
        _ = ViewModel.SearchImmediately(query);
    }

    // â”€â”€ Result selection (Req 12.4) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnTopResultClick(object sender, RoutedEventArgs e)
    {
        switch (ViewModel.TopResult)
        {
            case HomeSectionItem.SongItem s:
                // THROWAWAY DIAGNOSTIC (Bug A): confirm a search top-result song reaches the player.
                _ = ViewModel.PlaySongAsync(s.Song);
                break;
            case HomeSectionItem.AlbumItem a:
                NavigateToDetail("AlbumPage", a.Album.Id);
                break;
            case HomeSectionItem.PlaylistItem p:
                NavigateToDetail("PlaylistPage", p.Pl.Id);
                break;
            case HomeSectionItem.ArtistItem ar:
                NavigateToDetail("ArtistPage", ar.Artist.Id);
                break;
        }
    }

    private void OnSongClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Song song })
        {
            // THROWAWAY DIAGNOSTIC (Bug A): confirm a search song-row click reaches the player.
            _ = ViewModel.PlaySongAsync(song);
        }
    }

    private void OnAlbumClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Album album })
        {
            NavigateToDetail("AlbumPage", album.Id);
        }
    }

    private void OnArtistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Artist artist })
        {
            NavigateToDetail("ArtistPage", artist.Id);
        }
    }

    private void OnPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist playlist })
        {
            NavigateToDetail("PlaylistPage", playlist.Id);
        }
    }

    private void OnPodcastClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist podcast })
        {
            NavigateToDetail("PodcastShowPage", podcast.Id);
        }
    }

    /// <summary>
    /// Navigates the content frame to a detail page resolved by name, passing <paramref name="id"/>
    /// as the parameter. Guarded so a not-yet-implemented target (or an empty id) is a safe no-op.
    /// </summary>
    private void NavigateToDetail(string pageName, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var pageType = Navigation.NavigationHelper.ResolvePageType($"KasetWin.App.Views.{pageName}");
        if (pageType is not null)
        {
            this.Frame?.Navigate(pageType, id);
        }
    }
}
