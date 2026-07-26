using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;

namespace KasetWin.Core.Tests;

/// <summary>
/// Minimal in-memory <see cref="IYTMusicClient"/> test double used by the Library mutation
/// orchestration tests (task 14.5). Only the mutation endpoints exercised by those tests are
/// implemented; every other member throws so accidental use is obvious.
/// </summary>
internal sealed class FakeYTMusicClient : IYTMusicClient
{
    /// <summary>Playlist id returned by <see cref="CreatePlaylistAsync"/>.</summary>
    public string CreatedPlaylistId { get; init; } = "PL_created";

    /// <summary>When set, <see cref="CreatePlaylistAsync"/> throws it.</summary>
    public KasetError? CreateError { get; init; }

    /// <summary>When set, <see cref="DeletePlaylistAsync"/> throws it.</summary>
    public KasetError? DeleteError { get; init; }

    /// <summary>When set, <see cref="AddSongToPlaylistAsync"/> throws it.</summary>
    public KasetError? AddSongError { get; init; }

    /// <summary>When set, the subscribe/unsubscribe calls throw it.</summary>
    public KasetError? SubscribeError { get; init; }

    public Task<string> CreatePlaylistAsync(string title, string? description, PlaylistPrivacy privacy, IReadOnlyList<string>? videoIds, CancellationToken ct = default)
        => CreateError is not null ? Task.FromException<string>(CreateError) : Task.FromResult(CreatedPlaylistId);

    public Task DeletePlaylistAsync(string playlistId, CancellationToken ct = default)
        => DeleteError is not null ? Task.FromException(DeleteError) : Task.CompletedTask;

    public Task AddSongToPlaylistAsync(string videoId, string playlistId, bool allowDuplicates = false, CancellationToken ct = default)
        => AddSongError is not null ? Task.FromException(AddSongError) : Task.CompletedTask;

    public Task SubscribeArtistAsync(string channelId, CancellationToken ct = default)
        => SubscribeError is not null ? Task.FromException(SubscribeError) : Task.CompletedTask;

    public Task UnsubscribeArtistAsync(string channelId, CancellationToken ct = default)
        => SubscribeError is not null ? Task.FromException(SubscribeError) : Task.CompletedTask;

    // ── Unused members ───────────────────────────────────────────────────────────────────
    public Task<HomeResponse> GetHomeAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetHomeContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetExploreAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ChartsPage> GetChartsAsync(string? countryCode = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetMoodsAndGenresAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetNewReleasesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetMoodCategoryAsync(string browseId, string? categoryParams = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<LibraryContent> GetLibraryLandingAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Playlist>> GetLibraryPlaylistsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PlaylistDetail> GetLikedSongsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Song>> GetLibrarySongsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Song>> GetUploadedSongsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PlaylistDetail> GetPlaylistAsync(string playlistId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PlaylistDetail> GetPlaylistContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ArtistDetail> GetArtistAsync(string channelId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SearchResponse> SearchAsync(string query, SearchFilter? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SearchResponse> SearchContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string input, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SongMetadata> GetSongMetadataAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RadioQueueResult> GetRadioQueueAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RadioQueueResult> GetMixQueueAsync(string playlistId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<HomeResponse> GetSongRelatedAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<SearchSuggestion>> GetRichSearchSuggestionsAsync(string input, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<Artist>> GetLibrarySongArtistsAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public Task EditPlaylistMetadataAsync(string playlistId, string? title = null, string? description = null, PlaylistPrivacy? privacy = null, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ArtistSectionResult> GetArtistSectionAsync(string browseId, string artistName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RadioQueueResult> GetMixContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RateSongAsync(string videoId, LikeStatus rating, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RatePlaylistAsync(string playlistId, LikeStatus rating, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SendFeedbackAsync(IReadOnlyList<string> feedbackTokens, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AddToPlaylistMenu> GetAddToPlaylistOptionsAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UserAccount>> GetAccountsListAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public Task<UserAccount?> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Song>> GetHistoryAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PodcastShow> GetPodcastShowAsync(string showId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PodcastsResult> GetPodcastsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task SubscribePodcastAsync(string showId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UnsubscribePodcastAsync(string showId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PodcastChannel> GetPodcastChannelAsync(string channelId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<CaptionTrack>> GetPodcastCaptionTracksAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SyncedLyrics?> GetPodcastCaptionsAsync(string videoId, string? trackBaseUrl = null, CancellationToken ct = default) => throw new NotSupportedException();
    /// <summary>
    /// Stubs <see cref="GetYouTubeMusicLyricsAsync"/>. When left <c>null</c> the call throws like
    /// every other unused member; set it (per videoId) in the YouTube Music lyrics tests.
    /// </summary>
    public Func<string, KasetWin.Core.Services.Api.Parsers.YouTubeMusicLyrics?>? YouTubeMusicLyrics { get; init; }

    /// <summary>Number of times <see cref="GetYouTubeMusicLyricsAsync"/> was invoked.</summary>
    public int YouTubeMusicLyricsCalls;

    public Task<KasetWin.Core.Services.Api.Parsers.YouTubeMusicLyrics?> GetYouTubeMusicLyricsAsync(string videoId, CancellationToken ct = default)
    {
        if (YouTubeMusicLyrics is null)
        {
            throw new NotSupportedException();
        }

        Interlocked.Increment(ref YouTubeMusicLyricsCalls);
        return Task.FromResult(YouTubeMusicLyrics(videoId));
    }
}
