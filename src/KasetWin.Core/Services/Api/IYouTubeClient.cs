using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// High-level client for regular YouTube (video) InnerTube requests (Req 32). Parallel to
/// <see cref="IYTMusicClient"/> by design (ADR-0020): the request scaffolding is deliberately
/// duplicated rather than shared so the proven music path stays untouched. The critical
/// differences are the origin (<c>https://www.youtube.com</c>) and the <c>WEB</c> client context —
/// a music-origin SAPISIDHASH silently 401s on youtube.com.
/// </summary>
/// <remarks>
/// Implemented by <c>YouTubeClient</c> in <c>KasetWin.Core</c> with no WinUI/WinRT dependency, so it
/// is headless-testable and reusable by the API Explorer CLI. Cookies and SAPISID are secrets and
/// are never logged.
/// </remarks>
public interface IYouTubeClient
{
    // ── Feeds (Req 32.1) ─────────────────────────────────────────────────────────────────
    Task<YouTubeFeed> GetHomeFeedAsync(CancellationToken ct = default);                       // browse FEwhat_to_watch
    Task<YouTubeFeed> GetFeedContinuationAsync(string token, CancellationToken ct = default); // browse continuation
    Task<YouTubeFeed> GetSubscriptionsFeedAsync(CancellationToken ct = default);              // browse FEsubscriptions
    Task<YouTubeFeed> GetHistoryAsync(CancellationToken ct = default);                        // browse FEhistory
    Task<YouTubeFeed> GetDestinationFeedAsync(YouTubeDestination destination, CancellationToken ct = default); // Explore
    Task<IReadOnlyList<YouTubeVideo>> GetShortsAsync(CancellationToken ct = default);         // Shorts (Req 32.4)

    // ── Watch page (Req 32.2) ────────────────────────────────────────────────────────────
    Task<WatchNextData> GetWatchNextAsync(string videoId, CancellationToken ct = default);    // next
    Task<YouTubeCommentsPage> GetCommentsAsync(string continuation, CancellationToken ct = default); // next (comments)

    // ── Search (Req 32.1) ────────────────────────────────────────────────────────────────
    Task<YouTubeSearchResponse> SearchAsync(string query, YouTubeSearchFilter filter = YouTubeSearchFilter.All, CancellationToken ct = default);
    Task<YouTubeSearchResponse> GetSearchContinuationAsync(string token, CancellationToken ct = default);

    // ── Browse detail (Req 32.1) ─────────────────────────────────────────────────────────
    Task<YouTubeChannelDetail> GetChannelAsync(string channelId, CancellationToken ct = default);

    // ── Mutations (Req 32.5) ─────────────────────────────────────────────────────────────
    Task RateVideoAsync(string videoId, YouTubeRating rating, CancellationToken ct = default);     // like/like|dislike|removelike
    Task SetSubscribedAsync(bool subscribed, string channelId, CancellationToken ct = default);    // subscription/subscribe|unsubscribe
    Task AddToWatchLaterAsync(string videoId, CancellationToken ct = default);                     // browse/edit_playlist (WL)
    Task RemoveFromWatchLaterAsync(string videoId, CancellationToken ct = default);                // browse/edit_playlist (WL)
}
