using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="SearchResponseParser"/> (task 5.4). These verify the Top Result
/// extraction from <c>musicCardShelfRenderer</c> and the per-type classification of the
/// <c>musicShelfRenderer</c> rows against the sanitized Search fixture, plus the ParseError
/// contract on corrupted input (Req 12.1, 12.4, 20.3).
/// </summary>
public class SearchResponseParserTests
{
    private static SearchResponse ParseFixture()
    {
        var node = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Search, "search"));
        return SearchResponseParser.Parse(node);
    }

    [Fact]
    public void Parses_top_result_artist_from_card_shelf()
    {
        var response = ParseFixture();

        var artist = Assert.IsType<HomeSectionItem.ArtistItem>(response.TopResult);
        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", artist.Artist.Id);
        Assert.Equal("Sample Artist A", artist.Artist.Name);
        Assert.NotNull(artist.Artist.ThumbnailUrl);
    }

    [Fact]
    public void Classifies_song_row_by_video_id()
    {
        var response = ParseFixture();

        var song = Assert.Single(response.Songs);
        Assert.Equal("video0000001", song.VideoId);
        Assert.Equal("video0000001", song.Id);
        Assert.Equal("Sample Track One", song.Title);
        Assert.Equal("Sample Artist A", Assert.Single(song.Artists).Name);
    }

    [Fact]
    public void Classifies_album_row_by_browse_id_and_page_type()
    {
        var response = ParseFixture();

        var album = Assert.Single(response.Albums);
        Assert.Equal("MPREb_0000000album1", album.Id);
        Assert.Equal("Sample Album One", album.Title);
    }

    [Fact]
    public void Does_not_misclassify_top_result_into_groups()
    {
        var response = ParseFixture();

        // The artist top result lives in TopResult, not in the Artists group.
        Assert.Empty(response.Artists);
        Assert.Empty(response.Playlists);
        Assert.Empty(response.Podcasts);
    }

    [Fact]
    public void Empty_but_wellformed_response_yields_empty_groups()
    {
        var node = JsonNode.Parse("""
        { "contents": { "tabbedSearchResultsRenderer": { "tabs": [] } } }
        """);

        var response = SearchResponseParser.Parse(node);

        Assert.Null(response.TopResult);
        Assert.Empty(response.Songs);
        Assert.Empty(response.Albums);
        Assert.Empty(response.Artists);
        Assert.Empty(response.Playlists);
    }

    [Fact]
    public void Throws_parse_error_on_null_root()
    {
        var ex = Assert.Throws<KasetError>(() => SearchResponseParser.Parse(null));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_contents_missing()
    {
        var node = JsonNode.Parse("""{ "responseContext": { "visitorData": "REDACTED" } }""");

        var ex = Assert.Throws<KasetError>(() => SearchResponseParser.Parse(node));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Parse_is_deterministic()
    {
        var first = ParseFixture();
        var second = ParseFixture();

        Assert.Equal(first.TopResult?.Id, second.TopResult?.Id);
        Assert.Equal(first.Songs.Select(s => s.Id), second.Songs.Select(s => s.Id));
        Assert.Equal(first.Albums.Select(a => a.Id), second.Albums.Select(a => a.Id));
        Assert.Equal(first.Artists.Select(a => a.Id), second.Artists.Select(a => a.Id));
        Assert.Equal(first.Playlists.Select(p => p.Id), second.Playlists.Select(p => p.Id));
        Assert.Equal(first.Podcasts.Select(p => p.Id), second.Podcasts.Select(p => p.Id));
    }
}
