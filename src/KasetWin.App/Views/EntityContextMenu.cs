using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Views;

/// <summary>
/// Shared builder for the right-click (context) menus of NON-song entity cards/rows — albums,
/// playlists, artists and podcast shows — across Home, Explore, Search, Library, Artist and the
/// see-all list pages (tester request #190: "context menus everywhere, like YT Music").
/// </summary>
/// <remarks>
/// <para>
/// Menus are <b>contextual</b>: an item only appears when its underlying action actually exists for
/// that entity — Play only when a browse collection can be fetched and played
/// (<see cref="FeedNavigation.PlayBrowseCollectionAsync"/>), Go-to-artist only for a navigable
/// artist id, Share only when <see cref="ShareUrlBuilder"/> yields a URL (Req 34.2). No new
/// actions are introduced here; everything routes through the same helpers the click/tap
/// affordances already use.
/// </para>
/// <para>
/// The card templates are often polymorphic (<see cref="HomeCardItem"/> unions), so menus are built
/// dynamically from <c>ContextRequested</c> (the TrendingListPage pattern) instead of a static
/// <c>ContextFlyout</c>. Callers with page-specific extras (Library's delete-playlist, Home's
/// add-to-favorites, Podcasts' subscribe) call <see cref="BuildFor(object?)"/>, append their own
/// items, then <see cref="Show"/>.
/// </para>
/// </remarks>
internal sealed class EntityContextMenu
{
    private readonly IPlayerService? _player;
    private readonly IYTMusicClient? _client;
    private readonly IQueueService? _queue;
    private readonly Notifications.IInAppNotifier? _notifier;

    public EntityContextMenu(
        IPlayerService? player,
        IYTMusicClient? client,
        IQueueService? queue,
        Notifications.IInAppNotifier? notifier)
    {
        _player = player;
        _client = client;
        _queue = queue;
        _notifier = notifier;
    }

    /// <summary>Resolves the standard collaborators from the app DI container.</summary>
    public static EntityContextMenu FromServices(IServiceProvider services) => new(
        services.GetService<IPlayerService>(),
        services.GetService<IYTMusicClient>(),
        services.GetService<IQueueService>(),
        services.GetService<Notifications.IInAppNotifier>());

    /// <summary>
    /// Builds and shows the context menu for the sender's <c>DataContext</c> entity. Returns
    /// <see langword="true"/> (and marks the event handled) when a menu was shown; entities with no
    /// available action (e.g. mood chips) show nothing.
    /// </summary>
    public bool TryShow(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return false;
        }

        var menu = BuildFor(element.DataContext);
        if (menu is null || menu.Items.Count == 0)
        {
            return false;
        }

