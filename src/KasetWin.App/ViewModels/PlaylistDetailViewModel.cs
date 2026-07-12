using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.App.Navigation;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Backs both the <see cref="Views.PlaylistPage"/> and the <see cref="Views.AlbumPage"/>
/// (Task 14.6, Req 14.1â€“14.4). An album is treated as a playlist-detail surface: it is fetched
/// through <see cref="IYTMusicClient.GetPlaylistAsync"/> using the album browseId
/// (<c>MPRE.../OLAK...</c>), which the client resolves without mutation, while a bare playlist id
/// receives the <c>VL</c> browse prefix (Req 14.2).
/// </summary>
/// <remarks>
/// <para>
/// The ViewModel loads playlist/album metadata and its first page of tracks, exposes play affordances
/// that load the tracks into the queue via <see cref="IPlayerService.PlayCollectionAsync"/> (Req 14.4),
/// pages in additional tracks through <see cref="IYTMusicClient.GetPlaylistContinuationAsync"/>
/// (Req 8.4), and â€” only for a playlist owned by the user (<see cref="Playlist.IsOwnedByUser"/>, and
/// never for an album) â€” offers a delete affordance backed by
/// <see cref="IYTMusicClient.DeletePlaylistAsync"/> (Req 14.3).
/// </para>
/// <para>
/// Tracks keep stable identity (<see cref="Song.Id"/> == videoId) so the bound list virtualizes
/// without needless container churn (Req 16.1). All loads route through
/// <see cref="ViewModelBase.LoadAsync(string, Func{System.Threading.CancellationToken, Task}, System.Threading.CancellationToken)"/>
/// so re-entrant navigation coalesces into a single in-flight request (Req 16.3).
/// </para>
/// </remarks>
public sealed partial class PlaylistDetailViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;
    private readonly IQueueService? _queue;
    private readonly Notifications.IInAppNotifier? _notifier;
    private readonly ILikeStateStore? _likeStore;

    private string? _browseId;
    private string? _likePlaylistId;
    private string? _continuationToken;
    private Artist? _author;
    private Album? _albumContext;

    /// <summary>Creates the ViewModel from the DI-resolved client and player (resolved by the page).</summary>
    public PlaylistDetailViewModel(
        IYTMusicClient client,
        IPlayerService player,
        IQueueService? queue = null,
        Notifications.IInAppNotifier? notifier = null,
        ILikeStateStore? likeStore = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _queue = queue;
        _notifier = notifier;
        _likeStore = likeStore;
    }

    /// <summary>
    /// Overlays the session-remembered like/collection state onto the loaded tracks so a like made
    /// earlier in the session (on this or another page) is still reflected after navigating back,
    /// even if the server response for this reload didn't carry per-track like state.
    /// </summary>
    /// <summary>Re-applies session like state to the loaded tracks (call when the store changes).</summary>
    public void RefreshLikeOverlay() => OverlayLikeStates();

    /// <summary>Share target for the loaded playlist/album (title + public URL), or null pre-load.</summary>
    public Core.Services.Sharing.ShareTarget? ShareTarget =>
        string.IsNullOrEmpty(_browseId)
            ? null
            : Core.Services.Sharing.ShareUrlBuilder.TryCreate(new Playlist { Id = _browseId, Title = Title ?? "Playlist" });

    private void OverlayLikeStates()
    {
        if (_likeStore is null)
        {
            return;
        }

        for (var i = 0; i < Tracks.Count; i++)
        {
            var track = Tracks[i];
            if (string.IsNullOrEmpty(track.VideoId) || !_likeStore.TryGet(track.VideoId, out var status))
            {
                continue;
            }

            var liked = status == LikeStatus.Like;
            Tracks[i] = track with
            {
                LikeStatus = status == LikeStatus.Indifferent ? (LikeStatus?)null : status,
                IsInLibrary = liked,
            };
        }
    }

    /// <summary>
    /// Surfaces a short message both as the inline page status and as a global in-app toast (the
    /// little pop near the sidebar), so every action gives feedback in one place.
    /// </summary>
    private void Notify(string message) => ActionStatus = message;

    /// <summary>Every status update also fires the global in-app toast (feedback for all actions).</summary>
    partial void OnActionStatusChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _notifier?.Show(value);
        }
    }

    /// <summary>The tracks of the playlist/album, in order. Stable identity via <see cref="Song.Id"/>.</summary>
    public ObservableCollection<Song> Tracks { get; } = [];

    /// <summary>Playlist/album title shown in the header.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>The author/artist display line shown in the header, or <c>null</c> when unknown.</summary>
    [ObservableProperty]
    private string? _authorDisplay;

    /// <summary>
    /// The author/artist channel id (<c>UCâ€¦</c>) backing the clickable header link (Task 30.1,
    /// Req 37.1), or <c>null</c> when the source did not carry one. Ids are never fabricated.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthorLink))]
    [NotifyPropertyChangedFor(nameof(AuthorIsPlain))]
    [NotifyCanExecuteChangedFor(nameof(NavigateToArtistCommand))]
    private string? _authorId;

    /// <summary>
    /// Whether the header artist name should render as a clickable artist link (Req 37.1). True only
    /// for a real navigable channel id (<c>UCâ€¦</c>/<c>MPLAUCâ€¦</c>) â€” the playlist parser may fall back
    /// to a synthetic author id when no link exists, and those must stay non-interactive.
    /// </summary>
    public bool HasAuthorLink => ParsingHelpers.IsNavigableArtistId(AuthorId);

    /// <summary>Whether the header artist name should render as plain, non-interactive text.</summary>
    public bool AuthorIsPlain => !HasAuthorLink;

    /// <summary>The header artwork, or <c>null</c> when unavailable.</summary>
    [ObservableProperty]
    private Uri? _thumbnailUrl;

    /// <summary>The artist avatar shown next to the album artist name when the header carries one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtistThumbnail))]
    private Uri? _artistThumbnailUrl;

    public bool HasArtistThumbnail => ArtistThumbnailUrl is not null;

    /// <summary>Album/single/EP label from the YouTube Music header, when present.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentType))]
    private string? _contentTypeDisplay;

    public bool HasContentType => !string.IsNullOrWhiteSpace(ContentTypeDisplay);

    /// <summary>Release date/year text from the header, formatted defensively when parseable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseDate))]
    private string? _releaseDateDisplay;

    public bool HasReleaseDate => !string.IsNullOrWhiteSpace(ReleaseDateDisplay);

    /// <summary>Album description, shown only when YouTube Music sends one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>True only when the collapsed description is actually clipped (set by the view via
    /// <c>TextBlock.IsTextTrimmed</c>), so "Selengkapnya" appears only when the text really overflows.</summary>
    [ObservableProperty]
    private bool _canExpandDescription;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionStatus))]
    private string? _actionStatus;

    public bool HasActionStatus => !string.IsNullOrWhiteSpace(ActionStatus);

    /// <summary>Whether this album/playlist is currently saved to the user's collection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollectionButtonLabel))]
    private bool _isInCollection;

    /// <summary>Label for the header collection button, toggling with <see cref="IsInCollection"/>.</summary>
    public string CollectionButtonLabel => IsInCollection ? Localization.UiStrings.CollectionButtonLabelRemove : Localization.UiStrings.CollectionButtonLabelAdd;

    /// <summary>The "N songs" summary line for the header.</summary>
    [ObservableProperty]
    private string? _trackCountDisplay;

    /// <summary>
    /// Total running time of the loaded tracks, e.g. "1 jam 12 mnt" or "43 mnt", shown in the header.
    /// Reflects only tracks currently loaded (grows as continuation pages are paged in).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlbumDuration))]
    private string? _albumDurationDisplay;

    public bool HasAlbumDuration => !string.IsNullOrWhiteSpace(AlbumDurationDisplay);

    /// <summary>Whether the current surface is an album (suppresses the delete affordance, Req 14.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isAlbum;

    /// <summary>Whether the playlist is owned by the user (drives the delete affordance, Req 14.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isOwnedByUser;

    /// <summary>Whether the album/playlist header carries an explicit badge (shown beside the title).</summary>
    [ObservableProperty]
    private bool _isHeaderExplicit;

    /// <summary>Whether another page of tracks can be loaded via continuation (Req 8.4).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    private bool _hasMore;

    /// <summary>
    /// True only when the surface is a user-owned playlist (never an album): the delete affordance
    /// is shown exclusively in that case (Req 14.3).
    /// </summary>
    public bool CanDelete => IsOwnedByUser && !IsAlbum;

    /// <summary>True when there is a continuation page available to load (Req 8.4).</summary>
    public bool CanLoadMore => HasMore;

    /// <summary>
    /// Raised after the owned playlist is deleted so the hosting page can navigate back (Req 14.3).
    /// </summary>
    public event EventHandler? Deleted;

    /// <summary>
    /// Loads a playlist surface for <paramref name="playlistId"/> (a browseId or bare playlist id).
    /// </summary>
    public Task LoadPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(playlistId);
        IsAlbum = false;
        _browseId = playlistId;
        return LoadAsync($"playlist:{playlistId}", c => LoadDetailAsync(playlistId, c), ct);
    }

    /// <summary>
    /// Loads an album surface for <paramref name="albumBrowseId"/> (an <c>MPRE.../OLAK...</c> browseId),
    /// treated as a playlist-detail fetch (Req 14.2).
    /// </summary>
    public Task LoadAlbumAsync(string albumBrowseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(albumBrowseId);
        IsAlbum = true;
        _browseId = albumBrowseId;
        return LoadAsync($"album:{albumBrowseId}", c => LoadDetailAsync(albumBrowseId, c), ct);
    }

    /// <summary>
    /// Whether the loaded surface turned out to be a podcast playlist (episode rows the track
    /// parser cannot represent) — the page should reroute to the podcast show surface.
    /// </summary>
    public bool IsPodcastPlaylist { get; private set; }

    private async Task LoadDetailAsync(string browseId, CancellationToken ct)
    {
        var detail = await _client.GetPlaylistAsync(browseId, ct).ConfigureAwait(true);

        IsPodcastPlaylist = detail.IsPodcastPlaylist;
        _likePlaylistId = detail.LikePlaylistId;
        Title = detail.Playlist.Title;
        _author = detail.Playlist.Author;
        AuthorDisplay = detail.Playlist.Author?.Name;
        AuthorId = detail.Playlist.Author?.Id;
        ThumbnailUrl = detail.Playlist.ThumbnailUrl;
        ArtistThumbnailUrl = detail.Playlist.Author?.ThumbnailUrl;
        ContentTypeDisplay = detail.Playlist.ContentType ?? (IsAlbum ? "Album" : "Playlist");
        ReleaseDateDisplay = detail.Playlist.ReleaseDateText;
        Description = detail.Playlist.Description;
        CanExpandDescription = false; // re-measured by the view once collapsed text is laid out
        IsInCollection = false; // server state unknown on load; reflects user actions this session
        IsOwnedByUser = detail.Playlist.IsOwnedByUser;
        IsHeaderExplicit = detail.Playlist.IsExplicit;
        _albumContext = IsAlbum
            ? new Album
            {
                Id = browseId,
                Title = detail.Playlist.Title,
                Artists = detail.Playlist.Author is null ? [] : [detail.Playlist.Author],
                ThumbnailUrl = detail.Playlist.ThumbnailUrl,
                ReleaseDateText = detail.Playlist.ReleaseDateText,
                ContentType = detail.Playlist.ContentType,
                Description = detail.Playlist.Description,
            }
            : null;
        OnPropertyChanged(nameof(CurrentAlbum));

        Tracks.Clear();
        foreach (var track in detail.Tracks)
        {
            Tracks.Add(WithPlaylistContext(track, detail.Playlist.Author, _albumContext));
        }

        OverlayLikeStates();

        _continuationToken = detail.ContinuationToken;
        HasMore = !string.IsNullOrEmpty(_continuationToken);
        UpdateTrackCountDisplay(detail.Playlist.TrackCount);
        UpdateDurationDisplay();
    }

    /// <summary>
    /// Navigates to the header artist's page when a channel id is known (Task 30.1, Req 37.1).
    /// Disabled (and a no-op) when no author id is present, so the link only lights up for real ids.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasAuthorLink))]
    private void NavigateToArtist() => NavigationHelper.NavigateToArtist(AuthorId);

    /// <summary>Plays the whole collection from the top, loading it into the queue (Req 14.4, 8.1/8.2).</summary>
    [RelayCommand]
    private Task PlayAllAsync()
    {
        return Tracks.Count == 0 ? Task.CompletedTask : _player.PlayCollectionAsync([.. Tracks], startIndex: 0);
    }

    /// <summary>
    /// Plays the collection starting at <paramref name="song"/>, loading it into the queue (Req 14.4).
    /// </summary>
    [RelayCommand]
    private Task PlayTrackAsync(Song? song)
    {
        if (song is null)
        {
            return Task.CompletedTask;
        }

        var index = Tracks.IndexOf(song);
        return index < 0 ? Task.CompletedTask : _player.PlayCollectionAsync([.. Tracks], startIndex: index);
    }

    public Album? CurrentAlbum => _albumContext;

    [RelayCommand]
    private Task ShufflePlayAsync()
    {
        if (Tracks.Count == 0)
        {
            return Task.CompletedTask;
        }

        var shuffled = Tracks.OrderBy(_ => Random.Shared.Next()).ToList();
        ActionStatus = Localization.UiStrings.ToastShufflingAlbum;
        return _player.PlayCollectionAsync(shuffled, startIndex: 0);
    }

    [RelayCommand]
    private async Task StartMixAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_browseId))
        {
            return;
        }

        await RunSafeAsync(async c =>
        {
            var mix = await _client.GetMixQueueAsync(_browseId, c).ConfigureAwait(true);
            if (mix.Songs.Count == 0)
            {
                ActionStatus = Localization.UiStrings.ToastMixUnavailableAlbum;
                return;
            }

            ActionStatus = Localization.UiStrings.ToastStartingMix;
            await _player.PlayCollectionAsync(mix.Songs).ConfigureAwait(true);
        }, ct).ConfigureAwait(true);
    }

    /// <summary>Queues every loaded track right after the current one ("Putar setelah ini").</summary>
    [RelayCommand]
    private void PlayAllNext()
    {
        if (_queue is null || Tracks.Count == 0)
        {
            ActionStatus = Localization.UiStrings.ToastQueueUnavailable;
            return;
        }

        var added = _queue.InsertNext(Tracks);
        ActionStatus = added == 0 ? Localization.UiStrings.ToastAllAlreadyQueued : Localization.UiStrings.ToastCountPlayedNext(added);
    }

    [RelayCommand]
    private void AddAlbumToQueue()
    {
        if (_queue is null || Tracks.Count == 0)
        {
            ActionStatus = Localization.UiStrings.ToastQueueUnavailable;
            return;
        }

        var added = _queue.AppendDeduplicated(Tracks);
        ActionStatus = added == 0 ? Localization.UiStrings.ToastAllAlreadyQueued : Localization.UiStrings.ToastCountAddedToQueue(added);
    }

    [RelayCommand]
    private Task RateTrackAsync(SongRatingRequest? request)
    {
        if (request?.Song is null || string.IsNullOrEmpty(request.Song.VideoId))
        {
            return Task.CompletedTask;
        }

        // Optimistic: reflect the rating in the UI/store immediately (so like⇄collection light up
        // instantly and stay in sync), then persist. Revert if the server rejects it.
        var song = request.Song;
        var previous = song.LikeStatus ?? LikeStatus.Indifferent;
        ReplaceTrackLikeStatus(song, request.Rating);
        ActionStatus = request.Rating switch
        {
            LikeStatus.Like => Localization.UiStrings.ToastLiked(song.Title),
            LikeStatus.Dislike => Localization.UiStrings.ToastDisliked(song.Title),
            _ => Localization.UiStrings.ToastUnliked(song.Title),
        };

        return RunSafeAsync(async c =>
        {
            try
            {
                await _client.RateSongAsync(song.VideoId, request.Rating, c).ConfigureAwait(true);
            }
            catch (Exception)
            {
                ReplaceTrackLikeStatus(song, previous);
                Notify(Localization.UiStrings.ToastLikeFailed);
            }
        });
    }

    [RelayCommand]
    private void OpenTrackArtist(Song? song) => NavigationHelper.NavigateToSongArtist(song);

    /// <summary>Appends a single track to the play queue ("Tambahkan ke antrean").</summary>
    [RelayCommand]
    private void AddTrackToQueue(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (_queue is null)
        {
            ActionStatus = Localization.UiStrings.ToastQueueUnavailable;
            return;
        }

        var added = _queue.AppendDeduplicated([song]);
        ActionStatus = added == 0 ? Localization.UiStrings.ToastSongAlreadyQueued : Localization.UiStrings.ToastAddedToQueue(song.Title);
    }

    /// <summary>Queues a single track to play right after the current one ("Putar setelah ini").</summary>
    [RelayCommand]
    private void PlayTrackNext(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (_queue is null)
        {
            ActionStatus = Localization.UiStrings.ToastQueueUnavailable;
            return;
        }

        var added = _queue.InsertNext([song]);
        ActionStatus = added == 0 ? Localization.UiStrings.ToastSongAlreadyQueued : Localization.UiStrings.ToastPlayingNext(song.Title);
    }

    /// <summary>
    /// Toggles a track's presence in the user's collection ("Simpan ke koleksi" ⇄ "Hapus dari
    /// koleksi"). The available mutation is the song rating, so saving likes the track (which places
    /// it in the Liked-songs library) and removing clears it; the row's <c>IsInLibrary</c> flag is
    /// updated so the menu label flips.
    /// </summary>
    [RelayCommand]
    private Task ToggleTrackCollectionAsync(Song? song)
    {
        if (song is null || string.IsNullOrEmpty(song.VideoId))
        {
            return Task.CompletedTask;
        }

        var inCollection = song.IsInLibrary == true || song.LikeStatus == LikeStatus.Like;
        var rating = inCollection ? LikeStatus.Indifferent : LikeStatus.Like;

        // Optimistic (same rationale as RateTrackAsync): flip the collection/like state now, persist
        // after, revert on failure.
        ReplaceTrackCollection(song, added: !inCollection);
        ActionStatus = inCollection
            ? Localization.UiStrings.ToastRemovedFromCollection(song.Title)
            : Localization.UiStrings.ToastSavedToCollection(song.Title);

        return RunSafeAsync(async c =>
        {
            try
            {
                await _client.RateSongAsync(song.VideoId, rating, c).ConfigureAwait(true);
            }
            catch (Exception)
            {
                ReplaceTrackCollection(song, added: inCollection);
                Notify(Localization.UiStrings.ToastCollectionFailed);
            }
        });
    }

    /// <summary>
    /// The user's library playlists offered as save targets ("Simpan ke playlist"). Owned playlists
    /// only, so items can actually be added; empty when signed out or none exist.
    /// </summary>
    public async Task<IReadOnlyList<Playlist>> GetSaveTargetsAsync(string? sampleVideoId = null, CancellationToken ct = default)
    {
        try
        {
            // Prefer the add-to-playlist menu for a real track: it returns exactly the playlists the
            // song can be added to (the correct, editable set). Fall back to the library playlists
            // (unfiltered — the ownership flag isn't populated for library tiles, which is what made
            // the picker come up empty before).
            if (!string.IsNullOrEmpty(sampleVideoId))
            {
                var menu = await _client.GetAddToPlaylistOptionsAsync(sampleVideoId, ct).ConfigureAwait(true);
                if (menu.Playlists.Count > 0)
                {
                    return menu.Playlists;
                }
            }

            var playlists = await _client.GetLibraryPlaylistsAsync(ct).ConfigureAwait(true);
            return [.. playlists.Where(p => !string.IsNullOrEmpty(p.Id))];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The first loaded track's videoId, used to seed the add-to-playlist menu for the album.</summary>
    public string? FirstTrackVideoId => Tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.VideoId))?.VideoId;

    /// <summary>All loaded track videoIds (for saving the whole album to a playlist).</summary>
    public IReadOnlyList<string> AllTrackVideoIds =>
        [.. Tracks.Where(t => !string.IsNullOrEmpty(t.VideoId)).Select(t => t.VideoId)];

    /// <summary>Whether <paramref name="videoId"/> is already present in the target playlist.</summary>
    public async Task<bool> IsTrackInPlaylistAsync(string videoId, string playlistId)
    {
        try
        {
            var detail = await _client.GetPlaylistAsync(playlistId, CancellationToken.None).ConfigureAwait(true);
            return detail.Tracks.Any(t => string.Equals(t.VideoId, videoId, StringComparison.Ordinal));
        }
        catch
        {
            return false; // if we can't check, don't block the add
        }
    }

    /// <summary>Creates a new playlist seeded with <paramref name="videoIds"/> and reports the result.</summary>
    public Task CreatePlaylistWithTracksAsync(string name, IReadOnlyList<string> videoIds)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        return RunSafeAsync(async c =>
        {
            await _client.CreatePlaylistAsync(name, null, PlaylistPrivacy.Private, videoIds, c).ConfigureAwait(true);
            Notify(Localization.UiStrings.ToastPlaylistCreated(name));
        });
    }

    /// <summary>Pushes an actionable notification (with a button) through the global notifier.</summary>
    public void NotifyAction(string message, string actionText, Action onAction) =>
        _notifier?.Show(new Notifications.InAppNotification(message, ActionText: actionText, OnAction: onAction));

    /// <summary>Adds a single track to the chosen library playlist.</summary>
    public Task SaveTrackToPlaylistAsync(Song song, string playlistId, bool allowDuplicates = false)
    {
        if (song is null || string.IsNullOrEmpty(song.VideoId) || string.IsNullOrEmpty(playlistId))
        {
            return Task.CompletedTask;
        }

        return AddSingleToPlaylistAsync(song.VideoId, song.Title, playlistId, allowDuplicates);
    }

    /// <summary>Adds one track to a playlist, reporting both success and failure (never silent).</summary>
    private async Task AddSingleToPlaylistAsync(string videoId, string title, string playlistId, bool allowDuplicates)
    {
        try
        {
            await _client.AddSongToPlaylistAsync(videoId, playlistId, allowDuplicates, CancellationToken.None).ConfigureAwait(true);
            Notify(Localization.UiStrings.ToastAddedToPlaylist(title));
        }
        catch (Exception ex)
        {
            // Never silent — surface any failure (not just KasetError) so "Tetap Tambahkan" always
            // gives feedback instead of swallowing the error in an async void handler.
            Notify(Localization.UiStrings.ToastAddToPlaylistFailed(ex.Message));
        }
    }

    /// <summary>Adds every loaded album/playlist track to the chosen library playlist.</summary>
    public Task SaveAlbumToPlaylistAsync(string playlistId)
    {
        if (string.IsNullOrEmpty(playlistId) || Tracks.Count == 0)
        {
            return Task.CompletedTask;
        }

        var videoIds = Tracks.Where(t => !string.IsNullOrEmpty(t.VideoId)).Select(t => t.VideoId).ToList();
        return RunSafeAsync(async c =>
        {
            var added = 0;
            var failed = 0;
            foreach (var videoId in videoIds)
            {
                try
                {
                    await _client.AddSongToPlaylistAsync(videoId, playlistId, allowDuplicates: false, c).ConfigureAwait(true);
                    added++;
                }
                catch (Exception)
                {
                    // Keep going on a single failed track so one bad row doesn't abort the whole
                    // album save (the earlier version stopped silently, leaving no notification).
                    failed++;
                }
            }

            Notify(failed == 0
                ? Localization.UiStrings.ToastSavedCount(added)
                : Localization.UiStrings.ToastSavedCountWithFailures(added, failed));
        });
    }

    [RelayCommand]
    private async Task ToggleAlbumCollectionAsync()
    {
        // The like/like endpoint rejects an album's MPRE… browseId with HTTP 400; the correct target
        // is the album's audio-playlist id (OLAK…) parsed from the detail response. Fall back to the
        // browseId only when the response carried no likeable id.
        var target = !string.IsNullOrEmpty(_likePlaylistId) ? _likePlaylistId : _browseId;
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        var removing = IsInCollection;
        var rating = removing ? LikeStatus.Indifferent : LikeStatus.Like;

        // Report success and failure explicitly (the base RunSafeAsync routes errors to ErrorMessage,
        // which this page doesn't surface) so the button is never silent.
        try
        {
            await _client.RatePlaylistAsync(target, rating, CancellationToken.None).ConfigureAwait(true);
            IsInCollection = !removing;
            Notify(removing ? Localization.UiStrings.ToastRemovedFromCollectionShort : Localization.UiStrings.ToastAddedToCollectionShort);
        }
        catch (Core.Errors.KasetError ex)
        {
            Notify(Localization.UiStrings.ToastGenericFailed(ex.Message));
        }
    }

    [RelayCommand]
    private void ShowUnavailable(string? action)
    {
        ActionStatus = string.IsNullOrWhiteSpace(action)
            ? Localization.UiStrings.ToastFeatureUnavailable
            : Localization.UiStrings.ToastActionUnavailable(action);
    }

    /// <summary>Loads the next page of tracks via the continuation token and appends them (Req 8.4).</summary>
    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken ct)
    {
        var token = _continuationToken;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        await RunSafeAsync(async c =>
        {
            var page = await _client.GetPlaylistContinuationAsync(token, c).ConfigureAwait(true);
            foreach (var track in page.Tracks)
            {
                Tracks.Add(WithPlaylistContext(track, _author, _albumContext));
            }

            OverlayLikeStates();

            _continuationToken = page.ContinuationToken;
            HasMore = !string.IsNullOrEmpty(_continuationToken);
            UpdateTrackCountDisplay(Tracks.Count);
            UpdateDurationDisplay();
        }, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes the owned playlist and signals the page to navigate back (Req 14.3). No-op for albums
    /// or playlists the user does not own.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct)
    {
        if (!CanDelete || string.IsNullOrEmpty(_browseId))
        {
            return;
        }

        var deleted = false;
        await RunSafeAsync(async c =>
        {
            await _client.DeletePlaylistAsync(_browseId, c).ConfigureAwait(true);
            deleted = true;
        }, ct).ConfigureAwait(true);

        if (deleted)
        {
            Deleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Fills a track's missing artist from the album/playlist author. Album track rows on YouTube
    /// Music usually omit the per-track artist (it lives in the header), so without this the track
    /// list â€” and the queue / now-playing once played â€” would show only the title. Only applied when
    /// the track has no artist of its own, so playlists with real per-track artists are untouched.
    /// </summary>
    private static Song WithPlaylistContext(Song track, Artist? author, Album? album)
    {
        if (track.Artists.Count == 0 && author is not null)
        {
            track = track with { Artists = [author] };
        }

        return track.Album is null && album is not null
            ? track with { Album = album }
            : track;
    }

    private void UpdateTrackCountDisplay(int? metadataCount)
    {
        // Prefer the loaded count once tracks are present; fall back to the metadata-reported count.
        var count = Tracks.Count > 0 ? Tracks.Count : metadataCount ?? 0;
        TrackCountDisplay = Localization.UiStrings.TrackCountText(count);
    }

    /// <summary>Sums the durations of the loaded tracks into a friendly header line (Indonesian).</summary>
    private void UpdateDurationDisplay()
    {
        var total = TimeSpan.Zero;
        foreach (var track in Tracks)
        {
            if (track.Duration is { } d)
            {
                total += d;
            }
        }

        if (total <= TimeSpan.Zero)
        {
            AlbumDurationDisplay = null;
            return;
        }

        var hours = (int)total.TotalHours;
        var minutes = total.Minutes;
        AlbumDurationDisplay = hours > 0 ? $"{hours} jam {minutes} menit" : $"{minutes} menit";
    }

    /// <summary>
    /// Reflects a like/dislike/remove locally by swapping the track record in-place, so the row's
    /// bound like state updates without a reload (the mutation itself already hit the server).
    /// </summary>
    private void ReplaceTrackLikeStatus(Song song, LikeStatus rating)
    {
        // like == love == collection: a thumb-up also puts the track in the library so the row's
        // collection affordance stays in sync (and vice versa); a dislike/remove clears both.
        _likeStore?.Set(song.VideoId, rating);

        var index = IndexOfVideo(song.VideoId);
        if (index < 0)
        {
            return;
        }

        var newStatus = rating == LikeStatus.Indifferent ? (LikeStatus?)null : rating;
        Tracks[index] = Tracks[index] with
        {
            LikeStatus = newStatus,
            IsInLibrary = rating == LikeStatus.Like,
        };
    }

    /// <summary>Index of the first loaded track with the given videoId, or -1 (records are swapped
    /// in place, so lookups must be by id rather than reference identity).</summary>
    private int IndexOfVideo(string? videoId)
    {
        if (string.IsNullOrEmpty(videoId))
        {
            return -1;
        }

        for (var i = 0; i < Tracks.Count; i++)
        {
            if (string.Equals(Tracks[i].VideoId, videoId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Reflects a collection add/remove locally by flipping the track's library flag.</summary>
    private void ReplaceTrackCollection(Song song, bool added)
    {
        _likeStore?.Set(song.VideoId, added ? LikeStatus.Like : LikeStatus.Indifferent);

        var index = IndexOfVideo(song.VideoId);
        if (index < 0)
        {
            return;
        }

        Tracks[index] = Tracks[index] with
        {
            IsInLibrary = added,
            LikeStatus = added ? LikeStatus.Like : (LikeStatus?)null,
        };
    }
}

public sealed record SongRatingRequest(Song Song, LikeStatus Rating);
