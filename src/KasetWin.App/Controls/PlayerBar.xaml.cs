using System;
using System.ComponentModel;
using System.Globalization;
using KasetWin.App.Accessibility;
using KasetWin.App.Hosting;
using KasetWin.App.Navigation;
using KasetWin.App.Sharing;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Controls;

/// <summary>
/// Bottom transport bar (Task 14.1). Binds to the singleton <see cref="IPlayerService"/> resolved
/// from <c>App.Services</c> and exposes the minimal-but-functional playback controls required by
/// the shell: now-playing metadata/thumbnail, play/pause, next/previous, seek, volume, mute,
/// shuffle and repeat (Req 5.1â€“5.8, 9.1).
/// </summary>
/// <remarks>
/// The control surface is intentionally event-driven (Click/ValueChanged) rather than command-based:
/// <see cref="IPlayerService"/> exposes async methods directly and the bar is a thin view over them.
/// Seek is committed when the user finishes dragging the slider (pointer released / capture lost) so
/// the one-way <c>Progress</c> binding does not fight the drag, and is suppressed for live content
/// (Req 9.2).
/// </remarks>
public sealed partial class PlayerBar : UserControl
{
    private readonly IPlayerService? _player;

    /// <summary>Music client used to persist a like/unlike mutation for the current track (Req 37.3).</summary>
    private readonly IYTMusicClient? _music;

    private readonly Notifications.IInAppNotifier? _notifier;

    /// <summary>Play queue used by the cover context menu (add-to-queue / play-next).</summary>
    private readonly IQueueService? _queue;

    /// <summary>Session like state, shared with track lists so like/collection stays in sync.</summary>
    private readonly ILikeStateStore? _likeStore;

    /// <summary>Controls the docked right-hand queue/lyrics panel toggled by the buttons here.</summary>
    private readonly SidePanelController? _sidePanel;

    /// <summary>Local like state for the current track, updated optimistically on toggle.</summary>
    private LikeStatus? _currentLike;

    /// <summary>Whether the bar is showing podcast affordances (thumbs instead of heart).</summary>
    private bool _isPodcastUi;

    private bool _userSeeking;

    public PlayerBar()
    {
        this.InitializeComponent();
        ApplyLanguage();

        // Resolve the shared player service and bind directly to it. Guarded so the control still
        // instantiates in design-time / unexpected contexts where the host is unavailable.
        _player = (Application.Current as App)?.Services.GetService<IPlayerService>();
        _music = (Application.Current as App)?.Services.GetService<IYTMusicClient>();
        _notifier = (Application.Current as App)?.Services.GetService<Notifications.IInAppNotifier>();
        _queue = (Application.Current as App)?.Services.GetService<IQueueService>();
        _likeStore = (Application.Current as App)?.Services.GetService<ILikeStateStore>();
        _sidePanel = (Application.Current as App)?.Services.GetService<SidePanelController>();
        if (_sidePanel is not null)
        {
            _sidePanel.Changed += OnSidePanelChanged;
        }

        // Playback-speed menu (0.5x–3x, like YT Music's "Kecepatan pemutaran").
        var playback = (Application.Current as App)?.Services.GetService<KasetWin.Core.Abstractions.IPlaybackController>();
        foreach (var rate in new[] { 0.5, 0.8, 1.0, 1.2, 1.5, 1.8, 2.0, 2.5, 3.0 })
        {
            var item = new MenuFlyoutItem
            {
                Text = rate == 1.0 ? "Normal" : $"{rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}x",
            };
            var chosen = rate;
            item.Click += (_, _) => _ = playback?.SetPlaybackRateAsync(chosen);
            SpeedFlyout.Items.Add(item);
        }

        if (_player is not null)
        {
            DataContext = _player;
            _player.PropertyChanged += OnPlayerPropertyChanged;
            SeekSlider.IsEnabled = !_player.IsLive;
            UpdateLyricsAvailability();
            UpdateLikeAvailability();
        }

        // Live like sync: refresh the heart when the current track's like is changed elsewhere.
        if (_likeStore is not null)
        {
            _likeStore.Changed += OnLikeStoreChanged;
        }

        // Commit a seek only after the user finishes interacting with the slider. Handled events are
        // observed too because the Slider thumb marks pointer events handled.
        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);

