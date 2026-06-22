using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers.YouTube;

/// <summary>
/// Pure parser for YouTube feed responses — home (<c>FEwhat_to_watch</c>), subscriptions
/// (<c>FEsubscriptions</c>), history (<c>FEhistory</c>), destination feeds, and their continuations
/// (Req 32.1/32.4). Walks the response recursively (via <see cref="ResponseTreeSearch"/>) so the
/// frequent container reshuffles between renderer generations don't break the feed, splitting Shorts
/// out from the regular grid (Req 32.4) and extracting the pagination token.
/// </summary>
public static class YouTubeFeedParser
{
    /// <summary>Parses a feed response into videos, Shorts, and a continuation token.</summary>
    public static YouTubeFeed Parse(JsonNode? root)
    {
        var shorts = CollectShorts(root, out var shortIds);
        var videos = CollectVideos(root, shortIds);

        return new YouTubeFeed
        {
            Videos = videos,
            Shorts = shorts,
            ContinuationToken = ExtractContinuation(root),
        };
    }

    /// <summary>
    /// Parses a continuation response. Identical traversal to <see cref="Parse"/> — the recursive
    /// collector handles both <c>appendContinuationItemsCommand</c> and
    /// <c>reloadContinuationItemsCommand</c> wrappers transparently.
    /// </summary>
    public static YouTubeFeed ParseContinuation(JsonNode? root) => Parse(root);

    /// <summary>
    /// Collects regular (non-Shorts) videos from the response in document order, skipping any whose
    /// id already appeared as a Short (<paramref name="excludeIds"/>) and de-duplicating repeats.
    /// </summary>
    public static IReadOnlyList<YouTubeVideo> CollectVideos(JsonNode? root, ISet<string>? excludeIds = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var videos = new List<YouTubeVideo>();

        foreach (var key in YouTubeItemParser.VideoRendererKeys)
        {
            foreach (var renderer in ResponseTreeSearch.FindAll(root, key))
            {
                AddVideo(YouTubeItemParser.ParseVideo(renderer, isShort: false), videos, seen, excludeIds);
            }
        }

        // Modern video lockups (only those whose contentType is a video).
        foreach (var lockup in ResponseTreeSearch.FindAll(root, "lockupViewModel"))
        {
            if (IsVideoLockup(lockup))
            {
                AddVideo(YouTubeItemParser.ParseVideo(lockup, isShort: false), videos, seen, excludeIds);
            }
        }

        return videos;
    }

    /// <summary>
    /// Collects Shorts from the response (reel renderers / shorts lockups), returning the parsed
    /// Shorts and emitting their ids via <paramref name="shortIds"/> so the regular collector can
    /// exclude them.
    /// </summary>
    public static IReadOnlyList<YouTubeVideo> CollectShorts(JsonNode? root, out ISet<string> shortIds)
    {
        shortIds = new HashSet<string>(StringComparer.Ordinal);
        var shorts = new List<YouTubeVideo>();

        foreach (var key in YouTubeItemParser.ShortsRendererKeys)
        {
            foreach (var renderer in ResponseTreeSearch.FindAll(root, key))
            {
                var video = YouTubeItemParser.ParseVideo(renderer, isShort: true);
                if (video is not null && shortIds.Add(video.VideoId))
                {
                    shorts.Add(video);
                }
            }
        }

        return shorts;
    }

    /// <summary>Collects playlist summaries from the response (user playlists / search, Req 32.1).</summary>
    public static IReadOnlyList<YouTubePlaylist> CollectPlaylists(JsonNode? root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var playlists = new List<YouTubePlaylist>();

        // Legacy gridPlaylistRenderer / playlistRenderer.
        foreach (var key in new[] { "gridPlaylistRenderer", "playlistRenderer" })
        {
            foreach (var renderer in ResponseTreeSearch.FindAll(root, key))
            {
                var id = YouTubeParsingHelpers.GetString(renderer, "playlistId");
                var title = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "title"));
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title) && seen.Add(id))
                {
                    playlists.Add(new YouTubePlaylist
                    {
                        Id = id,
                        Title = title,
                        ThumbnailUrl = YouTubeParsingHelpers.BestThumbnailUrl(renderer),
                    });
                }
            }
        }

        // Modern playlist lockups.
        foreach (var lockup in ResponseTreeSearch.FindAll(root, "lockupViewModel"))
        {
            if (!IsPlaylistLockup(lockup))
            {
                continue;
            }

            var id = YouTubeParsingHelpers.GetString(lockup, "contentId");
            var metadata = ResponseTreeSearch.FindFirst(lockup, "lockupMetadataViewModel");
            var title = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(metadata, "title"));
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title) && seen.Add(id))
            {
                playlists.Add(new YouTubePlaylist
                {
                    Id = id,
                    Title = title,
                    ThumbnailUrl = YouTubeParsingHelpers.BestThumbnailUrl(lockup),
                });
            }
        }

        return playlists;
    }

    /// <summary>
    /// Extracts the next-page continuation token from the response, trying the modern
    /// <c>continuationCommand.token</c> first and the legacy <c>nextContinuationData.continuation</c>
    /// as a fallback. Returns <c>null</c> when the feed is exhausted.
    /// </summary>
    public static string? ExtractContinuation(JsonNode? root)
    {
        var command = ResponseTreeSearch.FindFirst(root, "continuationCommand");
        var token = YouTubeParsingHelpers.GetString(command, "token");
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        var legacy = ResponseTreeSearch.FindFirst(root, "nextContinuationData");
        var legacyToken = YouTubeParsingHelpers.GetString(legacy, "continuation");
        return string.IsNullOrEmpty(legacyToken) ? null : legacyToken;
    }

    private static void AddVideo(
        YouTubeVideo? video,
        List<YouTubeVideo> videos,
        HashSet<string> seen,
        ISet<string>? excludeIds)
    {
        if (video is null || (excludeIds is not null && excludeIds.Contains(video.VideoId)))
        {
            return;
        }

        if (seen.Add(video.VideoId))
        {
            videos.Add(video);
        }
    }

    private static bool IsVideoLockup(JsonNode? lockup)
    {
        var contentType = YouTubeParsingHelpers.GetString(lockup, "contentType");
        // When the type is declared, only accept video lockups; when absent, accept if it carries a
        // watchEndpoint (some lockups omit contentType but still resolve to a video).
        if (contentType is not null)
        {
            return contentType.Contains("VIDEO", StringComparison.Ordinal);
        }

        return ResponseTreeSearch.FindFirst(lockup, "watchEndpoint") is not null;
    }

    private static bool IsPlaylistLockup(JsonNode? lockup)
    {
        var contentType = YouTubeParsingHelpers.GetString(lockup, "contentType");
        return contentType is not null && contentType.Contains("PLAYLIST", StringComparison.Ordinal);
    }
}
