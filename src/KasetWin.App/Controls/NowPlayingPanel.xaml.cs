using System.Collections.ObjectModel;
using KasetWin.App.Navigation;
using KasetWin.App.ViewModels;
using KasetWin.App.Views;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Lyrics;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Controls;

/// <summary>
/// Apple-Music-style right-hand now-playing panel that docks in the shell and toggles between the
/// play queue (Berikutnya / Riwayat tabs) and the synced lyrics. Driven by the shared
/// <see cref="SidePanelController"/> which the <c>PlayerBar</c> buttons flip.
/// </summary>
public sealed partial class NowPlayingPanel : UserControl
{
    private readonly SidePanelController? _controller;
    private readonly IYTMusicClient? _client;
    private readonly IPlayerService? _player;
    private readonly IQueueService? _queueService;

    /// <summary>videoId the up-next radio autofill last ran for (once per track).</summary>
    private string? _radioSeededFor;

    /// <summary>Backs the queue tabs (Berikutnya / Riwayat).</summary>
    public QueueViewModel Queue { get; }

    /// <summary>Backs the lyrics view.</summary>
    public LyricsViewModel Lyrics { get; }

    /// <summary>"Terkait" related carousels for the current track (watch-next).</summary>
    public ObservableCollection<HomeSectionView> RelatedSections { get; } = [];

    private string? _relatedVideoId;
    private bool _isRelatedLoading;

    /// <summary>"Tentang artis" header from the Related surface (null when absent).</summary>
    public string? AboutTitle { get; private set; }

    /// <summary>Artist bio text from the Related surface (null when absent).</summary>
    public string? AboutText { get; private set; }

    /// <summary>Whether the Related surface carried an artist bio.</summary>
    public bool HasAbout => !string.IsNullOrWhiteSpace(AboutText);

    /// <summary>Whether the related content is being fetched (drives a spinner).</summary>
    public bool IsRelatedLoading => _isRelatedLoading;

    /// <summary>Whether the "Terkait" tab has no content and is not loading (empty state).</summary>
    public bool RelatedIsEmpty => !_isRelatedLoading && RelatedSections.Count == 0;

    public NowPlayingPanel()
    {
        var services = ((App)Application.Current).Services;
        _controller = services.GetService<SidePanelController>();
        _client = services.GetService<IYTMusicClient>();
        _player = services.GetService<IPlayerService>();
        _queueService = services.GetService<IQueueService>();
        Queue = new QueueViewModel(
            services.GetRequiredService<IQueueService>(),
            services.GetRequiredService<IPlayerService>());
        Lyrics = new LyricsViewModel(
            services.GetRequiredService<IPlayerService>(),
            services.GetRequiredService<ILyricsService>());

        this.InitializeComponent();

        Lyrics.ActiveLineChanged += OnActiveLineChanged;
        if (_controller is not null)
        {
            _controller.Changed += OnControllerChanged;
        }

        // Refill "Berikutnya" whenever the playing track changes while the queue panel is open —
        // opening-only refill made the autofill feel intermittent.
        Queue.PropertyChanged += OnQueueVmPropertyChanged;

        Unloaded += OnUnloaded;
    }

    /// <summary>Whether the panel is currently showing the queue.</summary>
    public bool IsQueue => _controller?.Mode == SidePanelMode.Queue;

    /// <summary>Whether the panel is currently showing lyrics.</summary>
    public bool IsLyrics => _controller?.Mode == SidePanelMode.Lyrics;

    // Which queue sub-tab is active: 0 = Berikutnya, 1 = Riwayat, 2 = Terkait. Tracked from the
    // SelectorBar's SelectedItem (reliable in the event) rather than each item's IsSelected, which
    // can lag by one change and swapped the tab contents.
    private int _queueTab;

    /// <summary>Whether the "Berikutnya" queue sub-tab is active.</summary>
    public bool ShowUpNext => IsQueue && _queueTab == 0;

    /// <summary>Whether the "Riwayat" queue sub-tab is active.</summary>
    public bool ShowHistory => IsQueue && _queueTab == 1;

    /// <summary>Whether the "Terkait" queue sub-tab is active.</summary>
    public bool ShowRelated => IsQueue && _queueTab == 2;

    private async void OnControllerChanged()
    {
        // Default the queue to the "Berikutnya" tab each time it opens.
        if (IsQueue)
        {
            _queueTab = 0;
            QueueTabs.SelectedItem = UpNextTab;
        }

        this.Bindings.Update();

        // Refresh lyrics for the current track whenever the lyrics panel becomes visible.
        if (IsLyrics)
        {
            await Lyrics.RefreshAsync();
        }
        else if (IsQueue)
        {
            await EnsureUpNextFilledAsync();
        }
    }

