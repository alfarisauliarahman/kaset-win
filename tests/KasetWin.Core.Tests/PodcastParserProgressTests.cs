using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Regression tests for episode playback progress parsing, using the exact shape captured from a
/// live podcast playlist page dump (playbackProgress.musicPlaybackProgressRenderer with
/// playbackProgressPercentage + playedText runs).
/// </summary>
public class PodcastParserProgressTests
{
    [Fact]
    public void Parses_playback_progress_percentage_from_live_shape()
    {
        // Mirrors the captured dump: percentage 59, playedText runs [" • ", "Played"],
        // durationText [" • ", "49 min"], subtitle "5d ago".
        var node = JsonNode.Parse("""
        {
          "contents": {
            "sectionListRenderer": {
              "contents": [
                { "musicPlaylistShelfRenderer": {
                    "contents": [
                      { "musicMultiRowListItemRenderer": {
                          "title": { "runs": [ { "text": "Njan Teman Canggung Saya" } ] },
                          "subtitle": { "runs": [ { "text": "5d ago" } ] },
                          "onTap": { "watchEndpoint": { "videoId": "NvGGlqXPV9I" } },
                          "playbackProgress": {
                            "musicPlaybackProgressRenderer": {
                              "playbackProgressPercentage": 59,
                              "playbackProgressText": { "runs": [ { "text": " • " }, { "text": "20 min left" } ] },
                              "durationText": { "runs": [ { "text": " • " }, { "text": "49 min" } ] },
                              "playedText": { "runs": [ { "text": " • " }, { "text": "Played" } ] }
                            }
                          } } }
                    ] } }
              ]
            }
          }
        }
        """);

        var sections = PodcastParser.ParseDiscovery(node);
        var episode = Assert.IsType<PodcastSectionItem.EpisodeItem>(Assert.Single(Assert.Single(sections).Items)).Episode;

        Assert.Equal(0.59, episode.Progress, precision: 2);
        Assert.True(episode.HasProgress);
        Assert.Equal(TimeSpan.FromMinutes(49), episode.Duration);
    }
}
