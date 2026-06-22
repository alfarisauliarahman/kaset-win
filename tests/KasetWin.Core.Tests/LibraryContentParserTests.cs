using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LibraryContentParser"/> (task 5.5). These verify the
/// per-type classification of the <c>FEmusic_library_landing</c> grid tiles by browseId
/// prefix / pageType against the sanitized Library fixture, plus the ParseError contract
/// on corrupted input (Req 13.1, 20.3).
/// </summary>
public class LibraryContentParserTests
{
    private static LibraryContent ParseFixture()
    {
        var node = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Library, "FEmusic_library_landing"));
        return LibraryContentParser.Parse(node);
    }

    [Fact]
    public void Classifies_liked_music_and_created_playlist_into_playlists()
    {
        var content = ParseFixture();

        // VLLM (Liked Music auto playlist) + VLPL... created playlist land in Playlists.
        var ids = content.Playlists.Select(p => p.Id).ToList();
        Assert.Contains("VLLM", ids);
        Assert.Contains("VLPL0000000playlist1", ids);

        var liked = content.Playlists.Single(p => p.Id == "VLLM");
        Assert.Equal("Liked Music", liked.Title);
    }

    [Fact]
    public void Surfaces_podcast_show_into_playlists()
    {
        var content = ParseFixture();

        // MPSPP podcast show has no dedicated collection; surfaced minimally as a playlist.
        var podcast = content.Playlists.Single(p => p.Id == "MPSPPL0000000podcast1");
        Assert.Equal("Sample Podcast Show", podcast.Title);
    }

    [Fact]
    public void Classifies_artist_by_channel_prefix()
    {
        var content = ParseFixture();

        var artist = Assert.Single(content.Artists);
        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", artist.Id);
        Assert.Equal("Sample Artist A", artist.Name);
    }

    [Fact]
    public void Does_not_misclassify_items_across_collections()
    {
        var content = ParseFixture();

        // The fixture has no albums or standalone songs on the landing grid.
        Assert.Empty(content.Albums);
        Assert.Empty(content.Songs);

        // Three playlist-bucket items (VLLM, created playlist, podcast) and one artist.
        Assert.Equal(3, content.Playlists.Count);
        Assert.Single(content.Artists);
    }

    [Fact]
    public void Parse_is_deterministic()
    {
        var first = ParseFixture();
        var second = ParseFixture();

        Assert.Equal(first.Playlists.Select(p => p.Id), second.Playlists.Select(p => p.Id));
        Assert.Equal(first.Albums.Select(a => a.Id), second.Albums.Select(a => a.Id));
        Assert.Equal(first.Artists.Select(a => a.Id), second.Artists.Select(a => a.Id));
        Assert.Equal(first.Songs.Select(s => s.Id), second.Songs.Select(s => s.Id));
    }

    [Fact]
    public void Wellformed_response_with_no_items_yields_empty_collections()
    {
        var node = JsonNode.Parse("""
        { "contents": { "singleColumnBrowseResultsRenderer": { "tabs": [
            { "tabRenderer": { "content": { "sectionListRenderer": { "contents": [] } } } }
        ] } } }
        """);

        var content = LibraryContentParser.Parse(node);

        Assert.Empty(content.Playlists);
        Assert.Empty(content.Albums);
        Assert.Empty(content.Artists);
        Assert.Empty(content.Songs);
    }

    [Fact]
    public void Classifies_album_browse_id_prefix()
    {
        // gridRenderer tile with an MPRE album browseId is bucketed as an album.
        var node = JsonNode.Parse("""
        { "contents": { "sectionListRenderer": { "contents": [
            { "gridRenderer": { "items": [
                { "musicTwoRowItemRenderer": {
                    "title": { "runs": [ { "text": "Sample Album" } ] },
                    "navigationEndpoint": { "browseEndpoint": { "browseId": "MPREb_album123" } }
                } }
            ] } }
        ] } } }
        """);

        var content = LibraryContentParser.Parse(node);

        var album = Assert.Single(content.Albums);
        Assert.Equal("MPREb_album123", album.Id);
        Assert.Equal("Sample Album", album.Title);
    }

    [Fact]
    public void Throws_parse_error_on_null_root()
    {
        var ex = Assert.Throws<KasetError>(() => LibraryContentParser.Parse((JsonNode?)null));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_section_list_missing()
    {
        var node = JsonNode.Parse("""{ "responseContext": { "visitorData": "REDACTED" } }""");

        var ex = Assert.Throws<KasetError>(() => LibraryContentParser.Parse(node));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_on_invalid_json_string()
    {
        var ex = Assert.Throws<KasetError>(() => LibraryContentParser.Parse("{ not json"));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }
}
