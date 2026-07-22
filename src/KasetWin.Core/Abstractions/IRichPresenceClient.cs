using KasetWin.Core.Services.RichPresence;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// Abstraction over a Discord Rich Presence connection — "Listening to …" on the user's profile.
/// </summary>
/// <remarks>
/// <para>
/// Defined in <c>Core</c> (no Windows dependency) so the player layer and DI composition can talk to
/// it without taking a named-pipe dependency; the concrete <c>DiscordRpcClient</c> lives in
/// <c>KasetWin.Platform</c>. Same inversion as <see cref="INowPlayingController"/> and
/// <see cref="INotificationService"/>.
/// </para>
/// <para>
/// Every member is best-effort: Discord may not be installed, may not be running, or may be
/// restarted underneath us. No implementation may throw for those cases — rich presence is
/// decoration and must never affect playback.
/// </para>
/// </remarks>
public interface IRichPresenceClient : IDisposable
{
    /// <summary>Whether a handshake has completed and activities are being delivered.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to a running Discord client and performs the handshake. Safe to call when Discord
    /// is absent (returns <c>false</c>) and idempotent while already connected.
    /// </summary>
    /// <param name="applicationId">
    /// The Discord application ("client") id whose name and artwork appear in the presence. Supplied
    /// by the user, since it belongs to their own Discord application.
    /// </param>
    /// <returns><c>true</c> when connected.</returns>
    Task<bool> ConnectAsync(string applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes <paramref name="activity"/>, or clears the presence when it is <c>null</c>.
    /// A no-op while disconnected.
    /// </summary>
    Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default);

    /// <summary>Clears the presence and closes the connection. Idempotent.</summary>
    Task DisconnectAsync();
}
