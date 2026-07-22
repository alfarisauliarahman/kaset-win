using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.RichPresence;
using Microsoft.Extensions.Logging;

namespace KasetWin.Platform.RichPresence;

/// <summary>
/// Discord Rich Presence over the local IPC named pipe, implemented directly against the wire
/// protocol — no third-party RPC library (AGENTS.md: no third-party frameworks without asking).
/// </summary>
/// <remarks>
/// <para>
/// The protocol is small: connect to <c>\\.\pipe\discord-ipc-N</c> (Discord uses the first free
/// index 0–9, and which one depends on how many Discord clients are running, so all ten are tried),
/// then exchange length-prefixed frames of <c>[int32 opcode][int32 byteLength][utf8 json]</c> —
/// little-endian. Opcode 0 is the handshake, 1 carries commands, 2 closes.
/// </para>
/// <para>
/// Everything is best-effort and swallowing: Discord not installed, not running, or killed
/// mid-session are all normal conditions, not errors worth surfacing. A dropped pipe flips
/// <see cref="IsConnected"/> back to false so the next reconnect attempt can pick it up. Presence is
/// decoration — it must never take playback down with it.
/// </para>
/// </remarks>
public sealed class DiscordRpcClient : IRichPresenceClient
{
    /// <summary>Discord probes pipes 0–9; more than one exists when several clients run side by side.</summary>
    private const int MaxPipeIndex = 9;

    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<DiscordRpcClient>? _logger;

    /// <summary>Serializes writes: activity updates arrive from player events and can overlap.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public DiscordRpcClient(ILogger<DiscordRpcClient>? logger = null) => _logger = logger;

    /// <inheritdoc />
    public bool IsConnected => _pipe is { IsConnected: true };

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return true;
            }

            await CloseQuietlyAsync().ConfigureAwait(false);

            for (var index = 0; index <= MaxPipeIndex; index++)
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    // A short timeout per index: ten indices must not add up to a visible stall on a
                    // machine without Discord.
                    await pipe.ConnectAsync(200, cancellationToken).ConfigureAwait(false);

                    _pipe = pipe;
                    await WriteFrameAsync(
                        OpHandshake,
                        new { v = 1, client_id = applicationId },
                        cancellationToken).ConfigureAwait(false);

                    _logger?.LogInformation("Discord rich presence connected on pipe {Index}.", index);
                    return true;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or UnauthorizedAccessException)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    if (ReferenceEquals(_pipe, pipe))
                    {
                        _pipe = null;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                }
            }

            _logger?.LogDebug("Discord rich presence: no IPC pipe found (Discord not running).");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsConnected)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                return;
            }

            var payload = new
            {
                cmd = "SET_ACTIVITY",
                nonce = Guid.NewGuid().ToString(),
                args = new
                {
                    pid = Environment.ProcessId,
                    activity = BuildActivityPayload(activity),
                },
            };

            await WriteFrameAsync(OpFrame, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Discord went away mid-write; drop the pipe so a later Connect can re-establish it.
            _logger?.LogDebug(ex, "Discord rich presence write failed; dropping the connection.");
            await CloseQuietlyAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                try
                {
                    // Clear the presence before closing, otherwise Discord keeps showing the last
                    // track until it notices the pipe is gone.
                    await WriteFrameAsync(
                        OpFrame,
                        new
                        {
                            cmd = "SET_ACTIVITY",
                            nonce = Guid.NewGuid().ToString(),
                            args = new { pid = Environment.ProcessId, activity = (object?)null },
                        },
                        CancellationToken.None).ConfigureAwait(false);

                    await WriteFrameAsync(OpClose, new { }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // Already gone — nothing left to clear.
                }
            }

            await CloseQuietlyAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Shapes the JSON Discord expects. <c>type: 2</c> is "Listening", which renders as
    /// "Listening to &lt;app name&gt;" instead of the default "Playing".
    /// </summary>
    private static object? BuildActivityPayload(DiscordActivity? activity)
    {
        if (activity is not { } value)
        {
            return null;
        }

        object? timestamps = value.StartUnixSeconds is null
            ? null
            : new { start = value.StartUnixSeconds, end = value.EndUnixSeconds };

        object? assets = value.LargeImageUrl is null
            ? null
            : new { large_image = value.LargeImageUrl, large_text = value.LargeImageText };

        return new
        {
            type = 2,
            details = value.Details,
            state = value.State,
            timestamps,
            assets,
            instance = false,
        };
    }

    /// <summary>Writes one <c>[opcode][length][json]</c> frame, little-endian as the protocol requires.</summary>
    private async Task WriteFrameAsync(int opcode, object payload, CancellationToken cancellationToken)
    {
        if (_pipe is not { IsConnected: true } pipe)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);

        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame.AsSpan(8));

        await pipe.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CloseQuietlyAsync()
    {
        if (_pipe is { } pipe)
        {
            _pipe = null;
            try
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Debug.WriteLine($"Discord pipe dispose ignored: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord disconnect on dispose ignored: {ex.Message}");
        }

        _gate.Dispose();
    }
}
