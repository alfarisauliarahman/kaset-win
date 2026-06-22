namespace KasetWin.Core.Models;

/// <summary>
/// Add / remove feedback tokens used for library and like mutations.
/// </summary>
public sealed record FeedbackTokens(string? Add, string? Remove);

/// <summary>
/// Detailed playback metadata resolved for a song (video type, live state,
/// feedback tokens, lyrics browseId, and radio continuation).
/// </summary>
public sealed record SongMetadata
{
    public required Song Song { get; init; }

    public MusicVideoType VideoType { get; init; }

    /// <summary>Live stream — disables seeking (Req 9).</summary>
    public bool IsLive { get; init; }

    public FeedbackTokens? FeedbackTokens { get; init; }

    public string? LyricsBrowseId { get; init; }

    public string? RadioContinuationToken { get; init; }
}

/// <summary>
/// Result of a radio / mix queue fetch: the songs plus an optional continuation
/// token used to drive the infinite mix (Req 25).
/// </summary>
public sealed record RadioQueueResult
{
    public IReadOnlyList<Song> Songs { get; init; } = [];

    public string? ContinuationToken { get; init; }
}
