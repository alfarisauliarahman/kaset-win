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
        // The server occasionally answers with an envelope that has no sectionListRenderer at all
        // (cookie race at startup / transient hiccup). Only a parseable page may enter the cache, and
        // a raced answer is retried once; if it is still empty we degrade to an EMPTY home rather than
        // throw — a scary red "no sectionListRenderer" toast for a transient hiccup is worse UX than a
        // momentarily blank Home that fills in on the next load (the bad envelope is never cached).
        for (var attempt = 0; ; attempt++)
        {
            var node = await RequestAsync(
                    "browse",
                    BrowseBody("FEmusic_home"),
                    ApiCacheTtl.Home,
                    ct,
                    shouldCache: static n => ResponseTreeSearch.FindFirst(n, "sectionListRenderer") is not null)
                .ConfigureAwait(false);

            try
            {
                return HomeResponseParser.Parse(node);
            }
            catch (KasetError ex) when (ex.Kind == KasetErrorKind.ParseError)
            {
                Diag.Write($"home parse FAILED (attempt {attempt}): {ex.Message}");
                if (attempt >= 1)
                {
                    // Record the alien envelope so its exact shape can be diagnosed offline.
                    Diag.Dump("home-parse-fail.json", node?.ToJsonString() ?? "null");
                    return new HomeResponse();
                }

                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Parses a Home-shaped section response, degrading a transient "no sectionListRenderer"
    /// envelope (cookie race / region hiccup) to an EMPTY result instead of throwing — so the shell
    /// never shows a scary red parse-error toast for a surface that simply came back empty.
    /// </summary>
    private static HomeResponse ParseHomeOrEmpty(JsonNode? node, string label)
    {
        try
        {
            return HomeResponseParser.Parse(node);
        }
        catch (KasetError ex) when (ex.Kind == KasetErrorKind.ParseError)
        {
            Diag.Write($"{label} parse degraded to empty: {ex.Message}");
            return new HomeResponse();
        }
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetSongRelatedAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        // 1) The watch-next response carries the tabs; the Related tab exposes a browseId.
        // NOTE: ConfigureAwait(true) — the SECOND request below must start on the caller's (UI)
        // thread because the WebView2 cookie source is thread-affine; resuming on the threadpool
        // made it throw COMException ("method can only be called from the thread that created it").
        var next = await RequestAsync(
            "next",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["enablePersistentPlaylistPanel"] = true,
                ["isAudioOnly"] = true,
                ["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
            },
            ApiCacheTtl.SongMetadata,
            ct).ConfigureAwait(true);

        var relatedBrowseId = SongMetadataParser.ExtractRelatedBrowseId(next);
        Diag.Write($"related videoId={videoId} browseId={relatedBrowseId ?? "<null>"}");
        if (string.IsNullOrEmpty(relatedBrowseId))
        {
            return new HomeResponse();
        }

        // 2) Browse the Related target; it is a section-based response like Home/Explore.
        // Related is best-effort end to end: request or parse failures degrade to "no related
        // content" (and are logged) rather than surfacing an exception.
        try
        {
            var node = await RequestAsync("browse", BrowseBody(relatedBrowseId), ApiCacheTtl.SongMetadata, ct)
                .ConfigureAwait(false);
            var parsed = HomeResponseParser.Parse(node);
            var (aboutTitle, aboutText) = HomeResponseParser.ParseDescriptionShelf(node);
            Diag.Write($"related parsed sections={parsed.Sections.Count} about={(aboutText is null ? "no" : "yes")}");
            return parsed with { AboutTitle = aboutTitle, AboutText = aboutText };
        }
        catch (System.Exception ex)
        {
            Diag.Write($"related FAILED: {ex.GetType().Name}: {ex.Message}");
            return new HomeResponse();
        }
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
        return ParseHomeOrEmpty(node, "explore");
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetChartsAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_charts"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return ParseHomeOrEmpty(node, "charts");
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetMoodsAndGenresAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_moods_and_genres"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return ParseHomeOrEmpty(node, "moods");
    }

    /// <inheritdoc />
    public async Task<HomeResponse> GetNewReleasesAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("browse", BrowseBody("FEmusic_new_releases"), ApiCacheTtl.Explore, ct)
            .ConfigureAwait(false);
        return ParseHomeOrEmpty(node, "new-releases");
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
        return ParseHomeOrEmpty(node, "mood-category");
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
    public async Task<IReadOnlyList<Song>> GetLibrarySongsAsync(CancellationToken ct = default)
    {
        // Library "Songs" = the saved-to-library tracks (FEmusic_liked_videos), a DIFFERENT dataset
        // from the Liked Music playlist (VLLM / GetLikedSongsAsync). This surface is capped per page,
        // so follow a few continuations to build a fuller list.
        var node = await RequestAsync("browse", BrowseBody("FEmusic_liked_videos"), ApiCacheTtl.Library, ct)
            .ConfigureAwait(false);

        var detail = PlaylistParser.ParsePlaylistDetail(node, "FEmusic_liked_videos");
        var songs = new List<Song>(detail.Tracks);

        // Paging is best-effort enrichment: a failing continuation returns what we already have
        // rather than throwing (which would blank the whole Library page).
        try
        {
            var token = detail.ContinuationToken;
            var pages = 0;
            while (!string.IsNullOrEmpty(token) && pages < 6)
            {
                ct.ThrowIfCancellationRequested();
                var cont = await GetPlaylistContinuationAsync(token, ct).ConfigureAwait(false);
                songs.AddRange(cont.Tracks);
                token = cont.ContinuationToken;
                pages++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Return the first page(s) collected so far.
        }

        return songs;
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Artist>> GetLibrarySongArtistsAsync(CancellationToken ct = default)
    {
        // The library "Artists" corpus: artists derived from the user's saved/liked songs, each row
        // carrying a "N lagu" subtitle (unlike the landing shelf, which lists channel subscriptions).
        var node = await RequestAsync("browse", BrowseBody("FEmusic_library_corpus_track_artists"), ApiCacheTtl.Library, ct)
            .ConfigureAwait(false);

        var artists = new List<Artist>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Parsers.ResponseTreeSearch.FindAll(node, "musicResponsiveListItemRenderer"))
        {
            var browseId = Parsers.ResponseTreeSearch.FindFirst(row, "browseEndpoint") is JsonObject be
                && be.TryGetPropertyValue("browseId", out var idNode)
                && idNode is JsonValue idValue && idValue.TryGetValue<string>(out var id)
                ? id
                : null;
            if (browseId is null || !seen.Add(browseId))
            {
                continue;
            }

            var name = Parsers.ParsingHelpers.ExtractTitleFromFlexColumns(row);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            artists.Add(new Artist
            {
                Id = browseId,
                Name = name,
                ThumbnailUrl = Parsers.ParsingHelpers.BestThumbnailUrl(row),
                SubtitleText = Parsers.ParsingHelpers.ExtractSecondFlexColumnText(row),
            });
        }

        Diag.Write($"library-artists count={artists.Count}");
        return artists;
    }

    // ── Detail ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PlaylistDetail> GetPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        var node = await RequestAsync("browse", BrowseBody(ResolvePlaylistBrowseId(playlistId)), ApiCacheTtl.Playlist, ct)
            .ConfigureAwait(false);

        // Identity stays the caller-supplied id (without any synthesized VL prefix).
        var detail = PlaylistParser.ParsePlaylistDetail(node, playlistId);
        var explicitCount = detail.Tracks.Count(t => t.IsExplicit == true);
        Diag.Write($"playlist id={playlistId} tracks={detail.Tracks.Count} explicit={explicitCount} title={detail.Playlist.Title}");

        // TEMPORARY: creator avatars still don't surface for other users' playlists — record which
        // avatar-bearing renderers the response actually contains so the right path can be parsed.
        if (detail.Playlist.Author is { ThumbnailUrl: null })
        {
            var json = node.ToJsonString();
            var probes = new[] { "avatarViewModel", "straplineThumbnail", "facepile", "accountPhoto", "channelThumbnail", "ownerBadge", "musicVisualHeaderRenderer" };
            Diag.Write("avatar-probe " + string.Join(" ", probes.Select(k => $"{k}={(json.Contains(k, StringComparison.Ordinal) ? 1 : 0)}")));
        }

        // TEMPORARY diagnosis: some albums report zero explicit tracks even though the web shows
        // badges (their rows carry no "badges" key at all) — locate where the marker actually lives.
        if (explicitCount == 0 && detail.Tracks.Count > 0)
        {
            var json = node.ToJsonString();
            var idx = json.IndexOf("MUSIC_EXPLICIT_BADGE", StringComparison.Ordinal);
            if (idx < 0)
            {
                Diag.Write("explicit-marker: NONE anywhere in the response");
            }
            else
            {
                var start = Math.Max(0, idx - 300);
                var ctx = json.Substring(start, Math.Min(500, json.Length - start)).Replace('\n', ' ');
                Diag.Write($"explicit-ctx: …{ctx}…");
            }
        }

        return detail;
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

    /// <inheritdoc />
    public async Task<ArtistSectionResult> GetArtistSectionAsync(string browseId, string artistName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(browseId);

        var node = await RequestAsync("browse", BrowseBody(browseId), ApiCacheTtl.Artist, ct)
            .ConfigureAwait(false);
        var pageArtist = new Artist { Id = browseId, Name = artistName ?? string.Empty };
        return ArtistParser.ParseSectionItems(node, pageArtist);
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
            // NOTE: the podcasts landing is region-gated by IP + account country (confirmed: Indonesia
            // 404s / redirects to home; only a real US IP via VPN works). Request-body gl/clientVersion
            // overrides do NOT bypass it, so we just issue the normal request and treat 404 as
            // "unavailable" (the tab shows an empty state).
            var node = await RequestAsync("browse", BrowseBody("FEmusic_podcasts"), ApiCacheTtl.Explore, ct)
                .ConfigureAwait(false);
            var sections = PodcastParser.ParseDiscovery(node);
            // TEMP diag: distinguish "region OK but parser found nothing" from a real region block.
            var hasSectionList = ResponseTreeSearch.FindFirst(node, "sectionListRenderer") is not null;
            Diag.Write($"podcasts landing: sections={sections.Count} hasSectionListRenderer={hasSectionList} rootLen={node?.ToJsonString().Length ?? 0}");
            return new PodcastsResult(IsAvailable: true, sections);
        }
        catch (KasetError ex) when (ex.Kind == KasetErrorKind.ApiError && ex.ApiStatusCode == 404)
        {
            // Unsupported region: hide the tab, no sections (Req 27.2).
            Diag.Write("podcasts landing: 404 (region unavailable)");
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
