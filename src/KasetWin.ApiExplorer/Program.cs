// KasetWin.ApiExplorer — CLI for exploring YouTube Music (InnerTube) endpoints on Windows
// (Req 24). Reuses the production YTMusicClient + parsers from KasetWin.Core so exploration
// verifies real code paths.
//
// Commands:
//   auth   [--cookies <path>] [--authuser <n>] [--brand <id>]   Report auth status.
//   list                                                        List known browse endpoints.
//   browse <id> [-v] [--cookies <path>] [--authuser <n>] [--brand <id>]
//                                                               Browse <id> and summarize.
//
// SECURITY: cookies / SAPISID are secrets. They are read only from --cookies <path> or the
// KASET_COOKIE environment variable at runtime, are never printed, never logged, never
// committed. All free-text output passes through Redactor as defense in depth.

using System.Text.Json;
using System.Text.Json.Nodes;
using KasetWin.ApiExplorer;
using KasetWin.Core.Diagnostics;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Api.Parsers;

return await ApiExplorerCli.RunAsync(args).ConfigureAwait(false);

/// <summary>Entry-point logic for the API Explorer CLI, factored out for clarity/testability.</summary>
internal static class ApiExplorerCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var options = CliOptions.Parse(args.AsSpan(1));

        try
        {
            return command switch
            {
                "auth" => await RunAuthAsync(options).ConfigureAwait(false),
                "list" => RunList(),
                "browse" => await RunBrowseAsync(options).ConfigureAwait(false),
                "lyrics" => await RunLyricsAsync(options).ConfigureAwait(false),
                "search" => await RunSearchAsync(options).ConfigureAwait(false),
                "-h" or "--help" or "help" => PrintUsageAndOk(),
                _ => UnknownCommand(command),
            };
        }
        catch (KasetError ex)
        {
            // KasetError messages are authored to be secret-free; redact as defense in depth.
            Console.Error.WriteLine($"Error [{ex.Kind}]: {Redactor.Redact(ex.Message)}");
            return 2;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Error: cookie file not found: {Redactor.Redact(ex.FileName ?? ex.Message)}");
            return 2;
        }
    }

    // ── auth ────────────────────────────────────────────────────────────────────────────

    private static async Task<int> RunAuthAsync(CliOptions options)
    {
        var cookieSource = CliCookieSource.FromRuntime(options.CookiePath, options.AuthUser, options.Brand);

        // Resolve the snapshot exactly as the client would (origin = music.youtube.com).
        _ = await cookieSource.GetCookiesAsync(InnerTubeSupport.MusicOrigin).ConfigureAwait(false);

        Console.WriteLine("Authentication status");
        Console.WriteLine($"  Origin        : {InnerTubeSupport.MusicOrigin}");
        Console.WriteLine($"  Cookie source : {cookieSource.SourceDescription}");
        Console.WriteLine($"  Cookies parsed: {cookieSource.CookieCount}");
        Console.WriteLine($"  SAPISID       : {(cookieSource.CanResolveSapisid ? "resolved" : "not resolved")}");
        Console.WriteLine($"  Authenticated : {(cookieSource.CanResolveSapisid ? "yes" : "no")}");

        if (options.Brand is not null)
        {
            Console.WriteLine($"  Brand account : {options.Brand}");
        }

        if (!cookieSource.CanResolveSapisid)
        {
            Console.WriteLine();
            Console.WriteLine("  No SAPISID resolved — only public endpoints will work.");
            Console.WriteLine("  Supply cookies via the KASET_COOKIE env var or --cookies <path>");
            Console.WriteLine("  (a file containing the raw Cookie header). Values are never printed.");
        }

        return 0;
    }

    // ── list ────────────────────────────────────────────────────────────────────────────

    private static int RunList()
    {
        Console.WriteLine("Known browse endpoints (pass to: browse <id>)");
        Console.WriteLine();
        Console.WriteLine($"  {"BROWSE ID",-42} {"AUTH",-5} NAME");
        Console.WriteLine($"  {new string('-', 42),-42} {new string('-', 5),-5} ----");

        foreach (var ep in KnownEndpoints.All)
        {
            var auth = ep.RequiresAuth ? "auth" : "pub";
            Console.WriteLine($"  {ep.BrowseId,-42} {auth,-5} {ep.Name} — {ep.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("  Detail ids: VL<playlistId> (playlist), UC<channelId> (artist),");
        Console.WriteLine("              OLAK../MPRE.. (album), RD.. (radio/mix).");
        return 0;
    }

    // ── browse ──────────────────────────────────────────────────────────────────────────

    private static async Task<int> RunBrowseAsync(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Positional))
        {
            Console.Error.WriteLine("Error: 'browse' requires a browse id. Example: browse FEmusic_home -v");
            return 1;
        }

        var browseId = options.Positional;
        var cookieSource = CliCookieSource.FromRuntime(options.CookiePath, options.AuthUser, options.Brand);

        using var http = YTMusicClient.CreateConfiguredHttpClient();
        var client = new YTMusicClient(
            http,
            cookieSource,
            new ApiCache(),
            new ExponentialBackoffRetryPolicy());

        Console.WriteLine($"browse {browseId}");
        Console.WriteLine($"  auth: {(cookieSource.CanResolveSapisid ? "yes" : "no (public)")}");
        Console.WriteLine();

        var node = await client.BrowseRawAsync(browseId).ConfigureAwait(false);

        PrintTypedSummary(browseId, node);
        PrintTopLevelKeys(node);

        var histogram = BuildRendererHistogram(node);
        Console.WriteLine($"  Renderers     : {histogram.Sum(p => p.Value)} total, {histogram.Count} distinct");

        if (options.Verbose)
        {
            PrintRendererHistogram(histogram);
            PrintCompactJson(node);
        }
        else
        {
            Console.WriteLine("  (pass -v for the renderer histogram and a redacted JSON preview)");
        }

        return 0;
    }

    // ── search ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches YouTube Music and prints the song hits as <c>videoId — title — artist</c>.
    /// <para>
    /// Exists to feed <c>lyrics &lt;videoId&gt;</c>: a music-video id (the sort a human copies out of
    /// a youtube.com URL) usually has <i>no</i> lyrics tab at all, so verifying the lyrics surface
    /// needs real YouTube Music <i>song</i> ids — which only search can hand you.
    /// </para>
    /// </summary>
    private static async Task<int> RunSearchAsync(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Positional))
        {
            Console.Error.WriteLine("Error: 'search' requires a query. Example: search \"billie eilish birds\"");
            return 1;
        }

        var cookieSource = CliCookieSource.FromRuntime(options.CookiePath, options.AuthUser, options.Brand);

        using var http = YTMusicClient.CreateConfiguredHttpClient();
        var client = new YTMusicClient(http, cookieSource, new ApiCache(), new ExponentialBackoffRetryPolicy());

        Console.WriteLine($"search {Redactor.Redact(options.Positional)}");
        Console.WriteLine($"  auth: {(cookieSource.CanResolveSapisid ? "yes" : "no (public)")}");
        Console.WriteLine();

        var response = await client.SearchAsync(options.Positional).ConfigureAwait(false);

        if (response.Songs.Count == 0)
        {
            Console.WriteLine("  no song results");
            return 0;
        }

        foreach (var song in response.Songs)
        {
            var artists = string.Join(", ", song.Artists.Select(a => a.Name));
            Console.WriteLine($"  {song.VideoId}  {Truncate(Redactor.Redact(song.Title), 44),-44}  {Truncate(Redactor.Redact(artists), 30)}");
        }

        return 0;
    }

    // ── lyrics ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// InnerTube clients worth trying for lyrics. WEB_REMIX is what the app already speaks and
    /// serves plain text; the Android Music client is the one reported to serve time-synced lyrics.
    /// Versions are pinned per client because InnerTube rejects a name/version mismatch outright.
    /// </summary>
    private static readonly (string Label, string? Name, string? Version, Dictionary<string, string>? Extras)[] LyricsClients =
    [
        ("WEB_REMIX (what Kaset uses today)", null, null, null),
        ("ANDROID_MUSIC 6.33.52 (bare)", "ANDROID_MUSIC", "6.33.52", null),
        ("ANDROID_MUSIC 6.33.52 + android context", "ANDROID_MUSIC", "6.33.52", AndroidContext),
        ("ANDROID_MUSIC 7.21.50 + android context", "ANDROID_MUSIC", "7.21.50", AndroidContext),
        ("IOS_MUSIC 6.33.3 + ios context", "IOS_MUSIC", "6.33.3", IosContext),
    ];

    /// <summary>
    /// Context fields a real Android Music client sends. Without them YouTube answers a mobile
    /// clientName with the web-shaped response, so they are part of the identity, not decoration.
    /// </summary>
    private static readonly Dictionary<string, string> AndroidContext = new()
    {
        ["androidSdkVersion"] = "30",
        ["osName"] = "Android",
        ["osVersion"] = "11",
        ["platform"] = "MOBILE",
    };

    private static readonly Dictionary<string, string> IosContext = new()
    {
        ["osName"] = "iOS",
        ["osVersion"] = "16.6.0.20G75",
        ["deviceModel"] = "iPhone14,3",
        ["platform"] = "MOBILE",
    };

    /// <summary>
    /// Explores the lyrics surface for a videoId: resolves the Lyrics tab's browse id from
    /// <c>next</c>, then browses it once per candidate client and reports which shapes come back.
    /// The question it exists to answer is whether any client returns per-line timings — so it
    /// looks explicitly for timing-shaped keys rather than only dumping the tree.
    /// </summary>
    private static async Task<int> RunLyricsAsync(CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Positional))
        {
            Console.Error.WriteLine("Error: 'lyrics' requires a videoId. Example: lyrics BiQIc7fG9pA -v");
            return 1;
        }

        var videoId = options.Positional;
        var cookieSource = CliCookieSource.FromRuntime(options.CookiePath, options.AuthUser, options.Brand);

        using var http = YTMusicClient.CreateConfiguredHttpClient();
        var client = new YTMusicClient(http, cookieSource, new ApiCache(), new ExponentialBackoffRetryPolicy());

        Console.WriteLine($"lyrics {Redactor.Redact(videoId)}");
        Console.WriteLine($"  auth: {(cookieSource.CanResolveSapisid ? "yes" : "no (public)")}");
        Console.WriteLine();

        // The whole flow is repeated per client, deliberately: the lyrics browse id itself is issued
        // by `next`, and asking a different client can yield a different id. Browsing a WEB_REMIX id
        // as an Android client only proves that one id is plain — not that the surface is.
        foreach (var (label, name, version, extras) in LyricsClients)
        {
            Console.WriteLine($"  ── {label} ──");
            try
            {
                var next = name is null || version is null
                    ? await client.NextRawAsync(videoId).ConfigureAwait(false)
                    : await client.NextRawAsync(videoId, name, version, extras).ConfigureAwait(false);

                var lyricsBrowseId = FindLyricsBrowseId(next, options.Verbose);
                if (lyricsBrowseId is null)
                {
                    Console.WriteLine("    no selectable lyrics tab (MPLY...) for this client");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"    browseId: {lyricsBrowseId}");

                var node = name is null || version is null
                    ? await client.BrowseRawAsync(lyricsBrowseId).ConfigureAwait(false)
                    : await client.BrowseRawAsync(lyricsBrowseId, name, version, extras).ConfigureAwait(false);

                ReportLyricsShape(node, options.Verbose);
            }
            catch (KasetError ex)
            {
                Console.WriteLine($"    failed [{ex.Kind}]: {Truncate(Redactor.Redact(ex.Message), 90)}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>Finds the selectable Lyrics tab's browse id in a <c>next</c> response.</summary>
    private static string? FindLyricsBrowseId(JsonNode next, bool verbose)
    {
        string? lyricsBrowseId = null;
        foreach (var tab in CollectRenderers(next, "tabRenderer"))
        {
            var title = tab?["title"]?.ToString() ?? tab?["endpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
            var id = tab?["endpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
            var unselectable = tab?["unselectable"]?.GetValue<bool>() == true;

            if (verbose)
            {
                Console.WriteLine($"      tab {Truncate(title ?? "(untitled)", 24),-26} id={id ?? "-"}{(unselectable ? "  [unselectable]" : string.Empty)}");
            }

            if (!unselectable && id is not null && id.StartsWith("MPLY", StringComparison.Ordinal))
            {
                lyricsBrowseId ??= id;
            }
        }

        return lyricsBrowseId;
    }

    /// <summary>
    /// Summarizes one lyrics response: which renderers it used, whether any timing-shaped data is
    /// present, and a sample of the text. Timing keys are searched for by name because that is the
    /// whole question — plain text is easy to recognize, timings are what we are hunting.
    /// </summary>
    private static void ReportLyricsShape(JsonNode node, bool verbose)
    {
        var histogram = BuildRendererHistogram(node);
        Console.WriteLine($"    renderers: {string.Join(", ", histogram.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key}×{p.Value}"))}");

        string[] timingKeys =
        [
            "timedLyricsData", "cueRange", "startTimeMs", "endTimeMs", "lyricLine",
            "timedLyrics", "syncedLyrics", "lyricsData", "timedLyricsModel",
            "elementRenderer", "musicTimedLyrics", "lyricsModel", "cueGroup",
        ];

        var found = timingKeys.Where(k => ContainsKey(node, k)).ToList();
        Console.WriteLine(found.Count > 0
            ? $"    TIMING KEYS FOUND: {string.Join(", ", found)}"
            : "    timing keys: none");

        // The decisive detail: the timed model can come back with lines but NO cueRange, which means
        // the track simply has no synced version. Distinguish "surface unsupported" from
        // "this song is unsynced", because they call for completely different conclusions.
        foreach (var data in CollectRenderers(node, "timedLyricsData"))
        {
            if (data is not JsonArray lines)
            {
                continue;
            }

            var withCues = lines.Count(l => l?["cueRange"] is not null);
            Console.WriteLine($"    timed lines: {lines.Count}, with cueRange: {withCues}");
            if (lines.FirstOrDefault(l => l?["cueRange"] is not null) is { } sample)
            {
                Console.WriteLine($"    sample     : {Truncate(Redactor.Redact(sample.ToJsonString()), 200)}");
            }
        }

        foreach (var shelf in CollectRenderers(node, "musicDescriptionShelfRenderer"))
        {
            var text = shelf?["description"]?["runs"]?[0]?["text"]?.ToString()
                ?? shelf?["description"]?["simpleText"]?.ToString();
            var footer = shelf?["footer"]?["runs"]?[0]?["text"]?.ToString()
                ?? shelf?["footer"]?["simpleText"]?.ToString();
            Console.WriteLine($"    text  : {Truncate(Redactor.Redact(text ?? "(none)").ReplaceLineEndings(" / "), 90)}");
            Console.WriteLine($"    footer: {Truncate(Redactor.Redact(footer ?? "(none)"), 60)}");
        }

        if (verbose)
        {
            PrintCompactJson(node);
        }
    }

    /// <summary>Depth-first search for any object carrying <paramref name="key"/>.</summary>
    private static bool ContainsKey(JsonNode? node, string key)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (string.Equals(pair.Key, key, StringComparison.Ordinal) || ContainsKey(pair.Value, key))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (ContainsKey(item, key))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>Collects every object nested under a named renderer key.</summary>
    private static List<JsonNode?> CollectRenderers(JsonNode? node, string rendererKey)
    {
        var found = new List<JsonNode?>();
        Walk(node);
        return found;

        void Walk(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var pair in obj)
                    {
                        if (string.Equals(pair.Key, rendererKey, StringComparison.Ordinal))
                        {
                            found.Add(pair.Value);
                        }

                        Walk(pair.Value);
                    }

                    break;
                case JsonArray arr:
                    foreach (var item in arr)
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    private static void PrintTypedSummary(string browseId, JsonNode node)
    {
        // Reuse the production parsers when the id maps to a known surface (Req 24.3).
        try
        {
            switch (KnownEndpoints.ClassifySurface(browseId))
            {
                case BrowseSurface.HomeSections:
                    var home = HomeResponseParser.Parse(node);
                    Console.WriteLine($"  Parsed (Home) : {home.Sections.Count} section(s)" +
                        (home.ContinuationToken is not null ? ", continuation available" : string.Empty));
                    foreach (var section in home.Sections.Take(10))
                    {
                        Console.WriteLine($"    - {Truncate(section.Title, 50)} ({section.Items.Count} items)");
                    }

                    break;

                case BrowseSurface.LibraryLanding:
                    var lib = LibraryContentParser.Parse(node);
                    Console.WriteLine($"  Parsed (Lib)  : {lib.Playlists.Count} playlists, {lib.Albums.Count} albums, " +
                        $"{lib.Artists.Count} artists, {lib.Songs.Count} songs");
                    break;

                case BrowseSurface.Playlist:
                    var pl = PlaylistParser.ParsePlaylistDetail(node, browseId);
                    Console.WriteLine($"  Parsed (PL)   : \"{Truncate(pl.Playlist.Title, 50)}\", {pl.Tracks.Count} track(s)" +
                        (pl.ContinuationToken is not null ? ", continuation available" : string.Empty));
                    break;

                case BrowseSurface.Artist:
                    var artist = ArtistParser.Parse(node);
                    Console.WriteLine($"  Parsed (UC)   : \"{Truncate(artist.Artist.Name, 50)}\", " +
                        $"{artist.TopSongs.Count} top songs, {artist.Albums.Count} albums, " +
                        $"{artist.SinglesAndEps.Count} singles/EPs");
                    break;

                case BrowseSurface.Unknown:
                default:
                    Console.WriteLine("  Parsed        : (no typed parser for this id — see renderer keys)");
                    break;
            }
        }
        catch (KasetError ex)
        {
            // A parse failure is informative for exploration; show kind, never raw secrets.
            Console.WriteLine($"  Parsed        : parser reported {ex.Kind} ({Redactor.Redact(ex.Message)})");
        }
    }

    private static void PrintTopLevelKeys(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            Console.WriteLine($"  Top-level keys: {string.Join(", ", obj.Select(p => p.Key))}");
        }
    }

    // ── Renderer histogram ────────────────────────────────────────────────────────────────

    private static Dictionary<string, int> BuildRendererHistogram(JsonNode? node)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Walk(node, counts);
        return counts;
    }

    private static void Walk(JsonNode? node, Dictionary<string, int> counts)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Key.EndsWith("Renderer", StringComparison.Ordinal))
                    {
                        counts[pair.Key] = counts.TryGetValue(pair.Key, out var c) ? c + 1 : 1;
                    }

                    Walk(pair.Value, counts);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Walk(item, counts);
                }

                break;

            default:
                break;
        }
    }

    private static void PrintRendererHistogram(Dictionary<string, int> histogram)
    {
        Console.WriteLine();
        Console.WriteLine("  Renderer histogram:");
        if (histogram.Count == 0)
        {
            Console.WriteLine("    (none)");
            return;
        }

        foreach (var pair in histogram.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"    {pair.Value,5}  {pair.Key}");
        }
    }

    private static void PrintCompactJson(JsonNode node)
    {
        const int MaxChars = 8000;

        var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Redact as defense in depth before anything reaches the console.
        var redacted = Redactor.Redact(json);
        if (redacted.Length > MaxChars)
        {
            redacted = redacted[..MaxChars] + $"\n... [truncated, {json.Length} chars total]";
        }

        Console.WriteLine();
        Console.WriteLine("  JSON preview (redacted):");
        Console.WriteLine(redacted);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max] + "…";
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static int PrintUsageAndOk()
    {
        PrintUsage();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Kaset API Explorer (Req 24) — explore YouTube Music InnerTube endpoints.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  auth   [--cookies <path>] [--authuser <n>] [--brand <id>]");
        Console.WriteLine("  list");
        Console.WriteLine("  browse <id> [-v] [--cookies <path>] [--authuser <n>] [--brand <id>]");
        Console.WriteLine("  search <query> [--cookies <path>]");
        Console.WriteLine("         Lists song hits as videoId + title + artist (feeds 'lyrics').");
        Console.WriteLine("  lyrics <videoId> [-v] [--cookies <path>]");
        Console.WriteLine("         Resolves the Lyrics tab for a track and browses it once per");
        Console.WriteLine("         InnerTube client, reporting which (if any) return timings.");
        Console.WriteLine();
        Console.WriteLine("Cookies (for authenticated endpoints) are supplied at runtime via:");
        Console.WriteLine("  - the KASET_COOKIE environment variable (raw Cookie header), or");
        Console.WriteLine("  - --cookies <path> (file containing the raw Cookie header).");
        Console.WriteLine("Cookie/SAPISID values are never printed, logged, or stored.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  KasetWin.ApiExplorer list");
        Console.WriteLine("  KasetWin.ApiExplorer browse FEmusic_home -v");
        Console.WriteLine("  KasetWin.ApiExplorer auth --cookies .\\cookie.txt");
    }
}

