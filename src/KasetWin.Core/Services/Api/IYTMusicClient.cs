using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// High-level YouTube Music (InnerTube) client. Builds authenticated requests via the pure
/// <see cref="InnerTubeSupport"/> helpers and delegates response parsing to the static parsers.
/// </summary>
/// <remarks>
/// The implementation (<c>YTMusicClient</c>) is a <c>sealed partial class</c>: the request
/// core, authorization headers, error mapping, and id helpers live in <c>YTMusicClient.cs</c>
/// (task 7.1), while the endpoint groups are added as partial files — browse/library/detail
/// (task 7.2), search/suggestions (task 7.3), and playback detail/mutations (task 7.4).
/// </remarks>
public interface IYTMusicClient
{
    // ── Browse (core) ───────────────────────────────────────────────────────────────────
    Task<HomeResponse> GetHomeAsync(CancellationToken ct = default);                 // FEmusic_home (Req 11)
    Task<HomeResponse> GetHomeContinuationAsync(string token, CancellationToken ct = default); // Req 11.2
    Task<HomeResponse> GetExploreAsync(CancellationToken ct = default);              // FEmusic_explore (Req 31)
    Task<HomeResponse> GetChartsAsync(CancellationToken ct = default);               // FEmusic_charts
    Task<HomeResponse> GetMoodsAndGenresAsync(CancellationToken ct = default);       // FEmusic_moods_and_genres
    Task<HomeResponse> GetNewReleasesAsync(CancellationToken ct = default);          // FEmusic_new_releases
    Task<HomeResponse> GetMoodCategoryAsync(string browseId, string? categoryParams = null, CancellationToken ct = default); // FEmusic_moods_and_genres_category (Req 31.2)

    // ── Library (core) ──────────────────────────────────────────────────────────────────
    Task<LibraryContent> GetLibraryLandingAsync(CancellationToken ct = default);     // FEmusic_library_landing (Req 13)
    Task<IReadOnlyList<Playlist>> GetLibraryPlaylistsAsync(CancellationToken ct = default); // FEmusic_liked_playlists
    Task<PlaylistDetail> GetLikedSongsAsync(CancellationToken ct = default);         // VLLM (Liked Music playlist)
    Task<IReadOnlyList<Song>> GetLibrarySongsAsync(CancellationToken ct = default);  // FEmusic_liked_videos (library saved songs)
    Task<IReadOnlyList<Song>> GetUploadedSongsAsync(CancellationToken ct = default); // FEmusic_library_privately_owned_tracks

    // ── Detail ──────────────────────────────────────────────────────────────────────────
    Task<PlaylistDetail> GetPlaylistAsync(string playlistId, CancellationToken ct = default);        // VL{id} (Req 14)
    Task<PlaylistDetail> GetPlaylistContinuationAsync(string token, CancellationToken ct = default); // Req 8.4
    Task<ArtistDetail> GetArtistAsync(string channelId, CancellationToken ct = default);             // UC{id} (Req 15)

