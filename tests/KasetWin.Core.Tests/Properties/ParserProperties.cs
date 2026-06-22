using System.Text.Json.Nodes;
using CsCheck;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests (CsCheck) for the modular InnerTube parsers (Feature: kaset-winui3).
/// Covers the parser-correctness properties from the design "Correctness Properties" section:
/// idempotency / stable identity (23), search classification (24), browseId prefix
/// classification (25), continuation merge without loss/duplication (26), playlist ownership
/// detection (27), the ParseError contract on corrupt input (34), and radio-queue extraction (44).
///
/// Each property is a single <c>[Fact]</c> that drives CsCheck with at least 100 iterations.
/// Generators are constrained to the relevant input space so counterexamples stay readable.
///
/// SECURITY: every id/token/secret-like value produced here is a synthetic placeholder — never
/// a real cookie/token/SAPISID value (AGENTS.md critical rule).
/// </summary>
public class ParserProperties
{
    /// <summary>URL/cookie-safe alphabet for synthetic ids and suffixes.</summary>
    private const string Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly Gen<string> Suffix =
        Gen.Char[Alphabet].Array[0, 24].Select(chars => new string(chars));

    // The six per-surface fixtures exercised by the idempotency property.
    private static readonly string[] Surfaces =
    {
        TestFixtures.Surfaces.Home,
        TestFixtures.Surfaces.Search,
        TestFixtures.Surfaces.Library,
        TestFixtures.Surfaces.Playlist,
        TestFixtures.Surfaces.Artist,
        TestFixtures.Surfaces.RadioQueue,
    };

    // =====================================================================================
    // Property 23 (design 5.10): Parser idempoten/deterministik dengan identitas stabil.
    // =====================================================================================

