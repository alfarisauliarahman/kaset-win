using System.Text.Json.Nodes;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for the pure parsing helpers (task 5.2). These cover the documented
/// behaviour of <see cref="ParsingHelpers"/> and <see cref="ResponseTreeSearch"/> so the
/// per-surface parsers (5.3–5.9) can build on a verified foundation.
/// </summary>
public class ParsingHelpersTests
{
    [Theory]
    [InlineData("3:45", 225)]
    [InlineData("0:30", 30)]
    [InlineData("1:02:03", 3723)]
    [InlineData(" 4:00 ", 240)]
    public void ParseDuration_parses_mm_ss_and_h_mm_ss(string text, int expectedSeconds)
    {
        var result = ParsingHelpers.ParseDuration(text);
        Assert.NotNull(result);
        Assert.Equal(expectedSeconds, (int)result!.Value.TotalSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3:xx")]
    [InlineData("1:2:3:4")]
    public void ParseDuration_returns_null_for_malformed_input(string? text)
    {
        Assert.Null(ParsingHelpers.ParseDuration(text));
    }

    [Fact]
    public void BestThumbnailUrl_picks_highest_resolution()
    {
        var node = JsonNode.Parse("""
        {
          "thumbnail": {
            "musicThumbnailRenderer": {
              "thumbnail": {
                "thumbnails": [
                  { "url": "//host/small.jpg", "width": 60, "height": 60 },
                  { "url": "https://host/large.jpg", "width": 544, "height": 544 },
                  { "url": "https://host/medium.jpg", "width": 226, "height": 226 }
                ]
              }
            }
          }
        }
        """);

        var best = ParsingHelpers.BestThumbnailUrl(node);
        Assert.Equal("https://host/large.jpg", best!.ToString());

        var all = ParsingHelpers.ExtractThumbnails(node);
        Assert.Equal(3, all.Count);
        // Protocol-relative URLs are normalized to https.
        Assert.Equal("https://host/small.jpg", all[0].ToString());
    }

    [Fact]
    public void ExtractIsExplicit_detects_badge()
    {
        var explicitNode = JsonNode.Parse("""
        { "badges": [ { "musicInlineBadgeRenderer": { "icon": { "iconType": "MUSIC_EXPLICIT_BADGE" } } } ] }
        """);
        var cleanNode = JsonNode.Parse("""
        { "badges": [ { "musicInlineBadgeRenderer": { "icon": { "iconType": "MUSIC_NEW_BADGE" } } } ] }
        """);

        Assert.True(ParsingHelpers.ExtractIsExplicit(explicitNode));
        Assert.False(ParsingHelpers.ExtractIsExplicit(cleanNode));
        Assert.False(ParsingHelpers.ExtractIsExplicit(JsonNode.Parse("{}")));
    }

    [Fact]
    public void ExtractArtists_keeps_navigable_id_and_preserves_plain_text()
    {
        var node = JsonNode.Parse("""
        {
          "subtitle": {
            "runs": [
              { "text": "Linked Artist", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCabc123" } } },
              { "text": " • " },
              { "text": "Plain Artist" }
            ]
          }
        }
        """);

        var artists = ParsingHelpers.ExtractArtists(node);
        Assert.Equal(2, artists.Count);
        Assert.Equal("UCabc123", artists[0].Id);
        Assert.Equal("Linked Artist", artists[0].Name);

        // Non-navigable text is preserved with a deterministic stable id.
        Assert.Equal("Plain Artist", artists[1].Name);
        Assert.False(ParsingHelpers.IsNavigableArtistId(artists[1].Id));
        Assert.Equal(artists[1].Id, ParsingHelpers.StableId("artist", "Plain Artist"));
    }

    [Theory]
    [InlineData("UCabc", true)]
    [InlineData("MPLAUCxyz", true)]
    [InlineData("MPREabc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsNavigableArtistId_matches_channel_and_library_prefixes(string? id, bool expected)
    {
        Assert.Equal(expected, ParsingHelpers.IsNavigableArtistId(id));
    }

    [Fact]
    public void StableId_is_deterministic()
    {
        Assert.Equal(
            ParsingHelpers.StableId("artist", "Daft Punk"),
            ParsingHelpers.StableId("artist", "Daft Punk"));
        Assert.NotEqual(
            ParsingHelpers.StableId("artist", "Daft Punk"),
            ParsingHelpers.StableId("artist", "Justice"));
    }

    [Fact]
    public void ResponseTreeSearch_finds_renderers_through_container_reshuffle()
    {
        // The target renderer is buried under arbitrary container nesting.
        var node = JsonNode.Parse("""
        {
          "contents": {
            "tabs": [
              { "tabRenderer": { "content": {
                "sectionListRenderer": { "contents": [
                  { "musicShelfRenderer": { "title": "A" } },
                  { "wrapper": { "musicShelfRenderer": { "title": "B" } } }
                ] } } } }
            ]
          }
        }
        """);

        var first = ResponseTreeSearch.FindFirst(node, "musicShelfRenderer");
        Assert.NotNull(first);

        var all = ResponseTreeSearch.FindAll(node, "musicShelfRenderer");
        Assert.Equal(2, all.Count);

        Assert.True(ResponseTreeSearch.ContainsKey(node, "tabRenderer"));
        Assert.False(ResponseTreeSearch.ContainsKey(node, "missingKey"));
        Assert.True(ResponseTreeSearch.ContainsText(node, "b"));
        Assert.Null(ResponseTreeSearch.FindFirst(node, "nope"));
        Assert.Empty(ResponseTreeSearch.FindAll(node, "nope"));
    }

    [Fact]
    public void ResponseTreeSearch_walks_real_playlist_fixture()
    {
        var node = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Playlist, "playlist"));

        var shelf = ResponseTreeSearch.FindFirst(node, "musicPlaylistShelfRenderer");
        Assert.NotNull(shelf);

        var rows = ResponseTreeSearch.FindAll(node, "musicResponsiveListItemRenderer");
        Assert.NotEmpty(rows);

        // The playlist title lives in the detail header; the text helper extracts it.
        var header = ResponseTreeSearch.FindFirst(node, "musicDetailHeaderRenderer");
        var title = ParsingHelpers.ExtractText(header, "title");
        Assert.Equal("Sample Playlist Detail", title);
    }
}
