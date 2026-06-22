using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers.YouTube;

/// <summary>
/// Pure parser that maps a single YouTube renderer node into a <see cref="YouTubeVideo"/> (Req 32.1).
/// YouTube is mid-migration between renderer generations, so this handles all the shapes that carry
/// a single video: legacy <c>videoRenderer</c>/<c>gridVideoRenderer</c>/<c>compactVideoRenderer</c>,
/// destination-feed <c>videoCardRenderer</c>, the modern <c>lockupViewModel</c>, and the Shorts
/// <c>reelItemRenderer</c>/<c>shortsLockupViewModel</c>. Defensive by design: unknown shapes yield
/// <c>null</c> rather than throwing.
/// </summary>
public static class YouTubeItemParser
{
    /// <summary>Renderer keys that wrap a single regular (non-Shorts) video.</summary>
    public static readonly IReadOnlyList<string> VideoRendererKeys =
    [
        "videoRenderer",
        "gridVideoRenderer",
        "compactVideoRenderer",
        "videoCardRenderer",
        "playlistVideoRenderer",
    ];

    /// <summary>Renderer keys that wrap a single Short.</summary>
    public static readonly IReadOnlyList<string> ShortsRendererKeys =
    [
        "reelItemRenderer",
        "shortsLockupViewModel",
    ];

    /// <summary>
    /// Parses a renderer's inner node into a <see cref="YouTubeVideo"/>. <paramref name="isShort"/>
    /// marks the result as a Short (Req 32.4). Returns <c>null</c> when no videoId can be resolved.
    /// </summary>
    public static YouTubeVideo? ParseVideo(JsonNode? renderer, bool isShort = false)
    {
        if (renderer is not JsonObject)
        {
            return null;
        }

        var videoId = ExtractVideoId(renderer);
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }

        var title = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "title"))
            ?? YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "headline"))
            ?? ExtractLockupTitle(renderer)
            ?? string.Empty;

        var (channelName, channelId) = ExtractChannel(renderer);

        return new YouTubeVideo
        {
            Id = videoId,
            VideoId = videoId,
            Title = title,
            ChannelName = channelName,
            ChannelId = channelId,
            LengthText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "lengthText")),
            ViewCountText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "viewCountText"))
                ?? YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "shortViewCountText")),
            PublishedText = YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, "publishedTimeText")),
            ThumbnailUrl = YouTubeParsingHelpers.BestThumbnailUrl(renderer),
            IsLive = YouTubeParsingHelpers.IsLive(renderer),
            IsShort = isShort,
            WatchedPercent = YouTubeParsingHelpers.WatchedPercent(renderer),
        };
    }

    /// <summary>
    /// Extracts the videoId from a renderer, trying the direct <c>videoId</c> field first, then the
    /// view-model <c>contentId</c>, then any <c>watchEndpoint.videoId</c> nested in the node.
    /// </summary>
    public static string? ExtractVideoId(JsonNode? renderer)
    {
        var direct = YouTubeParsingHelpers.GetString(renderer, "videoId");
        if (!string.IsNullOrEmpty(direct))
        {
            return direct;
        }

        // lockupViewModel / shortsLockupViewModel carry the id under "contentId".
        var contentId = YouTubeParsingHelpers.GetString(renderer, "contentId");
        if (!string.IsNullOrEmpty(contentId))
        {
            return contentId;
        }

        // Fall back to the first watchEndpoint.videoId anywhere in the node.
        var watch = ResponseTreeSearch.FindFirst(renderer, "watchEndpoint");
        return YouTubeParsingHelpers.GetString(watch, "videoId");
    }

    /// <summary>Extracts the lockupViewModel title (<c>metadata…title.content</c>).</summary>
    private static string? ExtractLockupTitle(JsonNode? renderer)
    {
        var metadata = ResponseTreeSearch.FindFirst(renderer, "lockupMetadataViewModel");
        return YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(metadata, "title"));
    }

    /// <summary>
    /// Extracts the channel display name and channel id (<c>UC…</c>) from the renderer's byline,
    /// searching <c>ownerText</c>/<c>shortBylineText</c>/<c>longBylineText</c> and any nested
    /// <c>browseEndpoint</c> with a channel browseId.
    /// </summary>
    private static (string? Name, string? ChannelId) ExtractChannel(JsonNode? renderer)
    {
        string? name = null;
        foreach (var key in new[] { "ownerText", "shortBylineText", "longBylineText" })
        {
            name ??= YouTubeParsingHelpers.Text(YouTubeParsingHelpers.Prop(renderer, key));
        }

        // The channel id is the first browseEndpoint whose browseId looks like a channel id.
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

        return (name, channelId);
    }
}
