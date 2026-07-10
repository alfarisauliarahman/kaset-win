using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="HomeResponseParser"/> (task 5.3). These verify section-based
/// traversal (singleColumnBrowseResultsRenderer → tabs → sectionListRenderer → contents),
/// per-item typing via <see cref="BrowseIdClassifier"/> and renderer pageType, continuation
/// token extraction, and the ParseError contract on corrupted input (Req 11.1, 11.3, 31.1).
/// </summary>
public class HomeResponseParserTests
{
    private static HomeResponse ParseFixture()
    {
        var node = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Home, "FEmusic_home"));
        return HomeResponseParser.Parse(node);
    }

    [Fact]
    public void Parses_all_carousel_sections_from_fixture()
    {
        var response = ParseFixture();

        Assert.Equal(2, response.Sections.Count);
        Assert.Equal("Quick picks", response.Sections[0].Title);
        Assert.Equal("Recommended playlists", response.Sections[1].Title);
    }

    [Fact]
    public void Classifies_two_row_song_item_via_watch_endpoint()
    {
        var response = ParseFixture();

        var song = Assert.IsType<HomeSectionItem.SongItem>(response.Sections[0].Items[0]);
        Assert.Equal("video0000001", song.Song.VideoId);
        Assert.Equal("video0000001", song.Song.Id);
        Assert.Equal("Sample Track One", song.Song.Title);
        Assert.Equal("Sample Artist A", Assert.Single(song.Song.Artists).Name);
        Assert.NotNull(song.Song.ThumbnailUrl);
    }

    [Fact]
    public void Classifies_two_row_album_item_via_browse_id_and_page_type()
    {
        var response = ParseFixture();

        var album = Assert.IsType<HomeSectionItem.AlbumItem>(response.Sections[0].Items[1]);
        Assert.Equal("MPREb_0000000album1", album.Album.Id);
        Assert.Equal("Sample Album One", album.Album.Title);
    }

    [Fact]
    public void Classifies_two_row_playlist_item_via_browse_id_and_page_type()
    {
        var response = ParseFixture();

        var playlist = Assert.IsType<HomeSectionItem.PlaylistItem>(response.Sections[1].Items[0]);
        Assert.Equal("VLPL0000000playlist1", playlist.Pl.Id);
        Assert.Equal("Sample Mix Playlist", playlist.Pl.Title);
    }

    [Fact]
    public void Extracts_continuation_token_from_legacy_shape()
    {
        var response = ParseFixture();

        Assert.Equal("REDACTED_HOME_CONTINUATION_TOKEN", response.ContinuationToken);
    }

    [Fact]
    public void Section_ids_are_stable_across_reparse()
    {
        var first = ParseFixture();
        var second = ParseFixture();

        Assert.Equal(
            first.Sections.Select(s => s.Id),
            second.Sections.Select(s => s.Id));
        Assert.Equal(
            first.Sections.SelectMany(s => s.Items.Select(i => i.Id)),
            second.Sections.SelectMany(s => s.Items.Select(i => i.Id)));
    }

    [Fact]
    public void Parse_is_deterministic()
    {
        var first = ParseFixture();
        var second = ParseFixture();

        Assert.Equal(first.ContinuationToken, second.ContinuationToken);
        Assert.Equal(first.Sections.Count, second.Sections.Count);
        Assert.Equal(
            first.Sections.Select(s => s.Title),
            second.Sections.Select(s => s.Title));
    }

    [Fact]
    public void Empty_but_wellformed_section_list_yields_empty_sections()
    {
        var node = JsonNode.Parse("""
        {
          "contents": {
            "singleColumnBrowseResultsRenderer": {
              "tabs": [
                { "tabRenderer": { "content": { "sectionListRenderer": { "contents": [] } } } }
              ]
            }
          }
        }
        """);

        var response = HomeResponseParser.Parse(node);

        Assert.Empty(response.Sections);
        Assert.Null(response.ContinuationToken);
    }

    [Fact]
    public void Parses_continuation_token_from_new_continuation_command_shape()
    {
        var node = JsonNode.Parse("""
        {
          "contents": {
            "sectionListRenderer": {
              "contents": [],
              "continuationItemRenderer": {
                "continuationEndpoint": {
                  "continuationCommand": { "token": "NEW_TOKEN_123" }
                }
              }
            }
          }
        }
        """);

        var response = HomeResponseParser.Parse(node);

        Assert.Equal("NEW_TOKEN_123", response.ContinuationToken);
    }

    [Fact]
    public void Throws_parse_error_on_invalid_json_string()
    {
        var ex = Assert.Throws<KasetError>(() => HomeResponseParser.Parse("{ not json"));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_root_is_not_object()
    {
        var ex = Assert.Throws<KasetError>(() => HomeResponseParser.Parse(JsonNode.Parse("[]")));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_section_list_missing()
    {
        var node = JsonNode.Parse("""{ "responseContext": { "visitorData": "REDACTED" } }""");

        var ex = Assert.Throws<KasetError>(() => HomeResponseParser.Parse(node));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Parses_top_artists_chart_rank_trend_and_subscriber()
    {
        // A "Top artists" chart shelf: responsive rows carry customIndexColumn (rank + trend icon)
        // and the subscriber count in the second flex column.
        var node = JsonNode.Parse("""
        {
          "contents": {
            "sectionListRenderer": {
              "contents": [
                {
                  "musicShelfRenderer": {
                    "title": { "runs": [ { "text": "Top artists" } ] },
                    "contents": [
                      {
                        "musicResponsiveListItemRenderer": {
                          "customIndexColumn": {
                            "musicCustomIndexColumnRenderer": {
                              "text": { "runs": [ { "text": "10" } ] },
                              "icon": { "iconType": "TRENDING_UP" }
                            }
                          },
                          "flexColumns": [
                            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Tulus" } ] } } },
                            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "2,22 jt subscriber" } ] } } }
                          ],
                          "navigationEndpoint": {
                            "browseEndpoint": {
                              "browseId": "UCartist0000000000001",
                              "browseEndpointContextSupportedConfigs": {
                                "browseEndpointContextMusicConfig": { "pageType": "MUSIC_PAGE_TYPE_ARTIST" }
                              }
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              ]
            }
          }
        }
        """);

        var response = HomeResponseParser.Parse(node);

        var artist = Assert.IsType<HomeSectionItem.ArtistItem>(response.Sections[0].Items[0]).Artist;
        Assert.Equal("Tulus", artist.Name);
        Assert.Equal(10, artist.Rank);
        Assert.Equal(TrendDirection.Up, artist.Trend);
        Assert.Equal("2,22 jt subscriber", artist.SubtitleText);
    }
}
