using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using KasetWin.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Networking.Connectivity;

namespace KasetWin.Platform.Network;

/// <summary>
/// <see cref="INetworkMonitor"/> adapter backed by
/// <see cref="NetworkInformation"/>. It tracks internet connectivity by inspecting the current
/// internet connection profile and re-evaluating it whenever WinRT raises
/// <see cref="NetworkInformation.NetworkStatusChanged"/> (Req 35.2).
/// </summary>
/// <remarks>
/// <para>
/// The WinRT <see cref="NetworkStatusChangedEventHandler"/> is invoked on a thread-pool thread,
/// so all mutable state is guarded by a lock and connectivity evaluation is non-blocking
/// (a single synchronous profile lookup). No WinRT type escapes this class — consumers observe
/// only the WinRT-free <see cref="INetworkMonitor"/> contract.
/// </para>
/// <para>
/// Change notifications (<see cref="PropertyChanged"/> / <see cref="ConnectivityChanged"/>) are
/// raised outside the lock to avoid re-entrancy deadlocks if a handler synchronously calls back
/// into the monitor.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class NetworkMonitor : INetworkMonitor, IDisposable
{
    private readonly object _gate = new();
    private readonly ILogger<NetworkMonitor> _logger;
    private NetworkStatusChangedEventHandler? _handler;
    private bool _isConnected;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Creates the monitor. Connectivity is not observed until <see cref="Start"/> is called.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public NetworkMonitor(ILogger<NetworkMonitor>? logger = null)
    {
        _logger = logger ?? NullLogger<NetworkMonitor>.Instance;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<bool>? ConnectivityChanged;

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _isConnected;
            }
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_started)
            {
                return;
            }

            _handler = OnNetworkStatusChanged;
            NetworkInformation.NetworkStatusChanged += _handler;
            _started = true;
        }

        _logger.LogDebug("NetworkMonitor started; subscribed to NetworkStatusChanged.");

        // Publish the initial status (raises change notification if it differs from the default).
        UpdateConnectivity();
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            if (_handler is not null)
            {
                NetworkInformation.NetworkStatusChanged -= _handler;
                _handler = null;
            }

            _started = false;
        }

        _logger.LogDebug("NetworkMonitor stopped; unsubscribed from NetworkStatusChanged.");
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

    // WinRT raises this on a thread-pool thread; keep it non-blocking.
    private void OnNetworkStatusChanged(object sender) => UpdateConnectivity();

    private void UpdateConnectivity()
    {
        bool connected = QueryHasInternet();
        bool changed;

        lock (_gate)
        {
            changed = _isConnected != connected;
            _isConnected = connected;
        }

        if (changed)
        {
            _logger.LogInformation("Network connectivity changed: IsConnected={IsConnected}.", connected);
            OnPropertyChanged(nameof(IsConnected));
            ConnectivityChanged?.Invoke(this, connected);
        }
    }

    private bool QueryHasInternet()
    {
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            return profile?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }
        catch (Exception ex)
        {
            // Defensive: treat a failed probe as "offline" rather than crashing the monitor.
            _logger.LogWarning(ex, "Failed to query internet connection profile; assuming offline.");
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
