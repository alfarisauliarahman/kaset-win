using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="RadioQueueParser"/> (task 5.8). These verify radio/mix queue song
/// extraction against the sanitized RadioQueue fixture — including the
/// <c>playlistPanelVideoWrapperRenderer.primaryRenderer.playlistPanelVideoRenderer</c> wrapper
/// and a direct <c>playlistPanelVideoRenderer</c> — continuation-token extraction from
/// <c>nextRadioContinuationData</c> (Req 25.1), continuation-response parsing via
/// <c>playlistPanelContinuation</c>, determinism (Property 23/44), and the ParseError contract
/// on corrupted input (Req 20.3).
/// </summary>
public class RadioQueueParserTests
{
    private static JsonNode FixtureNode() =>
        JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.RadioQueue, "next_radio_queue"))!;

    // MARK: - Songs

    [Fact]
    public void Parses_songs_in_order_from_wrapper_renderer()
    {
        var result = RadioQueueParser.Parse(FixtureNode());

        Assert.Equal(2, result.Songs.Count);

        var first = result.Songs[0];
        Assert.Equal("video0000001", first.VideoId);
        Assert.Equal("video0000001", first.Id);
        Assert.Equal("Sample Track One", first.Title);
        Assert.Equal(new TimeSpan(0, 3, 14), first.Duration);

        var second = result.Songs[1];
        Assert.Equal("video0000002", second.VideoId);
        Assert.Equal("Sample Track Two", second.Title);
        Assert.Equal(new TimeSpan(0, 4, 2), second.Duration);
    }

    [Fact]
    public void Keeps_navigable_artist_id_and_preserves_plain_text_artist()
    {
        var result = RadioQueueParser.Parse(FixtureNode());

        var linked = Assert.Single(result.Songs[0].Artists);
        Assert.Equal("UCxxxxxxxxxxxxxxxxxxxxxx", linked.Id);
        Assert.Equal("Sample Artist A", linked.Name);

        var plain = Assert.Single(result.Songs[1].Artists);
        Assert.Equal("Sample Artist B", plain.Name);
        Assert.False(string.IsNullOrEmpty(plain.Id)); // stable, non-empty id
    }

    [Fact]
    public void Extracts_radio_continuation_token()
    {
        var result = RadioQueueParser.Parse(FixtureNode());

        Assert.Equal("REDACTED_RADIO_CONTINUATION_TOKEN", result.ContinuationToken);
    }

    [Fact]
    public void Parse_is_deterministic()
    {
        var first = RadioQueueParser.Parse(FixtureNode());
        var second = RadioQueueParser.Parse(FixtureNode());

        Assert.Equal(
            first.Songs.Select(s => s.VideoId),
            second.Songs.Select(s => s.VideoId));
        Assert.Equal(first.ContinuationToken, second.ContinuationToken);
    }

    // MARK: - Direct (non-wrapper) renderer

    [Fact]
    public void Parses_direct_playlist_panel_video_renderer_without_wrapper()
    {
        var root = new JsonObject
        {
            ["contents"] = new JsonObject
            {
                ["playlistPanelRenderer"] = new JsonObject
                {
                    ["contents"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["playlistPanelVideoRenderer"] = new JsonObject
                            {
                                ["videoId"] = "directvideo01",
                                ["title"] = Runs("Direct Track"),
                                ["longBylineText"] = Runs("Direct Artist"),
                                ["lengthText"] = Runs("2:05"),
                            },
                        },
                    },
                },
            },
        };

        var result = RadioQueueParser.Parse(root);

        var song = Assert.Single(result.Songs);
        Assert.Equal("directvideo01", song.VideoId);
        Assert.Equal("Direct Track", song.Title);
        Assert.Equal(new TimeSpan(0, 2, 5), song.Duration);
        Assert.Null(result.ContinuationToken);
    }

    // MARK: - Continuation response

    [Fact]
    public void Parses_continuation_response_via_playlist_panel_continuation()
    {
        var root = new JsonObject
        {
            ["continuationContents"] = new JsonObject
            {
                ["playlistPanelContinuation"] = new JsonObject
                {
                    ["contents"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["playlistPanelVideoRenderer"] = new JsonObject
                            {
                                ["videoId"] = "contvideo01",
                                ["title"] = Runs("Continuation Track"),
                                ["longBylineText"] = Runs("Continuation Artist"),
                                ["lengthText"] = Runs("1:30"),
                            },
                        },
                    },
                    ["continuations"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["nextRadioContinuationData"] = new JsonObject
                            {
                                ["continuation"] = "NEXT_PAGE_TOKEN",
                            },
                        },
                    },
                },
            },
        };

        var result = RadioQueueParser.ParseContinuation(root);

        var song = Assert.Single(result.Songs);
        Assert.Equal("contvideo01", song.VideoId);
        Assert.Equal("NEXT_PAGE_TOKEN", result.ContinuationToken);
    }

    // MARK: - Edge cases

    [Fact]
    public void Skips_rows_without_video_id()
    {
        var root = new JsonObject
        {
            ["playlistPanelRenderer"] = new JsonObject
            {
                ["contents"] = new JsonArray
                {
                    // Non-video automix toggle row — no videoId.
                    new JsonObject { ["automixPreviewVideoRenderer"] = new JsonObject() },
                    new JsonObject
                    {
                        ["playlistPanelVideoRenderer"] = new JsonObject
                        {
                            ["videoId"] = "keepme01",
                            ["title"] = Runs("Keep Me"),
                        },
                    },
                },
            },
        };

        var result = RadioQueueParser.Parse(root);

        var song = Assert.Single(result.Songs);
        Assert.Equal("keepme01", song.VideoId);
    }

    [Fact]
    public void Wellformed_empty_panel_yields_empty_queue()
    {
        var root = new JsonObject
        {
            ["playlistPanelRenderer"] = new JsonObject { ["contents"] = new JsonArray() },
        };

        var result = RadioQueueParser.Parse(root);

        Assert.Empty(result.Songs);
        Assert.Null(result.ContinuationToken);
    }

    // MARK: - ParseError contract (Req 20.3)

    [Fact]
    public void Throws_parse_error_when_root_is_null()
    {
        var ex = Assert.Throws<KasetError>(() => RadioQueueParser.Parse(null));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_root_is_not_an_object()
    {
        var ex = Assert.Throws<KasetError>(() => RadioQueueParser.Parse(JsonValue.Create(42)));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    [Fact]
    public void Throws_parse_error_when_panel_renderer_is_missing()
    {
        var root = new JsonObject { ["responseContext"] = new JsonObject() };

        var ex = Assert.Throws<KasetError>(() => RadioQueueParser.Parse(root));
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }

    private static JsonObject Runs(string text) =>
        new() { ["runs"] = new JsonArray { new JsonObject { ["text"] = text } } };
}