        // The flyout is built by ApplyLanguage (called above), so only the subscription is needed here.
        _sleepTimer.StateChanged += OnSleepTimerStateChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Sleep timer ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure "when to stop" policy, shared with <see cref="IPlayerService"/> via DI so an "end of
    /// this track" timer is enforced at the real track-end event rather than guessed at from a
    /// CurrentTrack change (which also fires when the user skips manually). Falls back to a local
    /// instance only in design-time/host-less contexts, where duration timers still work.
    /// </summary>
    private readonly SleepTimer _sleepTimer =
        (Application.Current as App)?.Services.GetService<SleepTimer>() ?? new SleepTimer();

    /// <summary>
    /// Drives <see cref="_sleepTimer"/>. Runs only while a timer is armed — an idle timer costs no
    /// ticks. One second is fine: the countdown is displayed to the minute.
    /// </summary>
    private DispatcherTimer? _sleepTicker;

    /// <summary>Builds the sleep-timer menu: off, a few durations, and end-of-track.</summary>
    private void BuildSleepTimerFlyout()
    {
        SleepTimerFlyout.Items.Clear();

        var off = new MenuFlyoutItem { Text = Localization.UiStrings.SleepTimerOff };
        off.Click += (_, _) => CancelSleepTimer();
        SleepTimerFlyout.Items.Add(off);
        SleepTimerFlyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var minutes in SleepTimerPresets)
        {
            var label = minutes == 60
                ? Localization.UiStrings.SleepTimerHour
                : Localization.UiStrings.SleepTimerMinutes(minutes);
            var item = new MenuFlyoutItem { Text = label };
            var chosen = minutes;
            item.Click += (_, _) => ArmSleepTimer(chosen);
            SleepTimerFlyout.Items.Add(item);
        }

