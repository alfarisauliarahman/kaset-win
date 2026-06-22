using System.ComponentModel;
using KasetWin.App.Hosting;
using KasetWin.App.Sharing;
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
    private bool _userSeeking;

    public PlayerBar()
    {
        this.InitializeComponent();

        // Resolve the shared player service and bind directly to it. Guarded so the control still
        // instantiates in design-time / unexpected contexts where the host is unavailable.
        _player = (Application.Current as App)?.Services.GetService<IPlayerService>();
        _videoController = (Application.Current as App)?.Services.GetService<VideoWindowController>();
        if (_player is not null)
        {
            DataContext = _player;
            _player.PropertyChanged += OnPlayerPropertyChanged;
            SeekSlider.IsEnabled = !_player.IsLive;
            UpdateShareAvailability();
            UpdateVideoAvailability();
        }

        // Commit a seek only after the user finishes interacting with the slider. Handled events are
        // observed too because the Slider thumb marks pointer events handled.
        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);

        Unloaded += OnUnloaded;
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

        if (e.PropertyName is nameof(IPlayerService.CurrentTrack) or null)
        {
            // The shareable URL depends on the current track (Req 34.2). Marshal to the UI thread.
            DispatcherQueue.TryEnqueue(UpdateShareAvailability);

            // Video pop-out is only offered when the current track has real video (Req 26.1).
            DispatcherQueue.TryEnqueue(UpdateVideoAvailability);
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

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _ = _player?.TogglePlayPauseAsync();

    private void OnNextClick(object sender, RoutedEventArgs e) => _ = _player?.NextAsync();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _ = _player?.PreviousAsync();

    private void OnShuffleClick(object sender, RoutedEventArgs e) => _player?.ToggleShuffle();

    private void OnRepeatClick(object sender, RoutedEventArgs e) => _player?.CycleRepeat();

    private void OnMuteClick(object sender, RoutedEventArgs e) => _player?.ToggleMute();

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        _player?.SetVolume((int)e.NewValue);

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
