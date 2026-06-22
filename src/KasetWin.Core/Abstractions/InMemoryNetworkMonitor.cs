using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// WinRT-free <see cref="INetworkMonitor"/> implementation whose connectivity state is driven
/// programmatically. Intended for design-time view-models and headless tests that need to react
/// to connectivity changes without <c>Windows.Networking.Connectivity</c> (Design: fakes seam).
/// </summary>
/// <remarks>
/// Setting <see cref="IsConnected"/> raises <see cref="PropertyChanged"/> and
/// <see cref="ConnectivityChanged"/> only when the value actually changes, mirroring the
/// behaviour contract of the platform implementation.
/// </remarks>
public sealed class InMemoryNetworkMonitor : INetworkMonitor
{
    private readonly object _gate = new();
    private bool _isConnected;

    /// <summary>
    /// Creates the monitor with an initial connectivity state.
    /// </summary>
    /// <param name="initiallyConnected">The starting value of <see cref="IsConnected"/>.</param>
    public InMemoryNetworkMonitor(bool initiallyConnected = true)
    {
        _isConnected = initiallyConnected;
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
        set => SetConnected(value);
    }

    /// <inheritdoc />
    public void Start()
    {
        // No external source to subscribe to; state is driven via SetConnected. No-op for parity
        // with the platform monitor's lifecycle contract.
    }

    /// <inheritdoc />
    public void Stop()
    {
        // No external source to release. No-op for parity with the platform monitor.
    }

    /// <summary>
    /// Updates the connectivity state, raising change notifications when the value changes.
    /// </summary>
    /// <param name="connected">The new connectivity state.</param>
    public void SetConnected(bool connected)
    {
        lock (_gate)
        {
            if (_isConnected == connected)
            {
                return;
            }

            _isConnected = connected;
        }

        RaiseChanged(connected);
    }

    private void RaiseChanged(bool connected)
    {
        OnPropertyChanged(nameof(IsConnected));
        ConnectivityChanged?.Invoke(this, connected);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
