using KasetWin.Core.Models;

namespace KasetWin.Core.Services.RichPresence;

/// <summary>
/// A Discord Rich Presence activity payload, already shaped and clamped to Discord's limits.
/// </summary>
/// <param name="Details">Top line — the track title. 2–128 characters.</param>
/// <param name="State">Second line — the artist(s). 2–128 characters.</param>
/// <param name="LargeImageUrl">Artwork URL, or <c>null</c> when the track has none.</param>
/// <param name="LargeImageText">Hover text for the artwork (the album), or <c>null</c>.</param>
/// <param name="StartUnixSeconds">
/// When the current playback position started, so Discord renders a live "elapsed" counter.
/// <c>null</c> while paused — a paused track must not appear to keep counting.
/// </param>
/// <param name="EndUnixSeconds">
/// When the track will finish, so Discord renders a "remaining" counter. <c>null</c> while paused
/// or when the duration is unknown (e.g. a live stream).
/// </param>
public readonly record struct DiscordActivity(
    string Details,
    string State,
    string? LargeImageUrl,
    string? LargeImageText,
    long? StartUnixSeconds,
    long? EndUnixSeconds);

/// <summary>
/// Builds a <see cref="DiscordActivity"/> from player state. Pure and headless-testable: it does no
/// I/O and never touches the Discord pipe — that is <c>IRichPresenceClient</c>'s job.
/// </summary>
/// <remarks>
/// Discord silently rejects an activity whose <c>details</c> or <c>state</c> is shorter than 2 or
/// longer than 128 characters, and the failure is invisible from the app's side (the presence just
/// never appears). Clamping here, in one tested place, is what stops that being a mystery bug.
/// </remarks>
public static class DiscordActivityBuilder
{
    /// <summary>Discord's minimum accepted length for <c>details</c> / <c>state</c>.</summary>
    public const int MinFieldLength = 2;

    /// <summary>Discord's maximum accepted length for <c>details</c> / <c>state</c>.</summary>
    public const int MaxFieldLength = 128;

    /// <summary>Shown when a track carries no artist at all, so <c>state</c> still passes the minimum.</summary>
    private const string UnknownArtist = "Unknown artist";

    /// <summary>
    /// Maps the current track and playback state to an activity.
    /// </summary>
    /// <param name="track">The playing track; <c>null</c> yields <c>null</c> (presence cleared).</param>
    /// <param name="isPlaying">Whether playback is running. Paused drops the timestamps.</param>
    /// <param name="progress">Current position in seconds.</param>
    /// <param name="duration">Track length in seconds; non-positive means unknown (live).</param>
    /// <param name="nowUnixSeconds">Current wall-clock time, supplied so this stays deterministic.</param>
    /// <returns>The activity, or <c>null</c> when there is nothing to show.</returns>
    public static DiscordActivity? Build(
        Song? track,
        bool isPlaying,
        double progress,
        double duration,
        long nowUnixSeconds)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Title))
        {
            return null;
        }

        var artists = string.Join(", ", track.Artists.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        if (string.IsNullOrWhiteSpace(artists))
        {
            artists = UnknownArtist;
        }

        long? start = null;
        long? end = null;
        if (isPlaying)
        {
            // Discord renders elapsed time from `start`, so it must be "now minus how far in we are"
            // rather than "now" — otherwise every seek and every reconnect restarts the counter at 0.
            var safeProgress = double.IsFinite(progress) && progress > 0 ? progress : 0;
            start = nowUnixSeconds - (long)safeProgress;

            if (double.IsFinite(duration) && duration > 0)
            {
                end = start + (long)duration;
            }
        }

        return new DiscordActivity(
            Clamp(track.Title),
            Clamp(artists),
            track.ThumbnailUrl?.ToString(),
            string.IsNullOrWhiteSpace(track.Album?.Title) ? null : Clamp(track.Album!.Title),
            start,
            end);
    }

    /// <summary>
    /// Fits a field to Discord's 2–128 character window: pads a too-short value (a one-character
    /// title is legal on YouTube but is rejected by Discord) and ellipsises a too-long one.
    /// </summary>
    public static string Clamp(string value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > MaxFieldLength)
        {
            return string.Concat(text.AsSpan(0, MaxFieldLength - 1), "…");
        }

        return text.Length < MinFieldLength ? text.PadRight(MinFieldLength) : text;
    }
}