        SleepTimerFlyout.Items.Add(new MenuFlyoutSeparator());
        var endOfTrack = new MenuFlyoutItem { Text = Localization.UiStrings.SleepTimerEndOfTrack };
        endOfTrack.Click += (_, _) =>
        {
            _sleepTimer.StartEndOfTrack();
            _notifier?.Show(Localization.UiStrings.ToastSleepTimerSet(Localization.UiStrings.SleepTimerAtTrackEnd));
        };
        SleepTimerFlyout.Items.Add(endOfTrack);
    }

    private static readonly int[] SleepTimerPresets = [15, 30, 45, 60];

    private void ArmSleepTimer(int minutes)
    {
        _sleepTimer.StartDuration(TimeSpan.FromMinutes(minutes));
        _notifier?.Show(
            Localization.UiStrings.ToastSleepTimerSet(Localization.UiStrings.SleepTimerInMinutes(minutes)));
    }

    private void CancelSleepTimer()
    {
        if (!_sleepTimer.State.IsArmed)
        {
            return;
        }

        _sleepTimer.Cancel();
        _notifier?.Show(Localization.UiStrings.ToastSleepTimerCancelled);
    }

    /// <summary>
    /// Reflects the timer on the button and starts/stops the ticker. The ticker is created lazily
    /// and stopped whenever the timer disarms, so an unused sleep timer costs nothing.
    /// </summary>
    private void OnSleepTimerStateChanged(object? sender, SleepTimerState state)
    {
        if (state.IsArmed)
        {
            _sleepTicker ??= CreateSleepTicker();
            _sleepTicker.Start();

            // A lit icon is the only persistent signal that playback is going to stop by itself.
            SleepTimerIcon.Foreground =
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

            var remaining = state.Mode == SleepTimerMode.EndOfTrack
                ? Localization.UiStrings.SleepTimerAtTrackEnd
                : FormatRemaining(state.Remaining);
            A11y.Label(SleepTimerButton, Localization.UiStrings.A11ySleepTimerArmed(remaining));
            ApplySleepTimerBadge(state);
            return;
        }

        _sleepTicker?.Stop();
        SleepTimerIcon.Foreground =
            (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        SleepTimerBadge.Visibility = Visibility.Collapsed;
        A11y.Label(SleepTimerButton, Localization.UiStrings.TipSleepTimer);
    }

    private DispatcherTimer CreateSleepTicker()
    {
        var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ticker.Tick += (_, _) =>
        {
            if (_sleepTimer.Advance(TimeSpan.FromSeconds(1)))
            {
                _ = PauseForSleepTimerAsync();
            }
        };
        return ticker;
    }

    /// <summary>Pauses playback because the sleep timer expired, and says so.</summary>
    private async Task PauseForSleepTimerAsync()
    {
        try
        {
            if (_player is { IsPlaying: true })
            {
                await _player.PauseAsync();
            }

            _notifier?.Show(Localization.UiStrings.ToastSleepTimerFired);
        }
        catch (Exception)
        {
            // A failed pause must not take the shell down; the timer has already disarmed itself.
        }
    }

    /// <summary>Shrinks the shell to the compact always-on-top mini player.</summary>
    private void OnMiniPlayerClick(object sender, RoutedEventArgs e) =>
        ((Application.Current as App)?.MainWindow as MainWindow)?.ToggleMiniPlayer();

    /// <summary>
    /// Puts the countdown on the button itself: whole minutes left, or a musical note for the
    /// "end of this track" mode, which has no clock to show. Under a minute it counts seconds, so
    /// the last minute does not sit frozen at "1".
    /// </summary>
    private void ApplySleepTimerBadge(SleepTimerState state)
    {
        if (state.Mode == SleepTimerMode.EndOfTrack)
        {
            SleepTimerBadgeText.Text = "♪";
            SleepTimerBadge.Visibility = Visibility.Visible;
            return;
        }

        SleepTimerBadgeText.Text = state.Remaining >= TimeSpan.FromMinutes(1)
            ? ((int)Math.Ceiling(state.Remaining.TotalMinutes)).ToString(CultureInfo.CurrentCulture)
            : Math.Max(0, (int)state.Remaining.TotalSeconds).ToString(CultureInfo.CurrentCulture);
        SleepTimerBadge.Visibility = Visibility.Visible;
    }

    /// <summary>Formats the countdown coarsely ("12 minutes", then seconds in the final minute).</summary>
    private static string FormatRemaining(TimeSpan remaining) =>
        remaining >= TimeSpan.FromMinutes(1)
            ? Localization.UiStrings.SleepTimerMinutes((int)Math.Ceiling(remaining.TotalMinutes))
            : $"{Math.Max(0, (int)remaining.TotalSeconds)}s";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Seed the volume slider from the player once the control is live. The volume is driven from
        // code rather than a OneWay binding because that binding did not apply the initial value
        // reliably â€” the slider showed 0% at launch while audio actually played at full volume.
        if (_player is not null)
        {
            VolumeSlider.Value = _player.Volume;
            SeekSlider.Value = _player.Progress;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
        }

        if (_likeStore is not null)
        {
            _likeStore.Changed -= OnLikeStoreChanged;
        }

        if (_sidePanel is not null)
        {
            _sidePanel.Changed -= OnSidePanelChanged;
        }

        // The sleep timer is a DI singleton and outlives this control, so leaving the handler
        // attached would keep the bar alive with it.
        _sleepTimer.StateChanged -= OnSleepTimerStateChanged;
        _sleepTicker?.Stop();
    }

    /// <summary>Refreshes the like button when the current track's like state changes elsewhere.</summary>
    private void OnLikeStoreChanged(string videoId)
    {
        if (string.Equals(videoId, _player?.CurrentTrack?.VideoId, StringComparison.Ordinal))
        {
            DispatcherQueue.TryEnqueue(UpdateLikeAvailability);
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPlayerService.IsLive) or null && _player is not null)
        {
            // Live content disables seek (Req 9.2). Marshal to the UI thread.
            DispatcherQueue.TryEnqueue(() => SeekSlider.IsEnabled = !_player.IsLive);
        }

        if (e.PropertyName is nameof(IPlayerService.Volume) or null && _player is not null)
        {
            // Keep the volume slider in sync with the player (e.g. keyboard volume up/down, mute
            // restore). Driven from code because the OneWay binding was unreliable at init.
            DispatcherQueue.TryEnqueue(() => VolumeSlider.Value = _player.Volume);
        }

        if (e.PropertyName is nameof(IPlayerService.Progress) or null && _player is not null)
        {
            // Follow playback position, but NOT while the user is dragging the scrubber â€” otherwise
            // the periodic Progress update fights the drag and the thumb sticks/jumps back.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_userSeeking)
                {
                    SeekSlider.Value = _player.Progress;
                }
            });
        }

        if (e.PropertyName is nameof(IPlayerService.CurrentTrack) or null)
        {
            // The Lyrics affordance is only meaningful when something is playing (Bug 3).
            DispatcherQueue.TryEnqueue(UpdateLyricsAvailability);

            // Re-sync the like affordance to the newly loaded track (Req 37.3).
            DispatcherQueue.TryEnqueue(UpdateLikeAvailability);
        }

        if (e.PropertyName is nameof(IPlayerService.RepeatMode) or null)
        {
            // Reflect the active repeat mode in the button tooltip (off / all / one).
            DispatcherQueue.TryEnqueue(UpdateRepeatTooltip);
        }
    }

    /// <summary>Shows the current repeat mode in the Repeat button's tooltip (localized).</summary>
    private void UpdateRepeatTooltip()
    {
        var tip = (_player?.RepeatMode ?? Core.Models.RepeatMode.Off) switch
        {
            Core.Models.RepeatMode.All => Localization.UiStrings.RepeatTooltipAll,
            Core.Models.RepeatMode.One => Localization.UiStrings.RepeatTooltipOne,
            _ => Localization.UiStrings.RepeatTooltipOff,
        };
        A11y.Label(RepeatButton, tip);
    }

    /// <summary>
    /// Enables the Like button only when a track with a real videoId is loaded and the music client
    /// is available, and re-syncs the button visual to the current track's like state (Req 37.3).
    /// </summary>
    private void UpdateLikeAvailability()
    {
        var track = _player?.CurrentTrack;
        // Prefer the session store so a like made elsewhere (or on a previous view of this track)
        // is reflected here even if the player's track record predates it.
        _currentLike = track?.VideoId is { Length: > 0 } id && _likeStore?.TryGet(id, out var stored) == true
            ? stored
            : track?.LikeStatus;
        LikeButton.IsEnabled = _music is not null && !string.IsNullOrEmpty(track?.VideoId);
        DislikeButton.IsEnabled = LikeButton.IsEnabled;
        ApplyLikeVisual();
        ApplyDislikeVisual();
    }

    /// <summary>
    /// Reflects the local like state on the button: full opacity + "Unlike" tooltip when liked,
    /// dimmed + "Like" otherwise (mirrors the Shuffle/Repeat opacity convention).
    /// </summary>
    private void ApplyLikeVisual()
    {
        bool liked = _currentLike == LikeStatus.Like;
        LikeButton.Opacity = liked ? 1.0 : 0.5;
        if (_isPodcastUi)
        {
            // Podcasts rate with thumbs (like YT), not the heart.
            LikeIcon.Glyph = "";
            LikeIcon.Foreground = liked
                ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
            A11y.Label(LikeButton, liked ? Localization.UiStrings.TipUnlike : Localization.UiStrings.TipLike);
            return;
        }

        // Filled red heart when liked, plain outline heart otherwise.
        LikeIcon.Glyph = liked ? "" : "";
        LikeIcon.Foreground = liked
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE0, 0x24, 0x5E))
            : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        A11y.Label(LikeButton, liked ? Localization.UiStrings.TipUnlike : Localization.UiStrings.TipLike);
    }

    /// <summary>
    /// Applies the app language to the bar's static labels. The bar lives for the whole window
    /// (unlike pages, which are recreated on navigation), so this is re-invoked on language change.
    /// </summary>
    internal void ApplyLanguage()
    {
        // A11y.Label sets the accessible name alongside the tooltip: these are all icon-only
        // buttons, and a tooltip alone leaves them unnamed for Narrator.
        A11y.Label(ShuffleButton, Localization.UiStrings.TipShuffle);
        A11y.Label(PreviousButton, Localization.UiStrings.TipPrevious);
        A11y.Label(Rewind10Button, Localization.UiStrings.TipBack10);
        A11y.Label(PlayPauseButton, Localization.UiStrings.TipPlayPause);
        A11y.Label(Forward30Button, Localization.UiStrings.TipForward30);
        A11y.Label(NextButton, Localization.UiStrings.TipNext);
        UpdateRepeatTooltip();
        A11y.Label(DislikeButton, Localization.UiStrings.TipDislike);
        A11y.Label(SpeedButton, Localization.UiStrings.TipSpeed);
        ApplyLyricsButtonLabel();
        A11y.Label(QueueButton, Localization.UiStrings.TipQueue);
        A11y.Label(MuteButton, Localization.UiStrings.TipMute);
        A11y.Label(MiniPlayerButton, Localization.UiStrings.TipMiniPlayer);

        // Rebuild rather than relabel: the menu is generated, and an armed timer's button label
        // carries its own (localized) countdown, which OnSleepTimerStateChanged restores.
        BuildSleepTimerFlyout();
        OnSleepTimerStateChanged(this, _sleepTimer.State);

        // Sliders announce their value but not what the value means, so they need a name too —
        // without a tooltip, which would fight the seek/volume drag affordance.
        A11y.Name(SeekSlider, Localization.UiStrings.A11ySeekSlider);
        A11y.Name(VolumeSlider, Localization.UiStrings.A11yVolumeSlider);
        CtxPlayNextItem.Text = Localization.UiStrings.MenuPlayNext;
        CtxAddToQueueItem.Text = Localization.UiStrings.MenuAddToQueue;
        CtxToggleLikeItem.Text = Localization.UiStrings.MenuLikeToggle;
        CtxGoToArtistItem.Text = Localization.UiStrings.MenuGoToArtist;
        CtxGoToAlbumItem.Text = Localization.UiStrings.MenuGoToAlbum;
        CtxShareItem.Text = Localization.UiStrings.MenuShare;
        BarTrackInfo.ApplyLanguage();
        ApplyLikeVisual(); // refresh the language-dependent like tooltip
    }

    /// <summary>
    /// Toggles like/unlike for the current track (Task 30.3, Req 37.3). Updates the button
    /// optimistically, persists via <see cref="IYTMusicClient.RateSongAsync"/>, and reverts the
    /// visual if the mutation fails. A no-op when nothing is loaded or the client is unavailable.
    /// </summary>
    private async void OnLikeClick(object sender, RoutedEventArgs e)
    {
        var videoId = _player?.CurrentTrack?.VideoId;
        if (_music is null || string.IsNullOrEmpty(videoId))
        {
            return;
        }

        LikeStatus? previous = _currentLike;
        // Toggle: a liked track becomes indifferent (removelike); anything else becomes liked.
        LikeStatus next = previous == LikeStatus.Like ? LikeStatus.Indifferent : LikeStatus.Like;

        _currentLike = next;
        ApplyLikeVisual();

        try
        {
            await _music.RateSongAsync(videoId, next);
            _likeStore?.Set(videoId, next);
            var title = _player?.CurrentTrack?.Title ?? Localization.UiStrings.ThisSongFallback;
            _notifier?.Show(next == LikeStatus.Like
                ? Localization.UiStrings.ToastLiked(title)
                : Localization.UiStrings.ToastUnliked(title));
        }
        catch (Exception)
        {
            // Persisting the rating failed â€” revert the optimistic visual so it matches the server.
            _currentLike = previous;
            ApplyLikeVisual();
            _notifier?.Show(Localization.UiStrings.ToastLikeFailed);
        }
    }

    /// <summary>
    /// Enables the Lyrics button only when a track is loaded; clicking it navigates the shell's
    /// content frame to the Lyrics panel, which resolves lyrics for the current track itself
    /// (Bug 3). Routed through <see cref="NavigationHelper"/> because the PlayerBar lives outside
    /// the content <see cref="Frame"/>.
    /// </summary>
    /// <summary>
    /// Labels the Lyrics button for the current content kind: "Subtitles (CC)" for a podcast
    /// episode, "Lyrics" for a song — always from <see cref="Localization.UiStrings"/>, so the
    /// label follows the app language instead of being pinned to one of them.
    /// </summary>
    private void ApplyLyricsButtonLabel() => A11y.Label(
        LyricsButton,
        _isPodcastUi ? Localization.UiStrings.SubtitlesCcLabel : Localization.UiStrings.TipLyrics);

    private void UpdateLyricsAvailability()
    {
        var track = _player?.CurrentTrack;
        LyricsButton.IsEnabled = track is not null;

        // Podcast episodes show captions (CC) instead of song lyrics — swap the button's
        // glyph and tooltip so the affordance reads "Subtitel (CC)" like YT Music.
        var isPodcast = track?.IsPodcastEpisode == true;
        LyricsIcon.Glyph = isPodcast ? "" : ""; // ClosedCaption vs the default lyric glyph
        _isPodcastUi = isPodcast;
        ApplyLyricsButtonLabel();

        // Podcast-only affordances: ±seek jumps and the dislike rating.
        var podcastVisibility = isPodcast ? Visibility.Visible : Visibility.Collapsed;
        ApplyLikeVisual();
        Rewind10Button.Visibility = podcastVisibility;
        Forward30Button.Visibility = podcastVisibility;
        DislikeButton.Visibility = podcastVisibility;
        ApplyDislikeVisual();
    }

    /// <summary>Podcast: jump back 10 seconds.</summary>
    private async void OnRewind10Click(object sender, RoutedEventArgs e)
    {
        if (_player is not null)
        {
            await _player.SeekAsync(System.Math.Max(0, _player.Progress - 10));
        }
    }

    /// <summary>Podcast: jump forward 30 seconds (clamped by the player).</summary>
    private async void OnForward30Click(object sender, RoutedEventArgs e)
    {
        if (_player is not null)
        {
            await _player.SeekAsync(_player.Progress + 30);
        }
    }

    /// <summary>Dims/solidifies the thumbs-down to mirror the current dislike state.</summary>
    private void ApplyDislikeVisual() =>
        DislikeButton.Opacity = _currentLike == LikeStatus.Dislike ? 1.0 : 0.5;

    /// <summary>
    /// Toggles dislike for the current episode (podcast-only button): a disliked episode becomes
    /// indifferent, anything else becomes disliked. Optimistic, reverts on server failure.
    /// </summary>
    private async void OnDislikeClick(object sender, RoutedEventArgs e)
    {
        var videoId = _player?.CurrentTrack?.VideoId;
        if (_music is null || string.IsNullOrEmpty(videoId))
        {
            return;
        }

        LikeStatus? previous = _currentLike;
        LikeStatus next = previous == LikeStatus.Dislike ? LikeStatus.Indifferent : LikeStatus.Dislike;

        _currentLike = next;
        ApplyLikeVisual();
        ApplyDislikeVisual();

        try
        {
            await _music.RateSongAsync(videoId, next);
            _likeStore?.Set(videoId, next);
        }
        catch (Exception)
        {
            _currentLike = previous;
            ApplyLikeVisual();
            ApplyDislikeVisual();
            _notifier?.Show(Localization.UiStrings.ToastRateFailed);
        }
    }

    /// <summary>Likes / unlikes the current track — invoked by the global "+" keyboard shortcut.</summary>
    internal void ToggleLikeFromShortcut() => OnLikeClick(this, new RoutedEventArgs());

    /// <summary>Dislikes / clears the dislike on the current track — invoked by the global "_" shortcut.</summary>
    internal void DislikeFromShortcut() => OnDislikeClick(this, new RoutedEventArgs());

    private void OnLyricsClick(object sender, RoutedEventArgs e) => _sidePanel?.ToggleLyrics();

    private void OnQueueClick(object sender, RoutedEventArgs e) => _sidePanel?.ToggleQueue();

    /// <summary>Highlights the Lyrics/Queue buttons to match whichever panel (if any) is open.</summary>
    private void OnSidePanelChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LyricsButton.Opacity = _sidePanel?.Mode == SidePanelMode.Lyrics ? 1.0 : 0.75;
            QueueButton.Opacity = _sidePanel?.Mode == SidePanelMode.Queue ? 1.0 : 0.75;
        });
    }

    // ── Cover context menu (right-click on the now-playing artwork) ───────────────────────────────

    private void OnCtxPlayNext(object sender, RoutedEventArgs e)
    {
        if (_player?.CurrentTrack is { } track && _queue is not null)
        {
            _queue.InsertNext([track]);
            _notifier?.Show(Localization.UiStrings.ToastPlayingNext(track.Title));
        }
    }

    private void OnCtxAddToQueue(object sender, RoutedEventArgs e)
    {
        if (_player?.CurrentTrack is { } track && _queue is not null)
        {
            var added = _queue.AppendDeduplicated([track]);
            _notifier?.Show(added == 0
                ? Localization.UiStrings.ToastSongAlreadyQueued
                : Localization.UiStrings.ToastAddedToQueue(track.Title));
        }
    }

    private void OnCtxToggleLike(object sender, RoutedEventArgs e) => OnLikeClick(sender, e);

    private void OnCtxGoToArtist(object sender, RoutedEventArgs e) =>
        NavigationHelper.NavigateToSongArtist(_player?.CurrentTrack);

    private void OnCtxGoToAlbum(object sender, RoutedEventArgs e) =>
        NavigationHelper.NavigateToSongAlbum(_player?.CurrentTrack);

    private void OnCtxShare(object sender, RoutedEventArgs e)
    {
        var target = ShareUrlBuilder.TryCreate(_player?.CurrentTrack);
        if (target is not null)
        {
            ShareInvoker.TryShow((Application.Current as App)?.MainWindow, target);
        }
        else
        {
            _notifier?.Show(Localization.UiStrings.ToastNothingToShare);
        }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        // THROWAWAY DIAGNOSTIC (Bug A): confirm the transport play/pause reaches the player and
        // that it controls a loaded track (CurrentTrack non-null once something is playing).
        _ = _player?.TogglePlayPauseAsync();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        _ = _player?.NextAsync();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        _ = _player?.PreviousAsync();
    }

    private void OnShuffleClick(object sender, RoutedEventArgs e) => _player?.ToggleShuffle();

    private void OnRepeatClick(object sender, RoutedEventArgs e) => _player?.CycleRepeat();

    private void OnMuteClick(object sender, RoutedEventArgs e) => _player?.ToggleMute();

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Ignore echoes from seeding/syncing the slider (value already matches the player) so only a
        // real user adjustment reaches the player.
        if (_player is null || (int)e.NewValue == _player.Volume)
        {
            return;
        }

        _player.SetVolume((int)e.NewValue);
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

        _ = _player.SeekAsync(SeekSlider.Value);
    }
}