    // ── Search (core) ───────────────────────────────────────────────────────────────────
    Task<SearchResponse> SearchAsync(string query, SearchFilter? filter = null, CancellationToken ct = default); // Req 12
    Task<SearchResponse> SearchContinuationAsync(string token, CancellationToken ct = default);                   // Req 12 (paging)
    Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string input, CancellationToken ct = default);          // Req 12.3

    // ── Now playing / radio ─────────────────────────────────────────────────────────────
    Task<SongMetadata> GetSongMetadataAsync(string videoId, CancellationToken ct = default);    // next (video type, feedback tokens)
    Task<RadioQueueResult> GetRadioQueueAsync(string videoId, CancellationToken ct = default);  // RDAMVM{videoId}
    Task<RadioQueueResult> GetMixQueueAsync(string playlistId, CancellationToken ct = default); // RDEM... (Req 25.1)

    /// <summary>Related content for a track ("Terkait"/watch-next): the "next" tabs' Related browse,
    /// parsed into home-style carousels (you-might-also-like / playlists / similar artists / …).</summary>
    Task<HomeResponse> GetSongRelatedAsync(string videoId, CancellationToken ct = default);

    /// <summary>Search-as-you-type suggestions including rich entity rows (song/artist with
    /// thumbnail + subtitle), for the sidebar search box.</summary>
    Task<IReadOnlyList<SearchSuggestion>> GetRichSearchSuggestionsAsync(string input, CancellationToken ct = default);

    /// <summary>Artists derived from the user's saved/liked songs (library corpus), each with a
    /// "N lagu" subtitle — the YT Music Library ▸ Artists list.</summary>
    Task<IReadOnlyList<Artist>> GetLibrarySongArtistsAsync(CancellationToken ct = default);

    /// <summary>Edits a playlist's metadata (name / description / privacy); only supplied fields change.</summary>
    Task EditPlaylistMetadataAsync(string playlistId, string? title = null, string? description = null, PlaylistPrivacy? privacy = null, CancellationToken ct = default);

    /// <summary>Browses an artist rail's "See all" target and classifies its items (albums/videos/playlists/artists).</summary>
    Task<ArtistSectionResult> GetArtistSectionAsync(string browseId, string artistName, CancellationToken ct = default);
    Task<RadioQueueResult> GetMixContinuationAsync(string token, CancellationToken ct = default); // Req 25.2

    // ── Mutations (core + advanced) ─────────────────────────────────────────────────────
    Task RateSongAsync(string videoId, LikeStatus rating, CancellationToken ct = default);    // like/like|dislike|removelike
    Task RatePlaylistAsync(string playlistId, LikeStatus rating, CancellationToken ct = default); // like/like|removelike for playlist/album targets
    Task SendFeedbackAsync(IReadOnlyList<string> feedbackTokens, CancellationToken ct = default); // feedback
    Task SubscribeArtistAsync(string channelId, CancellationToken ct = default);              // subscription/subscribe (Req 15.3)
    Task UnsubscribeArtistAsync(string channelId, CancellationToken ct = default);
    Task<AddToPlaylistMenu> GetAddToPlaylistOptionsAsync(string videoId, CancellationToken ct = default); // playlist/get_add_to_playlist
    Task AddSongToPlaylistAsync(string videoId, string playlistId, bool allowDuplicates = false, CancellationToken ct = default); // browse/edit_playlist (Req 13.3)
    Task<string> CreatePlaylistAsync(string title, string? description, PlaylistPrivacy privacy, IReadOnlyList<string>? videoIds, CancellationToken ct = default); // playlist/create (Req 13.2)
    Task DeletePlaylistAsync(string playlistId, CancellationToken ct = default);              // playlist/delete (Req 13.4)
    Task<IReadOnlyList<UserAccount>> GetAccountsListAsync(CancellationToken ct = default);    // account/accounts_list (brand)
    Task<UserAccount?> GetAccountInfoAsync(CancellationToken ct = default);                   // account/account_menu (active account name/photo/handle)

    // ── Advanced ────────────────────────────────────────────────────────────────────────
    Task<IReadOnlyList<Song>> GetHistoryAsync(CancellationToken ct = default);                // FEmusic_history (Req 30)
    Task<PodcastShow> GetPodcastShowAsync(string showId, CancellationToken ct = default);     // MPSPP{id} (Req 27)

    // ── Podcasts (advanced, Req 27) ─────────────────────────────────────────────────────
    Task<PodcastsResult> GetPodcastsAsync(CancellationToken ct = default);                    // FEmusic_podcasts (Req 27.1/27.2)
    Task SubscribePodcastAsync(string showId, CancellationToken ct = default);                // like/like (MPSPP→P) (Req 27.4)
    Task UnsubscribePodcastAsync(string showId, CancellationToken ct = default);              // like/removelike (MPSPP→P) (Req 27.4)

    /// <summary>The podcast creator's channel page (<c>UC…</c>): header + shelves (episodes / shows).</summary>
    Task<PodcastChannel> GetPodcastChannelAsync(string channelId, CancellationToken ct = default); // UC{id} channel browse

    /// <summary>Lists the episode's selectable caption (CC) tracks; empty when none exist.</summary>
    Task<IReadOnlyList<CaptionTrack>> GetPodcastCaptionTracksAsync(string videoId, CancellationToken ct = default); // player captionTracks

    /// <summary>
    /// Fetches the episode's captions as timed lines shaped like synced lyrics, or <c>null</c> when
    /// the video has no caption tracks. <paramref name="trackBaseUrl"/> selects a specific track
    /// (from <see cref="GetPodcastCaptionTracksAsync"/>); <c>null</c> picks creator subs, then ASR.
    /// </summary>
    Task<SyncedLyrics?> GetPodcastCaptionsAsync(string videoId, string? trackBaseUrl = null, CancellationToken ct = default); // player + timedtext (CC)

    // ── Lyrics ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// YouTube Music's own (plain-text, untimed) lyrics for a track: <c>next</c> → the "Lyrics"
    /// tab's <c>browseId</c> → <c>browse</c>. Returns <c>null</c> when the track has no lyrics tab
    /// or the response carries no usable text.
    /// </summary>
    Task<Parsers.YouTubeMusicLyrics?> GetYouTubeMusicLyricsAsync(string videoId, CancellationToken ct = default);
}
