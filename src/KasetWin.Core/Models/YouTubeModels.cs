namespace KasetWin.Core.Models;

/// <summary>
/// A regular YouTube video as it appears in feeds, search results, related lists, and Shorts
/// (Req 32.1/32.4). Distinct from <see cref="Song"/>: YouTube's content model has no album/artist
/// concept — videos belong to channels and carry display-ready strings (view counts, relative
/// dates) rather than structured values. Faithful port of the macOS <c>YouTubeVideo</c>.
/// </summary>
/// <remarks>
/// Identity (<see cref="Id"/>) is the <see cref="VideoId"/> so list virtualization keeps stable
/// item identity across refreshes (Req 16.1). Lives in <c>KasetWin.Core</c> with no WinUI/WinRT
/// dependency so the YouTube client and parsers stay headless-testable.
/// </remarks>
public sealed record YouTubeVideo
{
    /// <summary>Stable identity == <see cref="VideoId"/>.</summary>
    public required string Id { get; init; }

    /// <summary>YouTube videoId.</summary>
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    public string? ChannelName { get; init; }

    public string? ChannelId { get; init; }

    /// <summary>Display duration, e.g. <c>"28:01"</c>. <c>null</c> for live streams.</summary>
    public string? LengthText { get; init; }

    /// <summary>Short display view count, e.g. <c>"29K views"</c>.</summary>
    public string? ViewCountText { get; init; }

    /// <summary>Relative publish date, e.g. <c>"1 year ago"</c>.</summary>
    public string? PublishedText { get; init; }

    public Uri? ThumbnailUrl { get; init; }

    public bool IsLive { get; init; }

    /// <summary>Whether this is a YouTube Short (vertical, ≤60s) — routed to the Shorts surface (Req 32.4).</summary>
    public bool IsShort { get; init; }

    /// <summary>
    /// Percent of the video the signed-in user has already watched (0–100) when YouTube reports
    /// resume progress; <c>null</c> when unwatched or unavailable.
    /// </summary>
    public int? WatchedPercent { get; init; }

    /// <summary>Deterministic thumbnail fallback derived from <see cref="VideoId"/>.</summary>
    public Uri FallbackThumbnailUrl => new($"https://i.ytimg.com/vi/{VideoId}/hqdefault.jpg");
}

/// <summary>
/// A YouTube channel as it appears in search results and on watch pages (Req 32.1/32.2).
/// Identity is the <see cref="ChannelId"/> (<c>UC…</c>).
/// </summary>
public sealed record YouTubeChannel
{
    public required string ChannelId { get; init; }

    public required string Name { get; init; }

    /// <summary>Channel handle, e.g. <c>"@veritasium"</c>.</summary>
    public string? Handle { get; init; }

    /// <summary>Display subscriber count, e.g. <c>"20.8M subscribers"</c>.</summary>
    public string? SubscriberCountText { get; init; }

    public string? DescriptionSnippet { get; init; }

    public Uri? ThumbnailUrl { get; init; }
}

/// <summary>A channel page: metadata plus the videos visible on the landing tab (Req 32.1).</summary>
public sealed record YouTubeChannelDetail
{
    public required YouTubeChannel Channel { get; init; }

    public IReadOnlyList<YouTubeVideo> Videos { get; init; } = [];

    public bool IsSubscribed { get; init; }
}

/// <summary>
/// A YouTube playlist summary (Req 32.1). Identity is the playlist id (<c>VL…/PL…</c>).
/// </summary>
public sealed record YouTubePlaylist
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? VideoCountText { get; init; }

    public Uri? ThumbnailUrl { get; init; }
}

/// <summary>
/// A page of a YouTube feed (home / subscriptions / history / destination), with Shorts split out
/// so the regular grids stay uniform and Shorts route to the dedicated surface (Req 32.4).
/// </summary>
public sealed record YouTubeFeed
{
    public IReadOnlyList<YouTubeVideo> Videos { get; init; } = [];

    /// <summary>Shorts found in the response, surfaced on the dedicated Shorts page (Req 32.4).</summary>
    public IReadOnlyList<YouTubeVideo> Shorts { get; init; } = [];

    public string? ContinuationToken { get; init; }

    public static YouTubeFeed Empty { get; } = new();
}

/// <summary>Results of a YouTube search, split by result kind (Req 32.1).</summary>
public sealed record YouTubeSearchResponse
{
    public IReadOnlyList<YouTubeVideo> Videos { get; init; } = [];

    public IReadOnlyList<YouTubeChannel> Channels { get; init; } = [];

    public IReadOnlyList<YouTubePlaylist> Playlists { get; init; } = [];

    public string? ContinuationToken { get; init; }

    public static YouTubeSearchResponse Empty { get; } = new();

    public bool IsEmpty => Videos.Count == 0 && Channels.Count == 0 && Playlists.Count == 0;
}

