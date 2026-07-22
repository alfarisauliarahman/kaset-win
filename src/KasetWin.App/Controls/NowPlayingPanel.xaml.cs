using System.Collections.ObjectModel;
using System.Linq;
using KasetWin.App.Accessibility;
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
        ApplyLanguage();

        // The SelectorBar's inner scroller can end up 1-2px scrollable, which renders as tiny
        // up/down arrows next to the last tab — kill its scrolling outright. The template child
        // may not exist yet at Loaded, so retry on SizeChanged until it is found.
        QueueTabs.Loaded += (_, _) => DisableSelectorBarScrolling();
        QueueTabs.SizeChanged += (_, _) => DisableSelectorBarScrolling();

        Lyrics.ActiveLineChanged += OnActiveLineChanged;
        // The "Sumber: …" attribution follows whichever provider answered the current lookup.
        Lyrics.PropertyChanged += OnLyricsVmPropertyChanged;
        if (_controller is not null)
        {
            _controller.Changed += OnControllerChanged;
        }

        // Refill "Berikutnya" whenever the playing track changes while the queue panel is open —
        // opening-only refill made the autofill feel intermittent.
        Queue.PropertyChanged += OnQueueVmPropertyChanged;

        // The lyrics header (Lirik vs Subtitel CC) and the CC picker follow the current track.
        if (_player is not null)
        {
            _player.PropertyChanged += OnPlayerPropertyChanged;
        }

        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Applies the app language to the panel's own labels. The panel lives for the whole window
    /// (unlike pages, which are recreated on navigation), so this is re-invoked on language change.
    /// </summary>
    internal void ApplyLanguage()
    {
        UpNextTab.Text = Localization.UiStrings.QueueTabUpNext;
        HistoryTab.Text = Localization.UiStrings.QueueTabHistory;
        RelatedTab.Text = Localization.UiStrings.QueueTabRelated;
        NowPlayingHeaderText.Text = Localization.UiStrings.QueueNowPlaying;
        UpNextHeaderText.Text = Localization.UiStrings.QueueUpNextHeader;
        QueueEmptyText.Text = Localization.UiStrings.QueueEmpty;
        RelatedEmptyText.Text = Localization.UiStrings.RelatedEmpty;
        LyricsEmptyText.Text = Localization.UiStrings.LyricsEmpty;
        A11y.Label(CloseButton, Localization.UiStrings.TipClose);
        A11y.Label(CcPickerButton, Localization.UiStrings.TipSubtitles);

        // The three list panes carry no visible heading of their own once a tab is active, so name
        // them for screen readers (no tooltip — hovering a long list to read a label is noise).
        A11y.Name(UpNextList, Localization.UiStrings.A11yQueueList);
        A11y.Name(HistoryList, Localization.UiStrings.A11yHistoryList);
        A11y.Name(LyricsList, Localization.UiStrings.A11yLyricsList);

        NowPlayingTrackInfo.ApplyLanguage();
        Bindings.Update(); // LyricsHeaderText follows the language too
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

    private void OnLyricsVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LyricsViewModel.ActiveProvider) or null or "")
        {
            DispatcherQueue.TryEnqueue(() => this.Bindings.Update());
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

    // ── Subtitle/CC track picker (podcast episodes) ───────────────────────────────────────────

    /// <summary>Header title: podcasts show captions, so the panel is titled accordingly.</summary>
    public string LyricsHeaderText =>
        _player?.CurrentTrack?.IsPodcastEpisode == true
            ? Localization.UiStrings.SubtitlesCcLabel
            : Localization.UiStrings.TipLyrics;

    /// <summary>Whether the CC track picker applies (lyrics mode + podcast episode).</summary>
    public bool ShowCcPicker => IsLyrics && _player?.CurrentTrack?.IsPodcastEpisode == true;

    // ── Lyrics provider attribution ("Sumber: …") ─────────────────────────────────────────────

    /// <summary>
    /// SINGLE HOOK-UP POINT for the lyrics provider name. It currently reads
    /// <c>ILyricsService.ActiveProvider</c> as mirrored by <see cref="LyricsViewModel.ActiveProvider"/>
    /// (the label of whichever provider answered: LRCLib / NetEase / YouTube captions). If Core moves
    /// the provider name onto the lyric result itself, change ONLY this expression — the two
    /// properties below and the XAML binding stay as they are.
    /// </summary>
    private string? LyricsProviderName => Lyrics.ActiveProvider;

    /// <summary>Whether a provider name is known and the attribution line should render.</summary>
    public bool HasLyricsSource => !string.IsNullOrWhiteSpace(LyricsProviderName);

    /// <summary>Localized "Sumber: &lt;provider&gt;" / "Source: &lt;provider&gt;" attribution line.</summary>
    public string LyricsSourceText => HasLyricsSource
        ? string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Localization.UiStrings.LyricsSourceFormat,
            LyricsProviderName)
        : string.Empty;

    /// <summary>Builds the CC menu on open: Nonaktif + every caption track of the current video.</summary>
    private async void OnCcFlyoutOpening(object? sender, object e)
    {
        var videoId = _player?.CurrentTrack?.VideoId;
        var services = ((App)Application.Current).Services;
        var provider = services.GetService<YouTubeCaptionsProvider>();
        var lyricsService = services.GetService<ILyricsService>();
        if (provider is null || lyricsService is null || string.IsNullOrEmpty(videoId))
        {
            return;
        }

        CcFlyout.Items.Clear();
        var (isOff, selectedUrl) = provider.GetSelection(videoId);

        var off = new ToggleMenuFlyoutItem { Text = Localization.UiStrings.CcOff, IsChecked = isOff };
        off.Click += async (_, _) =>
        {
            provider.SelectOff(videoId);
            lyricsService.Invalidate(videoId);
            await Lyrics.RefreshAsync();
        };
        CcFlyout.Items.Add(off);

        IReadOnlyList<CaptionTrack> tracks;
        try
        {
            tracks = await provider.GetTracksAsync(videoId);
        }
        catch (System.Exception)
        {
            tracks = [];
        }

        foreach (var track in tracks)
        {
            var isSelected = !isOff && (selectedUrl == track.BaseUrl
                // No explicit selection yet: the automatic pick highlights the default
                // (first creator track, else the first track at all).
                || (selectedUrl is null && ReferenceEquals(
                    track, tracks.FirstOrDefault(t => !t.IsAsr) ?? tracks.FirstOrDefault())));
            var item = new ToggleMenuFlyoutItem { Text = track.Name, IsChecked = isSelected };
            var chosen = track;
            item.Click += async (_, _) =>
            {
                provider.SelectTrack(videoId, chosen);
                lyricsService.Invalidate(videoId);
                await Lyrics.RefreshAsync();
            };
            CcFlyout.Items.Add(item);
        }

        // Word-level (karaoke) highlight is opt-in: the per-word advance follows the player's
        // progress ticks and can trail speech, so it lives here as a user switch.
        CcFlyout.Items.Add(new MenuFlyoutSeparator());
        var karaoke = new ToggleMenuFlyoutItem { Text = "Sorot per kata", IsChecked = Lyrics.KaraokeEnabled };
        karaoke.Click += (_, _) => Lyrics.KaraokeEnabled = karaoke.IsChecked;
        CcFlyout.Items.Add(karaoke);
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
        if (line is null)
        {
            return;
        }

        // Apple-Music-style transition: animate the active line into the upper third of the panel
        // instead of the jumpy ScrollIntoView-at-top. When the container is virtualized away
        // (user seeked far), fall back to the instant jump.
        if (FindDescendant<ScrollViewer>(LyricsList) is { } scroller
            && LyricsList.ContainerFromItem(line) is UIElement container)
        {
            var y = container.TransformToVisual(scroller).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var target = scroller.VerticalOffset + y - (scroller.ViewportHeight / 3);
            scroller.ChangeView(null, System.Math.Max(0, target), null, disableAnimation: false);
        }
        else
        {
            LyricsList.ScrollIntoView(line, ScrollIntoViewAlignment.Leading);
        }
    }

    /// <summary>Click-to-seek: tapping a lyric/subtitle line jumps playback to its timestamp.</summary>
    private void OnLyricLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLineItem line)
        {
            _ = Lyrics.SeekToLineAsync(line);
        }
    }

    private bool _selectorBarScrollFixed;

    /// <summary>
    /// Disables scrolling inside the queue-tab SelectorBar (whichever scroller type its template
    /// uses) so no vestigial scroll arrows can appear. Idempotent; logs the verdict once.
    /// </summary>
    private void DisableSelectorBarScrolling()
    {
        if (_selectorBarScrollFixed)
        {
            return;
        }

        if (FindDescendant<ScrollView>(QueueTabs) is { } sv)
        {
            sv.VerticalScrollMode = ScrollingScrollMode.Disabled;
            sv.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
            sv.HorizontalScrollMode = ScrollingScrollMode.Disabled;
            sv.HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
            _selectorBarScrollFixed = true;
            KasetWin.Core.Diag.Write("selectorbar: scrolling disabled (ScrollView)");
        }
        else if (FindDescendant<ScrollViewer>(QueueTabs) is { } svr)
        {
            svr.VerticalScrollMode = ScrollMode.Disabled;
            svr.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            svr.HorizontalScrollMode = ScrollMode.Disabled;
            svr.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _selectorBarScrollFixed = true;
            KasetWin.Core.Diag.Write("selectorbar: scrolling disabled (ScrollViewer)");
        }
        else
        {
            KasetWin.Core.Diag.Write("selectorbar: no inner scroller found yet");
        }
    }

    /// <summary>First descendant of <paramref name="root"/> of type <typeparamref name="T"/>, or null.</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : class
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPlayerService.CurrentTrack) or null or "")
        {
            DispatcherQueue.TryEnqueue(() => this.Bindings.Update());
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        Lyrics.ActiveLineChanged -= OnActiveLineChanged;
        Lyrics.PropertyChanged -= OnLyricsVmPropertyChanged;
        Queue.PropertyChanged -= OnQueueVmPropertyChanged;
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
        }
        if (_controller is not null)
        {
            _controller.Changed -= OnControllerChanged;
        }

        Queue.Dispose();
        Lyrics.Dispose();
    }
}
