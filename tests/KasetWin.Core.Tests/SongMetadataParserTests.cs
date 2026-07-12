using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="SongMetadataParser"/> (task 5.9). These verify base-song parsing
/// from both the <c>player</c> (<c>videoDetails</c>) and <c>next</c>
/// (<c>playlistPanelVideoRenderer</c>) shapes, the <see cref="MusicVideoType"/> mapping
/// (Property 37 surface), live-state detection, library feedback tokens, Lyrics-tab browseId,
/// radio continuation, determinism, and the ParseError contract on corrupted input (Req 9.1,
/// Req 20.3).
/// </summary>
public class SongMetadataParserTests
{
    private const string VideoId = "video0000001";

    private static JsonNode FixtureNode() =>
        JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.SongMetadata, "next_song_metadata"))!;

    // MARK: - Player (videoDetails) fixture

    [Fact]
    public void Parses_base_song_from_video_details()
    {
        var metadata = SongMetadataParser.Parse(FixtureNode(), VideoId);

        Assert.Equal(VideoId, metadata.Song.Id);
        Assert.Equal(VideoId, metadata.Song.VideoId);
        Assert.Equal("Sample Track One", metadata.Song.Title);
        Assert.Equal(TimeSpan.FromSeconds(194), metadata.Song.Duration);
        Assert.Equal(new Uri("https://example.invalid/song.jpg"), metadata.Song.ThumbnailUrl);
    }

    [Fact]
    public void Parses_navigable_artist_from_channel_id()
    {
        var metadata = SongMetadataParser.Parse(FixtureNode(), VideoId);

        var artist = Assert.Single(metadata.Song.Artists);
        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", artist.Id);
        Assert.Equal("Sample Artist A", artist.Name);
    }

    [Fact]
    public void Parses_music_video_type_atv_from_video_details()
    {
        var metadata = SongMetadataParser.Parse(FixtureNode(), VideoId);

        Assert.Equal(MusicVideoType.Atv, metadata.VideoType);
        Assert.Equal(MusicVideoType.Atv, metadata.Song.VideoType);
        Assert.False(metadata.VideoType.HasVideoContent());
        Assert.False(metadata.Song.HasVideo);
    }

    [Fact]
    public void Detects_not_live_from_video_details()
    {
        var metadata = SongMetadataParser.Parse(FixtureNode(), VideoId);

        Assert.False(metadata.IsLive);
    }

    [Fact]
    public void Extracts_lyrics_browse_id_from_lyrics_tab()
    {
        var metadata = SongMetadataParser.Parse(FixtureNode(), VideoId);

        Assert.Equal("MPLYt_0000000lyrics1", metadata.LyricsBrowseId);
    }

    [Fact]
    public void Fixture_has_no_feedback_tokens_or_radio_token()
    {
        var metadata = SongMetadataParser.Parse(FixtureNode(), VideoId);

        Assert.Null(metadata.FeedbackTokens);
        Assert.Null(metadata.RadioContinuationToken);
    }

    [Fact]
    public void Parse_is_deterministic()
    {
        var first = SongMetadataParser.Parse(FixtureNode(), VideoId);
        var second = SongMetadataParser.Parse(FixtureNode(), VideoId);

        Assert.Equal(first.Song.Title, second.Song.Title);
        Assert.Equal(first.VideoType, second.VideoType);
        Assert.Equal(first.LyricsBrowseId, second.LyricsBrowseId);
        Assert.Equal(first.Song.Artists.Select(a => a.Id), second.Song.Artists.Select(a => a.Id));
    }

    // MARK: - Next (playlistPanelVideoRenderer) shape

    [Fact]
    public void Parses_base_song_from_panel_renderer_with_wrapper()
    {
        var node = JsonNode.Parse("""
        {
          "contents": {
            "singleColumnMusicWatchNextResultsRenderer": {
              "tabbedRenderer": { "watchNextTabbedResultsRenderer": { "tabs": [
                { "tabRenderer": { "content": { "musicQueueRenderer": { "content": {
                  "playlistPanelRenderer": {
                    "contents": [
                      { "playlistPanelVideoWrapperRenderer": { "primaryRenderer": {
                        "playlistPanelVideoRenderer": {
                          "videoId": "video0000001",
                          "title": { "runs": [ { "text": "Never Gonna Give You Up" } ] },
                          "longBylineText": { "runs": [
                            { "text": "Rick Astley", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCabcabcabcabcabcabcabc1" } } },
                            { "text": " & " },
                            { "text": "Plain Label" },
                            { "text": " • " },
                            { "text": "Whenever You Need Somebody" },
                            { "text": " • " },
                            { "text": "1987" }
                          ] },
                          "lengthText": { "runs": [ { "text": "3:33" } ] },
                          "thumbnail": { "thumbnails": [ { "url": "https://example.invalid/omv.jpg", "width": 120, "height": 120 } ] },
                          "navigationEndpoint": { "watchEndpoint": { "watchEndpointMusicSupportedConfigs": {
                            "watchEndpointMusicConfig": { "musicVideoType": "MUSIC_VIDEO_TYPE_OMV" } } } },
                          "menu": { "menuRenderer": {
                            "items": [
                              { "toggleMenuServiceItemRenderer": {
                                "defaultIcon": { "iconType": "LIBRARY_ADD" },
                                "defaultServiceEndpoint": { "feedbackEndpoint": { "feedbackToken": "ADD_TOKEN" } },
                                "toggledServiceEndpoint": { "feedbackEndpoint": { "feedbackToken": "REMOVE_TOKEN" } } } }
                            ],
                            "topLevelButtons": [ { "likeButtonRenderer": { "likeStatus": "LIKE" } } ]
                          } }
                        }
                      } } }
                    ],
                    "continuations": [ { "nextRadioContinuationData": { "continuation": "RADIO_TOKEN" } } ]
                  }
                } } } } }
              ] } }
            }
          }
        }
        """);

        var metadata = SongMetadataParser.Parse(node, "video0000001");

        Assert.Equal("Never Gonna Give You Up", metadata.Song.Title);
        Assert.Equal(new TimeSpan(0, 3, 33), metadata.Song.Duration);
        Assert.Equal(new Uri("https://example.invalid/omv.jpg"), metadata.Song.ThumbnailUrl);

        Assert.Equal(MusicVideoType.Omv, metadata.VideoType);
        Assert.True(metadata.VideoType.HasVideoContent());
        Assert.True(metadata.Song.HasVideo);

        // The byline is bullet-segmented "Artists • Album • Year": only the first segment is
        // artists — the album title and year must NOT leak into the artist list.
        Assert.Equal(2, metadata.Song.Artists.Count);
        var linked = metadata.Song.Artists[0];
        Assert.Equal("UCabcabcabcabcabcabcabc1", linked.Id);
        Assert.Equal("Rick Astley", linked.Name);
        var plain = metadata.Song.Artists[1];
        Assert.Equal("Plain Label", plain.Name);
        Assert.False(string.IsNullOrEmpty(plain.Id));

        Assert.Equal("ADD_TOKEN", metadata.FeedbackTokens?.Add);
        Assert.Equal("REMOVE_TOKEN", metadata.FeedbackTokens?.Remove);
        Assert.False(metadata.Song.IsInLibrary);
        Assert.Equal(LikeStatus.Like, metadata.Song.LikeStatus);

        Assert.Equal("RADIO_TOKEN", metadata.RadioContinuationToken);
    }

    [Fact]
    public void Library_remove_toggle_marks_in_library_and_swaps_tokens()
    {
        var node = JsonNode.Parse("""
        {
          "playlistPanelRenderer": { "contents": [
            { "playlistPanelVideoRenderer": {
              "videoId": "v1",
              "title": { "runs": [ { "text": "T" } ] },
              "menu": { "menuRenderer": { "items": [
                { "toggleMenuServiceItemRenderer": {
                  "defaultIcon": { "iconType": "LIBRARY_REMOVE" },
                  "defaultServiceEndpoint": { "feedbackEndpoint": { "feedbackToken": "REMOVE_TOKEN" } },
                  "toggledServiceEndpoint": { "feedbackEndpoint": { "feedbackToken": "ADD_TOKEN" } } } }
              ] } }
            } }
          ] }
        }
        """);

        var metadata = SongMetadataParser.Parse(node, "v1");

        Assert.True(metadata.Song.IsInLibrary);
        Assert.Equal("ADD_TOKEN", metadata.FeedbackTokens?.Add);
        Assert.Equal("REMOVE_TOKEN", metadata.FeedbackTokens?.Remove);
    }

    [Fact]
    public void Detects_live_from_is_live_content()
    {
        var node = JsonNode.Parse("""
        { "videoDetails": { "videoId": "v1", "title": "Live Stream", "isLiveContent": true } }
        """);

        var metadata = SongMetadataParser.Parse(node, "v1");

        Assert.True(metadata.IsLive);
    }

    [Fact]
    public void Detects_live_from_live_streamability()
    {
        var node = JsonNode.Parse("""
        {
          "playabilityStatus": { "status": "OK", "liveStreamability": { "liveStreamabilityRenderer": {} } },
          "videoDetails": { "videoId": "v1", "title": "Live", "isLiveContent": false }
        }
        """);

        var metadata = SongMetadataParser.Parse(node, "v1");

        Assert.True(metadata.IsLive);
    }

    // MARK: - ParseMusicVideoType helper (Property 37 surface)

    [Theory]
    [InlineData("MUSIC_VIDEO_TYPE_OMV", MusicVideoType.Omv)]
    [InlineData("MUSIC_VIDEO_TYPE_ATV", MusicVideoType.Atv)]
    [InlineData("MUSIC_VIDEO_TYPE_UGC", MusicVideoType.Ugc)]
    [InlineData("MUSIC_VIDEO_TYPE_PODCAST_EPISODE", MusicVideoType.PodcastEpisode)]
    [InlineData("MUSIC_VIDEO_TYPE_SOMETHING_NEW", MusicVideoType.Unknown)]
    [InlineData("", MusicVideoType.Unknown)]
    public void ParseMusicVideoType_maps_direct_key(string raw, MusicVideoType expected)
    {
        var node = JsonNode.Parse($$"""{ "musicVideoType": "{{raw}}" }""");

        Assert.Equal(expected, SongMetadataParser.ParseMusicVideoType(node));
    }

    [Fact]
    public void ParseMusicVideoType_maps_watch_endpoint_path()
    {
        var node = JsonNode.Parse("""
        { "navigationEndpoint": { "watchEndpoint": { "watchEndpointMusicSupportedConfigs": {
          "watchEndpointMusicConfig": { "musicVideoType": "MUSIC_VIDEO_TYPE_OMV" } } } } }
        """);

        Assert.Equal(MusicVideoType.Omv, SongMetadataParser.ParseMusicVideoType(node));
    }

    [Fact]
    public void ParseMusicVideoType_null_node_is_unknown()
    {
        Assert.Equal(MusicVideoType.Unknown, SongMetadataParser.ParseMusicVideoType(null));
    }

    // MARK: - ParseError contract

    [Fact]
    public void Throws_parse_error_on_null_root()
    {
        var ex = Assert.Throws<KasetError>(() => SongMetadataParser.Parse(null, VideoId));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_no_song_source_present()
    {
        var node = JsonNode.Parse("""{ "responseContext": { "visitorData": "REDACTED" } }""");

        var ex = Assert.Throws<KasetError>(() => SongMetadataParser.Parse(node, VideoId));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_argument_error_on_empty_video_id()
    {
        Assert.Throws<ArgumentException>(() => SongMetadataParser.Parse(FixtureNode(), ""));
    }
}
