using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Browse / library / detail endpoints (task 7.2). All methods post to the InnerTube
/// <c>browse</c> endpoint via the shared <see cref="RequestAsync"/> core (task 7.1) and delegate
/// response shaping to the static per-surface parsers.
/// </summary>
/// <remarks>
/// The section-based surfaces (Home/Explore/Charts/Moods/NewReleases/History) share the
/// <see cref="HomeResponseParser"/> shape; library landing uses <see cref="LibraryContentParser"/>;
/// playlist/album detail uses <see cref="PlaylistParser"/>; the artist page uses
/// <see cref="ArtistParser"/>. TTLs come from <see cref="ApiCacheTtl"/> so Windows ages data
/// identically to macOS (history is intentionally uncached).
/// </remarks>
public sealed partial class YTMusicClient
{
    // ── Home / Explore / Charts / Moods / New Releases ──────────────────────────────────

    /// <inheritdoc />
    public async Task<HomeResponse> GetHomeAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_home"), ApiCacheTtl.Home, ct)
            .ConfigureAwait(false);
        return HomeResponseParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetHomeContinuationAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var node = await RequestAsync("browse", ContinuationBody(token), ApiCacheTtl.Home, ct)
            .ConfigureAwait(false);
        return HomeResponseParser.ParseContinuation(node);
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetExploreAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_explore"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return HomeResponseParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetChartsAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_charts"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return HomeResponseParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetMoodsAndGenresAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_moods_and_genres"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return HomeResponseParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetNewReleasesAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_new_releases"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return HomeResponseParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetMoodCategoryAsync(string browseId, string? categoryParams = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(browseId);

        // Moods/genres category pages browse like the section surfaces (they return a
        // Home-shaped response) but require the category-specific params token captured from the
        // navigation button. Reuse the shared HomeResponseParser (Req 31.2).
        var body = BrowseBody(browseId);
        if (!string.IsNullOrEmpty(categoryParams))
        {
            body["params"] = categoryParams;
        }

        var node = await RequestAsync("browse", body, ApiCacheTtl.Explore, ct).ConfigureAwait(false);
        return HomeResponseParser.Parse(node);
    }

    // ── Library ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LibraryContent> GetLibraryLandingAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_library_landing"), ApiCacheTtl.Library, ct)
            .ConfigureAwait(false);
        return LibraryContentParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Playlist>> GetLibraryPlaylistsAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_liked_playlists"), ApiCacheTtl.Library, ct)
            .ConfigureAwait(false);

