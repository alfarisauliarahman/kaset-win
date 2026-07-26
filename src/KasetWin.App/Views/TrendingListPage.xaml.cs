using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Full "Selengkapnya" list for a Trending shelf: the same items shown as a numbered vertical list
/// (rank + cover + title + subtitle). Receives the <see cref="HomeSectionView"/> as its navigation
/// parameter; activation routes the same way as the Explore cards.
/// </summary>
public sealed partial class TrendingListPage : Page
{
    private readonly IPlayerService? _player;
    private readonly IQueueService? _queue;
    private readonly Notifications.IInAppNotifier? _notifier;

    /// <summary>
    /// Section handed off by the caller right before navigating. A complex object must not travel as
    /// the <see cref="Frame"/> navigation parameter (it breaks navigation-state serialization), so
    /// callers set this and navigate with the section title string instead.
    /// </summary>
    public static HomeSectionView? PendingSection { get; set; }

    private HomeSectionView? _section;

    public TrendingListPage()
    {
        this.InitializeComponent();
        var services = App.Current.Services;
        _player = services.GetService<IPlayerService>();
        _queue = services.GetService<IQueueService>();
        _notifier = services.GetService<Notifications.IInAppNotifier>();
    }

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Prefer the handoff; keep the last shown section on back-navigation re-entry.
        _section = PendingSection ?? _section;
        PendingSection = null;

        if (_section is not null)
        {
            TitleText.Text = _section.Title;

            // Video shelves show a wrapping grid of 16:9 cards; everything else the numbered list.
            if (_section.IsVideoSection)
            {
                VideoGrid.ItemsSource = _section.Items;
                VideoGrid.Visibility = Visibility.Visible;
                ItemsHost.Visibility = Visibility.Collapsed;
            }
            else
            {
                ItemsHost.ItemsSource = _section.Items;
                ItemsHost.Visibility = Visibility.Visible;
                VideoGrid.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnItemClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            FeedNavigation.Activate(this.Frame, card.Model, _player);
        }
    }

    /// <summary>
    /// Right-click on a row/card: only SONG items get the YT-Music-style menu (play next / add to
    /// queue / go to album / go to artist / share). The card union is polymorphic, so the menu is
    /// built here instead of a static <c>ContextFlyout</c>; non-song items simply show nothing.
    /// This page has no ViewModel, so the queue actions call <see cref="IQueueService"/> directly
    /// (the same calls the detail-page ViewModels make).
    /// </summary>
    private void OnCardContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HomeCardItem card } element
            || card.Model is not HomeSectionItem.SongItem songItem)
        {
            return;
        }

        var song = songItem.Song;
        var menu = new MenuFlyout();

        var playNext = new MenuFlyoutItem { Text = Localization.UiStrings.MenuPlayNext };
        playNext.Click += (_, _) => PlayTrackNext(song);
        menu.Items.Add(playNext);

        var addToQueue = new MenuFlyoutItem { Text = Localization.UiStrings.MenuAddToQueue };
        addToQueue.Click += (_, _) => AddTrackToQueue(song);
        menu.Items.Add(addToQueue);

        // Contextual: only offered when the song actually carries a navigable id.
        if (song.HasAlbumLink)
        {
            var goToAlbum = new MenuFlyoutItem { Text = Localization.UiStrings.MenuGoToAlbum };
            goToAlbum.Click += (_, _) => Navigation.NavigationHelper.NavigateToSongAlbum(song);
            menu.Items.Add(goToAlbum);
        }

        if (song.HasArtistLink)
        {
            var goToArtist = new MenuFlyoutItem { Text = Localization.UiStrings.MenuGoToArtist };
            goToArtist.Click += (_, _) => Navigation.NavigationHelper.NavigateToSongArtist(song);
            menu.Items.Add(goToArtist);
        }

        var share = new MenuFlyoutItem { Text = Localization.UiStrings.MenuShare };
        share.Click += (_, _) =>
        {
            var target = KasetWin.Core.Services.Sharing.ShareUrlBuilder.TryCreate(song);
            if (!Sharing.ShareInvoker.TryShow(App.Current.MainWindow, target))
            {
                _notifier?.Show(Localization.UiStrings.ToastActionUnavailable(Localization.UiStrings.MenuShare));
            }
        };
        menu.Items.Add(share);

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
