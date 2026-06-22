using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers.YouTube;

/// <summary>
/// Pure parser for the YouTube <c>search</c> response, splitting results into videos, channels, and
/// playlists with a continuation token (Req 32.1). Reuses <see cref="YouTubeFeedParser"/> for the
/// video/playlist collection and adds channel extraction across renderer generations.
/// </summary>
public static class YouTubeSearchParser
{
    /// <summary>Parses a search response into its result kinds plus a continuation token.</summary>
    public static YouTubeSearchResponse Parse(JsonNode? root) =>
        new()
        {
            Videos = YouTubeFeedParser.CollectVideos(root),
            Channels = CollectChannels(root),
            Playlists = YouTubeFeedParser.CollectPlaylists(root),
            ContinuationToken = YouTubeFeedParser.ExtractContinuation(root),
        };

    /// <summary>Parses a search continuation response (same traversal as <see cref="Parse"/>).</summary>
    public static YouTubeSearchResponse ParseContinuation(JsonNode? root) => Parse(root);

    private static IReadOnlyList<YouTubeChannel> CollectChannels(JsonNode? root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var channels = new List<YouTubeChannel>();

        foreach (var key in new[] { "channelRenderer", "gridChannelRenderer" })
        {
            foreach (var renderer in ResponseTreeSearch.FindAll(root, key))
            {
                var channelId = YouTubeParsingHelpers.GetString(renderer, "channelId");
                var name = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "title"));
                if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(name) || !seen.Add(channelId))
                {
                    continue;
                }

                channels.Add(new YouTubeChannel
                {
                    ChannelId = channelId,
                    Name = name,
                    SubscriberCountText =
                        YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "subscriberCountText"))
                        ?? YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "videoCountText")),
                    DescriptionSnippet =
                        YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "descriptionSnippet")),
                    ThumbnailUrl = YouTubeParsingHelpers.BestThumbnailUrl(renderer),
                });
            }
        }

        return channels;
    }
}
