using System.Text.Json;

namespace KasetWin.Core.Tests;

/// <summary>
/// Loader for sanitized InnerTube response fixtures stored under
/// <c>Fixtures/{Surface}/</c>. Fixtures are copied to the test output
/// directory (see the csproj <c>&lt;Content&gt;</c> glob), so loading works
/// when <c>dotnet test</c> runs from the build output.
///
/// SECURITY: Fixtures are fully redacted. They MUST NOT contain real cookies,
/// tokens, SAPISID values, or any PII. Use placeholders only.
/// </summary>
public static class TestFixtures
{
    /// <summary>Known per-surface fixture folders (mirrors the macOS Tests/Fixtures layout).</summary>
    public static class Surfaces
    {
        public const string Home = "Home";
        public const string Charts = "Charts";
        public const string Search = "Search";
        public const string Library = "Library";
        public const string Playlist = "Playlist";
        public const string Artist = "Artist";
        public const string RadioQueue = "RadioQueue";
        public const string SongMetadata = "SongMetadata";
        public const string Lyrics = "Lyrics";
    }

    /// <summary>Absolute path to the Fixtures root in the test output directory.</summary>
    public static string FixturesRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>
    /// Resolves the absolute path to a fixture file. Appends a <c>.json</c>
    /// extension when <paramref name="name"/> has none.
    /// </summary>
    public static string ResolvePath(string surface, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var fileName = Path.HasExtension(name) ? name : name + ".json";
        return Path.Combine(FixturesRoot, surface, fileName);
    }

    /// <summary>
    /// Reads a fixture file as raw UTF-8 text. Throws <see cref="FileNotFoundException"/>
    /// with a helpful message when the fixture is missing.
    /// </summary>
    public static string LoadString(string surface, string name)
    {
        var path = ResolvePath(surface, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Fixture not found: '{path}'. Ensure the file exists under " +
                $"tests/KasetWin.Core.Tests/Fixtures/{surface}/ and is copied to output (Content glob in csproj).",
                path);
        }

        return File.ReadAllText(path);
    }

    /// <summary>Reads and parses a fixture file into a <see cref="JsonDocument"/>.</summary>
    public static JsonDocument LoadJson(string surface, string name)
    {
        var text = LoadString(surface, name);
        return JsonDocument.Parse(text);
    }
}
