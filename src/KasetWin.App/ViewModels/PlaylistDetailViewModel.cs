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

    private string? _browseId;
    private string? _continuationToken;
    private Artist? _author;
    private Album? _albumContext;

    /// <summary>Creates the ViewModel from the DI-resolved client and player (resolved by the page).</summary>
    public PlaylistDetailViewModel(IYTMusicClient client, IPlayerService player)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
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

    /// <summary>The "N songs" summary line for the header.</summary>
    [ObservableProperty]
    private string? _trackCountDisplay;

    /// <summary>Whether the current surface is an album (suppresses the delete affordance, Req 14.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isAlbum;

    /// <summary>Whether the playlist is owned by the user (drives the delete affordance, Req 14.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isOwnedByUser;

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

    private async Task LoadDetailAsync(string browseId, CancellationToken ct)
    {
        var detail = await _client.GetPlaylistAsync(browseId, ct).ConfigureAwait(true);

        Title = detail.Playlist.Title;
        _author = detail.Playlist.Author;
        AuthorDisplay = detail.Playlist.Author?.Name;
        AuthorId = detail.Playlist.Author?.Id;
        ThumbnailUrl = detail.Playlist.ThumbnailUrl;
        IsOwnedByUser = detail.Playlist.IsOwnedByUser;
        _albumContext = IsAlbum
            ? new Album
            {
                Id = browseId,
                Title = detail.Playlist.Title,
                Artists = detail.Playlist.Author is null ? [] : [detail.Playlist.Author],
                ThumbnailUrl = detail.Playlist.ThumbnailUrl,
            }
            : null;

        Tracks.Clear();
        foreach (var track in detail.Tracks)
        {
            Tracks.Add(WithPlaylistContext(track, detail.Playlist.Author, _albumContext));
        }

        _continuationToken = detail.ContinuationToken;
        HasMore = !string.IsNullOrEmpty(_continuationToken);
        UpdateTrackCountDisplay(detail.Playlist.TrackCount);
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
        // THROWAWAY DIAGNOSTIC (Bug A): confirm the header "Play" button reaches the player.
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

            _continuationToken = page.ContinuationToken;
            HasMore = !string.IsNullOrEmpty(_continuationToken);
            UpdateTrackCountDisplay(Tracks.Count);
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
        TrackCountDisplay = count == 1 ? "1 song" : $"{count} songs";
    }
}
