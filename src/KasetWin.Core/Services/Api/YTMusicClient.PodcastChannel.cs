using System.Linq;
using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Podcast creator channel page (browseId <c>UC…</c>): header (avatar / title / subscribe state)
/// plus the channel's shelves parsed through <see cref="PodcastParser.ParseDiscovery(JsonNode?)"/>
/// so episode rows and show cards come out in page order.
/// </summary>
public sealed partial class YTMusicClient
{
    /// <inheritdoc />
    public async Task<PodcastChannel> GetPodcastChannelAsync(string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);

        // ConfigureAwait(true): keep the WebView2 cookie source on its owning thread (see
        // GetPodcastShowAsync) in case follow-up requests are added here later.
        var node = await RequestAsync("browse", BrowseBody(channelId), ApiCacheTtl.Playlist, ct)
            .ConfigureAwait(true);

        // TEMP diag: record the raw page so missing shelves (Video / Playlist) can be diagnosed
        // offline and the parser extended without guessing.
        Diag.Dump("podcast-channel.json", node?.ToJsonString() ?? "null");

        var header = ResponseTreeSearch.FindFirst(node, "musicImmersiveHeaderRenderer")
            ?? ResponseTreeSearch.FindFirst(node, "musicVisualHeaderRenderer")
            ?? ResponseTreeSearch.FindFirst(node, "musicResponsiveHeaderRenderer");

        var title = ParsingHelpers.ExtractText(header, "title");
        var description = ParsingHelpers.ExtractText(header, "description");
        var avatar = ParsingHelpers.BestThumbnailUrl(header ?? node);

        var subscribeButton = ResponseTreeSearch.FindFirst(header ?? node, "subscribeButtonRenderer");
        string? subscriberText = null;
        var isSubscribed = false;
        if (subscribeButton is JsonObject sb)
        {
            subscriberText = ParsingHelpers.ExtractText(sb, "longSubscriberCountText")
                ?? ParsingHelpers.ExtractText(sb, "subscriberCountText");
            if (sb["subscribed"] is JsonValue sv && sv.TryGetValue<bool>(out var subscribed))
            {
                isSubscribed = subscribed;
            }
        }

        var sections = PodcastParser.ParseDiscovery(node);
        Diag.Write($"podcast channel {channelId}: sections=[{string.Join(" | ", sections.Select(s => $"{s.Title}({s.Items.Count})"))}]");

        return new PodcastChannel
        {
            Id = channelId,
            Title = string.IsNullOrEmpty(title) ? "Channel" : title!,
            Description = description,
            AvatarUrl = avatar,
            SubscriberText = subscriberText,
            IsSubscribed = isSubscribed,
            Sections = sections,
        };
    }
}
