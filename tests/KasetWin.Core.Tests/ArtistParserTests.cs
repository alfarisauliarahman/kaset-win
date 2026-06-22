using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ArtistParser"/> (task 5.7). These verify header / identity
/// extraction (<c>musicImmersiveHeaderRenderer</c> + <c>subscribeButtonRenderer</c>), the
/// Top songs shelf (<c>musicShelfRenderer</c>), the Albums carousel
/// (<c>musicCarouselShelfRenderer</c> → <c>musicTwoRowItemRenderer</c>, <c>MPRE…</c>), and the
/// ParseError contract on corrupted input (Req 15.1, 20.3).
/// </summary>
public class ArtistParserTests
{
    private static ArtistDetail ParseFixture()
    {
        var node = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Artist, "artist"));
        return ArtistParser.Parse(node);
    }

    [Fact]
    public void Parses_artist_identity_from_header_and_subscribe_button()
    {
        var detail = ParseFixture();

        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", detail.Artist.Id);
        Assert.Equal("Sample Artist A", detail.Artist.Name);
    }

    [Fact]
    public void Parses_description_from_header()
    {
        var detail = ParseFixture();

        Assert.Equal("A sanitized sample artist description.", detail.Description);
    }

    [Fact]
    public void Subscription_state_is_false_when_not_subscribed()
    {
        var detail = ParseFixture();

        Assert.False(detail.IsSubscribed);
    }

    [Fact]
    public void Parses_top_songs_from_music_shelf()
    {
        var detail = ParseFixture();

        var song = Assert.Single(detail.TopSongs);
        Assert.Equal("video0000001", song.VideoId);
        Assert.Equal("video0000001", song.Id);
        Assert.Equal("Sample Track One", song.Title);
    }

    [Fact]
    public void Parses_albums_from_carousel_by_browse_id()
    {
        var detail = ParseFixture();

        var album = Assert.Single(detail.Albums);
        Assert.Equal("MPREb_0000000album1", album.Id);
        Assert.Equal("Sample Album One", album.Title);
        Assert.Equal("2024", album.Year);
        // Albums on an artist page are attributed to the page artist.
        Assert.Equal("Sample Artist A", Assert.Single(album.Artists).Name);
    }

    [Fact]
    public void Singles_episodes_and_see_all_are_empty_for_fixture()
    {
        var detail = ParseFixture();

        Assert.Empty(detail.SinglesAndEps);
        Assert.Empty(detail.Episodes);
        Assert.Null(detail.SeeAll.SongsBrowseId);
        Assert.Null(detail.SeeAll.AlbumsBrowseId);
        Assert.Null(detail.SeeAll.SinglesBrowseId);
    }

    [Fact]
    public void Splits_singles_and_eps_by_shelf_title()
    {
        var node = JsonNode.Parse("""
        {
          "header": {
            "musicImmersiveHeaderRenderer": {
              "title": { "runs": [ { "text": "Sample Artist" } ] },
              "subscriptionButton": {
                "subscribeButtonRenderer": {
                  "channelId": "UCsampleeeeeeeeeeeeeeee",
                  "subscribed": true
                }
              }
            }
          },
          "contents": {
            "sectionListRenderer": {
              "contents": [
                {
                  "musicCarouselShelfRenderer": {
                    "header": {
                      "musicCarouselShelfBasicHeaderRenderer": {
                        "title": { "runs": [ { "text": "Singles & EPs" } ] },
                        "moreContentButton": {
                          "buttonRenderer": {
                            "navigationEndpoint": {
                              "browseEndpoint": { "browseId": "MPADsingles123" }
                            }
                          }
                        }
                      }
                    },
                    "contents": [
                      {
                        "musicTwoRowItemRenderer": {
                          "title": {
                            "runs": [
                              {
                                "text": "Sample Single",
                                "navigationEndpoint": {
                                  "browseEndpoint": { "browseId": "OLAK5uy_single001" }
                                }
                              }
                            ]
                          },
                          "subtitle": { "runs": [ { "text": "Single • 2023" } ] }
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

        var detail = ArtistParser.Parse(node);

        Assert.True(detail.IsSubscribed);
        Assert.Empty(detail.Albums);
        var single = Assert.Single(detail.SinglesAndEps);
        Assert.Equal("OLAK5uy_single001", single.Id);
        Assert.Equal("Sample Single", single.Title);
        Assert.Equal("2023", single.Year);
        Assert.Equal("MPADsingles123", detail.SeeAll.SinglesBrowseId);
    }

    [Fact]
    public void Throws_parse_error_on_null_root()
    {
        var ex = Assert.Throws<KasetError>(() => ArtistParser.Parse((JsonNode?)null));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_header_missing()
    {
        var node = JsonNode.Parse("""{ "responseContext": { "visitorData": "REDACTED" } }""");

        var ex = Assert.Throws<KasetError>(() => ArtistParser.Parse(node));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_on_invalid_json_string()
    {
        var ex = Assert.Throws<KasetError>(() => ArtistParser.Parse("{ not json"));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Parse_is_deterministic()
    {
        var first = ParseFixture();
        var second = ParseFixture();

        Assert.Equal(first.Artist.Id, second.Artist.Id);
        Assert.Equal(first.TopSongs.Select(s => s.Id), second.TopSongs.Select(s => s.Id));
        Assert.Equal(first.Albums.Select(a => a.Id), second.Albums.Select(a => a.Id));
        Assert.Equal(first.SinglesAndEps.Select(a => a.Id), second.SinglesAndEps.Select(a => a.Id));
    }
}
