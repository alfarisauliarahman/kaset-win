using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers.YouTube;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for the regular-YouTube (video) parsers (Feature: kaset-winui3, Task 25.1, Req 32.1/32.2).
/// All fixtures are inline, fully sanitized JSON using placeholder ids only — NO real videoIds,
/// cookies, tokens, or PII. They pin the renderer shapes the parsers walk across YouTube's renderer
/// generations.
/// </summary>
public class YouTubeParserTests
{
    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    // ── Feed parser ─────────────────────────────────────────────────────────────────────

    private const string FeedJson = """
    {
      "contents": { "twoColumnBrowseResultsRenderer": { "tabs": [ { "tabRenderer": { "content": {
        "richGridRenderer": { "contents": [
          { "richItemRenderer": { "content": { "videoRenderer": {
            "videoId": "PLACEHOLDER_VID1",
            "title": { "runs": [ { "text": "First Video" } ] },
            "lengthText": { "simpleText": "10:00" },
            "viewCountText": { "simpleText": "1.2M views" },
            "publishedTimeText": { "simpleText": "2 days ago" },
            "ownerText": { "runs": [ { "text": "Channel A", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCPLACEHOLDERA" } } } ] },
            "thumbnail": { "thumbnails": [ { "url": "https://example.test/1.jpg", "width": 480, "height": 360 } ] }
          } } } },
          { "richSectionRenderer": { "content": { "richShelfRenderer": { "contents": [
            { "richItemRenderer": { "content": { "reelItemRenderer": {
              "videoId": "PLACEHOLDER_SHORT1",
              "headline": { "simpleText": "A Short" },
              "thumbnail": { "thumbnails": [ { "url": "https://example.test/s1.jpg", "width": 405, "height": 720 } ] }
            } } } }
          ] } } } },
          { "continuationItemRenderer": { "continuationEndpoint": { "continuationCommand": { "token": "PLACEHOLDER_TOKEN" } } } }
        ] }
      } } } ] } }
    }
    """;

    [Fact]
    public void FeedParser_extracts_videos_shorts_and_continuation()
    {
        var feed = YouTubeFeedParser.Parse(Parse(FeedJson));

        var video = Assert.Single(feed.Videos);
        Assert.Equal("PLACEHOLDER_VID1", video.VideoId);
        Assert.Equal("First Video", video.Title);
        Assert.Equal("10:00", video.LengthText);
        Assert.Equal("1.2M views", video.ViewCountText);
        Assert.Equal("Channel A", video.ChannelName);
        Assert.Equal("UCPLACEHOLDERA", video.ChannelId);
        Assert.False(video.IsShort);

        var shortVideo = Assert.Single(feed.Shorts);
        Assert.Equal("PLACEHOLDER_SHORT1", shortVideo.VideoId);
        Assert.True(shortVideo.IsShort);

        Assert.Equal("PLACEHOLDER_TOKEN", feed.ContinuationToken);
    }

    [Fact]
    public void FeedParser_excludes_shorts_from_the_regular_video_grid()
    {
        var feed = YouTubeFeedParser.Parse(Parse(FeedJson));

        // The Short's id must not appear in the regular video list (Req 32.4).
        Assert.DoesNotContain(feed.Videos, v => v.VideoId == "PLACEHOLDER_SHORT1");
    }

    [Fact]
    public void FeedParser_on_empty_response_returns_empty_feed()
    {
        var feed = YouTubeFeedParser.Parse(Parse("{}"));

        Assert.Empty(feed.Videos);
        Assert.Empty(feed.Shorts);
        Assert.Null(feed.ContinuationToken);
    }

    [Fact]
    public void ItemParser_resolves_videoId_from_lockup_contentId()
    {
        var lockup = Parse("""
        { "contentType": "LOCKUP_CONTENT_TYPE_VIDEO", "contentId": "PLACEHOLDER_LOCKUP",
          "metadata": { "lockupMetadataViewModel": { "title": { "content": "Lockup Title" } } } }
        """);

        var video = YouTubeItemParser.ParseVideo(lockup);

        Assert.NotNull(video);
        Assert.Equal("PLACEHOLDER_LOCKUP", video!.VideoId);
        Assert.Equal("Lockup Title", video.Title);
    }

