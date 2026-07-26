using System;
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

    public MiniPlayerView()
    {
        var services = (Application.Current as App)?.Services;
        _player = services?.GetService<IPlayerService>();
        var lyricsService = services?.GetService<ILyricsService>();
        var queueService = services?.GetService<IQueueService>();
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

        ApplyToggleTints();
        UpdateQueueEmptyVisual();
        ApplyLanguage();
        Unloaded += (_, _) =>
        {
            if (_player is not null)
            {
                _player.PropertyChanged -= OnPlayerPropertyChanged;
            }

            if (Lyrics is not null)
            {
                Lyrics.ActiveLineChanged -= OnActiveLineChanged;
                Lyrics.Dispose();
            }

            if (Queue is not null)
            {
                Queue.PropertyChanged -= OnQueueVmPropertyChanged;
                Queue.Dispose();
            }
        };
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
    internal void OnEnteredMiniPlayer()
    {
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
            UpdateQueueEmptyVisual();
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
        MiniQueueEmptyText.Visibility = Queue is { HasUpNext: true } ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnQueueVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueueViewModel.HasUpNext) or null or "")
        {
            DispatcherQueue.TryEnqueue(UpdateQueueEmptyVisual);
        }
    }

    /// <summary>Keeps the active synced line in view while the lyrics panel is open.</summary>
    private void OnActiveLineChanged(object? sender, LyricLineItem? line)
    {
        if (line is null || _panel != MiniPanel.Lyrics)
        {
            return;
        }

        // The plain jump (not the side panel's animated glide) — at ~300px tall the glide buys
        // little and the layout-race it drags in (known-issues: half-visible previous line) a lot.
        MiniLyricsList.ScrollIntoView(line, ScrollIntoViewAlignment.Leading);
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