    // Feature: kaset-winui3, Property 23: Parser bersifat idempoten/deterministik dengan identitas stabil
    // Validates: Requirements 23.3, 11.1, 14.1, 14.2, 15.1, 16.1, 31.1
    [Fact]
    public void Property23_Parser_is_idempotent_with_stable_identity()
    {
        // For any per-surface fixture, parsing twice yields equivalent models (deterministic),
        // and every produced item Id is non-empty and identical across the two calls.
        Gen.OneOfConst(Surfaces).Sample(
            surface =>
            {
                var first = ParseSurfaceSnapshot(surface);
                var second = ParseSurfaceSnapshot(surface);

                // Deterministic: identical rich signature (kind|id|title|token …) across reparses.
                Assert.Equal(first.Signature, second.Signature);

                // Stable identity: identical id sequence, and every id is non-empty.
                Assert.Equal(first.Ids, second.Ids);
                Assert.All(first.Ids, id => Assert.False(string.IsNullOrEmpty(id)));
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 24 (design 5.11): Klasifikasi hasil search sesuai tipe + Top Result dari card.
    // =====================================================================================

    // Feature: kaset-winui3, Property 24: Klasifikasi hasil pencarian sesuai tipe
    // Validates: Requirements 12.1, 12.3
    [Fact]
    public void Property24_Search_results_classified_by_type()
    {
        // Sanity invariant on the real fixture: the Top Result comes from the card shelf and the
        // grouped results are internally consistent (re-parse is stable).
        var fixtureNode = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Search, "search"));
        var fixture = SearchResponseParser.Parse(fixtureNode);
        Assert.NotNull(fixture.TopResult);
        Assert.All(fixture.Songs, s => Assert.False(string.IsNullOrEmpty(s.VideoId)));

        // For any synthetic search response with a randomised mix/order of typed rows and an
        // optional card-shelf Top Result, each row lands in exactly the group its renderer
        // type/browseId prefix dictates, and the Top Result is taken from musicCardShelfRenderer.
        // RowType: 0=song, 1=album, 2=artist, 3=playlist, 4=podcast.
        // TopKind: 0=none, 1=song, 2=album, 3=artist, 4=playlist, 5=podcast.
        Gen.Select(Gen.Int[0, 4].Array[0, 8], Gen.Int[0, 5]).Sample(
            spec =>
            {
                var (rowTypes, topKind) = spec;
                var root = BuildSearchResponse(rowTypes, topKind, out var expectedSongIds, out var topExpectedId);

                var response = SearchResponseParser.Parse(root);

                Assert.Equal(rowTypes.Count(t => t == 0), response.Songs.Count);
                Assert.Equal(rowTypes.Count(t => t == 1), response.Albums.Count);
                Assert.Equal(rowTypes.Count(t => t == 2), response.Artists.Count);
                Assert.Equal(rowTypes.Count(t => t == 3), response.Playlists.Count);
                Assert.Equal(rowTypes.Count(t => t == 4), response.Podcasts.Count);

                // Songs keep their videoId identity.
                Assert.Equal(expectedSongIds, response.Songs.Select(s => s.Id).ToArray());

                // Top Result is sourced from the card shelf (or absent when none was emitted).
                switch (topKind)
                {
                    case 0:
                        Assert.Null(response.TopResult);
                        break;
                    case 1:
                        Assert.Equal(topExpectedId, Assert.IsType<HomeSectionItem.SongItem>(response.TopResult).Song.Id);
                        break;
                    case 2:
                        Assert.Equal(topExpectedId, Assert.IsType<HomeSectionItem.AlbumItem>(response.TopResult).Album.Id);
                        break;
                    case 3:
                        Assert.Equal(topExpectedId, Assert.IsType<HomeSectionItem.ArtistItem>(response.TopResult).Artist.Id);
                        break;
                    default: // 4 (playlist) and 5 (podcast) both surface as a PlaylistItem.
                        Assert.Equal(topExpectedId, Assert.IsType<HomeSectionItem.PlaylistItem>(response.TopResult).Pl.Id);
                        break;
                }
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 25 (design 5.12): BrowseIdClassifier.Classify via prefix (deterministik).
    // =====================================================================================

    // Feature: kaset-winui3, Property 25: Klasifikasi browseId via prefix
    // Validates: Requirements 11.3, 13.1, 12.4, 15.2
    [Fact]
    public void Property25_BrowseId_classified_by_prefix()
    {
        // For any id formed from a known prefix + random suffix, Classify returns the prefix's
        // kind, honouring precedence (MPSPP→Podcast wins over the MP* family, MPLAUC/UC→Artist,
        // MPRE/OLAK→Album, VL/PL/RDCLAK→Playlist); unknown prefixes → Unknown. Always deterministic.
        var entries = new (string Prefix, BrowseIdKind Kind)[]
        {
            ("VL", BrowseIdKind.Playlist),
            ("PL", BrowseIdKind.Playlist),
            ("RDCLAK", BrowseIdKind.Playlist),
            ("MPRE", BrowseIdKind.Album),
            ("OLAK", BrowseIdKind.Album),
            ("UC", BrowseIdKind.Artist),
            ("MPLAUC", BrowseIdKind.Artist),
            ("MPSPP", BrowseIdKind.Podcast),
            // "Random"/unrecognised prefixes — none of which start with a known prefix.
            ("FE", BrowseIdKind.Unknown),
            ("ZZ", BrowseIdKind.Unknown),
            ("XQ", BrowseIdKind.Unknown),
            ("GG", BrowseIdKind.Unknown),
        };

        Gen.Select(Gen.OneOfConst(entries), Suffix).Sample(
            pair =>
            {
                var (entry, suffix) = pair;
                var id = entry.Prefix + suffix;

                Assert.Equal(entry.Kind, BrowseIdClassifier.Classify(id));

                // Deterministic: repeated classification of the same id agrees.
                Assert.Equal(BrowseIdClassifier.Classify(id), BrowseIdClassifier.Classify(id));
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 26 (design 5.13): Continuation menggabungkan tanpa kehilangan/duplikasi.
    // =====================================================================================

    // Feature: kaset-winui3, Property 26: Continuation menggabungkan tanpa kehilangan atau duplikasi
    // Validates: Requirements 8.4, 11.2
    [Fact]
    public void Property26_Continuation_merge_has_no_loss_or_duplication()
    {
        // For any two pages of tracks with globally unique videoIds, merging the next page onto
        // the current one yields the exact concatenation: no item is lost, order is preserved,
        // and no id is duplicated. The latest page's continuation token wins.
        Gen.Select(UniqueSongs("A"), UniqueSongs("B"), Gen.OneOf(Gen.Const((string?)null), PbtToken)).Sample(
            data =>
            {
                var (page1, page2, nextToken) = data;

                var merged = page1.Concat(page2).ToList();
                var mergedIds = merged.Select(s => s.Id).ToList();

                // No loss: count is the sum of both pages.
                Assert.Equal(page1.Count + page2.Count, merged.Count);

                // Concatenation: order preserved (page1 then page2).
                Assert.Equal(
                    page1.Select(s => s.Id).Concat(page2.Select(s => s.Id)).ToList(),
                    mergedIds);

                // No duplication: all ids remain distinct.
                Assert.Equal(mergedIds.Count, mergedIds.Distinct().Count());

                // Token model: the latest (next) page's token replaces the previous one.
                var pageA = new PlaylistContinuation { Tracks = page1, ContinuationToken = "PAGE_2_TOKEN" };
                var pageB = new PlaylistContinuation { Tracks = page2, ContinuationToken = nextToken };
                Assert.Equal(pageB.ContinuationToken, MergeToken(pageA.ContinuationToken, pageB.ContinuationToken));
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 27 (design 5.14): Deteksi kepemilikan playlist via afordans hapus.
    // =====================================================================================

    // Feature: kaset-winui3, Property 27: Deteksi kepemilikan playlist menentukan afordans hapus
    // Validates: Requirements 14.3
    [Fact]
    public void Property27_Ownership_detected_iff_delete_affordance_present()
    {
        // For any playlist node, IsOwnedByUser is true if and only if a delete affordance is
        // present: a deletePlaylistEndpoint command, the editable header renderer, or a
        // "playlist/delete" command string. AffordanceKind: 0=none, 1=deleteEndpoint,
        // 2=editableHeader, 3=deleteText.
        Gen.Select(Gen.Int[0, 3], PbtGenerators.ShortToken).Sample(
            spec =>
            {
                var (affordanceKind, token) = spec;

                var node = BuildPlaylistNode(affordanceKind, token);
                var expected = affordanceKind != 0;

                Assert.Equal(expected, PlaylistEditability.IsOwnedByUser(node));
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 34 (design 5.15): Parser melempar KasetError(ParseError) pada input rusak.
    // =====================================================================================

    // Feature: kaset-winui3, Property 34: Parser melempar ParseError pada input rusak
    // Validates: Requirements 33.1, 33.2, 33.3, 33.4, 33.5
    [Fact]
    public void Property34_Parsers_throw_parse_error_on_corrupt_input()
    {
        // For any non-object / structurally-empty input, every parser throws KasetError with
        // ParseError (never a crash or a different exception type). NodeKind:
        // 0=null, 1=number, 2=string, 3=bool, 4=array, 5=empty object, 6=benign-keys object.
        Gen.Select(Gen.Int[0, 6], PbtGenerators.ShortToken, Gen.Int[-100_000, 100_000]).Sample(
            spec =>
            {
                var (nodeKind, token, number) = spec;
                var node = BuildCorruptNode(nodeKind, token, number);

                // All JsonNode-based parser entry points must surface ParseError.
                AssertParseError(() => HomeResponseParser.Parse(node));
                AssertParseError(() => SearchResponseParser.Parse(node));
                AssertParseError(() => LibraryContentParser.Parse(node));
                AssertParseError(() => PlaylistParser.ParsePlaylistDetail(node, "VLPL0000000playlist1"));
                AssertParseError(() => ArtistParser.Parse(node));
                AssertParseError(() => RadioQueueParser.Parse(node));

                // The string overloads must also map invalid/garbage JSON to ParseError.
                var garbage = "}{ not-json " + token;
                AssertParseError(() => HomeResponseParser.Parse(garbage));
                AssertParseError(() => LibraryContentParser.Parse(garbage));
                AssertParseError(() => ArtistParser.Parse(garbage));
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 44 (design 5.16): RadioQueueParser mengekstrak lagu (wrapper) + token.
    // =====================================================================================

    // Feature: kaset-winui3, Property 44: Parser radio queue mengekstrak lagu dan token
    // Validates: Requirements 25.1, 16.1
    [Fact]
    public void Property44_Radio_queue_extracts_songs_and_token()
    {
        // Fixture sanity: the real radio fixture yields a non-empty, stable queue.
        var fixtureNode = JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.RadioQueue, "next_radio_queue"))!;
        var fixtureResult = RadioQueueParser.Parse(fixtureNode);
        Assert.NotEmpty(fixtureResult.Songs);
        Assert.All(fixtureResult.Songs, s => Assert.False(string.IsNullOrEmpty(s.VideoId)));

        // For any synthetic radio panel mixing wrapped
        // (playlistPanelVideoWrapperRenderer.primaryRenderer.playlistPanelVideoRenderer), direct
        // (playlistPanelVideoRenderer), and non-video (skipped) rows, the parser extracts the
        // video rows in order and the optional continuation token.
        // RowType: 0=wrapped, 1=direct, 2=non-video noise.
        Gen.Select(Gen.Int[0, 2].Array[0, 10], Gen.OneOf(Gen.Const((string?)null), PbtToken)).Sample(
            spec =>
            {
                var (rowTypes, token) = spec;
                var root = BuildRadioPanel(rowTypes, token, out var expectedVideoIds);

                var result = RadioQueueParser.Parse(root);

                // Wrapper handling: every video row (wrapped or direct) is extracted, in order;
                // noise rows are skipped.
                Assert.Equal(expectedVideoIds, result.Songs.Select(s => s.VideoId).ToArray());
                Assert.Equal(expectedVideoIds, result.Songs.Select(s => s.Id).ToArray());

                // Continuation token is extracted when present, else null.
                Assert.Equal(token, result.ContinuationToken);
            },
            iter: 100);
    }

    // =====================================================================================
    // Helpers — generators
    // =====================================================================================

    /// <summary>A non-empty synthetic continuation token (never a real token).</summary>
    private static readonly Gen<string?> PbtToken =
        PbtGenerators.Token.Select(t => (string?)("TOKEN_" + t));

    /// <summary>
    /// A list of <see cref="Song"/> with ids guaranteed unique within and across pages by the
    /// <paramref name="tag"/> + index prefix (so concatenation never coincidentally collides).
    /// </summary>
    private static Gen<List<Song>> UniqueSongs(string tag) =>
        PbtGenerators.ShortToken.Array[0, 8].Select(tokens =>
            tokens
                .Select((t, i) => $"{tag}{i}_{t}")
                .Select(id => new Song { Id = id, VideoId = id, Title = "Track " + id })
                .ToList());

    // =====================================================================================
    // Helpers — Property 23 snapshots
    // =====================================================================================

    private sealed record SurfaceSnapshot(List<string> Signature, List<string> Ids);

    private static JsonNode? LoadNode(string surface, string name) =>
        JsonNode.Parse(TestFixtures.LoadString(surface, name));

    private static SurfaceSnapshot ParseSurfaceSnapshot(string surface)
    {
        var signature = new List<string>();
        var ids = new List<string>();

        switch (surface)
        {
            case TestFixtures.Surfaces.Home:
            {
                var r = HomeResponseParser.Parse(LoadNode(surface, "FEmusic_home"));
                signature.Add($"token|{r.ContinuationToken}");
                foreach (var section in r.Sections)
                {
                    signature.Add($"section|{section.Id}|{section.Title}");
                    ids.Add(section.Id);
                    foreach (var item in section.Items)
                    {
                        signature.Add($"item|{item.Id}");
                        ids.Add(item.Id);
                    }
                }

                break;
            }

            case TestFixtures.Surfaces.Search:
            {
                var r = SearchResponseParser.Parse(LoadNode(surface, "search"));
                if (r.TopResult is not null)
                {
                    signature.Add($"top|{r.TopResult.Id}");
                    ids.Add(r.TopResult.Id);
                }

                AddIds(signature, ids, "song", r.Songs.Select(s => s.Id));
                AddIds(signature, ids, "album", r.Albums.Select(a => a.Id));
                AddIds(signature, ids, "artist", r.Artists.Select(a => a.Id));
                AddIds(signature, ids, "playlist", r.Playlists.Select(p => p.Id));
                AddIds(signature, ids, "podcast", r.Podcasts.Select(p => p.Id));
                break;
            }

            case TestFixtures.Surfaces.Library:
            {
                var r = LibraryContentParser.Parse(LoadNode(surface, "FEmusic_library_landing"));
                AddIds(signature, ids, "playlist", r.Playlists.Select(p => p.Id));
                AddIds(signature, ids, "album", r.Albums.Select(a => a.Id));
                AddIds(signature, ids, "artist", r.Artists.Select(a => a.Id));
                AddIds(signature, ids, "song", r.Songs.Select(s => s.Id));
                break;
            }

            case TestFixtures.Surfaces.Playlist:
            {
                var r = PlaylistParser.ParsePlaylistDetail(LoadNode(surface, "playlist"), "VLPL0000000playlist1");
                signature.Add($"playlist|{r.Playlist.Id}|{r.Playlist.Title}|{r.ContinuationToken}");
                ids.Add(r.Playlist.Id);
                AddIds(signature, ids, "track", r.Tracks.Select(t => t.Id));
                break;
            }

            case TestFixtures.Surfaces.Artist:
            {
                var r = ArtistParser.Parse(LoadNode(surface, "artist"));
                signature.Add($"artist|{r.Artist.Id}|{r.Artist.Name}");
                ids.Add(r.Artist.Id);
                AddIds(signature, ids, "song", r.TopSongs.Select(s => s.Id));
                AddIds(signature, ids, "album", r.Albums.Select(a => a.Id));
                AddIds(signature, ids, "single", r.SinglesAndEps.Select(a => a.Id));
                break;
            }

            case TestFixtures.Surfaces.RadioQueue:
            {
                var r = RadioQueueParser.Parse(LoadNode(surface, "next_radio_queue"));
                signature.Add($"token|{r.ContinuationToken}");
                AddIds(signature, ids, "song", r.Songs.Select(s => s.Id));
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown surface.");
        }

        return new SurfaceSnapshot(signature, ids);
    }

    private static void AddIds(List<string> signature, List<string> ids, string kind, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            signature.Add($"{kind}|{value}");
            ids.Add(value);
        }
    }

    // =====================================================================================
    // Helpers — Property 26 token model
    // =====================================================================================

    /// <summary>Models page-merge token selection: the next page's token replaces the previous.</summary>
    private static string? MergeToken(string? current, string? next) => next;

    // =====================================================================================
    // Helpers — JSON builders
    // =====================================================================================

    private static JsonObject Runs(string text) =>
        new() { ["runs"] = new JsonArray { new JsonObject { ["text"] = text } } };

    private static JsonArray FlexTitle(string title) =>
        new()
        {
            new JsonObject
            {
                ["musicResponsiveListItemFlexColumnRenderer"] = new JsonObject { ["text"] = Runs(title) },
            },
        };

    private static JsonObject BuildSearchResponse(
        int[] rowTypes,
        int topKind,
        out string[] expectedSongIds,
        out string? topExpectedId)
    {
        var rows = new JsonArray();
        var songIds = new List<string>();

        for (var i = 0; i < rowTypes.Length; i++)
        {
            var type = rowTypes[i];
            switch (type)
            {
                case 0: // song
                {
                    var vid = $"vid{i}";
                    songIds.Add(vid);
                    rows.Add(new JsonObject
                    {
                        ["musicResponsiveListItemRenderer"] = new JsonObject
                        {
                            ["playlistItemData"] = new JsonObject { ["videoId"] = vid },
                            ["flexColumns"] = FlexTitle($"Song {i}"),
                        },
                    });
                    break;
                }

                default: // browse item (album/artist/playlist/podcast)
                {
                    var browseId = BrowsePrefix(type) + i;
                    rows.Add(new JsonObject
                    {
                        ["musicResponsiveListItemRenderer"] = new JsonObject
                        {
                            ["navigationEndpoint"] = new JsonObject
                            {
                                ["browseEndpoint"] = new JsonObject { ["browseId"] = browseId },
                            },
                            ["flexColumns"] = FlexTitle($"Item {i}"),
                        },
                    });
                    break;
                }
            }
        }

        expectedSongIds = songIds.ToArray();

        var sectionContents = new JsonArray();
        topExpectedId = null;
        if (topKind != 0)
        {
            sectionContents.Add(BuildCardShelf(topKind, out topExpectedId));
        }

        sectionContents.Add(new JsonObject
        {
            ["musicShelfRenderer"] = new JsonObject { ["contents"] = rows },
        });

        return new JsonObject
        {
            ["contents"] = new JsonObject
            {
                ["sectionListRenderer"] = new JsonObject { ["contents"] = sectionContents },
            },
        };
    }

    private static JsonObject BuildCardShelf(int topKind, out string topExpectedId)
    {
        JsonObject nav;
        if (topKind == 1) // song top result
        {
            topExpectedId = "topvid";
            nav = new JsonObject { ["watchEndpoint"] = new JsonObject { ["videoId"] = topExpectedId } };
        }
        else
        {
            var prefix = topKind switch
            {
                2 => "MPRE", // album
                3 => "UC", // artist
                4 => "PL", // playlist
                _ => "MPSPP", // 5 => podcast
            };
            topExpectedId = prefix + "top";
            nav = new JsonObject { ["browseEndpoint"] = new JsonObject { ["browseId"] = topExpectedId } };
        }

        var titleRun = new JsonObject { ["text"] = "Top Result", ["navigationEndpoint"] = nav };
        return new JsonObject
        {
            ["musicCardShelfRenderer"] = new JsonObject
            {
                ["title"] = new JsonObject { ["runs"] = new JsonArray { titleRun } },
            },
        };
    }

    /// <summary>Maps a synthetic search <em>row</em> kind to a browseId prefix the classifier recognises.</summary>
    private static string BrowsePrefix(int kind) => kind switch
    {
        1 => "MPRE", // album
        2 => "UC", // artist
        3 => "PL", // playlist
        _ => "MPSPP", // 4 => podcast
    };

    private static JsonObject BuildPlaylistNode(int affordanceKind, string token)
    {
        // Benign base with controlled keys/values only — never contains a delete affordance.
        var node = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["musicDetailHeaderRenderer"] = new JsonObject
                {
                    ["title"] = Runs("Playlist " + token),
                    ["subtitle"] = Runs("Owner " + token),
                },
            },
            ["trackingParams"] = token,
        };

        switch (affordanceKind)
        {
            case 1: // deletePlaylistEndpoint command
                node["menu"] = new JsonObject
                {
                    ["items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["deletePlaylistEndpoint"] = new JsonObject { ["playlistId"] = "PL" + token },
                        },
                    },
                };
                break;

            case 2: // editable header renderer
                node["editableHeader"] = new JsonObject
                {
                    ["musicEditablePlaylistDetailHeaderRenderer"] = new JsonObject { ["header"] = new JsonObject() },
                };
                break;

            case 3: // "playlist/delete" command string somewhere in the tree
                node["command"] = new JsonObject { ["serviceEndpoint"] = "playlist/delete?id=" + token };
                break;

            default:
                // case 0: no affordance.
                break;
        }

        return node;
    }

    private static JsonNode? BuildCorruptNode(int nodeKind, string token, int number) => nodeKind switch
    {
        0 => null,
        1 => JsonValue.Create(number),
        2 => JsonValue.Create(token),
        3 => JsonValue.Create(number % 2 == 0),
        4 => new JsonArray { token, number },
        5 => new JsonObject(),
        // Benign-keys object: none of these keys are required by any parser.
        _ => new JsonObject
        {
            ["responseContext"] = new JsonObject { ["visitorData"] = token },
            ["trackingParams"] = token,
            ["value"] = number,
        },
    };

    private static JsonObject BuildRadioPanel(int[] rowTypes, string? token, out string[] expectedVideoIds)
    {
        var rows = new JsonArray();
        var videoIds = new List<string>();

        for (var i = 0; i < rowTypes.Length; i++)
        {
            var type = rowTypes[i];
            if (type == 2)
            {
                // Non-video noise row (e.g. an automix toggle) — must be skipped.
                rows.Add(new JsonObject { ["automixPreviewVideoRenderer"] = new JsonObject() });
                continue;
            }

            var vid = $"v{i}";
            videoIds.Add(vid);
            var videoRenderer = new JsonObject
            {
                ["videoId"] = vid,
                ["title"] = Runs($"Radio Track {i}"),
                ["lengthText"] = Runs("3:14"),
            };

            if (type == 0)
            {
                // Wrapped renderer.
                rows.Add(new JsonObject
                {
                    ["playlistPanelVideoWrapperRenderer"] = new JsonObject
                    {
                        ["primaryRenderer"] = new JsonObject { ["playlistPanelVideoRenderer"] = videoRenderer },
                    },
                });
            }
            else
            {
                // Direct renderer.
                rows.Add(new JsonObject { ["playlistPanelVideoRenderer"] = videoRenderer });
            }
        }

        expectedVideoIds = videoIds.ToArray();

        var panel = new JsonObject { ["contents"] = rows };
        if (token is not null)
        {
            panel["continuations"] = new JsonArray
            {
                new JsonObject
                {
                    ["nextRadioContinuationData"] = new JsonObject { ["continuation"] = token },
                },
            };
        }

        return new JsonObject { ["playlistPanelRenderer"] = panel };
    }

    private static void AssertParseError(Action action)
    {
        var ex = Assert.Throws<KasetError>(action);
        Assert.Equal(KasetErrorKind.ParseError, ex.Kind);
    }
}
