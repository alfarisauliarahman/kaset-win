namespace KasetWin.ApiExplorer;

/// <summary>
/// A known InnerTube <c>browse</c> endpoint surfaced by the <c>list</c> command (Req 24.1).
/// Non-secret, static catalog mirroring <c>docs/api-discovery.md</c>.
/// </summary>
/// <param name="BrowseId">The browse id passed to <c>browse &lt;id&gt;</c>.</param>
/// <param name="Name">Human-readable surface name.</param>
/// <param name="RequiresAuth">Whether the surface needs a signed-in session.</param>
/// <param name="Description">Short description of the content returned.</param>
internal readonly record struct KnownEndpoint(string BrowseId, string Name, bool RequiresAuth, string Description);

/// <summary>
/// Static catalog of well-known browse endpoints. Used by <c>list</c> for discovery and by
/// <c>browse</c> to pick a typed parser when the id is recognised.
/// </summary>
internal static class KnownEndpoints
{
    public static readonly IReadOnlyList<KnownEndpoint> All =
    [
        new("FEmusic_home", "Home", false, "Personalized recommendations, mixes, quick picks"),
        new("FEmusic_explore", "Explore", false, "New releases, charts, moods shortcuts"),
        new("FEmusic_charts", "Charts", false, "Top songs/albums by country/genre"),
        new("FEmusic_moods_and_genres", "Moods & Genres", false, "Browse by mood/genre grids"),
        new("FEmusic_new_releases", "New Releases", false, "Recent albums, singles, videos"),
        new("FEmusic_library_landing", "Library Landing", true, "All library content (playlists, podcasts, artists)"),
        new("FEmusic_liked_playlists", "Library Playlists", true, "User's saved/created playlists"),
        new("FEmusic_library_privately_owned_tracks", "Uploaded Songs", true, "User-uploaded songs (paginated)"),
        new("VLLM", "Liked Songs", true, "All liked songs (use VLLM, not FEmusic_liked_videos)"),
        new("FEmusic_history", "History", true, "Recently played tracks"),
        new("FEmusic_podcasts", "Podcasts Discovery", false, "Podcast shows and episodes carousel"),
    ];

    /// <summary>
    /// Classifies a browse id into the typed parser surface it maps to, so <c>browse</c> can print
    /// a structured summary in addition to the generic renderer histogram.
    /// </summary>
    public static BrowseSurface ClassifySurface(string browseId)
    {
        if (browseId is "FEmusic_home" or "FEmusic_explore" or "FEmusic_charts"
            or "FEmusic_moods_and_genres" or "FEmusic_new_releases")
        {
            return BrowseSurface.HomeSections;
        }

        if (browseId is "FEmusic_library_landing")
        {
            return BrowseSurface.LibraryLanding;
        }

        if (browseId.StartsWith("UC", StringComparison.Ordinal))
        {
            return BrowseSurface.Artist;
        }

        if (browseId is "VLLM"
            || browseId.StartsWith("VL", StringComparison.Ordinal)
            || browseId.StartsWith("PL", StringComparison.Ordinal)
            || browseId.StartsWith("OLAK", StringComparison.Ordinal)
            || browseId.StartsWith("MPRE", StringComparison.Ordinal)
            || browseId.StartsWith("RD", StringComparison.Ordinal))
        {
            return BrowseSurface.Playlist;
        }

        return BrowseSurface.Unknown;
    }
}

/// <summary>The typed parser surface a browse id maps to (for structured summaries).</summary>
internal enum BrowseSurface
{
    Unknown,
    HomeSections,
    LibraryLanding,
    Playlist,
    Artist,
}
