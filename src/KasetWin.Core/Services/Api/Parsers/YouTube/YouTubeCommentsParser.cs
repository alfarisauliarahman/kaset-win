using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers.YouTube;

/// <summary>
/// Pure parser for a YouTube comments page from the <c>next</c> continuation response (Req 32.2).
/// YouTube serves comments two ways depending on the rollout: modern responses carry
/// <c>commentEntityPayload</c> entities under <c>frameworkUpdates</c>; older ones inline
/// <c>commentRenderer</c>s. Both are handled, with the legacy path as a fallback.
/// </summary>
public static class YouTubeCommentsParser
{
    /// <summary>Parses a comments-page response into comments plus the next-page token.</summary>
    public static YouTubeCommentsPage Parse(JsonNode? root)
    {
        var comments = FromEntityPayloads(root);
        if (comments.Count == 0)
        {
            comments = FromLegacyRenderers(root);
        }

        return new YouTubeCommentsPage
        {
            Comments = comments,
            ContinuationToken = YouTubeFeedParser.ExtractContinuation(root),
            CreateCommentParams = ExtractCreateCommentParams(root),
        };
    }

    // ── Modern entity payloads (2024+) ──────────────────────────────────────────────────

    private static IReadOnlyList<YouTubeComment> FromEntityPayloads(JsonNode? root)
    {
        var comments = new List<YouTubeComment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var payload in ResponseTreeSearch.FindAll(root, "commentEntityPayload"))
        {
            var comment = FromEntityPayload(payload);
            if (comment is not null && seen.Add(comment.Id))
            {
                comments.Add(comment);
            }
        }

        return comments;
    }

    private static YouTubeComment? FromEntityPayload(JsonNode? payload)
    {
        var properties = YouTubeParsingHelpers.Prop(payload, "properties");
        var author = YouTubeParsingHelpers.Prop(payload, "author");
        var toolbar = YouTubeParsingHelpers.Prop(payload, "toolbar");

        var commentId = YouTubeParsingHelpers.GetString(properties, "commentId");
        var text = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(properties, "content"))
            ?? YouTubeParsingHelpers.GetString(YouTubeParsingHelpers.Prop(properties, "content"), "content");
        if (string.IsNullOrEmpty(commentId) || string.IsNullOrEmpty(text))
        {
            return null;
        }

        return new YouTubeComment
        {
            Id = commentId,
            Author = YouTubeParsingHelpers.GetString(author, "displayName") ?? "Unknown",
            AuthorAvatarUrl = ExtractAvatar(author),
            Text = text,
            PublishedText = YouTubeParsingHelpers.GetString(properties, "publishedTime"),
            LikeCountText = YouTubeParsingHelpers.GetString(toolbar, "likeCountNotliked"),
            AuthorChannelId = YouTubeParsingHelpers.GetString(author, "channelId"),
        };
    }

    // ── Legacy commentRenderer ──────────────────────────────────────────────────────────

    private static IReadOnlyList<YouTubeComment> FromLegacyRenderers(JsonNode? root)
    {
        var comments = new List<YouTubeComment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var renderer in ResponseTreeSearch.FindAll(root, "commentRenderer"))
        {
            var commentId = YouTubeParsingHelpers.GetString(renderer, "commentId");
            var text = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "contentText"));
            if (string.IsNullOrEmpty(commentId) || string.IsNullOrEmpty(text) || !seen.Add(commentId))
            {
                continue;
            }

            string? channelId = null;
            foreach (var endpoint in ResponseTreeSearch.FindAll(renderer, "browseEndpoint"))
            {
                var browseId = YouTubeParsingHelpers.GetString(endpoint, "browseId");
                if (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("UC", StringComparison.Ordinal))
                {
                    channelId = browseId;
                    break;
                }
            }

            comments.Add(new YouTubeComment
            {
                Id = commentId,
                Author = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "authorText")) ?? "Unknown",
                AuthorAvatarUrl = YouTubeParsingHelpers.BestThumbnailUrl(
                    YouTubeParsingHelpers.Prop(renderer, "authorThumbnail")),
                Text = text,
                PublishedText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "publishedTimeText")),
                LikeCountText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "voteCount")),
                AuthorChannelId = channelId,
            });
        }

        return comments;
    }

    private static Uri? ExtractAvatar(JsonNode? author)
    {
        // The entity payload may expose the avatar as a direct string URL or a thumbnail object.
        var direct = YouTubeParsingHelpers.ToUri(YouTubeParsingHelpers.GetString(author, "avatarThumbnailUrl"));
        return direct ?? YouTubeParsingHelpers.BestThumbnailUrl(YouTubeParsingHelpers.Prop(author, "avatar"));
    }

    private static string? ExtractCreateCommentParams(JsonNode? root)
    {
        var createRenderer = ResponseTreeSearch.FindFirst(root, "commentSimpleboxRenderer")
            ?? ResponseTreeSearch.FindFirst(root, "createCommentParams");
        return YouTubeParsingHelpers.GetString(createRenderer, "createCommentParams");
    }
}