/// <summary>Parsed CLI flags shared across commands. All values are non-secret except none here.</summary>
internal sealed record CliOptions
{
    public string? Positional { get; init; }

    public bool Verbose { get; init; }

    public string? CookiePath { get; init; }

    public int? AuthUser { get; init; }

    public string? Brand { get; init; }

    public static CliOptions Parse(ReadOnlySpan<string> args)
    {
        string? positional = null;
        var verbose = false;
        string? cookiePath = null;
        int? authUser = null;
        string? brand = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-v" or "--verbose":
                    verbose = true;
                    break;

                case "--cookies":
                    cookiePath = NextValue(args, ref i, "--cookies");
                    break;

                case "--authuser":
                    var raw = NextValue(args, ref i, "--authuser");
                    if (raw is not null && int.TryParse(raw, out var parsed))
                    {
                        authUser = parsed;
                    }

                    break;

                case "--brand":
                    brand = NextValue(args, ref i, "--brand");
                    break;

                default:
                    // First non-flag token is the positional argument (e.g. the browse id).
                    positional ??= arg;
                    break;
            }
        }

        return new CliOptions
        {
            Positional = positional,
            Verbose = verbose,
            CookiePath = cookiePath,
            AuthUser = authUser,
            Brand = brand,
        };
    }

    private static string? NextValue(ReadOnlySpan<string> args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Warning: {flag} expects a value; ignoring.");
            return null;
        }

        return args[++i];
    }
}