/// <summary>
/// Watch-page companion data from the <c>next</c> endpoint: video metadata, channel, the
/// related-videos rail, subscribe state, and the comments continuation token (Req 32.2).
/// </summary>
public sealed record WatchNextData
{
    public required string VideoId { get; init; }

    public string? Title { get; init; }

    /// <summary>Full display view count, e.g. <c>"29,754 views"</c>.</summary>
    public string? ViewCountText { get; init; }

    /// <summary>Relative publish date, e.g. <c>"1 year ago"</c>.</summary>
    public string? PublishedText { get; init; }

    public YouTubeChannel? Channel { get; init; }

    public IReadOnlyList<YouTubeVideo> Related { get; init; } = [];

    /// <summary>Whether the signed-in user is subscribed to the channel; <c>null</c> when unknown.</summary>
    public bool? IsSubscribed { get; init; }

    /// <summary>Continuation token for the video's comments section (Req 32.2).</summary>
    public string? CommentsContinuationToken { get; init; }
}

/// <summary>A single comment on a YouTube video (Req 32.2).</summary>
public sealed record YouTubeComment
{
    public required string Id { get; init; }

    public required string Author { get; init; }

    public Uri? AuthorAvatarUrl { get; init; }

    public required string Text { get; init; }

    public string? PublishedText { get; init; }

    public string? LikeCountText { get; init; }

    /// <summary>The author's channel id (for navigation to their page).</summary>
    public string? AuthorChannelId { get; init; }

    /// <summary>Action token for liking this comment (Req 32.5).</summary>
    public string? LikeAction { get; init; }

    /// <summary>Action token for removing a like.</summary>
    public string? UnlikeAction { get; init; }

    /// <summary>Action token for disliking this comment.</summary>
    public string? DislikeAction { get; init; }

    /// <summary>Action token for removing a dislike.</summary>
    public string? UndislikeAction { get; init; }

    /// <summary>Continuation token for this comment's reply thread.</summary>
    public string? RepliesContinuationToken { get; init; }
}

/// <summary>One page of a video's comments (Req 32.2).</summary>
public sealed record YouTubeCommentsPage
{
    public IReadOnlyList<YouTubeComment> Comments { get; init; } = [];

    /// <summary>Token for the next page (<c>null</c> when exhausted).</summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Params for posting a top-level comment (<c>null</c> when signed out / disabled).</summary>
    public string? CreateCommentParams { get; init; }

    public static YouTubeCommentsPage Empty { get; } = new();
}

/// <summary>
/// Public destination feeds shown on the YouTube Explore surface (Req 32.1). YouTube retired the
/// Trending feed (<c>FEtrending</c> → HTTP 400); these destination feeds replaced it.
/// </summary>
public enum YouTubeDestination
{
    Gaming,
    News,
    Sports,
    Live,
    Fashion,
    Learning,
}

/// <summary>Rating actions for a YouTube video (Req 32.5).</summary>
public enum YouTubeRating
{
    Like,
    Dislike,
    None,
}

/// <summary>Search result kind filter, mapped to an InnerTube <c>params</c> token (Req 32.1).</summary>
public enum YouTubeSearchFilter
{
    All,
    Videos,
    Channels,
    Playlists,
}

/// <summary>Pure helpers for the YouTube enums (endpoints / browse ids / filter params).</summary>
public static class YouTubeEnumExtensions
{
    /// <summary>The InnerTube browse id for a destination feed (e.g. <c>FEgaming_destination</c>).</summary>
    public static string BrowseId(this YouTubeDestination destination) => destination switch
    {
        YouTubeDestination.Gaming => "FEgaming_destination",
        YouTubeDestination.News => "FEnews_destination",
        YouTubeDestination.Sports => "FEsports_destination",
        YouTubeDestination.Live => "FElive_destination",
        YouTubeDestination.Fashion => "FEfashion_destination",
        YouTubeDestination.Learning => "FElearning_destination",
        _ => "FEgaming_destination",
    };

    /// <summary>The InnerTube <c>like/*</c> endpoint for a rating action (Req 32.5).</summary>
    public static string Endpoint(this YouTubeRating rating) => rating switch
    {
        YouTubeRating.Like => "like/like",
        YouTubeRating.Dislike => "like/dislike",
        _ => "like/removelike",
    };

    /// <summary>
    /// The InnerTube search <c>params</c> token for a filter, or <c>null</c> for
    /// <see cref="YouTubeSearchFilter.All"/> (no filter). Base64 tokens confirmed via the macOS
    /// reference / api-explorer.
    /// </summary>
    public static string? Params(this YouTubeSearchFilter filter) => filter switch
    {
        YouTubeSearchFilter.Videos => "EgIQAQ==",
        YouTubeSearchFilter.Channels => "EgIQAg==",
        YouTubeSearchFilter.Playlists => "EgIQAw==",
        _ => null,
    };
}
