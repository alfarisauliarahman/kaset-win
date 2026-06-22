using System.ComponentModel;
using System.Runtime.Versioning;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace KasetWin.Platform.Smtc;

/// <summary>
/// <see cref="INowPlayingController"/> adapter backed by the Windows System Media Transport
/// Controls (SMTC) (Req 10). It mirrors <see cref="IPlayerService"/> state onto the system
/// Now Playing surface — title / artist / artwork (Req 10.1) and playback status (Req 10.3) —
/// and forwards the system media buttons (play / pause / next / previous) back into the player
/// (Req 10.2).
/// </summary>
/// <remarks>
/// <para>
/// On WinUI 3 desktop the SMTC is obtained from a single <see cref="MediaPlayer"/> placed in
/// manual-control mode: its <see cref="MediaPlaybackCommandManager"/> is disabled so this class
/// owns the controls outright. This is Microsoft's recommended approach for an app that hosts a
/// single SMTC instance while doing its own playback (here through the hidden WebView2) and,
/// unlike <c>SystemMediaTransportControlsInterop.GetForWindow(hwnd)</c>, needs no window handle.
/// The <see cref="MediaPlayer"/> is never given a media source; it exists only to vend the SMTC.
/// </para>
/// <para>
/// SMTC raises <see cref="SystemMediaTransportControls.ButtonPressed"/> on a thread-pool thread,
/// and the observed <see cref="IPlayerService"/> may raise <see cref="INotifyPropertyChanged"/>
/// from any thread. All access to the SMTC objects is therefore serialized under a lock, and the
/// player's <c>async</c> commands are dispatched fire-and-forget with failures logged. No WinRT
/// type escapes this class — consumers observe only the WinRT-free
/// <see cref="INowPlayingController"/> contract.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class SmtcController : INowPlayingController
{
    private readonly object _gate = new();
    private readonly IPlayerService _player;
    private readonly ILogger<SmtcController> _logger;

    private readonly PropertyChangedEventHandler _playerChangedHandler;
    private readonly TypedEventHandler<SystemMediaTransportControls, SystemMediaTransportControlsButtonPressedEventArgs> _buttonPressedHandler;

    private MediaPlayer? _mediaPlayer;
    private SystemMediaTransportControls? _smtc;
    private SystemMediaTransportControlsDisplayUpdater? _displayUpdater;
    private bool _active;
    private bool _disposed;

    /// <summary>
    /// Creates the controller bound to <paramref name="player"/>. The SMTC is not activated until
    /// <see cref="Start"/> is called.
    /// </summary>
    /// <param name="player">The player whose state is mirrored to, and controlled by, the SMTC.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public SmtcController(IPlayerService player, ILogger<SmtcController>? logger = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _logger = logger ?? NullLogger<SmtcController>.Instance;

        _playerChangedHandler = OnPlayerPropertyChanged;
        _buttonPressedHandler = OnButtonPressed;
    }

    /// <inheritdoc />
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_active)
            {
                return;
            }

            // A MediaPlayer in manual-control mode is the host for a single app-wide SMTC
            // instance. It is never given a source — it only vends SystemMediaTransportControls.
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.CommandManager.IsEnabled = false;

            SystemMediaTransportControls smtc = _mediaPlayer.SystemMediaTransportControls;
            smtc.IsEnabled = true;

            // Buttons Kaset forwards to the player (Req 10.2).
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;

            smtc.ButtonPressed += _buttonPressedHandler;

            SystemMediaTransportControlsDisplayUpdater updater = smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;

            _smtc = smtc;
            _displayUpdater = updater;
            _active = true;

            _player.PropertyChanged += _playerChangedHandler;
        }

        _logger.LogInformation("SMTC controller started; now playing surface active.");

        // Publish the current track / status immediately so the surface is not blank.
        UpdateDisplay();
        UpdatePlaybackStatus();
    }

    /// <inheritdoc />
    public void Stop()
    {
        MediaPlayer? player;

        lock (_gate)
        {
            if (!_active)
            {
                return;
            }

            _player.PropertyChanged -= _playerChangedHandler;

            if (_smtc is not null)
            {
                _smtc.ButtonPressed -= _buttonPressedHandler;
                _smtc.IsPlayEnabled = false;
                _smtc.IsPauseEnabled = false;
                _smtc.IsNextEnabled = false;
                _smtc.IsPreviousEnabled = false;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                _smtc.IsEnabled = false;
            }

            player = _mediaPlayer;
            _smtc = null;
            _displayUpdater = null;
            _mediaPlayer = null;
            _active = false;
        }

        // Dispose the MediaPlayer outside the lock (its teardown may marshal internally).
        player?.Dispose();

        _logger.LogInformation("SMTC controller stopped; now playing surface released.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    // IPlayerService may raise this on any thread.
    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null:
            case "":
            case nameof(IPlayerService.CurrentTrack):
                UpdateDisplay();
                UpdatePlaybackStatus();
                break;

            case nameof(IPlayerService.IsPlaying):
                UpdatePlaybackStatus();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Pushes the current track's title / artist / artwork to the SMTC display (Req 10.1).
    /// Clears the surface when nothing is playing.
    /// </summary>
    private void UpdateDisplay()
    {
        Song? track = _player.CurrentTrack;

        lock (_gate)
        {
            if (!_active || _smtc is null || _displayUpdater is null)
            {
                return;
            }

            try
            {
                if (track is null)
                {
                    _displayUpdater.ClearAll();
                    _displayUpdater.Type = MediaPlaybackType.Music;
                    _displayUpdater.Update();
                    return;
                }

                _displayUpdater.Type = MediaPlaybackType.Music;
                MusicDisplayProperties music = _displayUpdater.MusicProperties;
                music.Title = track.Title ?? string.Empty;
                music.Artist = track.ArtistsDisplay;
                if (track.Album is { Title.Length: > 0 } album)
                {
                    music.AlbumTitle = album.Title;
                }

                // Prefer the track thumbnail; fall back to the deterministic videoId thumbnail so
                // the surface always has artwork. RandomAccessStreamReference fetches the http(s) URI.
                Uri artwork = track.ThumbnailUrl ?? track.FallbackThumbnailUrl;
                _displayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(artwork);

                _displayUpdater.Update();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update SMTC display metadata.");
            }
        }
    }

    /// <summary>Mirrors the player's playing/paused/stopped state onto the SMTC (Req 10.3).</summary>
    private void UpdatePlaybackStatus()
    {
        bool hasTrack = _player.CurrentTrack is not null;
        bool isPlaying = _player.IsPlaying;

        lock (_gate)
        {
            if (!_active || _smtc is null)
            {
                return;
            }

            try
            {
                _smtc.PlaybackStatus = !hasTrack
                    ? MediaPlaybackStatus.Closed
                    : isPlaying
                        ? MediaPlaybackStatus.Playing
                        : MediaPlaybackStatus.Paused;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update SMTC playback status.");
            }
        }
    }

    // SMTC raises this on a thread-pool thread; forward to the player and observe failures.
    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
            case SystemMediaTransportControlsButton.Pause:
                DispatchPlayerCommand(args.Button.ToString(), _player.TogglePlayPauseAsync);
                break;

            case SystemMediaTransportControlsButton.Next:
                DispatchPlayerCommand("Next", _player.NextAsync);
                break;

            case SystemMediaTransportControlsButton.Previous:
                DispatchPlayerCommand("Previous", _player.PreviousAsync);
                break;

            default:
                break;
        }
    }

    private void DispatchPlayerCommand(string buttonName, Func<Task> command)
    {
        _logger.LogDebug("SMTC button pressed: {Button}.", buttonName);

        _ = Task.Run(async () =>
        {
            try
            {
                await command().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMTC '{Button}' command failed.", buttonName);
            }
        });
    }
}
