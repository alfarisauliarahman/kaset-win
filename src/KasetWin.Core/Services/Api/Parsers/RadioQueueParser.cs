using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free parser for YouTube Music <c>next</c> radio / mix responses and their
/// infinite-mix continuation pages (Req 25.1). Mirrors the macOS <c>RadioQueueParser</c>:
/// the queue lives under a <c>playlistPanelRenderer</c> (initial <c>next</c> response) or a
/// <c>playlistPanelContinuation</c> (continuation response), and each row is either a
/// <c>playlistPanelVideoWrapperRenderer.primaryRenderer.playlistPanelVideoRenderer</c> wrapper
/// or a direct <c>playlistPanelVideoRenderer</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c>, side-effect free and deterministic, so the parser satisfies
/// the idempotency / stable-identity guarantee (Property 23) and the radio-queue extraction
/// guarantee (Property 44). Container reshuffling is tolerated by locating the panel and its
/// rows recursively via <see cref="ResponseTreeSearch"/>. Song identity is the
/// <c>videoId</c> (Req 16.1).
/// </para>
/// <para>This type lives in <c>KasetWin.Core</c> and has no WinUI/WinRT dependency.</para>
/// </remarks>
public static class RadioQueueParser
{
    private static readonly string[] ArtistSeparators =
    {
        " • ", " & ", ", ", "•", "&", ",",
    };

    // MARK: - Public API

    /// <summary>
    /// Parses an initial <c>next</c> radio / mix response into a <see cref="RadioQueueResult"/>:
    /// the ordered songs plus the optional continuation token used to drive the infinite mix.
    /// </summary>
    /// <param name="root">The decoded <c>next</c> response tree.</param>
    /// <returns>
    /// The parsed queue. A well-formed response with no rows yields an empty
    /// <see cref="RadioQueueResult.Songs"/> list.
    /// </returns>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or carries neither a
    /// <c>playlistPanelRenderer</c> nor a <c>playlistPanelContinuation</c> (corrupted input,
    /// Req 20.3).
    /// </exception>
    public static RadioQueueResult Parse(JsonNode? root) => ParsePanel(root, "radio queue");

    /// <summary>
    /// Parses an infinite-mix continuation response into a <see cref="RadioQueueResult"/>.
    /// The continuation payload lives under <c>continuationContents.playlistPanelContinuation</c>;
    /// the initial <c>playlistPanelRenderer</c> shape is also accepted defensively.
    /// </summary>
    /// <param name="root">The decoded continuation response tree.</param>
    /// <exception cref="KasetError">
    /// Thrown with <see cref="KasetErrorKind.ParseError"/> when <paramref name="root"/> is
    /// <see langword="null"/>, not a JSON object, or carries neither a
    /// <c>playlistPanelContinuation</c> nor a <c>playlistPanelRenderer</c> (Req 20.3).
    /// </exception>
    public static RadioQueueResult ParseContinuation(JsonNode? root) =>
        ParsePanel(root, "radio queue continuation");

    // MARK: - Core

    private static RadioQueueResult ParsePanel(JsonNode? root, string surface)
    {
        if (root is not JsonObject obj)
        {
            throw new KasetError(KasetErrorKind.ParseError, $"{Capitalize(surface)} response is not a JSON object.");
        }

        // Either the initial panel renderer or the continuation panel carries the queue; both
        // expose the same contents[] + continuations[] shape, so accept whichever is present.
        var panel = ResponseTreeSearch.FindFirst(obj, "playlistPanelContinuation")
                    ?? ResponseTreeSearch.FindFirst(obj, "playlistPanelRenderer");

        if (panel is null)
        {
            throw new KasetError(
                KasetErrorKind.ParseError,
                $"{Capitalize(surface)} response is missing 'playlistPanelRenderer' / 'playlistPanelContinuation'.");
        }

        var songs = ParseSongs(AsArray(Prop(panel, "contents")));
        return new RadioQueueResult
        {
            Songs = songs,
            ContinuationToken = ExtractContinuationToken(panel),
        };
    }

    // MARK: - Song rows

    private static List<Song> ParseSongs(JsonArray? contents)
    {
        if (contents is null)
        {
            return new List<Song>();
        }

        var songs = new List<Song>(contents.Count);
        foreach (var item in contents)
        {
            var renderer = ResolveVideoRenderer(item);
            if (renderer is null)
            {
                continue;
            }

            var song = ParseSong(renderer);
            if (song is not null)
            {
                songs.Add(song);
            }
        }

        return songs;
    }

