using System.Text.Json;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// The classification of an inbound YouTube <em>watch-page</em> bridge message after validation
/// (Req 32.2). Parallel to <see cref="PlaybackMessageKind"/> but for the regular-YouTube watch
/// surface, whose observer script (<c>youtubeWatch.js</c>) targets the <c>#movie_player</c> DOM and
/// posts a different message set than the music player (no DRM probe; <c>VIDEO_ENDED</c> instead of
/// <c>TRACK_ENDED</c>; an <c>isAd</c> flag).
/// </summary>
public enum YouTubeWatchMessageKind
{
    /// <summary>The payload was malformed, untyped, or of an unknown type — ignore it.</summary>
    Ignored,

    /// <summary>A <c>STATE_UPDATE</c> carrying current watch-page playback state (Req 32.2).</summary>
    StateUpdate,

    /// <summary>A <c>VIDEO_ENDED</c> carrying the videoId that ended (Req 32.2).</summary>
    VideoEnded,
}

/// <summary>
/// The strongly-typed result of parsing a single untrusted YouTube watch-page bridge message.
/// Fields are populated according to <see cref="Kind"/>; an
/// <see cref="YouTubeWatchMessageKind.Ignored"/> result carries only the defaults.
/// </summary>
/// <param name="Kind">The classification of the message.</param>
/// <param name="IsPlaying">Whether the watch-page video element reports playing (STATE_UPDATE).</param>
/// <param name="Progress">Current position in seconds, clamped to be non-negative (STATE_UPDATE).</param>
/// <param name="Duration">Total duration in seconds, clamped to be non-negative (STATE_UPDATE).</param>
/// <param name="VideoId">The videoId the message refers to (STATE_UPDATE / VIDEO_ENDED).</param>
/// <param name="Title">The video title, when reported (STATE_UPDATE).</param>
/// <param name="IsAd">Whether an ad is currently showing (STATE_UPDATE).</param>
public readonly record struct YouTubeWatchMessage(
    YouTubeWatchMessageKind Kind,
    bool IsPlaying = false,
    double Progress = 0,
    double Duration = 0,
    string VideoId = "",
    string Title = "",
    bool IsAd = false)
{
    /// <summary>A shared "ignore this message" result.</summary>
    public static YouTubeWatchMessage Ignored => new(YouTubeWatchMessageKind.Ignored);
}

/// <summary>
/// Pure, WinRT-free parser for the untrusted JSON messages posted by the injected
/// <c>youtubeWatch.js</c> observer (Req 32.2). Mirrors <see cref="PlaybackMessageParser"/>: the page
/// is untrusted, so every payload is shape-validated before mapping; anything malformed, untyped, or
/// of an unknown type is reported as <see cref="YouTubeWatchMessageKind.Ignored"/> rather than
/// throwing. Kept in <c>Core</c> so the validation stays headless-testable and the WinRT
/// <c>YouTubeWatchController</c> only owns WebView2 concerns. Cookie / token values never appear in
/// these messages and are never logged.
/// </summary>
public static class YouTubeWatchMessageParser
{
    /// <summary>
    /// Parses a single untrusted watch-page bridge message. Never throws for malformed input —
    /// invalid, untyped, or unknown messages return <see cref="YouTubeWatchMessage.Ignored"/>.
    /// </summary>
    /// <param name="json">The raw <c>WebMessageAsJson</c> payload, or <see langword="null"/>.</param>
    /// <returns>The classified, strongly-typed message.</returns>
    public static YouTubeWatchMessage Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return YouTubeWatchMessage.Ignored;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return YouTubeWatchMessage.Ignored;
            }

            if (!doc.RootElement.TryGetProperty("type", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String)
            {
                return YouTubeWatchMessage.Ignored;
            }

            switch (typeProp.GetString())
            {
                case "STATE_UPDATE":
                    return new YouTubeWatchMessage(
                        YouTubeWatchMessageKind.StateUpdate,
                        IsPlaying: ReadBool(doc.RootElement, "isPlaying") ?? false,
                        Progress: ReadDouble(doc.RootElement, "progress"),
                        Duration: ReadDouble(doc.RootElement, "duration"),
                        VideoId: ReadString(doc.RootElement, "videoId") ?? string.Empty,
                        Title: ReadString(doc.RootElement, "title") ?? string.Empty,
                        IsAd: ReadBool(doc.RootElement, "isAd") ?? false);

                case "VIDEO_ENDED":
                    var videoId = ReadString(doc.RootElement, "videoId");
                    return string.IsNullOrEmpty(videoId)
                        ? YouTubeWatchMessage.Ignored // an ended message without a videoId is not actionable
                        : new YouTubeWatchMessage(YouTubeWatchMessageKind.VideoEnded, VideoId: videoId);

                default:
                    return YouTubeWatchMessage.Ignored; // unknown type
            }
        }
        catch (JsonException)
        {
            // Malformed payload from the (untrusted) page — ignore.
            return YouTubeWatchMessage.Ignored;
        }
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

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
