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
