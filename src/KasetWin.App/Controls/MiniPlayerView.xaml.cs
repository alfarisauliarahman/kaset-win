using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Lyrics;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Controls;

/// <summary>
/// The compact now-playing surface shown while the shell is in mini-player (CompactOverlay) mode.
/// Binds to the same singleton <see cref="IPlayerService"/> as the full <see cref="PlayerBar"/>, so
/// the two never disagree and switching modes carries no state. Two toggles (lyrics / queue) open an
/// Apple-Music-style bottom panel backed by the same <see cref="LyricsViewModel"/> and
/// <see cref="QueueViewModel"/> shapes the side panel uses; opening asks <see cref="MainWindow"/> to
/// grow the compact window and closing shrinks it back.
/// </summary>
/// <remarks>
/// <para>
/// Seeking mirrors the player bar: the slider follows <see cref="IPlayerService.Progress"/> except
/// while the user is dragging it, and the seek is committed on release — otherwise the periodic
/// progress update fights the drag and the thumb snaps back.
/// </para>
/// <para>
/// The two ViewModels are created eagerly in the constructor (the <see cref="NowPlayingPanel"/>
/// pattern) rather than lazily on first open. That is deliberate: <c>LyricsService.CurrentLyrics</c>
/// is an <c>[ObservableProperty]</c>, so re-publishing the SAME cached result raises no change
/// notification — a ViewModel born mid-track would wait for an event that never comes and show a
/// blank panel. A ViewModel that has been subscribed since startup has already seen every change.
/// </para>
/// </remarks>
public sealed partial class MiniPlayerView : UserControl
{
    private readonly IPlayerService? _player;
    private bool _userSeeking;

    /// <summary>Which bottom panel is open, if any.</summary>
    private enum MiniPanel
    {
        None,
        Lyrics,
        Queue,
    }

    private MiniPanel _panel = MiniPanel.None;

    /// <summary>Backs the lyrics panel. Null only when DI is unavailable (design time).</summary>
    public LyricsViewModel? Lyrics { get; }

    /// <summary>Backs the queue panel. Null only when DI is unavailable (design time).</summary>
    public QueueViewModel? Queue { get; }

    /// <summary>
    /// The tail of <see cref="QueueViewModel.History"/> shown dimmed above the now-playing card.
    /// The side panel renders the whole history; at mini scale anything beyond
    /// <see cref="PlayedTailMax"/> rows would push "Up next" out of the ~300px viewport.
    /// </summary>
    // INTERNAL on purpose, and it must stay that way: a PUBLIC property on a XAML UserControl is
    // catalogued into XamlTypeInfo, and a generic collection drags its item type in with it —
    // Song, whose init-only members the generator then tries to write setters for (36× CS8852).
    // {x:Bind} only needs same-assembly visibility, so internal costs nothing.
    internal ObservableCollection<Song> PlayedTail { get; } = [];

    private const int PlayedTailMax = 3;

