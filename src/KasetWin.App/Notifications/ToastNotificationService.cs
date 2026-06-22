using System.ComponentModel;
using System.Runtime.Versioning;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Notifications;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace KasetWin.App.Notifications;

/// <summary>
/// <see cref="INotificationService"/> adapter backed by the Windows App SDK
/// <see cref="AppNotificationManager"/> (Req 35.1). It observes <see cref="IPlayerService"/> and
/// shows a "now playing" toast — title = song title, body = artist(s) — whenever the current track
/// changes. Faithful port of the macOS <c>NotificationService</c>.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>KasetWin.App</c> (not <c>KasetWin.Platform</c>) because the toast APIs ship with the
/// Windows App SDK, which only the packaged App project references; <c>KasetWin.Platform</c>
/// deliberately stays a plain class library that restores/builds without MSIX/PRI tooling. No
/// Windows App SDK type escapes this class — consumers observe only the WinRT-free
/// <see cref="INotificationService"/> contract.
/// </para>
/// <para>
/// <b>Manager lifecycle.</b> <see cref="AppNotificationManager.Register"/> is called from
/// <see cref="Start"/> and <see cref="AppNotificationManager.Unregister"/> from <see cref="Stop"/>,
/// both guarded so an unavailable surface (e.g. running unpackaged) never breaks the shell.
/// </para>
/// <para>
/// <b>Eligibility.</b> The decision to toast is delegated to the pure
/// <see cref="TrackChangeNotificationPolicy"/>, so a toast fires only once active playback is
/// running for a new, fully-resolved track and never twice for the same track. The previously
/// observed track/playing state and the last-notified id are tracked here and updated on every
/// evaluation. <see cref="IPlayerService"/> may raise <see cref="INotifyPropertyChanged"/> from any
/// thread; all mutable state is guarded by a lock and the toast is built/shown outside it.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class ToastNotificationService : INotificationService
{
    private const string UnknownArtist = "Unknown Artist";

    private readonly object _gate = new();
    private readonly IPlayerService _player;
    private readonly ILogger<ToastNotificationService> _logger;
    private readonly PropertyChangedEventHandler _playerChangedHandler;

    private bool _active;
    private bool _disposed;
    private bool _notificationsEnabled = true;
    private string? _previousTrackId;
    private bool _previousIsPlaying;
    private string? _lastNotifiedTrackId;

    /// <summary>
    /// Creates the service bound to <paramref name="player"/>. Observation does not begin until
    /// <see cref="Start"/> is called.
    /// </summary>
    /// <param name="player">The player whose track changes drive the toasts.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public ToastNotificationService(IPlayerService player, ILogger<ToastNotificationService>? logger = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _logger = logger ?? NullLogger<ToastNotificationService>.Instance;
        _playerChangedHandler = OnPlayerPropertyChanged;
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
    public bool NotificationsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _notificationsEnabled;
            }
        }
        set
        {
            lock (_gate)
            {
                _notificationsEnabled = value;
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

            try
            {
                AppNotificationManager.Default.Register();
            }
            catch (Exception ex)
            {
                // A failed registration (e.g. unpackaged context) must not break the shell; the
                // service simply stays inactive and raises no toasts.
                _logger.LogWarning(ex, "Failed to register AppNotificationManager; track-change toasts disabled.");
                return;
            }

            // Seed the observation baseline so the very first state (often a placeholder/silent
            // track) does not produce a spurious toast.
            _previousTrackId = _player.CurrentTrack?.VideoId;
            _previousIsPlaying = _player.IsPlaying;
            _active = true;

            _player.PropertyChanged += _playerChangedHandler;
        }

        _logger.LogInformation("Toast notification service started; observing track changes.");
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (!_active)
            {
                return;
            }

            _player.PropertyChanged -= _playerChangedHandler;

            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister AppNotificationManager.");
            }

            _active = false;
            _previousTrackId = null;
            _previousIsPlaying = false;
            _lastNotifiedTrackId = null;
        }

        _logger.LogInformation("Toast notification service stopped.");
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
        if (e.PropertyName is null
            or ""
            or nameof(IPlayerService.CurrentTrack)
            or nameof(IPlayerService.IsPlaying))
        {
            EvaluateAndNotify();
        }
    }

    private void EvaluateAndNotify()
    {
        Song? track = _player.CurrentTrack;
        bool isPlaying = _player.IsPlaying;
        string? currentTrackId = track?.VideoId;

        bool shouldNotify;

        lock (_gate)
        {
            if (!_active)
            {
                return;
            }

            if (!_notificationsEnabled)
            {
                // Still advance the baseline so re-enabling does not retro-fire for stale state.
                _previousTrackId = currentTrackId;
                _previousIsPlaying = isPlaying;
                return;
            }

            shouldNotify = TrackChangeNotificationPolicy.ShouldNotify(
                currentTrackId,
                track?.Title,
                isPlaying,
                _previousTrackId,
                _previousIsPlaying,
                _lastNotifiedTrackId);

            _previousTrackId = currentTrackId;
            _previousIsPlaying = isPlaying;

            if (shouldNotify)
            {
                _lastNotifiedTrackId = currentTrackId;
            }
        }

        if (shouldNotify && track is not null)
        {
            PostTrackToast(track);
        }
    }

    /// <summary>
    /// Builds and shows a "now playing" toast for <paramref name="track"/> — title = song title,
    /// body = artist(s) (Req 35.1). Fully guarded so a toast failure never affects playback.
    /// </summary>
    private void PostTrackToast(Song track)
    {
        try
        {
            string title = string.IsNullOrEmpty(track.Title) ? UnknownArtist : track.Title;
            string artist = string.IsNullOrEmpty(track.ArtistsDisplay) ? UnknownArtist : track.ArtistsDisplay;

            AppNotificationBuilder builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(artist);

            // Prefer the track artwork; the toast still shows without it if the fetch fails.
            Uri? artwork = track.ThumbnailUrl;
            if (artwork is not null)
            {
                builder.SetAppLogoOverride(artwork, AppNotificationImageCrop.Default);
            }

            AppNotification notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;

            AppNotificationManager.Default.Show(notification);

            _logger.LogDebug("Posted track-change toast for {VideoId}.", track.VideoId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post track-change toast.");
        }
    }
}