    // ── Watch-next parser ───────────────────────────────────────────────────────────────

    private const string WatchJson = """
    {
      "contents": { "twoColumnWatchNextResults": {
        "results": { "results": { "contents": [
          { "videoPrimaryInfoRenderer": {
            "title": { "runs": [ { "text": "Watch Title" } ] },
            "dateText": { "simpleText": "Jan 1, 2020" },
            "viewCount": { "videoViewCountRenderer": { "viewCount": { "simpleText": "29,754 views" } } }
          } },
          { "videoSecondaryInfoRenderer": { "owner": { "videoOwnerRenderer": {
            "title": { "runs": [ { "text": "Owner Channel", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCOWNER" } } } ] },
            "subscriberCountText": { "simpleText": "1M subscribers" },
            "thumbnail": { "thumbnails": [ { "url": "https://example.test/avatar.jpg", "width": 88, "height": 88 } ] },
            "subscribeButton": { "subscribeButtonRenderer": { "subscribed": false } }
          } } } },
          { "itemSectionRenderer": { "sectionIdentifier": "comment-item-section", "contents": [
            { "continuationItemRenderer": { "continuationEndpoint": { "continuationCommand": { "token": "COMMENTS_TOKEN" } } } }
          ] } }
        ] } },
        "secondaryResults": { "secondaryResults": { "results": [
          { "compactVideoRenderer": { "videoId": "PLACEHOLDER_RELATED", "title": { "simpleText": "Related Video" } } }
        ] } }
      } }
    }
    """;

    [Fact]
    public void WatchNextParser_extracts_metadata_channel_related_and_comments_token()
    {
        var data = WatchNextParser.Parse(Parse(WatchJson), "PLACEHOLDER_WATCH");

        Assert.Equal("PLACEHOLDER_WATCH", data.VideoId);
        Assert.Equal("Watch Title", data.Title);
        Assert.Equal("29,754 views", data.ViewCountText);
        Assert.Equal("Jan 1, 2020", data.PublishedText);

        Assert.NotNull(data.Channel);
        Assert.Equal("Owner Channel", data.Channel!.Name);
        Assert.Equal("UCOWNER", data.Channel.ChannelId);
        Assert.Equal("1M subscribers", data.Channel.SubscriberCountText);

        Assert.False(data.IsSubscribed);
        Assert.Equal("COMMENTS_TOKEN", data.CommentsContinuationToken);

        var related = Assert.Single(data.Related);
        Assert.Equal("PLACEHOLDER_RELATED", related.VideoId);
    }

    // ── Comments parser ─────────────────────────────────────────────────────────────────

    private const string CommentsEntityJson = """
    {
      "frameworkUpdates": { "entityBatchUpdate": { "mutations": [
        { "payload": { "commentEntityPayload": {
          "properties": { "commentId": "PLACEHOLDER_COMMENT", "content": { "content": "Nice video!" }, "publishedTime": "2 days ago" },
          "author": { "displayName": "Commenter", "channelId": "UCCOMMENTER", "avatarThumbnailUrl": "https://example.test/c.jpg" },
          "toolbar": { "likeCountNotliked": "42" }
        } } }
      ] } },
      "onResponseReceivedEndpoints": [ { "reloadContinuationItemsCommand": { "continuationItems": [
        { "continuationItemRenderer": { "continuationEndpoint": { "continuationCommand": { "token": "NEXT_COMMENTS" } } } }
      ] } } ]
    }
    """;

