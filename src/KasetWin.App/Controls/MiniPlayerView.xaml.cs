using System;
using System.ComponentModel;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace KasetWin.App.Controls;

/// <summary>
/// The compact now-playing surface shown while the shell is in mini-player (CompactOverlay) mode.
/// Binds to the same singleton <see cref="IPlayerService"/> as the full <see cref="PlayerBar"/>, so
/// the two never disagree and switching modes carries no state.
/// </summary>
/// <remarks>
/// Seeking mirrors the player bar: the slider follows <see cref="IPlayerService.Progress"/> except
/// while the user is dragging it, and the seek is committed on release — otherwise the periodic
/// progress update fights the drag and the thumb snaps back.
/// </remarks>
public sealed partial class MiniPlayerView : UserControl
{
    private readonly IPlayerService? _player;
    private bool _userSeeking;

    public MiniPlayerView()
    {
        this.InitializeComponent();

        _player = (Application.Current as App)?.Services.GetService<IPlayerService>();
        if (_player is not null)
        {
            DataContext = _player;
            _player.PropertyChanged += OnPlayerPropertyChanged;
            MiniSeekSlider.IsEnabled = !_player.IsLive;
        }

        MiniSeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPointerPressed), handledEventsToo: true);
        MiniSeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);
        MiniSeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekPointerReleased), handledEventsToo: true);

        ApplyLanguage();
        Unloaded += (_, _) =>
        {
            if (_player is not null)
            {
                _player.PropertyChanged -= OnPlayerPropertyChanged;
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
        Accessibility.A11y.Name(MiniSeekSlider, Localization.UiStrings.A11ySeekSlider);
        MiniTrackInfo.ApplyLanguage();
    }

    /// <summary>
    /// Called when the shell enters mini-player mode: re-syncs the scrubber, which does not receive
    /// progress updates while the view is collapsed.
    /// </summary>
    internal void OnEnteredMiniPlayer()
    {
        if (_player is not null)
        {
            MiniSeekSlider.Value = _player.Progress;
            MiniSeekSlider.IsEnabled = !_player.IsLive;
        }

        ApplyLanguage();
    }

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
