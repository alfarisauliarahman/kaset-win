namespace KasetWin.Core.Services.Api;

/// <summary>
/// Time-to-live (TTL) constants per API surface (Task 4.1, Requirements 23.1).
/// Values mirror the macOS reference cache so Windows and macOS age data identically.
/// </summary>
public static class ApiCacheTtl
{
    /// <summary>Home feed — 5 minutes.</summary>
    public static readonly TimeSpan Home = TimeSpan.FromMinutes(5);

    /// <summary>Explore (Charts / Moods / New Releases) — 5 minutes.</summary>
    public static readonly TimeSpan Explore = TimeSpan.FromMinutes(5);

    /// <summary>Library landing and collections — 5 minutes.</summary>
    public static readonly TimeSpan Library = TimeSpan.FromMinutes(5);

    /// <summary>Search results — 2 minutes.</summary>
    public static readonly TimeSpan Search = TimeSpan.FromMinutes(2);

    /// <summary>Playlist and album detail — 30 minutes.</summary>
    public static readonly TimeSpan Playlist = TimeSpan.FromMinutes(30);

    /// <summary>Song / video metadata (player endpoint) — 30 minutes.</summary>
    public static readonly TimeSpan SongMetadata = TimeSpan.FromMinutes(30);

    /// <summary>Artist pages — 1 hour.</summary>
    public static readonly TimeSpan Artist = TimeSpan.FromHours(1);

    /// <summary>Lyrics — 24 hours.</summary>
    public static readonly TimeSpan Lyrics = TimeSpan.FromHours(24);
}
