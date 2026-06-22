using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="PlaylistParser"/> and <see cref="PlaylistEditability"/> (task 5.6).
/// These verify playlist detail metadata + track parsing against the sanitized Playlist fixture,
/// continuation-token extraction (Req 8.4), the add-to-playlist menu and create-playlist id
/// parsing, ownership/delete-affordance detection (Req 14.3), and the ParseError contract on
/// corrupted input (Req 20.3).
/// </summary>
public class PlaylistParserTests
{
    private const string PlaylistId = "VLPL0000000playlist1";

    private static JsonNode FixtureNode() =>
        JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Playlist, "playlist"))!;

    // MARK: - Detail metadata + tracks

    [Fact]
    public void Parses_playlist_header_metadata()
    {
        var detail = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);

        Assert.Equal(PlaylistId, detail.Playlist.Id);
        Assert.Equal("Sample Playlist Detail", detail.Playlist.Title);
        Assert.Equal("Sample Owner", detail.Playlist.Author?.Name);
        Assert.Equal(2, detail.Playlist.TrackCount);
    }

    [Fact]
    public void Parses_track_rows_in_order()
    {
        var detail = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);

        Assert.Equal(2, detail.Tracks.Count);

        var first = detail.Tracks[0];
        Assert.Equal("video0000001", first.VideoId);
        Assert.Equal("video0000001", first.Id);
        Assert.Equal("Sample Track One", first.Title);
        Assert.Equal(new TimeSpan(0, 3, 14), first.Duration);

        var second = detail.Tracks[1];
        Assert.Equal("video0000002", second.VideoId);
        Assert.Equal("Sample Track Two", second.Title);
        Assert.Equal(new TimeSpan(0, 4, 2), second.Duration);
    }

    [Fact]
    public void Keeps_navigable_artist_id_and_preserves_plain_text_artist()
    {
        var detail = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);

        var linked = Assert.Single(detail.Tracks[0].Artists);
        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", linked.Id);
        Assert.Equal("Sample Artist A", linked.Name);

        var plain = Assert.Single(detail.Tracks[1].Artists);
        Assert.Equal("Sample Artist B", plain.Name);
        Assert.False(string.IsNullOrEmpty(plain.Id)); // stable, non-empty id
    }

    [Fact]
    public void Extracts_continuation_token_from_playlist_shelf()
    {
        var detail = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);

        Assert.Equal("REDACTED_PLAYLIST_CONTINUATION_TOKEN", detail.ContinuationToken);
    }

    [Fact]
    public void Parse_detail_is_deterministic()
    {
        var first = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);
        var second = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);

        Assert.Equal(first.Playlist.Title, second.Playlist.Title);
        Assert.Equal(first.ContinuationToken, second.ContinuationToken);
        Assert.Equal(first.Tracks.Select(t => t.Id), second.Tracks.Select(t => t.Id));
        Assert.Equal(
            first.Tracks.SelectMany(t => t.Artists.Select(a => a.Id)),
            second.Tracks.SelectMany(t => t.Artists.Select(a => a.Id)));
    }

    [Fact]
    public void Fixture_playlist_is_not_owned_without_delete_affordance()
    {
        var detail = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);

        // The fixture exposes a "Remove from playlist" track menu but no playlist-delete
        // affordance, so ownership is conservatively false (Property 27).
        Assert.False(detail.Playlist.IsOwnedByUser);
    }

    // MARK: - ParseError contract

    [Fact]
    public void Detail_throws_parse_error_on_null_root()
    {
        var ex = Assert.Throws<KasetError>(() => PlaylistParser.ParsePlaylistDetail(null, PlaylistId));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Detail_throws_parse_error_when_header_and_contents_missing()
    {
        var node = JsonNode.Parse("""{ "responseContext": { "visitorData": "REDACTED" } }""");

        var ex = Assert.Throws<KasetError>(() => PlaylistParser.ParsePlaylistDetail(node, PlaylistId));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    // MARK: - Editability

    [Fact]
    public void Owned_when_delete_endpoint_present()
    {
        var node = JsonNode.Parse("""
        { "header": { "x": 1 }, "menu": { "items": [ { "deletePlaylistEndpoint": { "playlistId": "PLabc" } } ] } }
        """);

        Assert.True(PlaylistEditability.IsOwnedByUser(node));
    }

    [Fact]
    public void Owned_when_editable_header_present()
    {
        var node = JsonNode.Parse("""
        { "header": { "musicEditablePlaylistDetailHeaderRenderer": { "header": {} } } }
        """);

        Assert.True(PlaylistEditability.IsOwnedByUser(node));
    }

    [Fact]
    public void Not_owned_when_no_affordance()
    {
        var node = JsonNode.Parse("""{ "header": { "musicDetailHeaderRenderer": {} } }""");

        Assert.False(PlaylistEditability.IsOwnedByUser(node));
    }

    [Fact]
    public void Editability_null_is_false()
    {
        Assert.False(PlaylistEditability.IsOwnedByUser(null));
    }

    // MARK: - Add to playlist menu

    [Fact]
    public void Parses_add_to_playlist_options_with_dedup()
    {
        var node = JsonNode.Parse("""
        {
          "contents": {
            "addToPlaylistRenderer": {
              "playlists": [
                { "playlistAddToOptionRenderer": { "playlistId": "PLaaa", "title": { "runs": [ { "text": "My Mix" } ] } } },
                { "playlistAddToOptionRenderer": { "playlistId": "PLbbb", "title": { "runs": [ { "text": "Chill" } ] } } },
                { "playlistAddToOptionRenderer": { "playlistId": "PLaaa", "title": { "runs": [ { "text": "Duplicate" } ] } } }
              ],
              "createPlaylistButton": { "buttonRenderer": { "navigationEndpoint": { "createPlaylistEndpoint": {} } } }
            }
          }
        }
        """);

        var menu = PlaylistParser.ParseAddToPlaylistMenu(node);

        Assert.True(menu.CanCreate);
        Assert.Equal(new[] { "PLaaa", "PLbbb" }, menu.Playlists.Select(p => p.Id));
        Assert.Equal("My Mix", menu.Playlists[0].Title);
    }

    [Fact]
    public void Add_to_playlist_cannot_create_without_endpoint()
    {
        var node = JsonNode.Parse("""
        {
          "addToPlaylistRenderer": {
            "playlists": [
              { "playlistAddToOptionRenderer": { "playlistId": "PLaaa", "title": { "runs": [ { "text": "Only" } ] } } }
            ]
          }
        }
        """);

        var menu = PlaylistParser.ParseAddToPlaylistMenu(node);

        Assert.False(menu.CanCreate);
        Assert.Equal("PLaaa", Assert.Single(menu.Playlists).Id);
    }

    [Fact]
    public void Add_to_playlist_throws_parse_error_on_null()
    {
        var ex = Assert.Throws<KasetError>(() => PlaylistParser.ParseAddToPlaylistMenu(null));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    // MARK: - Created playlist id

    [Fact]
    public void Prefers_top_level_created_playlist_id()
    {
        var node = JsonNode.Parse("""{ "playlistId": "PLnewlycreated", "status": "STATUS_SUCCEEDED" }""");

        Assert.Equal("PLnewlycreated", PlaylistParser.ParseCreatedPlaylistId(node));
    }

    [Fact]
    public void Falls_back_to_nested_created_playlist_id()
    {
        var node = JsonNode.Parse("""
        { "command": { "browseEndpoint": { "browseId": "VLPLnested", "playlistId": "PLnested" } } }
        """);

        Assert.Equal("PLnested", PlaylistParser.ParseCreatedPlaylistId(node));
    }

    [Fact]
    public void Created_playlist_id_throws_parse_error_when_absent()
    {
        var node = JsonNode.Parse("""{ "status": "STATUS_FAILED" }""");

        var ex = Assert.Throws<KasetError>(() => PlaylistParser.ParseCreatedPlaylistId(node));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    // MARK: - Continuation

    [Fact]
    public void Parses_legacy_shelf_continuation()
    {
        var node = JsonNode.Parse("""
        {
          "continuationContents": {
            "musicPlaylistShelfContinuation": {
              "contents": [
                { "musicResponsiveListItemRenderer": {
                    "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Track 3" } ] } } } ],
                    "playlistItemData": { "videoId": "video0000003" } } }
              ],
              "continuations": [ { "nextContinuationData": { "continuation": "TOKEN_PAGE_2" } } ]
            }
          }
        }
        """);

        var page = PlaylistParser.ParsePlaylistContinuation(node);

        Assert.Equal("video0000003", Assert.Single(page.Tracks).VideoId);
        Assert.Equal("TOKEN_PAGE_2", page.ContinuationToken);
    }

    [Fact]
    public void Parses_2025_append_continuation()
    {
        var node = JsonNode.Parse("""
        {
          "onResponseReceivedActions": [
            { "appendContinuationItemsAction": {
                "continuationItems": [
                  { "musicResponsiveListItemRenderer": {
                      "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Track 4" } ] } } } ],
                      "playlistItemData": { "videoId": "video0000004" } } },
                  { "continuationItemRenderer": { "continuationEndpoint": { "continuationCommand": { "token": "TOKEN_PAGE_3" } } } }
                ] } }
          ]
        }
        """);

        var page = PlaylistParser.ParsePlaylistContinuation(node);

        Assert.Equal("video0000004", Assert.Single(page.Tracks).VideoId);
        Assert.Equal("TOKEN_PAGE_3", page.ContinuationToken);
    }

    [Fact]
    public void Continuation_concatenates_without_loss_or_duplication()
    {
        // Page 1 from the fixture, page 2 from a continuation: merging yields all unique ids.
        var detail = PlaylistParser.ParsePlaylistDetail(FixtureNode(), PlaylistId);
        var continuation = JsonNode.Parse("""
        {
          "continuationContents": {
            "musicPlaylistShelfContinuation": {
              "contents": [
                { "musicResponsiveListItemRenderer": {
                    "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Track 3" } ] } } } ],
                    "playlistItemData": { "videoId": "video0000003" } } }
              ]
            }
          }
        }
        """);

        var page2 = PlaylistParser.ParsePlaylistContinuation(continuation);
        var merged = detail.Tracks.Concat(page2.Tracks).Select(t => t.Id).ToList();

        Assert.Equal(new[] { "video0000001", "video0000002", "video0000003" }, merged);
        Assert.Equal(merged.Count, merged.Distinct().Count());
        Assert.Null(page2.ContinuationToken);
    }

    [Fact]
    public void Continuation_throws_parse_error_on_null()
    {
        var ex = Assert.Throws<KasetError>(() => PlaylistParser.ParsePlaylistContinuation(null));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }
}
