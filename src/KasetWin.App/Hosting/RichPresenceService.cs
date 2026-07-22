using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.RichPresence;
using Microsoft.Extensions.Logging;

namespace KasetWin.App.Hosting;

/// <summary>
/// Keeps Discord Rich Presence in step with playback: observes <see cref="IPlayerService"/> and
/// pushes a "Listening to …" activity whenever the track or play/pause state changes.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> driven by <see cref="IPlayerService.Progress"/>, which ticks about once a
/// second. Discord rate-limits presence updates to roughly one every 15 seconds and silently drops
/// the rest, so a per-tick push would be both wasteful and unreliable. Instead the activity carries
/// start/end timestamps and Discord animates the counter itself — one update per track is enough,
/// with a re-push on pause/resume and after a seek settles.
/// </para>
/// <para>
/// Everything here is best-effort. Discord absent, closed, or restarted are ordinary conditions:
/// the service simply retries on the next track change. Rich presence must never disturb playback.
/// </para>
/// </remarks>
public sealed class RichPresenceService : IDisposable
{
    /// <summary>Discord's own floor for presence updates; pushing faster is silently discarded.</summary>
    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromSeconds(15);

    private readonly IPlayerService _player;
    private readonly IRichPresenceClient _client;
    private readonly ILogger<RichPresenceService>? _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _applicationId = string.Empty;
    private bool _enabled;
    private bool _disposed;

    /// <summary>Last pushed activity, so an identical repeat can be skipped.</summary>
    private DiscordActivity? _lastActivity;
    private DateTimeOffset _lastPush = DateTimeOffset.MinValue;

    public RichPresenceService(
        IPlayerService player,
        IRichPresenceClient client,
        ILogger<RichPresenceService>? logger = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
    }

    /// <summary>Whether presence is currently being published.</summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// Starts publishing presence using <paramref name="applicationId"/> (the user's own Discord
    /// application id). A blank id disables the feature — there is no sensible default, because the
    /// id determines the app name and artwork Discord shows.
    /// </summary>
    public async Task StartAsync(string applicationId)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(applicationId))
        {
            await StopAsync().ConfigureAwait(false);
            return;
        }

        _applicationId = applicationId.Trim();

        if (!_enabled)
        {
            _player.PropertyChanged += OnPlayerPropertyChanged;
            _enabled = true;
        }

        await ConnectAndPushAsync().ConfigureAwait(false);
    }

    /// <summary>Stops publishing and clears any presence already shown. Idempotent.</summary>
    public async Task StopAsync()
    {
        if (!_enabled)
        {
            return;
        }

        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _enabled = false;
        _lastActivity = null;

        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Ignoring failure while disconnecting Discord rich presence.");
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Progress is excluded on purpose — see the remarks on rate limiting.
        if (e.PropertyName is nameof(IPlayerService.CurrentTrack)
            or nameof(IPlayerService.IsPlaying)
            or nameof(IPlayerService.Duration)
            or null)
        {
            _ = ConnectAndPushAsync();
        }
    }

    private async Task ConnectAndPushAsync()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_enabled || _disposed)
            {
                return;
            }

            if (!_client.IsConnected && !await _client.ConnectAsync(_applicationId).ConfigureAwait(false))
            {
                return; // Discord isn't running; try again on the next track change.
            }

            var activity = DiscordActivityBuilder.Build(
                _player.CurrentTrack,
                _player.IsPlaying,
                _player.Progress,
                _player.Duration,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // Skip an identical repeat, and respect Discord's floor unless the track itself changed
            // (a new song must appear immediately, however recently the last push happened).
            var trackChanged = activity?.Details != _lastActivity?.Details
                || activity?.State != _lastActivity?.State;

            if (!trackChanged && DateTimeOffset.UtcNow - _lastPush < MinUpdateInterval)
            {
                return;
            }

            await _client.SetActivityAsync(activity).ConfigureAwait(false);
            _lastActivity = activity;
            _lastPush = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Ignoring Discord rich presence update failure.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Ignoring failure while stopping Discord rich presence.");
        }

        _client.Dispose();
        _gate.Dispose();
    }
}
