using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers.YouTube;

/// <summary>
/// Pure parser for the YouTube <c>next</c> (watch page) response: video metadata, the channel,
/// the related-videos rail, subscribe state, and the comments-section continuation token (Req 32.2).
/// Defensive across renderer generations — missing pieces resolve to <c>null</c>/empty rather than
/// throwing.
/// </summary>
public static class WatchNextParser
{
    /// <summary>Parses a <c>next</c> response into <see cref="WatchNextData"/> for <paramref name="videoId"/>.</summary>
    public static WatchNextData Parse(JsonNode? root, string videoId)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var primary = ResponseTreeSearch.FindFirst(root, "videoPrimaryInfoRenderer");
        var owner = ResponseTreeSearch.FindFirst(root, "videoOwnerRenderer");

        return new WatchNextData
        {
            VideoId = videoId,
            Title = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(primary, "title")),
            ViewCountText = ExtractViewCount(primary),
            PublishedText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(primary, "dateText")),
            Channel = ParseChannel(owner),
            Related = ParseRelated(root, videoId),
            IsSubscribed = ExtractSubscribed(root),
            CommentsContinuationToken = ExtractCommentsContinuation(root),
        };
    }

    private static string? ExtractViewCount(JsonNode? primary)
    {
        var viewCount = ResponseTreeSearch.FindFirst(primary, "videoViewCountRenderer");
        return YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(viewCount, "viewCount"));
    }

    private static YouTubeChannel? ParseChannel(JsonNode? owner)
    {
        if (owner is null)
        {
            return null;
        }

        var name = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(owner, "title"));
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string? channelId = null;
        foreach (var endpoint in ResponseTreeSearch.FindAll(owner, "browseEndpoint"))
        {
            var browseId = YouTubeParsingHelpers.GetString(endpoint, "browseId");
            if (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("UC", StringComparison.Ordinal))
            {
                channelId = browseId;
                break;
            }
        }

        return new YouTubeChannel
        {
            ChannelId = channelId ?? name,
            Name = name,
            SubscriberCountText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(owner, "subscriberCountText")),
            ThumbnailUrl = YouTubeParsingHelpers.BestThumbnailUrl(owner),
        };
    }

    private static IReadOnlyList<YouTubeVideo> ParseRelated(JsonNode? root, string currentVideoId)
    {
        // Related videos live under secondaryResults; fall back to the whole tree when not found.
        var secondary = ResponseTreeSearch.FindFirst(root, "secondaryResults") ?? root;
        var related = YouTubeFeedParser.CollectVideos(secondary);

        // Drop the currently-playing video if it appears in its own related list.
        return [.. related.Where(v => !string.Equals(v.VideoId, currentVideoId, StringComparison.Ordinal))];
    }

    private static bool? ExtractSubscribed(JsonNode? root)
    {
        var button = ResponseTreeSearch.FindFirst(root, "subscribeButtonRenderer");
        if (button is JsonObject obj
            && obj.TryGetPropertyValue("subscribed", out var value)
            && value is JsonValue jv
            && jv.TryGetValue<bool>(out var subscribed))
        {
            return subscribed;
        }

        return null;
    }

    private static string? ExtractCommentsContinuation(JsonNode? root)
    {
        // The comments section is the itemSectionRenderer with sectionIdentifier
        // "comment-item-section"; its continuationCommand token loads the first comments page.
        foreach (var section in ResponseTreeSearch.FindAll(root, "itemSectionRenderer"))
        {
            var identifier = YouTubeParsingHelpers.GetString(section, "sectionIdentifier");
            if (identifier == "comment-item-section")
            {
                var token = YouTubeFeedParser.ExtractContinuation(section);
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }
            }
        }

        return null;
    }
}
