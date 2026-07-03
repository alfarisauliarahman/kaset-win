using System.Text.Json;
using KasetWin.Core.Abstractions;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// The classification of an inbound playback bridge message after validation (Req 2 / Req 1.7).
/// </summary>
public enum PlaybackMessageKind
{
    /// <summary>The payload was malformed, untyped, or of an unknown/unsupported type — ignore it.</summary>
    Ignored,

    /// <summary>A <c>STATE_UPDATE</c> carrying current playback state (Req 2.1/2.2/2.6).</summary>
    StateUpdate,

    /// <summary>A <c>TRACK_ENDED</c> carrying the videoId that ended (Req 2.3/2.4).</summary>
    TrackEnded,

    /// <summary>A <c>DRM_STATUS</c> reporting Widevine availability (Req 1.3/1.7).</summary>
    DrmStatus,
}

/// <summary>
/// The strongly-typed result of parsing a single untrusted playback bridge message. Exactly one
/// of <see cref="State"/> / <see cref="TrackEndedMessage"/> / <see cref="DrmAvailable"/> is set,
/// according to <see cref="Kind"/>; an <see cref="PlaybackMessageKind.Ignored"/> result carries none.
/// </summary>
/// <param name="Kind">The classification of the message.</param>
/// <param name="State">The parsed <c>STATE_UPDATE</c>, when <see cref="Kind"/> is <see cref="PlaybackMessageKind.StateUpdate"/>.</param>
/// <param name="TrackEndedMessage">The parsed <c>TRACK_ENDED</c>, when <see cref="Kind"/> is <see cref="PlaybackMessageKind.TrackEnded"/>.</param>
/// <param name="DrmAvailable">The reported Widevine availability, when <see cref="Kind"/> is <see cref="PlaybackMessageKind.DrmStatus"/>.</param>
public readonly record struct PlaybackWebMessage(
    PlaybackMessageKind Kind,
    PlaybackStateMessage? State = null,
    TrackEndedMessage? TrackEndedMessage = null,
    bool? DrmAvailable = null)
{
    /// <summary>A shared, allocation-free "ignore this message" result.</summary>
    public static PlaybackWebMessage Ignored => new(PlaybackMessageKind.Ignored);
}

/// <summary>
/// Pure, WinRT-free parser for the untrusted JSON messages posted by the injected
/// <c>observer.js</c> bridge (Req 2). The page content is untrusted, so every payload is
/// shape-validated before it is mapped to a strongly-typed result; anything malformed, untyped,
/// or of an unknown type is reported as <see cref="PlaybackMessageKind.Ignored"/> rather than
/// throwing.
/// </summary>
/// <remarks>
/// This logic was lifted out of the WinRT <c>WebView2PlaybackController.OnWebMessageReceived</c>
/// so the security-relevant validation — including the DRM-availability detection path (Req 1.7)
/// — stays headless-testable. The controller now delegates to <see cref="Parse"/> and only owns
/// the WebView2-specific concerns (reading <c>WebMessageAsJson</c>, raising events, flipping the
/// cached DRM flag). Cookie / token values never appear in these messages and are never logged.
/// </remarks>
public static class PlaybackMessageParser
{
    /// <summary>
    /// Parses a single untrusted bridge message. Never throws for malformed input — invalid,
    /// untyped, or unknown messages return <see cref="PlaybackWebMessage.Ignored"/>.
    /// </summary>
    /// <param name="json">The raw <c>WebMessageAsJson</c> payload, or <see langword="null"/>.</param>
    /// <returns>The classified, strongly-typed message.</returns>
    public static PlaybackWebMessage Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return PlaybackWebMessage.Ignored;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return PlaybackWebMessage.Ignored;
            }

            if (!doc.RootElement.TryGetProperty("type", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String)
            {
                return PlaybackWebMessage.Ignored;
            }

            switch (typeProp.GetString())
            {
                case "STATE_UPDATE":
                    return new PlaybackWebMessage(
                        PlaybackMessageKind.StateUpdate,
                        State: ParseStateUpdate(doc.RootElement));

                case "TRACK_ENDED":
                    var videoId = ReadString(doc.RootElement, "videoId");
                    return string.IsNullOrEmpty(videoId)
                        ? PlaybackWebMessage.Ignored // an ended message without a videoId is not actionable
                        : new PlaybackWebMessage(
                            PlaybackMessageKind.TrackEnded,
                            TrackEndedMessage: new TrackEndedMessage(videoId));

                case "DRM_STATUS":
                    var available = ReadBool(doc.RootElement, "available");
                    return available is { } value
                        ? new PlaybackWebMessage(PlaybackMessageKind.DrmStatus, DrmAvailable: value)
                        : PlaybackWebMessage.Ignored; // missing/non-boolean flag — nothing to apply

                default:
                    return PlaybackWebMessage.Ignored; // unknown type
            }
        }
        catch (JsonException)
        {
            // Malformed payload from the (untrusted) page — ignore.
            return PlaybackWebMessage.Ignored;
        }
    }

    private static PlaybackStateMessage ParseStateUpdate(JsonElement root) => new(
        IsPlaying: ReadBool(root, "isPlaying") ?? false,
        Progress: ReadDouble(root, "progress"),
        Duration: ReadDouble(root, "duration"),
        VideoId: ReadString(root, "videoId") ?? string.Empty,
        Title: ReadString(root, "title") ?? string.Empty,
        Artist: ReadString(root, "artist") ?? string.Empty,
        TrackChanged: ReadBool(root, "trackChanged") ?? false,
        HasVideo: ReadBool(root, "hasVideo"),
        VideoType: null,
        ThumbnailUrl: ReadUri(root, "thumbnailUrl"));

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static Uri? ReadUri(JsonElement obj, string name)
    {
        var value = ReadString(obj, name);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool? ReadBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static double ReadDouble(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.Number &&
            p.TryGetDouble(out var value) &&
            double.IsFinite(value))
        {
            return value < 0 ? 0 : value;
        }

        return 0;
    }
}
