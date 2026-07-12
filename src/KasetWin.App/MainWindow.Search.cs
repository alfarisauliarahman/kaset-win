using KasetWin.App.Auth;
using KasetWin.App.Hosting;
using KasetWin.App.ViewModels;
using KasetWin.App.Views;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Activation;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Auth;
using KasetWin.Core.Services.Localization;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Globalization;
using System.Linq;
using Windows.Foundation;
using Windows.System;

namespace KasetWin.App;

public sealed partial class MainWindow
{
    // ── Sidebar: search-as-you-type ───────────────────────────────────────────────────────────────

    private async void OnSidebarSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var text = sender.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            // Empty box: surface the local search history (deletable via the row's X).
            sender.ItemsSource = _searchHistory
                .Select(q => new SearchSuggestion(q, IsHistory: true))
                .ToList();
            return;
        }

        var seq = ++_suggestSeq;
        try
        {
            var client = App.Current.Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return;
            }

            var suggestions = await client.GetRichSearchSuggestionsAsync(text);
            if (seq == _suggestSeq)
            {
                sender.ItemsSource = suggestions;
            }
        }
        catch (Exception)
        {
            // Suggestions are best-effort; typing must never surface an error.
        }
    }

    private void OnSidebarSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var suggestion = args.ChosenSuggestion as SearchSuggestion;
        var query = suggestion?.Query ?? args.QueryText;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        RememberSearch(query);

        // Rich rows navigate straight to their entity (artist/album/playlist) or play the song;
        // plain completions land on the full search-results page.
        if (suggestion is { BrowseId: { } browseId })
        {
            var handled = suggestion.PageType switch
            {
                "MUSIC_PAGE_TYPE_ARTIST" => Navigation.NavigationHelper.NavigateToArtist(browseId),
                "MUSIC_PAGE_TYPE_ALBUM" => Navigation.NavigationHelper.NavigateToAlbum(browseId),
                "MUSIC_PAGE_TYPE_PLAYLIST" => Navigation.NavigationHelper.NavigateToPlaylist(browseId),
                "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE" => Navigation.NavigationHelper.NavigateToPodcastShow(browseId),
                // Unknown page type but a podcast-show browse id: still route to the show page
                // (a podcast suggestion must never dead-end on the generic search page).
                _ => browseId.StartsWith("MPSP", StringComparison.Ordinal)
                    && Navigation.NavigationHelper.NavigateToPodcastShow(browseId),
            };
            if (handled)
            {
                return;
            }
        }

        if (suggestion is { VideoId: { } videoId })
        {
            _ = OpenSuggestedSongAsync(videoId);
            return;
        }

        if (Navigation.NavigationHelper.ResolvePageType(PageTypeNamesByTag["Search"]) is { } searchType)
        {
            ContentFrame.Navigate(searchType, query);
        }
    }

    /// <summary>A rich SONG suggestion goes to its album page when one is known; otherwise it plays.</summary>
    private async Task OpenSuggestedSongAsync(string videoId)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        try
        {
            if (client is not null)
            {
                var metadata = await client.GetSongMetadataAsync(videoId);
                if (Navigation.NavigationHelper.NavigateToSongAlbum(metadata.Song))
                {
                    return;
                }

                if (metadata.Song is { } song)
                {
                    await (_player?.PlayCollectionAsync([song], startIndex: 0) ?? Task.CompletedTask);
                }
            }
        }
        catch (Exception)
        {
            // Best-effort; a failed lookup simply does nothing.
        }
    }

    // ── Search history (local, persisted) ────────────────────────────────────────────────────────

    private const string SearchHistoryKey = "SearchHistory";
    private const int SearchHistoryLimit = 10;

    private static System.Collections.Generic.List<string> LoadSearchHistory()
    {
        try
        {
            var raw = Windows.Storage.ApplicationData.Current.LocalSettings.Values[SearchHistoryKey] as string;
            return string.IsNullOrEmpty(raw)
                ? []
                : System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(raw) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void SaveSearchHistory()
    {
        try
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[SearchHistoryKey] =
                System.Text.Json.JsonSerializer.Serialize(_searchHistory);
        }
        catch (Exception)
        {
            // History is a convenience; persistence failures are ignored.
        }
    }

    private void RememberSearch(string query)
    {
        _searchHistory.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        _searchHistory.Insert(0, query);
        if (_searchHistory.Count > SearchHistoryLimit)
        {
            _searchHistory.RemoveRange(SearchHistoryLimit, _searchHistory.Count - SearchHistoryLimit);
        }

        SaveSearchHistory();
    }

    /// <summary>The X on a history row: remove the entry and refresh the open suggestion list.</summary>
    private void OnDeleteHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchSuggestion { IsHistory: true } entry })
        {
            _searchHistory.RemoveAll(q => string.Equals(q, entry.Query, StringComparison.OrdinalIgnoreCase));
            SaveSearchHistory();
            SidebarSearchBox.ItemsSource = _searchHistory
                .Select(q => new SearchSuggestion(q, IsHistory: true))
                .ToList();
        }
    }
}