        // No dedicated "library playlists" parser exists in Core; the library content parser
        // already classifies the landing/liked-playlists tiles into the playlists collection.
        return LibraryContentParser.Parse(node).Playlists;
    }

    /// <inheritdoc />
    public async Task<PlaylistDetail> GetLikedSongsAsync(CancellationToken ct = default)
    {
        // Liked Music quirk: VLLM returns the full liked-songs playlist with proper pagination,
        // unlike FEmusic_liked_videos which is capped at ~13 rows.
        var node = await RequestAsync("browse", BrowseBody("VLLM"), ApiCacheTtl.Library, ct)
            .ConfigureAwait(false);
        return PlaylistParser.ParsePlaylistDetail(node, "VLLM");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Song>> GetUploadedSongsAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync(
                "browse",
                BrowseBody("FEmusic_library_privately_owned_tracks"),
                ApiCacheTtl.Library,
                ct)
            .ConfigureAwait(false);

        // The uploads surface returns a playlist-shaped body; reuse the playlist detail parser
        // and surface its tracks.
        return PlaylistParser.ParsePlaylistDetail(node, "FEmusic_library_privately_owned_tracks").Tracks;
    }

    // ── Detail ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PlaylistDetail> GetPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        var node = await RequestAsync("browse", BrowseBody(ResolvePlaylistBrowseId(playlistId)), ApiCacheTtl.Playlist, ct)
            .ConfigureAwait(false);

        // Identity stays the caller-supplied id (without any synthesized VL prefix).
        return PlaylistParser.ParsePlaylistDetail(node, playlistId);
    }

    /// <inheritdoc />
    public async Task<PlaylistDetail> GetPlaylistContinuationAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var node = await RequestAsync("browse", ContinuationBody(token), ApiCacheTtl.Playlist, ct)
            .ConfigureAwait(false);

        var continuation = PlaylistParser.ParsePlaylistContinuation(node);

        // A continuation response carries no playlist header. Wrap the next batch of tracks in a
        // PlaylistDetail (with a minimal placeholder Playlist) so the caller can concatenate the
        // tracks onto the original PlaylistDetail and carry the next continuation token forward.
        return new PlaylistDetail
        {
            Playlist = new Playlist { Id = string.Empty, Title = string.Empty },
            Tracks = continuation.Tracks,
            ContinuationToken = continuation.ContinuationToken,
        };
    }

    /// <inheritdoc />
    public async Task<ArtistDetail> GetArtistAsync(string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);

        var node = await RequestAsync("browse", BrowseBody(channelId), ApiCacheTtl.Artist, ct)
            .ConfigureAwait(false);
        return ArtistParser.Parse(node);
    }

    // ── Podcasts (advanced, Req 27) ─────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Region-aware (Req 27.1/27.2): YouTube Music does not offer the Podcasts discovery surface in
    /// every region. Where it is unavailable, <c>FEmusic_podcasts</c> returns HTTP 404 — that is a
    /// normal, non-fatal signal here, mapped to <see cref="PodcastsResult.Unavailable"/> (tab hidden)
    /// rather than surfaced as an error. A successful response (even with no sections) means the
    /// surface is available and the tab should be shown. See ADR-0019.
    /// </remarks>
    public async Task<PodcastsResult> GetPodcastsAsync(CancellationToken ct = default)
    {
        try
        {
            var node = await RequestAsync("browse", BrowseBody("FEmusic_podcasts"), ApiCacheTtl.Explore, ct)
                .ConfigureAwait(false);
            var sections = PodcastParser.ParseDiscovery(node);
            return new PodcastsResult(IsAvailable: true, sections);
        }
        catch (KasetError ex) when (ex.Kind == KasetErrorKind.ApiError && ex.ApiStatusCode == 404)
        {
            // Unsupported region: hide the tab, no sections (Req 27.2).
            return PodcastsResult.Unavailable;
        }
    }

    // ── Generic / exploration ─────────────────────────────────────────────────────────
    /// <summary>
    /// Issues a raw <c>browse</c> request for an arbitrary <paramref name="browseId"/> and returns
    /// the unparsed InnerTube JSON response root. Intended for the API Explorer CLI (Req 24.3) to
    /// inspect endpoints that have no dedicated typed parser; the response is intentionally
    /// uncached so exploration always observes live structure.
    /// </summary>
    /// <param name="browseId">The InnerTube browse id (e.g. <c>FEmusic_home</c>, <c>VL...</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON response root. Cookies/SAPISID are never present in the payload.</returns>
    public Task<JsonNode> BrowseRawAsync(string browseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(browseId);
        return RequestAsync("browse", BrowseBody(browseId), ttl: null, ct);
    }

    // ── Body helpers ────────────────────────────────────────────────────────────────────

    /// <summary>Builds a <c>{ "browseId": ... }</c> request body.</summary>
    private static JsonObject BrowseBody(string browseId) => new() { ["browseId"] = browseId };

    /// <summary>Builds a <c>{ "continuation": ... }</c> request body for browse continuations.</summary>
    private static JsonObject ContinuationBody(string token) => new() { ["continuation"] = token };

    /// <summary>
    /// Normalizes a playlist identifier into a browse id. Ids that are already browse-ready
    /// (<c>VL…</c> playlist, <c>RD…</c> radio/mix, <c>OLAK…</c>/<c>MPRE…</c> album, <c>UC…</c>
    /// channel) are used as-is; everything else (notably bare <c>PL…</c> ids) gets the <c>VL</c>
    /// browse prefix.
    /// </summary>
    private static string ResolvePlaylistBrowseId(string id)
    {
        if (id.StartsWith("VL", StringComparison.Ordinal)
            || id.StartsWith("RD", StringComparison.Ordinal)
            || id.StartsWith("OLAK", StringComparison.Ordinal)
            || id.StartsWith("MPRE", StringComparison.Ordinal)
            || id.StartsWith("UC", StringComparison.Ordinal))
        {
            return id;
        }

        return YTMusicIds.PlaylistBrowsePrefix + id;
    }
}
