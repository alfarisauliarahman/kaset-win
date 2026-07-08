using System.Text.Json.Nodes;
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
        var home = HomeResponseParser.Parse(node);
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

        var node = await RequestAsync("browse", BrowseBody(showId), ApiCacheTtl.Playlist, ct)
            .ConfigureAwait(false);

        // TODO(task 7.x): port a dedicated PodcastParser. For now return a minimal, crash-safe
        // show: best-effort title from any header renderer, with no episodes.
        var title = ResponseTreeSearch.FindFirst(node, "musicDetailHeaderRenderer") is { } detail
                ? ParsingHelpers.ExtractText(detail, "title")
            : ResponseTreeSearch.FindFirst(node, "musicResponsiveHeaderRenderer") is { } responsive
                ? ParsingHelpers.ExtractText(responsive, "title")
            : ResponseTreeSearch.FindFirst(node, "musicImmersiveHeaderRenderer") is { } immersive
                ? ParsingHelpers.ExtractText(immersive, "title")
                : null;

        return new PodcastShow
        {
            Id = showId,
            Title = string.IsNullOrEmpty(title) ? "Podcast" : title,
            Episodes = Array.Empty<PodcastEpisode>(),
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