    /// <summary>
    /// Resolves the <c>playlistPanelVideoRenderer</c> from a <c>contents[]</c> item, handling
    /// both the <c>playlistPanelVideoWrapperRenderer.primaryRenderer</c> wrapper and the direct
    /// renderer. Returns <c>null</c> for non-video rows (e.g. automix toggles).
    /// </summary>
    private static JsonNode? ResolveVideoRenderer(JsonNode? item)
    {
        var wrapped = Prop(
            Prop(Prop(item, "playlistPanelVideoWrapperRenderer"), "primaryRenderer"),
            "playlistPanelVideoRenderer");
        return wrapped ?? Prop(item, "playlistPanelVideoRenderer");
    }

    private static Song? ParseSong(JsonNode renderer)
    {
        var videoId = GetString(renderer, "videoId");
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }

        var (artists, album) = ExtractByline(renderer);
        return new Song
        {
            Id = videoId,
            VideoId = videoId,
            Title = ParsingHelpers.ExtractText(renderer, "title") ?? "Unknown",
            Artists = artists,
            Album = album,
            Duration = ParsingHelpers.ParseDuration(ParsingHelpers.ExtractText(renderer, "lengthText")),
            ThumbnailUrl = ParsingHelpers.BestThumbnailUrl(renderer),
        };
    }

    // MARK: - Artists (byline)

    /// <summary>
    /// Extracts the artists and the album from <c>longBylineText.runs</c>, falling back to
    /// <c>shortBylineText.runs</c>.
    /// </summary>
    /// <remarks>
    /// The byline is bullet-segmented: <c>Artist(s) • Album • Year</c>. Only the FIRST segment is
    /// artists — treating every non-separator run as an artist put the album and year into the
    /// artist list ("Xdinary Heroes, Livelock, 2023" in the player bar, and a dead artist link
    /// because the first "artist" id could be the album's <c>MPRE…</c>). The album run is
    /// recognised by its <c>MPRE…</c> browse id wherever it appears; the year/views text is
    /// dropped. Linked artist runs keep their browse id; plain-text runs get a deterministic
    /// <see cref="ParsingHelpers.StableId"/> so the artist line is never blank.
    /// </remarks>
    private static (IReadOnlyList<Artist> Artists, Album? Album) ExtractByline(JsonNode renderer)
    {
        foreach (var key in new[] { "longBylineText", "shortBylineText" })
        {
            var runs = AsArray(Prop(Prop(renderer, key), "runs"));
            if (runs is null)
            {
                continue;
            }

            var artists = new List<Artist>();
            Album? album = null;
            var segment = 0;
            foreach (var run in runs)
            {
                var text = GetString(run, "text");
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (text.Contains('•', StringComparison.Ordinal))
                {
                    segment++;
                    continue;
                }

                if (IsArtistSeparator(text))
                {
                    continue;
                }

                var browseId = ParsingHelpers.ExtractBrowseId(run);
                if (browseId is not null && browseId.StartsWith("MPRE", StringComparison.Ordinal))
                {
                    album ??= new Album { Id = browseId, Title = text };
                    continue;
                }

                if (segment == 0)
                {
                    artists.Add(browseId is not null
                        ? new Artist { Id = browseId, Name = text }
                        : new Artist { Id = ParsingHelpers.StableId("artist", text), Name = text });
                }
            }

            if (artists.Count > 0)
            {
                return (artists, album);
            }
        }

        return (Array.Empty<Artist>(), null);
    }

    // MARK: - Continuation token

    /// <summary>
    /// Extracts the infinite-mix continuation token from
    /// <c>continuations[].nextRadioContinuationData.continuation</c>. Returns <c>null</c> when no
    /// continuation is present (finite queue).
    /// </summary>
    private static string? ExtractContinuationToken(JsonNode panel)
    {
        var continuations = AsArray(Prop(panel, "continuations"));
        if (continuations is null)
        {
            return null;
        }

        foreach (var continuation in continuations)
        {
            var token = GetString(Prop(continuation, "nextRadioContinuationData"), "continuation");
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }
        }

        return null;
    }

    // MARK: - Helpers

    private static bool IsArtistSeparator(string text) => Array.IndexOf(ArtistSeparators, text) >= 0;

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static JsonNode? Prop(JsonNode? node, string? key)
    {
        if (key is not null && node is JsonObject obj && obj.TryGetPropertyValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static JsonArray? AsArray(JsonNode? node) => node as JsonArray;

    private static string? GetString(JsonNode? node, string key)
    {
        var value = Prop(node, key);
        return value is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
    }
}
