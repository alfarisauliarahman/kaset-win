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
    [Fact]
    public void ExtractAlbumFromFlexColumns_reads_album_link_from_flex_run()
    {
        // A song row whose 3rd flex column carries an album navigation endpoint (MPRE…) — the
        // title link should light up (Bug 5).
        var row = JsonNode.Parse("""
        {
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Song Title" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
              { "text": "Artist Name", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCartist00000000000000" } } }
            ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
              { "text": "Sample Album", "navigationEndpoint": { "browseEndpoint": { "browseId": "MPREb_album0000000001" } } }
            ] } } }
          ]
        }
        """);

        var album = ParsingHelpers.ExtractAlbumFromFlexColumns(row);

        Assert.NotNull(album);
        Assert.Equal("MPREb_album0000000001", album!.Id);
        Assert.Equal("Sample Album", album.Title);
    }

    [Fact]
    public void ExtractAlbumFromFlexColumns_returns_null_when_no_album_link()
    {
        // A row with only an artist link (no album browse target) must stay plain — ids are never
        // fabricated (Bug 5).
        var row = JsonNode.Parse("""
        {
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Song Title" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
              { "text": "Artist Name", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCartist00000000000000" } } }
            ] } } }
          ]
        }
        """);

        Assert.Null(ParsingHelpers.ExtractAlbumFromFlexColumns(row));
    }

    [Fact]
    public void ExtractAlbumFromFlexColumns_reads_olak_album_via_page_type()
    {
        var row = JsonNode.Parse("""
        {
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Song Title" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
              { "text": "Sample Album", "navigationEndpoint": { "browseEndpoint": {
                "browseId": "OLAK5uy_album00000001",
                "browseEndpointContextSupportedConfigs": { "browseEndpointContextMusicConfig": { "pageType": "MUSIC_PAGE_TYPE_ALBUM" } }
              } } }
            ] } } }
          ]
        }
        """);

        var album = ParsingHelpers.ExtractAlbumFromFlexColumns(row);

        Assert.NotNull(album);
        Assert.Equal("OLAK5uy_album00000001", album!.Id);
    }

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

    // MARK: - Artist list conjunctions ("dan" / "and")

    [Theory]
    // The reported bug: the player page byline under hl=id, already bullet-joined by the
    // observer script, glues the Indonesian conjunction to the last name.
    [InlineData("Tenxi • Anangga • dan Suisei", new[] { "Tenxi", "Anangga", "Suisei" })]
    // The same credit before the observer script bullets it, and its English equivalent.
    [InlineData("Tenxi, Anangga dan Suisei", new[] { "Tenxi", "Anangga", "Suisei" })]
    [InlineData("Tenxi, Anangga and Suisei", new[] { "Tenxi", "Anangga", "Suisei" })]
    [InlineData("Tenxi • Anangga • and Suisei", new[] { "Tenxi", "Anangga", "Suisei" })]
    // A conjunction that arrives as its own segment is a separator, not a name.
    [InlineData("Tenxi • Anangga • dan • Suisei", new[] { "Tenxi", "Anangga", "Suisei" })]
    public void SplitArtistNames_splits_localized_conjunction_lists(string line, string[] expected)
    {
        Assert.Equal(expected, ParsingHelpers.SplitArtistNames(line));
    }

    [Theory]
    // False positives: the conjunction words are part of the name, and there is no other
    // separator establishing a list — the line must survive untouched. A long artist line is
    // always better than a fabricated artist.
    [InlineData("Dan Auerbach")]
    [InlineData("Simon and Garfunkel")]
    [InlineData("Florence + The Machine")]
    [InlineData("Danny Elfman")]
    [InlineData("Sleep Token")]
    public void SplitArtistNames_never_splits_a_name_that_merely_contains_a_conjunction(string name)
    {
        Assert.Equal(new[] { name }, ParsingHelpers.SplitArtistNames(name));
    }

    [Fact]
    public void SplitArtistNames_keeps_capitalised_conjunctions_and_symbols_intact()
    {
        // "Dan"/"And" as a real (capitalised) name inside a genuine list must not be eaten, and
        // "&" is never treated as a conjunction because it lives inside names too.
        Assert.Equal(
            new[] { "Tenxi", "Dan Auerbach" },
            ParsingHelpers.SplitArtistNames("Tenxi • Dan Auerbach"));

        Assert.Equal(
            new[] { "Wind & Fire" },
            ParsingHelpers.SplitArtistNames("Wind & Fire"));
    }

    [Fact]
    public void SplitArtistNames_returns_empty_for_blank_input()
    {
        Assert.Empty(ParsingHelpers.SplitArtistNames(null));
        Assert.Empty(ParsingHelpers.SplitArtistNames("   "));
    }

    [Fact]
    public void ExtractArtistsFromFlexColumns_drops_the_conjunction_glued_to_the_last_artist()
    {
        // hl=id: "Tenxi, Anangga dan Suisei" — the last linked run carries the conjunction.
        var row = JsonNode.Parse("""
        {
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "attached" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
              { "text": "Tenxi", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCtenxi000000000000000" } } },
              { "text": ", " },
              { "text": "Anangga", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCanangga0000000000000" } } },
              { "text": "dan Suisei", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCsuisei00000000000000" } } }
            ] } } }
          ]
        }
        """);

        var artists = ParsingHelpers.ExtractArtistsFromFlexColumns(row);

        Assert.Equal(new[] { "Tenxi", "Anangga", "Suisei" }, artists.Select(a => a.Name));
    }

    [Fact]
    public void ExtractArtistsFromFlexColumns_keeps_a_leading_conjunction_that_is_a_real_name()
    {
        // "Dan Auerbach" is capitalised, so it is a name and never a glued conjunction.
        var row = JsonNode.Parse("""
        {
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Song" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
              { "text": "Tenxi", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCtenxi000000000000000" } } },
              { "text": ", " },
              { "text": "Dan Auerbach", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCauerbach000000000000" } } }
            ] } } }
          ]
        }
        """);

        var artists = ParsingHelpers.ExtractArtistsFromFlexColumns(row);

        Assert.Equal(new[] { "Tenxi", "Dan Auerbach" }, artists.Select(a => a.Name));
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
