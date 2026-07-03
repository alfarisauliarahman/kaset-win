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

    public Task AddSongToPlaylistAsync(string videoId, string playlistId, CancellationToken ct = default)
        => AddSongError is not null ? Task.FromException(AddSongError) : Task.CompletedTask;

    public Task SubscribeArtistAsync(string channelId, CancellationToken ct = default)
        => SubscribeError is not null ? Task.FromException(SubscribeError) : Task.CompletedTask;

    public Task UnsubscribeArtistAsync(string channelId, CancellationToken ct = default)
        => SubscribeError is not null ? Task.FromException(SubscribeError) : Task.CompletedTask;

    // ── Unused members ───────────────────────────────────────────────────────────────────
    public Task<HomeResponse> GetHomeAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetHomeContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetExploreAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetChartsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetMoodsAndGenresAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetNewReleasesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<HomeResponse> GetMoodCategoryAsync(string browseId, string? categoryParams = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<LibraryContent> GetLibraryLandingAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Playlist>> GetLibraryPlaylistsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PlaylistDetail> GetLikedSongsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Song>> GetUploadedSongsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PlaylistDetail> GetPlaylistAsync(string playlistId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PlaylistDetail> GetPlaylistContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ArtistDetail> GetArtistAsync(string channelId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SearchResponse> SearchAsync(string query, SearchFilter? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string input, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SongMetadata> GetSongMetadataAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RadioQueueResult> GetRadioQueueAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RadioQueueResult> GetMixQueueAsync(string playlistId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RadioQueueResult> GetMixContinuationAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RateSongAsync(string videoId, LikeStatus rating, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SendFeedbackAsync(IReadOnlyList<string> feedbackTokens, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AddToPlaylistMenu> GetAddToPlaylistOptionsAsync(string videoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UserAccount>> GetAccountsListAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public Task<UserAccount?> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Song>> GetHistoryAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PodcastShow> GetPodcastShowAsync(string showId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PodcastsResult> GetPodcastsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task SubscribePodcastAsync(string showId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UnsubscribePodcastAsync(string showId, CancellationToken ct = default) => throw new NotSupportedException();
}