        Show(menu, element, e);
        return true;
    }

    /// <summary>Shows an already-built menu at the pointer position (or the element as fallback).</summary>
    public static void Show(MenuFlyout menu, FrameworkElement element, ContextRequestedEventArgs e)
    {
        if (e.TryGetPosition(element, out var point))
        {
            menu.ShowAt(element, point);
        }
        else
        {
            menu.ShowAt(element);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Builds the context-appropriate menu for a card/row model, or <see langword="null"/> when the
    /// entity offers no action (mood chips, unshareable/unnavigable leftovers).
    /// </summary>
    public MenuFlyout? BuildFor(object? model) => model switch
    {
        Song song => BuildForSong(song),
        Album album => BuildForAlbum(album),
        Playlist playlist => BuildForPlaylist(playlist),
        Artist artist => BuildForArtist(artist),
        PodcastShow show => BuildForShow(show),
        HomeCardItem card => BuildFor(Unwrap(card.Model)),
        HomeSectionItem item => BuildFor(Unwrap(item)),
        _ => null,
    };

    /// <summary>Unwraps the Home/Explore union into its concrete model.</summary>
    private static object? Unwrap(HomeSectionItem item) => item switch
    {
        HomeSectionItem.SongItem s => s.Song,
        HomeSectionItem.AlbumItem a => a.Album,
        HomeSectionItem.PlaylistItem p => p.Pl,
        HomeSectionItem.ArtistItem ar => ar.Artist,
        _ => null,
    };

    /// <summary>Play next / add to queue / go to album / go to artist / share (YT-Music style).</summary>
    public MenuFlyout BuildForSong(Song song)
    {
        var menu = new MenuFlyout();

        AddItem(menu, Localization.UiStrings.MenuPlayNext, () => PlayTrackNext(song));
        AddItem(menu, Localization.UiStrings.MenuAddToQueue, () => AddTrackToQueue(song));

        if (song.HasAlbumLink)
        {
            AddItem(menu, Localization.UiStrings.MenuGoToAlbum, () => Navigation.NavigationHelper.NavigateToSongAlbum(song));
        }

        if (song.HasArtistLink)
        {
            AddItem(menu, Localization.UiStrings.MenuGoToArtist, () => Navigation.NavigationHelper.NavigateToSongArtist(song));
        }

        AddShare(menu, ShareUrlBuilder.TryCreate(song));
        return menu;
    }

    /// <summary>Play / go to artist (when a navigable id exists) / share (when shareable).</summary>
    public MenuFlyout? BuildForAlbum(Album album)
    {
        var menu = new MenuFlyout();
        AddPlayCollection(menu, album.Id);
        AddGoToArtist(menu, FirstNavigableArtistId(album.Artists));
        AddShare(menu, ShareUrlBuilder.TryCreate(album));
        return menu.Items.Count == 0 ? null : menu;
    }

    /// <summary>
    /// Play / share for regular playlists; share-only for podcast shows surfaced as playlists
    /// (<c>MPSP…</c> ids); nothing for moods/genres entry points (they only browse).
    /// </summary>
    public MenuFlyout? BuildForPlaylist(Playlist playlist)
    {
        if (MoodCategoryId.IsMoodCategory(playlist.Id))
        {
            return null;
        }

        var menu = new MenuFlyout();
        if (playlist.Id.StartsWith("MPSP", StringComparison.Ordinal))
        {
            // A podcast show in playlist clothing: play/queue don't apply (episodes, not tracks);
            // share uses the podcast browse URL, offered only for a navigable MPSPP… id.
            var url = ShareUrlBuilder.BuildUrl(ShareContentKind.Podcast, playlist.Id);
            AddShare(menu, url is null ? null : new ShareTarget(playlist.Title, playlist.Author?.Name, url));
        }
        else
        {
            AddPlayCollection(menu, playlist.Id);
            AddShare(menu, ShareUrlBuilder.TryCreate(playlist));
        }

        return menu.Items.Count == 0 ? null : menu;
    }

    /// <summary>Open the artist page / share (public <c>UC…</c> channels only, Req 34.2).</summary>
    public MenuFlyout? BuildForArtist(Artist artist)
    {
        var menu = new MenuFlyout();
        AddGoToArtist(menu, artist.Id);
        AddShare(menu, ShareUrlBuilder.TryCreate(artist));
        return menu.Items.Count == 0 ? null : menu;
    }

    /// <summary>Share for a podcast show card (navigation happens on click; no new actions).</summary>
    public MenuFlyout? BuildForShow(PodcastShow show)
    {
        var menu = new MenuFlyout();
        AddShare(menu, ShareUrlBuilder.TryCreate(show));
        return menu.Items.Count == 0 ? null : menu;
    }

    // ── Item helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Appends "Putar" that fetches the album/playlist and plays it as the queue.</summary>
    public void AddPlayCollection(MenuFlyout menu, string? browseId)
    {
        if (_player is null || _client is null || string.IsNullOrEmpty(browseId))
        {
            return;
        }

        AddItem(menu, Localization.UiStrings.PlayLabel, async () =>
            await FeedNavigation.PlayBrowseCollectionAsync(browseId, _player, _client));
    }

    /// <summary>Appends "Buka halaman artis" when <paramref name="artistId"/> is navigable.</summary>
    public static void AddGoToArtist(MenuFlyout menu, string? artistId)
    {
        if (ParsingHelpers.IsNavigableArtistId(artistId))
        {
            AddItem(menu, Localization.UiStrings.MenuGoToArtist, () => Navigation.NavigationHelper.NavigateToArtist(artistId));
        }
    }

    /// <summary>Appends "Bagikan" only when the entity has a shareable URL (Req 34.2).</summary>
    public void AddShare(MenuFlyout menu, ShareTarget? target)
    {
        if (target is null)
        {
            return;
        }

        AddItem(menu, Localization.UiStrings.MenuShare, () =>
        {
            if (!Sharing.ShareInvoker.TryShow(App.Current.MainWindow, target))
            {
                _notifier?.Show(Localization.UiStrings.ToastActionUnavailable(Localization.UiStrings.MenuShare));
            }
        });
    }

    /// <summary>Appends a plain menu item.</summary>
    public static void AddItem(MenuFlyout menu, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static string? FirstNavigableArtistId(IReadOnlyList<Artist> artists)
    {
        foreach (var artist in artists)
        {
            if (ParsingHelpers.IsNavigableArtistId(artist.Id))
            {
                return artist.Id;
            }
        }

        return null;
    }

    /// <summary>Queues a song to play right after the current one (same call the detail VMs make).</summary>
    private void PlayTrackNext(Song song)
    {
        if (_queue is null)
        {
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.InsertNext([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastPlayingNext(song.Title));
    }

    /// <summary>Appends a song to the play queue (same call the detail VMs make).</summary>
    private void AddTrackToQueue(Song song)
    {
        if (_queue is null)
        {
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.AppendDeduplicated([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastAddedToQueue(song.Title));
    }
}
