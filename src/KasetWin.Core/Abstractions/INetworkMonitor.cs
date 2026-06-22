using System.ComponentModel;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// Observable seam over the device's network connectivity state (Req 35.2/35.3).
/// </summary>
/// <remarks>
/// <para>
/// Defined in <c>Core</c> so view-models and services can react to connectivity changes without
/// taking a WinUI/WinRT dependency. The platform-layer implementation
/// (<c>KasetWin.Platform.Network.NetworkMonitor</c>) is backed by
/// <c>Windows.Networking.Connectivity.NetworkInformation</c>; a WinRT-free
/// <see cref="InMemoryNetworkMonitor"/> lives in <c>Core</c> for design-time and headless tests.
/// </para>
/// <para>
/// The monitor implements <see cref="INotifyPropertyChanged"/> so XAML can bind directly to
/// <see cref="IsConnected"/> (e.g. to surface an offline indicator, Req 35.3). A dedicated
/// <see cref="ConnectivityChanged"/> event is also raised for non-binding consumers.
/// Implementations must raise change notifications on a thread that is safe for UI marshalling
/// expectations and must never block while determining connectivity.
/// </para>
/// </remarks>
public interface INetworkMonitor : INotifyPropertyChanged
{
    /// <summary>
    /// <see langword="true"/> when the device currently has internet connectivity,
    /// otherwise <see langword="false"/>. Reflects the latest observed status (Req 35.2).
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Raised whenever <see cref="IsConnected"/> transitions to a new value. The event argument
    /// carries the new connectivity state.
    /// </summary>
    event EventHandler<bool>? ConnectivityChanged;

    /// <summary>
    /// Begins observing connectivity changes and publishes the current status. Calling
    /// <see cref="Start"/> more than once without an intervening <see cref="Stop"/> is a no-op.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops observing connectivity changes and releases any underlying subscriptions. Safe to
    /// call when the monitor is not started.
    /// </summary>
    void Stop();
}