    private async void OnQueueVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueueViewModel.NowPlaying) && IsQueue)
        {
            await EnsureUpNextFilledAsync();
        }
    }

    /// <summary>
    /// Fills "Berikutnya" with the current track's radio when the explicit queue is running low, so
    /// the panel always shows plenty of upcoming songs (YT Music autoplay behaviour). Runs once per
    /// track; appended songs are deduplicated against the queue.
    /// </summary>
    private async System.Threading.Tasks.Task EnsureUpNextFilledAsync()
    {
        var videoId = _player?.CurrentTrack?.VideoId;
        if (_client is null || _queueService is null
            || string.IsNullOrEmpty(videoId) || videoId == _radioSeededFor
            || Queue.UpNext.Count >= 10)
        {
            return;
        }

        _radioSeededFor = videoId;
        try
        {
            var radio = await _client.GetRadioQueueAsync(videoId);
            if (radio.Songs.Count > 0)
            {
                _queueService.AppendDeduplicated(radio.Songs);
            }
        }
        catch (System.Exception)
        {
            // Autofill is best-effort; allow a retry on the next open.
            _radioSeededFor = null;
        }
    }

    private void OnQueueTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _queueTab = sender.SelectedItem == HistoryTab ? 1 : sender.SelectedItem == RelatedTab ? 2 : 0;
        this.Bindings.Update();

        if (_queueTab == 2)
        {
            _ = LoadRelatedAsync();
        }
    }

    /// <summary>
    /// Fetches the "Terkait" carousels for the current track (once per track) and projects them to
    /// the same card views used on Home/Explore.
    /// </summary>
    private async System.Threading.Tasks.Task LoadRelatedAsync()
    {
        var videoId = _player?.CurrentTrack?.VideoId;
        if (_client is null || string.IsNullOrEmpty(videoId) || videoId == _relatedVideoId)
        {
            return;
        }

        _relatedVideoId = videoId;
        RelatedSections.Clear();
        _isRelatedLoading = true;
        this.Bindings.Update();

        try
        {
            var response = await _client.GetSongRelatedAsync(videoId);
            foreach (var section in response.Sections)
            {
                var view = HomeSectionView.FromModel(section);
                if (view.Items.Count > 0)
                {
                    RelatedSections.Add(view);
                }
            }

            AboutTitle = string.IsNullOrWhiteSpace(response.AboutTitle) ? "Tentang artis" : response.AboutTitle;
            AboutText = response.AboutText;
        }
        catch (System.Exception ex)
        {
            // Related is best-effort; leave the section list empty on failure.
            KasetWin.Core.Diag.Write($"related PANEL failed: {ex.GetType().Name}: {ex.Message}");
            _relatedVideoId = null;
        }

        _isRelatedLoading = false;
        this.Bindings.Update();
    }

    private async void OnRelatedItemClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HomeCardItem card })
        {
            return;
        }

        switch (card.Model)
        {
            case HomeSectionItem.SongItem:
                await FeedNavigation.PlayItemAsync(card.Model, _player, _client);
                break;
            case HomeSectionItem.AlbumItem a:
                NavigationHelper.NavigateToAlbum(a.Album.Id);
                _controller?.Close();
                break;
            case HomeSectionItem.PlaylistItem p:
                NavigationHelper.NavigateToPlaylist(p.Pl.Id);
                _controller?.Close();
                break;
            case HomeSectionItem.ArtistItem ar:
                NavigationHelper.NavigateToArtist(ar.Artist.Id);
                _controller?.Close();
                break;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _controller?.Close();

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            Queue.PlayTrackCommand.Execute(song);
        }
    }

    private void OnUpNextWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        UpNextScroller.ChangeView(null, UpNextScroller.VerticalOffset - delta, null, disableAnimation: true);
        e.Handled = true;
    }

    private void OnActiveLineChanged(object? sender, LyricLineItem? line)
    {
        if (line is not null)
        {
            LyricsList.ScrollIntoView(line, ScrollIntoViewAlignment.Leading);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        Lyrics.ActiveLineChanged -= OnActiveLineChanged;
        Queue.PropertyChanged -= OnQueueVmPropertyChanged;
        if (_controller is not null)
        {
            _controller.Changed -= OnControllerChanged;
        }

        Queue.Dispose();
        Lyrics.Dispose();
    }
}