    [Fact]
    public void CommentsParser_reads_modern_entity_payloads_and_next_token()
    {
        var page = YouTubeCommentsParser.Parse(Parse(CommentsEntityJson));

        var comment = Assert.Single(page.Comments);
        Assert.Equal("PLACEHOLDER_COMMENT", comment.Id);
        Assert.Equal("Commenter", comment.Author);
        Assert.Equal("Nice video!", comment.Text);
        Assert.Equal("2 days ago", comment.PublishedText);
        Assert.Equal("42", comment.LikeCountText);
        Assert.Equal("UCCOMMENTER", comment.AuthorChannelId);

        Assert.Equal("NEXT_COMMENTS", page.ContinuationToken);
    }

    [Fact]
    public void CommentsParser_falls_back_to_legacy_comment_renderers()
    {
        var legacy = Parse("""
        { "continuationContents": { "itemSectionContinuation": { "contents": [
          { "commentThreadRenderer": { "comment": { "commentRenderer": {
            "commentId": "PLACEHOLDER_LEGACY",
            "contentText": { "runs": [ { "text": "Legacy comment" } ] },
            "authorText": { "simpleText": "Legacy Author" },
            "publishedTimeText": { "runs": [ { "text": "1 week ago" } ] },
            "voteCount": { "simpleText": "7" }
          } } } }
        ] } } }
        """);

        var page = YouTubeCommentsParser.Parse(legacy);

        var comment = Assert.Single(page.Comments);
        Assert.Equal("PLACEHOLDER_LEGACY", comment.Id);
        Assert.Equal("Legacy Author", comment.Author);
        Assert.Equal("Legacy comment", comment.Text);
    }

    // ── Search parser ───────────────────────────────────────────────────────────────────

    private const string SearchJson = """
    {
      "contents": { "twoColumnSearchResultsRenderer": { "primaryContents": { "sectionListRenderer": { "contents": [
        { "itemSectionRenderer": { "contents": [
          { "videoRenderer": { "videoId": "PLACEHOLDER_SV", "title": { "runs": [ { "text": "Search Video" } ] } } },
          { "channelRenderer": { "channelId": "UCSEARCHCHAN", "title": { "simpleText": "Search Channel" }, "subscriberCountText": { "simpleText": "500K subscribers" } } },
          { "playlistRenderer": { "playlistId": "PLPLACEHOLDER", "title": { "simpleText": "Search Playlist" } } }
        ] } }
      ] } } } }
    }
    """;

    [Fact]
    public void SearchParser_splits_videos_channels_and_playlists()
    {
        var response = YouTubeSearchParser.Parse(Parse(SearchJson));

        Assert.Equal("PLACEHOLDER_SV", Assert.Single(response.Videos).VideoId);

        var channel = Assert.Single(response.Channels);
        Assert.Equal("UCSEARCHCHAN", channel.ChannelId);
        Assert.Equal("Search Channel", channel.Name);

        var playlist = Assert.Single(response.Playlists);
        Assert.Equal("PLPLACEHOLDER", playlist.Id);
        Assert.False(response.IsEmpty);
    }

    // ── Enum helpers ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(YouTubeRating.Like, "like/like")]
    [InlineData(YouTubeRating.Dislike, "like/dislike")]
    [InlineData(YouTubeRating.None, "like/removelike")]
    public void Rating_maps_to_endpoint(YouTubeRating rating, string expected) =>
        Assert.Equal(expected, rating.Endpoint());

    [Fact]
    public void Destination_browse_ids_are_well_formed()
    {
        Assert.Equal("FEgaming_destination", YouTubeDestination.Gaming.BrowseId());
        Assert.Equal("FElearning_destination", YouTubeDestination.Learning.BrowseId());
    }

    [Fact]
    public void Search_filter_params_match_known_tokens()
    {
        Assert.Null(YouTubeSearchFilter.All.Params());
        Assert.Equal("EgIQAQ==", YouTubeSearchFilter.Videos.Params());
        Assert.Equal("EgIQAg==", YouTubeSearchFilter.Channels.Params());
        Assert.Equal("EgIQAw==", YouTubeSearchFilter.Playlists.Params());
    }
}
