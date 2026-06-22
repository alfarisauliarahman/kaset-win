namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// The track-identifying information a <see cref="ILyricsProvider"/> needs to look up lyrics
/// (Req 17.1). Carries the YouTube <c>videoId</c> as the stable cache key (Req 17.4).
/// </summary>
/// <param name="Title">Track title used to search the provider (required, Req 17.1).</param>
/// <param name="Artist">Primary artist name used to search the provider (Req 17.1).</param>
/// <param name="Album">Optional album name; improves match precision when available.</param>
/// <param name="Duration">Optional track duration; lets providers prefer a duration-matched result.</param>
/// <param name="VideoId">
/// Stable YouTube <c>videoId</c>. Used by <see cref="ILyricsService"/> as the cache key and to
/// detect track changes (Req 17.4).
/// </param>
public sealed record LyricsSearchInfo(
    string Title,
    string Artist,
    string? Album,
    TimeSpan? Duration,
    string VideoId);
