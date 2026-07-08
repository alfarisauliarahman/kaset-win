using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Library;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services;

namespace KasetWin.App.ViewModels;

/// <summary>The Library filter chips (Req 13.5).</summary>
public enum LibraryFilter
{
    /// <summary>Show every collection.</summary>
    All,

    /// <summary>Playlists only.</summary>
    Playlists,

    /// <summary>Liked + uploaded songs only.</summary>
    Songs,

    /// <summary>Followed artists only.</summary>
    Artists,

    /// <summary>Albums only.</summary>
    Albums,
}

/// <summary>
/// ViewModel for the Library page (Task 14.5, Req 13). Loads the user's playlists, liked songs,
/// followed artists, and uploaded songs (Req 13.1), filters them by chip (Req 13.5), and performs
/// the create/add-song/delete mutations (Req 13.2/13.3/13.4) through the pure Core
/// <see cref="LibraryContentReconciler"/> + <see cref="LibraryMutationActions"/> so every change is
/// applied <b>optimistically</b>, reconciled against a fresh backend snapshot, and rolled back on
/// failure (Req 13.6/13.7).
/// </summary>
/// <remarks>
/// The single source of truth is the immutable <see cref="LibraryState"/> held in
/// <see cref="_state"/>; the observable collections are identity-preserving projections of it
/// (Req 16.1). Albums are tracked separately because they are display-only here (no album mutation
/// on this surface).
/// </remarks>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private const string LoadKey = "library";

    private readonly IYTMusicClient _client;
    private readonly IPlayerService? _player;
    private readonly LibraryMutationActions _actions;

    private LibraryState _state = LibraryState.Empty;

    /// <summary>Creates the ViewModel.</summary>
    /// <param name="client">InnerTube client used for fetches and mutations (required).</param>
    /// <param name="player">Optional player for "play" affordances on songs.</param>
    /// <param name="singleFlight">Optional shared single-flight (see <see cref="ViewModelBase"/>).</param>
    public LibraryViewModel(IYTMusicClient client, IPlayerService? player = null, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _player = player;
        _actions = new LibraryMutationActions(client);
    }

    /// <summary>The user's playlists (Req 13.1).</summary>
    public ObservableCollection<Playlist> Playlists { get; } = [];

    /// <summary>The user's albums (Req 13.1, display-only here).</summary>
    public ObservableCollection<Album> Albums { get; } = [];

    /// <summary>Artists the user follows (Req 13.1).</summary>
    public ObservableCollection<Artist> Artists { get; } = [];

    /// <summary>The user's liked songs (Req 13.1).</summary>
    public ObservableCollection<Song> LikedSongs { get; } = [];

    /// <summary>The user's uploaded songs (Req 13.1).</summary>
    public ObservableCollection<Song> UploadedSongs { get; } = [];

    /// <summary>The active filter chip (Req 13.5).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaylists))]
    [NotifyPropertyChangedFor(nameof(ShowSongs))]
    [NotifyPropertyChangedFor(nameof(ShowArtists))]
    [NotifyPropertyChangedFor(nameof(ShowAlbums))]
    private LibraryFilter _selectedFilter = LibraryFilter.All;

    /// <summary>Title for a new playlist (bound to the create text box, Req 13.2).</summary>
    [ObservableProperty]
    private string _newPlaylistTitle = string.Empty;

    /// <summary>The playlist a liked/uploaded song is added to (Req 13.3).</summary>
    [ObservableProperty]
    private Playlist? _selectedPlaylist;

    /// <summary>Whether the playlists section is visible under the active filter.</summary>
    public bool ShowPlaylists => SelectedFilter is LibraryFilter.All or LibraryFilter.Playlists;

    /// <summary>Whether the songs sections are visible under the active filter.</summary>
    public bool ShowSongs => SelectedFilter is LibraryFilter.All or LibraryFilter.Songs;

    /// <summary>Whether the artists section is visible under the active filter.</summary>
    public bool ShowArtists => SelectedFilter is LibraryFilter.All or LibraryFilter.Artists;

    /// <summary>Whether the albums section is visible under the active filter.</summary>
    public bool ShowAlbums => SelectedFilter is LibraryFilter.All or LibraryFilter.Albums;

    /// <summary>Loads (or refreshes) the whole library surface (Req 13.1).</summary>
    [RelayCommand]
    public Task RefreshAsync(CancellationToken ct = default) =>
        LoadAsync(LoadKey, async token =>
        {
            var landing = await _client.GetLibraryLandingAsync(token).ConfigureAwait(true);

            // Liked + uploaded songs are best-effort: a failure on one secondary surface must not
            // blank the whole page (the primary landing already loaded).
            var liked = await TryGetAsync(t => _client.GetLikedSongsAsync(t), token).ConfigureAwait(true);
            var uploaded = await TryGetListAsync(t => _client.GetUploadedSongsAsync(t), token).ConfigureAwait(true);

            // Artists come from the saved-songs corpus (with "N lagu" subtitles) rather than the
            // landing shelf, which lists channel subscriptions; fall back to the landing on failure.
            var corpusArtists = await TryGetListAsync(t => _client.GetLibrarySongArtistsAsync(t), token).ConfigureAwait(true);

            _state = new LibraryState
            {
                Playlists = landing.Playlists,
                FollowedArtists = corpusArtists is { Count: > 0 } ? corpusArtists : landing.Artists,
                LikedSongs = liked?.Tracks ?? [],
                UploadedSongs = uploaded ?? [],
            };

            SyncCollection(Albums, landing.Albums, static a => a.Id);
            SyncFromState();
        }, ct);

    /// <summary>
    /// Sets the active filter chip (Req 13.5). Accepts the <see cref="LibraryFilter"/> name as a
    /// string so it can be passed directly as a XAML <c>CommandParameter</c>; an unrecognised value
    /// falls back to <see cref="LibraryFilter.All"/>.
    /// </summary>
    [RelayCommand]
    private void SetFilter(string? filter) =>
        SelectedFilter = Enum.TryParse<LibraryFilter>(filter, ignoreCase: true, out var parsed)
            ? parsed
            : LibraryFilter.All;

    /// <summary>Creates a new playlist optimistically (Req 13.2/13.6/13.7).</summary>
    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        var title = NewPlaylistTitle?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        // Optimistic placeholder shown immediately; the orchestrator swaps in the real id on success.
        var optimistic = new Playlist
        {
            Id = "PL__optimistic__" + Guid.NewGuid().ToString("N"),
            Title = title,
            IsOwnedByUser = true,
            TrackCount = 0,
        };

        await RunMutationAsync(new CreatePlaylistMutation(optimistic, title)).ConfigureAwait(true);
        NewPlaylistTitle = string.Empty;
    }

    /// <summary>Deletes a playlist the user owns (Req 13.4/13.6/13.7).</summary>
    [RelayCommand]
    private Task DeletePlaylistAsync(Playlist? playlist)
    {
        // Only owned playlists expose the delete affordance (Req 14.3); guard the command too.
        if (playlist is null || !playlist.IsOwnedByUser)
        {
            return Task.CompletedTask;
        }

        return RunMutationAsync(new DeletePlaylistMutation(playlist.Id));
    }

    /// <summary>Adds a song to the <see cref="SelectedPlaylist"/> (Req 13.3/13.6/13.7).</summary>
    [RelayCommand]
    private Task AddSongToPlaylistAsync(Song? song)
    {
        if (song is null || SelectedPlaylist is null)
        {
            return Task.CompletedTask;
        }

        return RunMutationAsync(new AddSongToPlaylistMutation(SelectedPlaylist.Id, song));
    }

    /// <summary>Adds <paramref name="song"/> to the playlist chosen from the picker (Req 13.3).</summary>
    public Task AddSongToPlaylistAsync(Song song, string playlistId)
    {
        if (song is null || string.IsNullOrEmpty(playlistId))
        {
            return Task.CompletedTask;
        }

        return RunMutationAsync(new AddSongToPlaylistMutation(playlistId, song));
    }

    /// <summary>Plays a single song (Req 8 convenience for liked/uploaded lists).</summary>
    [RelayCommand]
    private Task PlaySongAsync(Song? song)
    {
        // THROWAWAY DIAGNOSTIC (Bug A): confirm a Library liked/uploaded "Play" button reaches the
        // player and that the optional player resolved non-null on this surface.
        return song is null || _player is null ? Task.CompletedTask : _player.PlaySongAsync(song);
    }

    /// <summary>
    /// Runs a mutation through the optimistic-update / reconcile / rollback orchestration. The
    /// <c>publish</c> callback mirrors each produced <see cref="LibraryState"/> into the observable
    /// collections; a fresh landing fetch supplies the reconciliation snapshot (Req 13.6).
    /// </summary>
    private Task RunMutationAsync(LibraryMutation mutation) =>
        RunSafeAsync(ct => _actions.ExecuteAsync(
            _state,
            mutation,
            publish: state =>
            {
                _state = state;
                SyncFromState();
            },
            fetchSnapshot: async token =>
            {
                var landing = await _client.GetLibraryLandingAsync(token).ConfigureAwait(true);
                return _state with
                {
                    Playlists = landing.Playlists,
                    FollowedArtists = landing.Artists,
                };
            },
            ct));

    private void SyncFromState()
    {
        SyncCollection(Playlists, _state.Playlists, static p => p.Id);
        SyncCollection(Artists, _state.FollowedArtists, static a => a.Id);
        SyncCollection(LikedSongs, _state.LikedSongs, static s => s.Id);
        SyncCollection(UploadedSongs, _state.UploadedSongs, static s => s.Id);
    }

    /// <summary>
    /// In-place synchronisation of an observable collection to a target sequence, keyed by stable
    /// identity so existing containers are preserved/moved rather than recreated (Req 16.1).
    /// Items whose value changed (e.g. a playlist track-count bump) are replaced in position.
    /// </summary>
    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, string> key)
    {
        var sourceKeys = new HashSet<string>(source.Select(key), StringComparer.Ordinal);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!sourceKeys.Contains(key(target[i])))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var k = key(item);

            if (i < target.Count && string.Equals(key(target[i]), k, StringComparison.Ordinal))
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    target[i] = item;
                }

                continue;
            }

            var existing = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (string.Equals(key(target[j]), k, StringComparison.Ordinal))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                target.Move(existing, i);
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    target[i] = item;
                }
            }
            else
            {
                target.Insert(i, item);
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static async Task<PlaylistDetail?> TryGetAsync(Func<CancellationToken, Task<PlaylistDetail>> get, CancellationToken ct)
    {
        try
        {
            return await get(ct).ConfigureAwait(true);
        }
        catch (Core.Errors.KasetError)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<T>?> TryGetListAsync<T>(Func<CancellationToken, Task<IReadOnlyList<T>>> get, CancellationToken ct)
    {
        try
        {
            return await get(ct).ConfigureAwait(true);
        }
        catch (Core.Errors.KasetError)
        {
            return null;
        }
    }
}
