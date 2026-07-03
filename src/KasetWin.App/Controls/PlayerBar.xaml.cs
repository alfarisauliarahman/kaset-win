using System;
using System.ComponentModel;
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
/// shuffle and repeat (Req 5.1–5.8, 9.1).
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
    private readonly VideoWindowController? _videoController;

    /// <summary>Music client used to persist a like/unlike mutation for the current track (Req 37.3).</summary>
    private readonly IYTMusicClient? _music;

    /// <summary>Local like state for the current track, updated optimistically on toggle.</summary>
    private LikeStatus? _currentLike;

    private bool _userSeeking;

    public PlayerBar()
    {
        this.InitializeComponent();

        // Resolve the shared player service and bind directly to it. Guarded so the control still
        // instantiates in design-time / unexpected contexts where the host is unavailable.
        _player = (Application.Current as App)?.Services.GetService<IPlayerService>();
        _videoController = (Application.Current as App)?.Services.GetService<VideoWindowController>();
        _music = (Application.Current as App)?.Services.GetService<IYTMusicClient>();
        if (_player is not null)
        {
            DataContext = _player;
            _player.PropertyChanged += OnPlayerPropertyChanged;
            SeekSlider.IsEnabled = !_player.IsLive;
            UpdateShareAvailability();
            UpdateVideoAvailability();
            UpdateLyricsAvailability();
            UpdateLikeAvailability();
        }

        // Commit a seek only after the user finishes interacting with the slider. Handled events are
        // observed too because the Slider thumb marks pointer events handled.
        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Seed the volume slider from the player once the control is live. The volume is driven from
        // code rather than a OneWay binding because that binding did not apply the initial value
        // reliably — the slider showed 0% at launch while audio actually played at full volume.
        if (_player is not null)
        {
            VolumeSlider.Value = _player.Volume;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
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

        if (e.PropertyName is nameof(IPlayerService.CurrentTrack) or null)
        {
            // The shareable URL depends on the current track (Req 34.2). Marshal to the UI thread.
            DispatcherQueue.TryEnqueue(UpdateShareAvailability);

            // Video pop-out is only offered when the current track has real video (Req 26.1).
            DispatcherQueue.TryEnqueue(UpdateVideoAvailability);

            // The Lyrics affordance is only meaningful when something is playing (Bug 3).
            DispatcherQueue.TryEnqueue(UpdateLyricsAvailability);

            // Re-sync the like affordance to the newly loaded track (Req 37.3).
            DispatcherQueue.TryEnqueue(UpdateLikeAvailability);
        }
    }

    /// <summary>
    /// Enables the Like button only when a track with a real videoId is loaded and the music client
    /// is available, and re-syncs the button visual to the current track's like state (Req 37.3).
    /// </summary>
    private void UpdateLikeAvailability()
    {
        var track = _player?.CurrentTrack;
        _currentLike = track?.LikeStatus;
        LikeButton.IsEnabled = _music is not null && !string.IsNullOrEmpty(track?.VideoId);
        ApplyLikeVisual();
    }

    /// <summary>
    /// Reflects the local like state on the button: full opacity + "Unlike" tooltip when liked,
    /// dimmed + "Like" otherwise (mirrors the Shuffle/Repeat opacity convention).
    /// </summary>
    private void ApplyLikeVisual()
    {
        bool liked = _currentLike == LikeStatus.Like;
        LikeButton.Opacity = liked ? 1.0 : 0.5;
        ToolTipService.SetToolTip(LikeButton, liked ? "Unlike" : "Like");
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
        }
        catch (Exception)
        {
            // Persisting the rating failed — revert the optimistic visual so it matches the server.
            _currentLike = previous;
            ApplyLikeVisual();
        }
    }

    /// <summary>
    /// Enables the pop-out video button only when the current track exposes genuine video content
    /// (OMV, per <see cref="VideoAvailability"/>); disables it for audio-only tracks (ATV/UGC), when
    /// nothing is playing, or when the floating-video controller is unavailable (Req 26.1).
    /// </summary>
    private void UpdateVideoAvailability() =>
        VideoButton.IsEnabled = _videoController is not null
            && VideoAvailability.IsVideoAvailable(_player?.CurrentTrack);

    private void OnVideoClick(object sender, RoutedEventArgs e) => _ = _videoController?.TogglePopOutAsync();

    /// <summary>
    /// Enables the Lyrics button only when a track is loaded; clicking it navigates the shell's
    /// content frame to the Lyrics panel, which resolves lyrics for the current track itself
    /// (Bug 3). Routed through <see cref="NavigationHelper"/> because the PlayerBar lives outside
    /// the content <see cref="Frame"/>.
    /// </summary>
    private void UpdateLyricsAvailability() =>
        LyricsButton.IsEnabled = _player?.CurrentTrack is not null;

    private void OnLyricsClick(object sender, RoutedEventArgs e) => NavigationHelper.NavigateToLyrics();

    /// <summary>
    /// Enables the Share button only when the current track resolves to a shareable URL; disables it
    /// when nothing is playing or the track has no shareable id (Req 34.2).
    /// </summary>
    private void UpdateShareAvailability() =>
        ShareButton.IsEnabled = ShareUrlBuilder.TryCreate(_player?.CurrentTrack) is not null;

    private void OnShareClick(object sender, RoutedEventArgs e)
    {
        var target = ShareUrlBuilder.TryCreate(_player?.CurrentTrack);
        if (target is not null)
        {
            ShareInvoker.TryShow((Application.Current as App)?.MainWindow, target);
        }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        // THROWAWAY DIAGNOSTIC (Bug A): confirm the transport play/pause reaches the player and
        // that it controls a loaded track (CurrentTrack non-null once something is playing).
        KasetWin.Core.Diagnostics.KasetTrace.Log(
            "Play:PlayerBar.TogglePlayPause", $"playerNull={_player is null} hasTrack={_player?.CurrentTrack is not null}");
        _ = _player?.TogglePlayPauseAsync();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        KasetWin.Core.Diagnostics.KasetTrace.Log("Play:PlayerBar.Next", $"playerNull={_player is null}");
        _ = _player?.NextAsync();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        KasetWin.Core.Diagnostics.KasetTrace.Log("Play:PlayerBar.Previous", $"playerNull={_player is null}");
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
