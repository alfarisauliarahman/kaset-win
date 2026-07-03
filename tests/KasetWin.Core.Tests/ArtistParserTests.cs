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
    public void Parses_subscribe_channel_id_from_subscribe_button()
    {
        // Bug 4: the subscribe/unsubscribe mutation must use the channel id from the subscribe
        // button renderer, not the browse id used to load the page. The fixture's button carries
        // an explicit channelId which must be surfaced for the mutation.
        var detail = ParseFixture();

        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", detail.SubscribeChannelId);
    }

    [Fact]
    public void Subscribe_channel_id_is_null_when_button_has_no_channel_id()
    {
        // A header whose subscribe button omits channelId yields no mutation id; the ViewModel then
        // falls back to the navigable browse id (mirroring the macOS client).
        var node = JsonNode.Parse("""
        {
          "header": {
            "musicImmersiveHeaderRenderer": {
              "title": { "runs": [ { "text": "No Channel Artist" } ] },
              "subscriptionButton": {
                "subscribeButtonRenderer": { "subscribed": false }
              }
            }
          },
          "contents": { "sectionListRenderer": { "contents": [] } }
        }
        """);

        var detail = ArtistParser.Parse(node);

        Assert.Null(detail.SubscribeChannelId);
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
        Assert.Equal("UCsampleeeeeeeeeeeeeeee", detail.SubscribeChannelId);
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

    // ── Rich fixture: header banner, subscriber/monthly, radio, all section kinds ────────────────

    private static ArtistDetail ParseFullFixture()
    {
        var node = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Artist, "artist-full"));
        return ArtistParser.Parse(node);
    }

    [Fact]
    public void Full_parses_header_banner_and_identity()
    {
        var detail = ParseFullFixture();

        Assert.Equal("UCfullartist0000000000", detail.Artist.Id);
        Assert.Equal("Sample Artist Full", detail.Artist.Name);
        // Best (highest-resolution) thumbnail is used as both the avatar and the header banner.
        Assert.Equal(new Uri("https://example.test/banner-large.jpg"), detail.Artist.ThumbnailUrl);
        Assert.Equal(new Uri("https://example.test/banner-large.jpg"), detail.HeaderImageUrl);
    }

    [Fact]
    public void Full_prefers_short_subscriber_count_and_reads_monthly_listeners()
    {
        var detail = ParseFullFixture();

        Assert.Equal("1.2M subscribers", detail.SubscriberText);
        Assert.Equal("93.2M monthly listeners", detail.MonthlyListenersText);
        Assert.True(detail.IsSubscribed);
    }

    [Fact]
    public void Full_reads_radio_playlist_from_start_radio_button()
    {
        var detail = ParseFullFixture();

        Assert.Equal("RDEMsampleradio00000", detail.RadioPlaylistId);
        Assert.Null(detail.RadioVideoId);
    }

    [Fact]
    public void Full_parses_top_songs_and_see_all_bottom_endpoint()
    {
        var detail = ParseFullFixture();

        Assert.Equal(2, detail.TopSongs.Count);
        Assert.Equal("vidsong0000001", detail.TopSongs[0].VideoId);
        Assert.Equal("Top Song One", detail.TopSongs[0].Title);
        Assert.Equal("VLsongsfullartist0001", detail.SeeAll.SongsBrowseId);
    }

    [Fact]
    public void Full_classifies_albums_and_singles_into_separate_rails()
    {
        var detail = ParseFullFixture();

        var album = Assert.Single(detail.Albums);
        Assert.Equal("MPREb_fullalbum00001", album.Id);
        Assert.Equal("2024", album.Year);
        Assert.Equal("UCfullartist0000000000", detail.SeeAll.AlbumsBrowseId);

        var single = Assert.Single(detail.SinglesAndEps);
        Assert.Equal("OLAK5uy_single000001", single.Id);
        Assert.Equal("2023", single.Year);
        Assert.Equal("MPADsinglesfull00001", detail.SeeAll.SinglesBrowseId);
    }

    [Fact]
    public void Full_parses_videos_from_watch_endpoint_items()
    {
        var detail = ParseFullFixture();

        var video = Assert.Single(detail.Videos);
        Assert.Equal("vidclip0000001", video.VideoId);
        Assert.Equal("Sample Music Video", video.Title);
        Assert.True(video.HasVideo);
    }

    [Fact]
    public void Full_parses_featured_playlists_and_related_artists()
    {
        var detail = ParseFullFixture();

        var playlist = Assert.Single(detail.FeaturedPlaylists);
        Assert.Equal("VLPLfeatured0000001", playlist.Id);
        Assert.Equal("Sample Featured Playlist", playlist.Title);
        Assert.Equal("Sample Artist Full", playlist.Author?.Name);

        var related = Assert.Single(detail.RelatedArtists);
        Assert.Equal("UCrelatedartist000001", related.Id);
        Assert.Equal("Related Artist One", related.Name);
    }
}
