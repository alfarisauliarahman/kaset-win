using System.Text.Json.Nodes;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Playback detail (now playing / radio), mutations, and advanced surfaces (task 7.4). Posts to
/// the InnerTube <c>next</c>, <c>like/*</c>, <c>feedback</c>, <c>subscription/*</c>,
/// <c>playlist/*</c>, <c>browse/edit_playlist</c>, <c>account/accounts_list</c> and assorted
/// browse endpoints via the shared <see cref="RequestAsync"/> core (task 7.1).
/// </summary>
/// <remarks>
/// Successful mutations call <see cref="IApiCache.InvalidateMutationCaches"/> so library views,
/// now-playing metadata, and the add-to-playlist menu refresh on the next read. A few advanced
/// surfaces (<c>account/accounts_list</c>, <c>FEmusic_history</c>, <c>MPSPP…</c> podcast show)
/// have no dedicated Core parser yet and use a minimal, crash-safe inline parse (marked TODO).
/// </remarks>
public sealed partial class YTMusicClient
{
    // Shared "next" tuner setting used for now-playing / radio / mix requests.
    private const string AutomixSettingNormal = "AUTOMIX_SETTING_NORMAL";

    // ── Now playing / radio ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SongMetadata> GetSongMetadataAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["enablePersistentPlaylistPanel"] = true,
            ["isAudioOnly"] = true,
            ["tunerSettingValue"] = AutomixSettingNormal,
        };

        var node = await RequestAsync("next", body, ApiCacheTtl.SongMetadata, ct).ConfigureAwait(false);

        // TEMPORARY probe for the "always play the song version" feature request. The question it
        // answers: does OUR session's watch-next response carry a counterpart (the paired ATV/OMV
        // videoId behind YT Music's own SONG/VIDEO toggle)? Anonymous probes said no; the answer for
        // a signed-in session can only come from inside the app, and guessing response shapes is
        // exactly what AGENTS.md forbids. One line per lookup, dump only on the first hit.
        try
        {
            string raw = node.ToJsonString();
            bool hasCounterpart = raw.Contains("counterpart", StringComparison.Ordinal);
            Diag.Write($"next-probe videoId={videoId} counterpart={hasCounterpart} wrapper={raw.Contains("playlistPanelVideoWrapperRenderer", StringComparison.Ordinal)}");
            if (hasCounterpart)
            {
                Diag.Dump($"next-counterpart-{videoId}.json", raw);
            }
        }
        catch
        {
            // The probe must never affect metadata fetching.
        }

        return SongMetadataParser.Parse(node, videoId);
    }

    /// <inheritdoc />
    public async Task<RadioQueueResult> GetRadioQueueAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["playlistId"] = "RDAMVM" + videoId,
            ["enablePersistentPlaylistPanel"] = true,
            ["isAudioOnly"] = true,
            ["tunerSettingValue"] = AutomixSettingNormal,
        };

        // Radio/mix queues are never cached — they are seeded fresh per playback session.
        var node = await RequestAsync("next", body, ttl: null, ct).ConfigureAwait(false);
        return RadioQueueParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<RadioQueueResult> GetMixQueueAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        var body = new JsonObject
        {
            ["playlistId"] = playlistId,
            ["enablePersistentPlaylistPanel"] = true,
            ["isAudioOnly"] = true,
            ["tunerSettingValue"] = AutomixSettingNormal,
        };

        var node = await RequestAsync("next", body, ttl: null, ct).ConfigureAwait(false);
        return RadioQueueParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<RadioQueueResult> GetMixContinuationAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var body = new JsonObject
        {
            ["continuation"] = token,
            ["enablePersistentPlaylistPanel"] = true,
            ["isAudioOnly"] = true,
        };

        var node = await RequestAsync("next", body, ttl: null, ct).ConfigureAwait(false);
        return RadioQueueParser.ParseContinuation(node);
    }

    // ── Like / library mutations ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task RateSongAsync(string videoId, LikeStatus rating, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var endpoint = rating switch
        {
            LikeStatus.Like => "like/like",
            LikeStatus.Dislike => "like/dislike",
            _ => "like/removelike",
        };

        var body = new JsonObject
        {
            ["target"] = new JsonObject { ["videoId"] = videoId },
        };

        await RequestAsync(endpoint, body, ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    public async Task RatePlaylistAsync(string playlistId, LikeStatus rating, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        var endpoint = rating == LikeStatus.Like ? "like/like" : "like/removelike";
        await RequestAsync(endpoint, PlaylistTargetBody(YTMusicIds.StripVlPrefix(playlistId)), ttl: null, ct)
            .ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    public async Task SendFeedbackAsync(IReadOnlyList<string> feedbackTokens, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(feedbackTokens);

        // No tokens → nothing to do; avoid issuing an empty feedback request.
        if (feedbackTokens.Count == 0)
        {
            return;
        }

        var tokens = new JsonArray();
        foreach (var token in feedbackTokens)
        {
            tokens.Add(token);
        }

        var body = new JsonObject { ["feedbackTokens"] = tokens };

        await RequestAsync("feedback", body, ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    public async Task SubscribeArtistAsync(string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);
        await RequestAsync("subscription/subscribe", ChannelIdsBody(channelId), ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    public async Task UnsubscribeArtistAsync(string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);
        await RequestAsync("subscription/unsubscribe", ChannelIdsBody(channelId), ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    // ── Podcast subscription (Req 27.4) ──────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// A podcast show is "subscribed" by liking its playlist counterpart: the <c>MPSPP…</c> show id
    /// is converted to the <c>PL…</c> playlist id (<see cref="YTMusicIds.ConvertPodcastShowIdToPlaylistId"/>,
    /// Req 27.4) and sent to <c>like/like</c>. Reuses the same conversion as Property 36.
    /// </remarks>
    public async Task SubscribePodcastAsync(string showId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(showId);

        var playlistId = YTMusicIds.ConvertPodcastShowIdToPlaylistId(showId);
        await RequestAsync("like/like", PlaylistTargetBody(playlistId), ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mirror of <see cref="SubscribePodcastAsync"/>: the converted <c>PL…</c> id is sent to
    /// <c>like/removelike</c> to remove the show from the library (Req 27.4).
    /// </remarks>
    public async Task UnsubscribePodcastAsync(string showId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(showId);

        var playlistId = YTMusicIds.ConvertPodcastShowIdToPlaylistId(showId);
        await RequestAsync("like/removelike", PlaylistTargetBody(playlistId), ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    // ── Playlist mutations ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AddToPlaylistMenu> GetAddToPlaylistOptionsAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var body = new JsonObject { ["videoIds"] = new JsonArray { videoId } };

        var node = await RequestAsync("playlist/get_add_to_playlist", body, ApiCacheTtl.Library, ct)
            .ConfigureAwait(false);
        return PlaylistParser.ParseAddToPlaylistMenu(node);
    }

    /// <inheritdoc />
    public async Task AddSongToPlaylistAsync(string videoId, string playlistId, bool allowDuplicates = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        // ytmusicapi (the proven reference client) sets dedupeOption ON THE ACTION (not the body
        // root) with DEDUPE_OPTION_SKIP, which skips the server's duplicate check so the song is
        // added again ("Tetap Tambahkan"). Placing it on the root (an earlier attempt) is ignored.
        var action = new JsonObject
        {
            ["action"] = "ACTION_ADD_VIDEO",
            ["addedVideoId"] = videoId,
        };

        if (allowDuplicates)
        {
            action["dedupeOption"] = "DEDUPE_OPTION_SKIP";
        }

        // Mutation endpoints reject the VL browse prefix; strip it before sending.
        var body = new JsonObject
        {
            ["playlistId"] = YTMusicIds.StripVlPrefix(playlistId),
            ["actions"] = new JsonArray { action },
        };

        await RequestAsync("browse/edit_playlist", body, ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    public async Task EditPlaylistMetadataAsync(
        string playlistId,
        string? title = null,
        string? description = null,
        PlaylistPrivacy? privacy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        // ytmusicapi edit_playlist: browse/edit_playlist with SET_PLAYLIST_NAME / _DESCRIPTION /
        // _PRIVACY actions; only the provided fields are sent.
        var actions = new JsonArray();
        if (!string.IsNullOrWhiteSpace(title))
        {
            actions.Add(new JsonObject { ["action"] = "ACTION_SET_PLAYLIST_NAME", ["playlistName"] = title });
        }

        if (description is not null)
        {
            actions.Add(new JsonObject { ["action"] = "ACTION_SET_PLAYLIST_DESCRIPTION", ["playlistDescription"] = description });
        }

        if (privacy is { } p)
        {
            actions.Add(new JsonObject { ["action"] = "ACTION_SET_PLAYLIST_PRIVACY", ["playlistPrivacy"] = PrivacyStatus(p) });
        }

        if (actions.Count == 0)
        {
            return;
        }

        var body = new JsonObject
        {
            ["playlistId"] = YTMusicIds.StripVlPrefix(playlistId),
            ["actions"] = actions,
        };

        await RequestAsync("browse/edit_playlist", body, ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    /// <inheritdoc />
    public async Task<string> CreatePlaylistAsync(
        string title,
        string? description,
        PlaylistPrivacy privacy,
        IReadOnlyList<string>? videoIds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);

        var body = new JsonObject
        {
            ["title"] = title,
            ["privacyStatus"] = PrivacyStatus(privacy),
        };

        // Omit a blank description and an empty seed list rather than sending empty values.
        if (!string.IsNullOrWhiteSpace(description))
        {
            body["description"] = description;
        }

        if (videoIds is { Count: > 0 })
        {
            var ids = new JsonArray();
            foreach (var id in videoIds)
            {
                ids.Add(id);
            }

            body["videoIds"] = ids;
        }

        var node = await RequestAsync("playlist/create", body, ttl: null, ct).ConfigureAwait(false);
        var playlistId = PlaylistParser.ParseCreatedPlaylistId(node);
        _cache.InvalidateMutationCaches();
        return playlistId;
    }

    /// <inheritdoc />
    public async Task DeletePlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);

        var body = new JsonObject { ["playlistId"] = YTMusicIds.StripVlPrefix(playlistId) };

        await RequestAsync("playlist/delete", body, ttl: null, ct).ConfigureAwait(false);
        _cache.InvalidateMutationCaches();
    }

    // ── Accounts ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccount>> GetAccountsListAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("account/accounts_list", new JsonObject(), ttl: null, ct)
            .ConfigureAwait(false);
        return ParseAccounts(node);
    }

    // ── Advanced ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<Song>> GetHistoryAsync(CancellationToken ct = default)
    {
        // History changes with every play, so it is intentionally uncached.
        var node = await RequestAsync("browse", BrowseBody("FEmusic_history"), ttl: null, ct)
            .ConfigureAwait(false);

        // History uses the section-based Home shape; flatten the song items out of the sections.
        // A transient "no sectionListRenderer" envelope (cookie race / empty history) degrades to an
        // empty list instead of throwing — otherwise it surfaced the scary red parse-error toast.
        HomeResponse home;
        try
        {
            home = HomeResponseParser.Parse(node);
        }
        catch (KasetError ex) when (ex.Kind == KasetErrorKind.ParseError)
        {
            Diag.Write($"history parse degraded to empty: {ex.Message}");
            return [];
        }

        var songs = new List<Song>();
        foreach (var section in home.Sections)
        {
            foreach (var item in section.Items)
            {
                if (item is HomeSectionItem.SongItem songItem)
                {
                    songs.Add(songItem.Song);
                }
            }
        }

        return songs;
    }

    /// <inheritdoc />
    public async Task<PodcastShow> GetPodcastShowAsync(string showId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(showId);

        // No region override: an individual show isn't region-locked, and forcing gl=US made YT
        // return English descriptions/durations instead of the account's original language.
        // ConfigureAwait(true): the follow-up continuation requests below must START on the caller's
        // (UI) thread — the WebView2 cookie source is thread-affine and throws COMException when a
        // request begins on the thread pool (same lesson as GetSongRelatedAsync).
        var node = await RequestAsync("browse", BrowseBody(showId), ApiCacheTtl.Playlist, ct)
            .ConfigureAwait(true);

        // TEMP diag: dump the raw page for offline analysis (playback-progress fields, header).
        Diag.Dump("podcast-show.json", node?.ToJsonString() ?? "null");

        // Header extraction is scoped to the page's header renderer so the cover/author don't get
        // grabbed from an arbitrary episode row (the old FindFirst-anywhere bug).
        var headerNode = ResponseTreeSearch.FindFirst(node, "musicResponsiveHeaderRenderer")
            ?? ResponseTreeSearch.FindFirst(node, "musicDetailHeaderRenderer")
            ?? ResponseTreeSearch.FindFirst(node, "musicImmersiveHeaderRenderer");

        var title = ParsingHelpers.ExtractText(headerNode, "title");

        // Author / publisher line: the 2024+ responsive header carries it in straplineTextOne;
        // older detail headers put it in the subtitle runs.
        var author = ParsingHelpers.ExtractText(headerNode, "straplineTextOne")
            ?? ParsingHelpers.ExtractText(headerNode, "subtitle");

        // Author channel id: the first UC browse endpoint inside the header (strapline link).
        string? authorChannelId = null;
        foreach (var browseEndpoint in ResponseTreeSearch.FindAll(headerNode ?? node, "browseEndpoint"))
        {
            if (browseEndpoint is JsonObject be
                && be["browseId"] is JsonValue bv
                && bv.TryGetValue<string>(out var bid)
                && bid.StartsWith("UC", StringComparison.Ordinal))
            {
                authorChannelId = bid;
                break;
            }
        }

        // Show/playlist description (the header hosts a description shelf on 2024+ pages).
        var description = ParsingHelpers.ExtractText(
            ResponseTreeSearch.FindFirst(headerNode ?? node, "musicDescriptionShelfRenderer"), "description");

        // Saved-to-library state: the header's bookmark toggle button carries isToggled=true when
        // the show/playlist is already in the user's collection (drives the Simpan/Tersimpan label).
        var isSaved = false;
        if (ResponseTreeSearch.FindFirst(headerNode, "toggleButtonRenderer") is JsonObject toggle
            && toggle["isToggled"] is JsonValue toggledValue
            && toggledValue.TryGetValue<bool>(out var toggled))
        {
            isSaved = toggled;
        }

        // Creator avatar: the strapline thumbnail next to the author line (2024+ responsive
        // header); falls back to an avatarViewModel (image.sources[].url) inside the header.
        Uri? authorAvatar = null;
        if (headerNode is JsonObject headerObj && headerObj["straplineThumbnail"] is JsonObject strapline)
        {
            // The strapline nests {musicThumbnailRenderer:{thumbnail:{thumbnails}}} (dump-verified);
            // BestThumbnailUrl expects to start one level up from the renderer, so unwrap first.
            authorAvatar = ParsingHelpers.BestThumbnailUrl(strapline["musicThumbnailRenderer"])
                ?? ParsingHelpers.BestThumbnailUrl(strapline);
        }

        if (authorAvatar is null
            && ResponseTreeSearch.FindFirst(headerNode, "avatarViewModel") is JsonObject avatar
            && avatar["image"] is JsonObject avatarImage
            && avatarImage["sources"] is JsonArray avatarSources)
        {
            foreach (var source in avatarSources)
            {
                if (source is JsonObject so
                    && so["url"] is JsonValue uv
                    && uv.TryGetValue<string>(out var url)
                    && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    authorAvatar = uri;
                    break;
                }
            }
        }

        // Episodes: reuse the discovery/section parser, then flatten every episode row across the
        // show's shelves in order (the show page lays its episodes out as musicResponsiveListItem
        // / multi-row renderers that PodcastParser already understands).
        var episodes = new List<PodcastEpisode>();
        try
        {
            foreach (var section in PodcastParser.ParseDiscovery(node))
            {
                foreach (var item in section.Items)
                {
                    if (item is PodcastSectionItem.EpisodeItem ep)
                    {
                        // The page title is the authoritative show name here; a row's own subtitle
                        // is the publish date on playlist pages and would leak into the player bar
                        // as the "artist".
                        episodes.Add(ep.Episode with
                        {
                            ShowTitle = title ?? ep.Episode.ShowTitle,
                            Number = episodes.Count + 1,
                        });
                    }
                }
            }

            // The first page is capped (~100 rows); follow continuations until the list is
            // exhausted so long shows/playlists load completely. Best-effort: a failing page
            // keeps what already landed.
            var token = PodcastParser.ExtractEpisodesContinuationToken(node);
            Diag.Write($"podcast show {showId}: first page episodes={episodes.Count} contToken={(token is null ? "NONE" : "yes")}");
            var pages = 0;
            while (!string.IsNullOrEmpty(token) && pages < 30)
            {
                ct.ThrowIfCancellationRequested();
                var contNode = await RequestAsync("browse", ContinuationBody(token), ttl: null, ct)
                    .ConfigureAwait(true);
                var (more, nextToken) = PodcastParser.ParseEpisodesContinuation(contNode);
                Diag.Write($"podcast show {showId}: cont page {pages + 1} episodes+={more.Count} next={(nextToken is null ? "NONE" : "yes")}");
                foreach (var episode in more)
                {
                    episodes.Add(episode with
                    {
                        ShowTitle = title ?? episode.ShowTitle,
                        Number = episodes.Count + 1,
                    });
                }

                if (more.Count == 0)
                {
                    break;
                }

                token = nextToken;
                pages++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Malformed show page / failed paging: keep whatever episodes landed rather than
            // failing the whole page.
            Diag.Write($"podcast show {showId}: paging FAILED {ex.GetType().Name}: {ex.Message}");
        }

        // Cover: strictly from the header renderer; falling back to the whole tree grabbed the
        // first episode's thumbnail instead of the show/playlist cover.
        var thumbnail = ParsingHelpers.BestThumbnailUrl(headerNode ?? node);

        Diag.Write($"podcast show {showId}: episodes={episodes.Count} title={(string.IsNullOrEmpty(title) ? "<none>" : title)}");

        return new PodcastShow
        {
            Id = showId,
            Title = string.IsNullOrEmpty(title) ? "Podcast" : title,
            Author = author,
            AuthorChannelId = authorChannelId,
            AuthorThumbnailUrl = authorAvatar,
            Description = description,
            IsSaved = isSaved,
            ThumbnailUrl = thumbnail,
            Episodes = episodes,
        };
    }

    // ── Body helpers ────────────────────────────────────────────────────────────────────

    private static JsonObject ChannelIdsBody(string channelId) =>
        new() { ["channelIds"] = new JsonArray { channelId } };

    /// <summary>Builds a <c>{ "target": { "playlistId": ... } }</c> body for like/removelike mutations.</summary>
    private static JsonObject PlaylistTargetBody(string playlistId) =>
        new() { ["target"] = new JsonObject { ["playlistId"] = playlistId } };

    private static string PrivacyStatus(PlaylistPrivacy privacy) => privacy switch
    {
        PlaylistPrivacy.Public => "PUBLIC",
        PlaylistPrivacy.Unlisted => "UNLISTED",
        _ => "PRIVATE",
    };

    // ── Minimal inline accounts parsing (TODO: dedicated AccountsListParser) ─────────────

    /// <summary>
    /// Best-effort parse of <c>account/accounts_list</c> into <see cref="UserAccount"/> rows.
    /// Walks every <c>accountItemRenderer</c> in the response; a brand account is identified by a
    /// <c>pageIdToken.pageId</c> (primary accounts have none), and the currently-active account by
    /// <c>isSelected</c>. Returns an empty list on any unexpected shape rather than throwing.
    /// </summary>
    private static IReadOnlyList<UserAccount> ParseAccounts(JsonNode? root)
    {
        if (root is null)
        {
            return Array.Empty<UserAccount>();
        }

        var accounts = new List<UserAccount>();
        foreach (var item in ResponseTreeSearch.FindAll(root, "accountItemRenderer"))
        {
            if (item is not JsonObject account)
            {
                continue;
            }

            var name = FirstRunText(account, "accountName");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var handle = FirstRunText(account, "channelHandle");
            var brandId = ExtractBrandId(account);
            var isCurrent = account.TryGetPropertyValue("isSelected", out var selected)
                && selected is JsonValue sv && sv.TryGetValue<bool>(out var b) && b;
            var avatarUrl = ExtractAccountPhoto(account);

            accounts.Add(new UserAccount(
                Name: name,
                Handle: handle,
                BrandId: brandId,
                IsPrimary: brandId is null,
                IsCurrent: isCurrent,
                AvatarUrl: avatarUrl));
        }

        return accounts;
    }

    /// <summary>
    /// Fetches the active account's display info (name + avatar + handle) from
    /// <c>account/account_menu</c>. Unlike <see cref="GetAccountsListAsync"/> (which lists brand
    /// accounts for switching) this returns the header of the currently-signed-in account, so it is
    /// the reliable source for the sidebar identity. Returns <see langword="null"/> when signed out
    /// or the response lacks an account header.
    /// </summary>
    public async Task<UserAccount?> GetAccountInfoAsync(CancellationToken ct = default)
    {
        var node = await RequestAsync("account/account_menu", new JsonObject(), ttl: null, ct)
            .ConfigureAwait(false);
        return ParseAccountInfo(node);
    }

    /// <summary>
    /// Parses the <c>activeAccountHeaderRenderer</c> (account name, photo, handle) from an
    /// <c>account/account_menu</c> response. Returns <see langword="null"/> on any unexpected shape.
    /// </summary>
    private static UserAccount? ParseAccountInfo(JsonNode? root)
    {
        if (root is null || ResponseTreeSearch.FindFirst(root, "activeAccountHeaderRenderer") is not JsonObject header)
        {
            return null;
        }

        var name = FirstRunText(header, "accountName");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return new UserAccount(
            Name: name,
            Handle: FirstRunText(header, "channelHandle"),
            BrandId: null,
            IsPrimary: true,
            IsCurrent: true,
            AvatarUrl: ExtractAccountPhoto(header));
    }

    /// <summary>
    /// Extracts the account profile photo from <c>accountPhoto.thumbnails</c>, preferring the last
    /// (largest) thumbnail. Returns <see langword="null"/> when absent or malformed. Mirrors the
    /// macOS <c>AccountsListParser</c>.
    /// </summary>
    private static Uri? ExtractAccountPhoto(JsonObject account)
    {
        if ((account["accountPhoto"] as JsonObject)?["thumbnails"] is not JsonArray thumbnails
            || thumbnails.Count == 0)
        {
            return null;
        }

        for (var i = thumbnails.Count - 1; i >= 0; i--)
        {
            if ((thumbnails[i] as JsonObject)?["url"] is JsonValue urlValue
                && urlValue.TryGetValue<string>(out var url)
                && !string.IsNullOrWhiteSpace(url)
                && Uri.TryCreate(ParsingHelpers.NormalizeUrl(url), UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    /// <summary>Extracts a brand account id from <c>serviceEndpoint…supportedTokens[].pageIdToken.pageId</c>.</summary>
    private static string? ExtractBrandId(JsonObject account)
    {
        var supportedTokens = ((account["serviceEndpoint"] as JsonObject)
            ?["selectActiveIdentityEndpoint"] as JsonObject)
            ?["supportedTokens"] as JsonArray;
        if (supportedTokens is null)
        {
            return null;
        }

        foreach (var token in supportedTokens)
        {
            if ((token as JsonObject)?["pageIdToken"] is JsonObject pageIdToken
                && pageIdToken.TryGetPropertyValue("pageId", out var pageId)
                && pageId is JsonValue pv
                && pv.TryGetValue<string>(out var id)
                && !string.IsNullOrEmpty(id))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Reads the first run's <c>text</c> from a <c>{ key: { runs: [ { text } ] } }</c> node.</summary>
    private static string? FirstRunText(JsonObject node, string key)
    {
        if ((node[key] as JsonObject)?["runs"] is JsonArray runs
            && runs.Count > 0
            && runs[0] is JsonObject first
            && first.TryGetPropertyValue("text", out var value)
            && value is JsonValue jv
            && jv.TryGetValue<string>(out var text))
        {
            return text;
        }

        return null;
    }
}