    public MiniPlayerView()
    {
        var services = (Application.Current as App)?.Services;
        _player = services?.GetService<IPlayerService>();
        var lyricsService = services?.GetService<ILyricsService>();
        var queueService = services?.GetService<IQueueService>();
        _queueService = queueService;
        _music = services?.GetService<KasetWin.Core.Services.Api.IYTMusicClient>();
        if (_player is not null && lyricsService is not null)
        {
            Lyrics = new LyricsViewModel(_player, lyricsService);
            Lyrics.ActiveLineChanged += OnActiveLineChanged;
        }

        if (_player is not null && queueService is not null)
        {
            Queue = new QueueViewModel(queueService, _player);
            Queue.PropertyChanged += OnQueueVmPropertyChanged;
        }

        this.InitializeComponent();

        if (_player is not null)
        {
            DataContext = _player;
            _player.PropertyChanged += OnPlayerPropertyChanged;
            MiniSeekSlider.IsEnabled = !_player.IsLive;
        }

        MiniSeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), handledEventsToo: true);
        MiniSeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);
        MiniSeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);

        // Wheel forwarding for the mini queue strip must observe HANDLED events too: the lists'
        // inner (disabled) ScrollViewer marks the wheel handled before a plain XAML handler would
        // ever fire — the exact gotcha LibraryPage documented — so with the plain attribute the
        // strip simply could not be scrolled and 49 queued tracks read as "ga ngeload banyak".
        // Attached on the whole panel so the card and headers forward too, not just the lists.
        MiniQueuePanel.AddHandler(
            PointerWheelChangedEvent,
            new PointerEventHandler(OnMiniQueueWheel),
            handledEventsToo: true);

        ApplyToggleTints();
        RefreshPlayedTail();
        UpdateQueueEmptyVisual();
        ApplyLanguage();
        // NO Unloaded teardown — deliberately, and this exact line has a body count. This control
        // is constructed once and lives inside MainWindow for the window's whole lifetime; Unloaded
        // fires every time the mini layout leaves the visual tree, NOT at shutdown. Disposing the
        // view models here unsubscribed them from the queue/lyrics services after the FIRST mini
        // session, so every later session only ever saw the snapshot taken on entry ("Selanjutnya
        // ga ngeload" — round 14 through 16; the diag trace proved the data fresh at open and the
        // updates dead after). Same failure shape as PlayerBar's sleep-timer Expired handler.
        // The subscriptions' lifetime IS the window's lifetime; the OS reclaims it all at exit.
    }

    /// <summary>Applies the app language to this view's labels (see <see cref="PlayerBar.ApplyLanguage"/>).</summary>
    internal void ApplyLanguage()
    {
        Accessibility.A11y.Label(MiniPreviousButton, Localization.UiStrings.TipPrevious);
        Accessibility.A11y.Label(MiniPlayPauseButton, Localization.UiStrings.TipPlayPause);
        Accessibility.A11y.Label(MiniNextButton, Localization.UiStrings.TipNext);
        Accessibility.A11y.Label(MiniRestoreButton, Localization.UiStrings.TipExitMiniPlayer);
        Accessibility.A11y.Label(MiniLyricsButton, Localization.UiStrings.TipLyrics);
        Accessibility.A11y.Label(MiniQueueButton, Localization.UiStrings.TipQueue);
        Accessibility.A11y.Name(MiniSeekSlider, Localization.UiStrings.A11ySeekSlider);
        Accessibility.A11y.Name(MiniLyricsList, Localization.UiStrings.A11yLyricsList);
        Accessibility.A11y.Name(MiniQueueList, Localization.UiStrings.A11yQueueList);
        Accessibility.A11y.Name(MiniPlayedList, Localization.UiStrings.A11yPlayedList);
        MiniQueuePlayedHeader.Text = Localization.UiStrings.QueuePlayedHeader;
        MiniQueueNowPlayingHeader.Text = Localization.UiStrings.QueueNowPlaying;
        MiniQueueUpNextHeader.Text = Localization.UiStrings.QueueUpNextHeader;
        MiniQueueEmptyText.Text = Localization.UiStrings.QueueEmpty;
        UpdateLyricsEmptyText();
        MiniTrackInfo.ApplyLanguage();
        MiniNowPlayingTrackInfo.ApplyLanguage();
    }

    /// <summary>
    /// Called when the shell enters mini-player mode: re-syncs the scrubber, which does not receive
    /// progress updates while the view is collapsed, and resets the bottom panel — the window has
    /// just been sized to the base 400×150, so the view must match it.
    /// </summary>
    /// <summary>
    /// Re-syncs the queue panel's data when entering the mini player. Round 15 reported "Up next"
    /// staying stale after the queue ran out until the FULL player was visited once; the exact
    /// trigger is unproven, but visiting full mode amounts to a resync, so entering mini performs
    /// one explicitly. Cheap (a list rebuild) and honest about being a mitigation, not a root fix.
    /// </summary>
    private readonly IQueueService? _queueService;
    private readonly KasetWin.Core.Services.Api.IYTMusicClient? _music;

    /// <summary>VideoId the radio autofill last seeded for (once per track; see below).</summary>
    private string? _radioSeededFor;

    /// <summary>
    /// The full panel's <c>EnsureUpNextFilledAsync</c>, verbatim in behaviour: when "Up next" is
    /// running low, seed the queue with the current track's radio. This is THE "priming" the owner
    /// kept having to do by hand — the full panel performed this fill on open and the mini never
    /// did, so entering mini directly off a one-track queue showed an honest-but-useless empty
    /// list until full mode was visited once. Same guards: once per videoId, only under 10
    /// upcoming, appended deduplicated, best-effort.
    /// </summary>
    private async System.Threading.Tasks.Task EnsureMiniUpNextFilledAsync()
    {
        var videoId = _player?.CurrentTrack?.VideoId;
        if (_music is null || _queueService is null || Queue is null
            || string.IsNullOrEmpty(videoId) || videoId == _radioSeededFor
            || Queue.UpNext.Count >= 10)
        {
            return;
        }

        _radioSeededFor = videoId;
        try
        {
            var radio = await _music.GetRadioQueueAsync(videoId);
            KasetWin.Core.Diag.Write($"mini-queue radio fill videoId={videoId} got={radio.Songs.Count}");
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

    private void ResyncQueuePanel()
    {
        Queue?.Resync();
        RefreshPlayedTail();
        UpdateQueueEmptyVisual();
        _ = EnsureMiniUpNextFilledAsync();

        // The resync did NOT cure the stale panel (round 16), which means the mini's view model
        // already held this data and the divergence is elsewhere. This line is the reproduction
        // instrument: compare it against what the panel visibly shows next time it happens.
        if (Queue is { } q)
        {
            KasetWin.Core.Diag.Write(
                $"mini-queue open: history={q.History.Count} upnext={q.UpNext.Count} "
                + $"now={q.NowPlaying?.VideoId ?? "<none>"} tracks={q.Tracks.Count}");
        }
    }

    internal void OnEnteredMiniPlayer()
    {
        ResyncQueuePanel();

        if (_player is not null)
        {
            MiniSeekSlider.Value = _player.Progress;
            MiniSeekSlider.IsEnabled = !_player.IsLive;
        }

        ResetPanels();
        ApplyLanguage();
    }

    /// <summary>
    /// Collapses the bottom panel WITHOUT touching the window. Called on the way out of mini-player
    /// mode — which can run while the window is hidden in the tray (the deferred exit, ADR 0003), so
    /// this must never resize or re-show anything; it only resets XAML state.
    /// </summary>
    internal void ResetPanels() => ApplyPanel(MiniPanel.None, resizeWindow: false);

    // ── Bottom panel (lyrics / queue) ────────────────────────────────────────────────────────────

    private void OnLyricsToggleClick(object sender, RoutedEventArgs e) => TogglePanel(MiniPanel.Lyrics);

    private void OnQueueToggleClick(object sender, RoutedEventArgs e) => TogglePanel(MiniPanel.Queue);

    private void TogglePanel(MiniPanel panel) =>
        ApplyPanel(_panel == panel ? MiniPanel.None : panel, resizeWindow: true);

    /// <summary>
    /// Shows the requested panel (or none), flips the grid rows so the panel takes all the extra
    /// height, and — when <paramref name="resizeWindow"/> — asks the window to grow or shrink.
    /// </summary>
    private void ApplyPanel(MiniPanel panel, bool resizeWindow)
    {
        if (panel != MiniPanel.None && (Lyrics is null || Queue is null))
        {
            return;
        }

        _panel = panel;
        var expanded = panel != MiniPanel.None;

        MainRow.Height = expanded ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        PanelRow.Height = expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MiniPanelHost.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MiniLyricsPanel.Visibility = panel == MiniPanel.Lyrics ? Visibility.Visible : Visibility.Collapsed;
        MiniQueuePanel.Visibility = panel == MiniPanel.Queue ? Visibility.Visible : Visibility.Collapsed;
        ApplyToggleTints();

        if (panel == MiniPanel.Queue)
        {
            // Opening the queue tab is the moment the fill matters — same trigger the full panel
            // uses (its fill runs when the panel opens).
            _ = EnsureMiniUpNextFilledAsync();
        }

        if (resizeWindow)
        {
            ((Application.Current as App)?.MainWindow as MainWindow)?.SetMiniPlayerExpanded(expanded);
        }

        // The window height itself cannot be tweened smoothly (AppWindow.Resize lives outside the
        // XAML compositor, and hand-rolled DispatcherTimer tweens both stutter and hammer AppWindow —
        // ADR 0003 territory), so the resize is instant and the CONTENT animates: the panel fades
        // and slides in over ~200ms. Started AFTER the resize so the first animated frame already
        // renders in the tall window. Restarting on lyrics ⇄ queue switches is deliberate feedback.
        // On collapse — including ResetPanels while the window is hidden in the tray — the
        // storyboard is stopped, which is pure XAML state and resets opacity/offset to base values.
        if (expanded)
        {
            MiniPanelEnterStoryboard.Begin();
        }
        else
        {
            MiniPanelEnterStoryboard.Stop();
        }

        if (panel == MiniPanel.Lyrics && Lyrics is not null)
        {
            UpdateLyricsEmptyText();
            // Populate for the track already playing (the panel may have been closed for the whole
            // song so far); cache + single-flight in the service make this cheap.
            _ = Lyrics.RefreshAsync();
            ScrollToActiveLine();
        }
        else if (panel == MiniPanel.Queue)
        {
            RefreshPlayedTail();
            UpdateQueueEmptyVisual();
            ScrollToMiniNowPlaying();
        }
    }

    /// <summary>Accent-tints the icon of whichever panel toggle is active (the like-button pattern).</summary>
    private void ApplyToggleTints()
    {
        MiniLyricsIcon.Foreground = BrushFor(_panel == MiniPanel.Lyrics);
        MiniQueueIcon.Foreground = BrushFor(_panel == MiniPanel.Queue);
    }

    private static Microsoft.UI.Xaml.Media.Brush BrushFor(bool active) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            active ? "AccentFillColorDefaultBrush" : "TextFillColorPrimaryBrush"];

    /// <summary>
    /// Picks the empty-state wording (mirrors <c>NowPlayingPanel.UpdateLyricsEmptyText</c>): idle
    /// prompt when nothing plays, "unavailable" once a track is loaded, captions wording for podcasts.
    /// </summary>
    private void UpdateLyricsEmptyText()
    {
        MiniLyricsEmptyText.Text = _player?.CurrentTrack is { } track
            ? (track.IsPodcastEpisode
                ? Localization.UiStrings.CaptionsUnavailable
                : Localization.UiStrings.LyricsUnavailable)
            : Localization.UiStrings.LyricsEmpty;
    }

    private void UpdateQueueEmptyVisual()
    {
        // The empty text overlays the whole queue scroller, so it may only show when there is
        // truly nothing to render — a queue with only history or a now-playing card still counts.
        var hasAnything = Queue is { } q && (q.HasUpNext || q.HasNowPlaying || q.HasHistory);
        MiniQueueEmptyText.Visibility = hasAnything ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Re-mirrors the last <see cref="PlayedTailMax"/> entries of the queue history into
    /// <see cref="PlayedTail"/>. Runs on every section rebuild — the collection is tiny.
    /// </summary>
    private void RefreshPlayedTail()
    {
        PlayedTail.Clear();
        if (Queue is null)
        {
            return;
        }

        for (var i = Math.Max(0, Queue.History.Count - PlayedTailMax); i < Queue.History.Count; i++)
        {
            PlayedTail.Add(Queue.History[i]);
        }
    }

    private void OnQueueVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // QueueViewModel raises all three section flags on every rebuild (RebuildSections), so
        // HasHistory doubles as the "sections changed" signal for the played tail.
        if (e.PropertyName is nameof(QueueViewModel.HasUpNext) or nameof(QueueViewModel.HasHistory)
            or nameof(QueueViewModel.HasNowPlaying) or null or "")
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshPlayedTail();
                UpdateQueueEmptyVisual();
                ScrollToMiniNowPlaying(); // no-op unless the queue panel is open and measured
            });
        }
    }

    /// <summary>
    /// Parks the queue scroller at the "Now playing" card so the dimmed played tail sits above the
    /// fold and "Up next" is what shows by default (the side panel's <c>ScrollToNowPlaying</c> at
    /// mini scale). Queued at low priority so the played list has been measured before the offset
    /// is read — measuring before layout settles is the known-issues "half row" bug class.
    /// </summary>
    private void ScrollToMiniNowPlaying()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_panel != MiniPanel.Queue
                || MiniQueueScroller.Content is not FrameworkElement content
                || MiniNowPlayingCard.Visibility != Visibility.Visible
                || MiniNowPlayingCard.ActualHeight <= 0)
            {
                return;
            }

            // Offset inside the scrolled CONTENT (not the viewport), minus room for the "Played"
            // heading so the previous track stays hinted at above the card (side panel: -28).
            var y = MiniNowPlayingCard.TransformToVisual(content).TransformPoint(default).Y - 24;
            MiniQueueScroller.ChangeView(null, Math.Max(0, y), null, disableAnimation: true);
        });
    }

    /// <summary>
    /// Forwards the wheel from the scroll-disabled inner lists to the queue scroller (the side
    /// panel's <c>OnUpNextWheel</c>) — without this, hovering a row eats the wheel entirely.
    /// </summary>
    private void OnMiniQueueWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        MiniQueueScroller.ChangeView(null, MiniQueueScroller.VerticalOffset - delta, null, disableAnimation: true);
        e.Handled = true;
    }

    /// <summary>
    /// Breathing room kept above the active lyric line so it never sits flush against the panel's
    /// top edge. Smaller than the side panel's 36 — this viewport is a third the height.
    /// </summary>
    private const double MiniLyricsActiveLineTopInset = 12;

    /// <summary>Keeps the active synced line in view while the lyrics panel is open.</summary>
    private void OnActiveLineChanged(object? sender, LyricLineItem? line)
    {
        if (line is null || _panel != MiniPanel.Lyrics)
        {
            return;
        }

        // The side panel's Apple-Music glide, with ONE deliberate difference: the measurement is
        // deferred a dispatcher hop (Low = after bindings AND the layout they trigger). The active
        // line's font grows 20→28 on this very event; measuring synchronously read the PRE-growth
        // offsets, and by the time layout caught up the glide target sat too low — a wrapped
        // active line landed with its top cut off (round 15). The side panel gets away with a
        // synchronous measure only because its 36px inset absorbs the error; the mini's 12px
        // cannot. Re-checked at dispatch: the panel may have closed or the line moved on.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_panel != MiniPanel.Lyrics || !line.IsActive)
            {
                return;
            }

            if (FindDescendant<ScrollViewer>(MiniLyricsList) is { } scroller
                && MiniLyricsList.ContainerFromItem(line) is UIElement container)
            {
                // The tail of the song can only reach the top if there is empty space below it.
                UpdateMiniLyricsTailSpacer(scroller);

                var target = scroller.Content is FrameworkElement content
                    ? container.TransformToVisual(content).TransformPoint(default).Y - MiniLyricsActiveLineTopInset
                    : scroller.VerticalOffset
                        + container.TransformToVisual(scroller).TransformPoint(default).Y
                        - MiniLyricsActiveLineTopInset;

                scroller.ChangeView(null, Math.Max(0, target), null, disableAnimation: false);
            }
            else
            {
                MiniLyricsList.ScrollIntoView(line, ScrollIntoViewAlignment.Leading);
            }
        });
    }

    /// <summary>
    /// Sizes the trailing spacer under the last lyric line to (almost) a full viewport so the
    /// final lines can still glide up to the top (<c>NowPlayingPanel.UpdateLyricsTailSpacer</c> at
    /// mini scale). No-op while the viewport is unmeasured.
    /// </summary>
    private void UpdateMiniLyricsTailSpacer(ScrollViewer scroller)
    {
        if (scroller.ViewportHeight <= 0)
        {
            return;
        }

        // Leave roughly one active line's worth of content visible at the very bottom.
        var wanted = Math.Max(0, scroller.ViewportHeight - 72);
        if (Math.Abs(MiniLyricsTailSpacer.Height - wanted) > 1)
        {
            MiniLyricsTailSpacer.Height = wanted;
        }
    }

    /// <summary>Depth-first visual-tree search (copy of <c>NowPlayingPanel.FindDescendant</c>).</summary>
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

    /// <summary>
    /// Scrolls to the line that is ALREADY active when the panel opens mid-song —
    /// <see cref="LyricsViewModel.ActiveLineChanged"/> only fires on the next line change. Queued at
    /// low priority so the list has been measured first.
    /// </summary>
    private void ScrollToActiveLine()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_panel != MiniPanel.Lyrics || Lyrics is null)
            {
                return;
            }

            if (Lyrics.Lines.FirstOrDefault(l => l.IsActive) is { } active)
            {
                MiniLyricsList.ScrollIntoView(active, ScrollIntoViewAlignment.Leading);
            }
        });
    }

    /// <summary>Click-to-seek: tapping a lyric line jumps playback to its timestamp.</summary>
    private void OnLyricLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLineItem line && Lyrics is not null)
        {
            _ = SafeAsync(() => Lyrics.SeekToLineAsync(line));
        }
    }

    /// <summary>Plays the queue from the tapped row (same behaviour as the side panel).</summary>
    private void OnQueueTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            Queue?.PlayTrackCommand.Execute(song);
        }
    }

    // ── Transport & seek (unchanged behaviour) ───────────────────────────────────────────────────

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        if (e.PropertyName is nameof(IPlayerService.IsLive) or null)
        {
            DispatcherQueue.TryEnqueue(() => MiniSeekSlider.IsEnabled = !_player.IsLive);
        }

        if (e.PropertyName is nameof(IPlayerService.Progress) or null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_userSeeking)
                {
                    MiniSeekSlider.Value = _player.Progress;
                }
            });
        }

        if (e.PropertyName is nameof(IPlayerService.CurrentTrack) or null)
        {
            // The lyrics empty-state wording depends on whether anything is playing at all.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_panel == MiniPanel.Lyrics)
                {
                    UpdateLyricsEmptyText();
                }
            });
        }
    }

    private void OnSeekPointerPressed(object sender, PointerRoutedEventArgs e) => _userSeeking = true;

    private void OnSeekPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_userSeeking)
        {
            return;
        }

        _userSeeking = false;

        // Never seek live content (Req 9.2).
        if (_player is null || _player.IsLive)
        {
            return;
        }

        _ = _player.SeekAsync(MiniSeekSlider.Value);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _ = SafeAsync(() => _player?.PreviousAsync());

    private void OnNextClick(object sender, RoutedEventArgs e) => _ = SafeAsync(() => _player?.NextAsync());

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _ = SafeAsync(() => _player?.TogglePlayPauseAsync());

    /// <summary>Leaves mini-player mode and restores the full shell.</summary>
    private void OnRestoreClick(object sender, RoutedEventArgs e) =>
        ((Application.Current as App)?.MainWindow as MainWindow)?.ToggleMiniPlayer();

    /// <summary>Runs a transport call without letting a playback failure take the overlay down.</summary>
    private static async System.Threading.Tasks.Task SafeAsync(Func<System.Threading.Tasks.Task?> action)
    {
        try
        {
            if (action() is { } task)
            {
                await task;
            }
        }
        catch (Exception)
        {
            // Transport failures are surfaced by the player itself; the overlay stays usable.
        }
    }
}
