using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the Search page (Task 14.4, Req 12.1–12.4). Coordinates three concerns over the
/// <see cref="IYTMusicClient"/>: <b>debounced</b> query execution (Req 12.2), live
/// <see cref="Suggestions"/> while typing (Req 12.3) and the grouped result surface
/// — <see cref="TopResult"/> plus per-type collections (Req 12.1). Playing a song result is
/// delegated to the shared <see cref="IPlayerService"/> (Req 12.4); navigation to album / artist /
/// playlist / podcast detail pages is performed by the view (which owns the navigation
/// <c>Frame</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Debounce mechanism.</b> Each keystroke calls <see cref="UpdateQuery(string)"/>, which restarts
/// a single ~300 ms <see cref="CancellationToken"/> + <see cref="Task.Delay(int, CancellationToken)"/>
/// timer. Only when the user pauses typing long enough for the delay to elapse without being
/// superseded do we hit the network — once for suggestions and once for the grouped search — so
/// rapid typing collapses to a single request pair (Req 12.2). <see cref="SearchImmediately(string)"/>
/// cancels any pending debounce and runs the search synchronously-from-the-caller's-perspective,
/// used when the user commits a query (Enter / chosen suggestion).
/// </para>
/// <para>
/// In-flight searches are superseded via a linked <see cref="CancellationTokenSource"/> so a newer
/// query's results always win over a slower older one. All observable state is mutated on the
/// awaiting caller's context (the UI thread for a bound page), matching <see cref="ViewModelBase"/>.
/// </para>
/// </remarks>
public sealed partial class SearchViewModel : ViewModelBase
{
    /// <summary>Debounce window applied to typing before a network request is issued (Req 12.2).</summary>
    private const int DebounceMilliseconds = 300;

    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _searchCts;

    /// <summary>Creates the Search ViewModel.</summary>
    /// <param name="client">The YouTube Music client used for search + suggestions (Req 12).</param>
    /// <param name="player">The shared player used to play a selected song result (Req 12.4).</param>
    public SearchViewModel(IYTMusicClient client, IPlayerService player)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>The current search text. Bound to the page's <c>AutoSuggestBox</c>.</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>
    /// The highlighted top result (<c>musicCardShelfRenderer</c>), or <see langword="null"/> when
    /// there is none. Changing it refreshes the derived display properties below.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTopResult))]
    [NotifyPropertyChangedFor(nameof(TopResultTitle))]
    [NotifyPropertyChangedFor(nameof(TopResultSubtitle))]
    [NotifyPropertyChangedFor(nameof(TopResultThumbnail))]
    private HomeSectionItem? _topResult;

    /// <summary><see langword="true"/> once at least one search has produced a response.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private bool _hasSearched;

    // ── Grouped result collections (Req 12.1) ───────────────────────────────────────────────────

    /// <summary>Live search suggestions for the current partial query (Req 12.3).</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    /// <summary>Song results (play on click, Req 12.4).</summary>
    public ObservableCollection<Song> Songs { get; } = [];

    /// <summary>Album results (navigate on click).</summary>
    public ObservableCollection<Album> Albums { get; } = [];

    /// <summary>Artist results (navigate on click).</summary>
    public ObservableCollection<Artist> Artists { get; } = [];

    /// <summary>Playlist results (navigate on click).</summary>
    public ObservableCollection<Playlist> Playlists { get; } = [];

    /// <summary>Podcast-show results (navigate on click).</summary>
    public ObservableCollection<Playlist> Podcasts { get; } = [];

    /// <summary>Music-video results (wide 16:9 cards; play on click).</summary>
    public ObservableCollection<Song> MusicVideos { get; } = [];

    // ── Per-type filter params (ported from the macOS reference / ytmusicapi) ────────────────────
    // Pattern: EgWKAQ (filtered) + type code + AWoMEA4QChADEAQQCRAF (no spelling correction).
    private static readonly SearchFilter SongsFilter = new("Songs", "EgWKAQIIAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter VideosFilter = new("Videos", "EgWKAQIQAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter AlbumsFilter = new("Albums", "EgWKAQIYAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter ArtistsFilter = new("Artists", "EgWKAQIgAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter PlaylistsFilter = new("Playlists", "EgWKAQIoAWoMEA4QChADEAQQCRAF");

    // ── Derived display flags / values ───────────────────────────────────────────────────────────

    /// <summary>Whether a top result is present (section visibility).</summary>
    public bool HasTopResult => TopResult is not null;

    /// <summary>Whether any song results exist (section visibility).</summary>
    public bool HasSongs => Songs.Count > 0;

    /// <summary>Whether any album results exist (section visibility).</summary>
    public bool HasAlbums => Albums.Count > 0;

    /// <summary>Whether any artist results exist (section visibility).</summary>
    public bool HasArtists => Artists.Count > 0;

    /// <summary>Whether any playlist results exist (section visibility).</summary>
    public bool HasPlaylists => Playlists.Count > 0;

    /// <summary>Whether any podcast results exist (section visibility).</summary>
    public bool HasPodcasts => Podcasts.Count > 0;

    /// <summary>Whether any music-video results exist (section visibility).</summary>
    public bool HasMusicVideos => MusicVideos.Count > 0;

    /// <summary>Whether a completed search yielded nothing (empty-state visibility).</summary>
    public bool ShowNoResults =>
        HasSearched && !HasTopResult && !HasSongs && !HasAlbums && !HasArtists && !HasPlaylists && !HasPodcasts && !HasMusicVideos;

    /// <summary>Display title for the <see cref="TopResult"/> regardless of its concrete kind.</summary>
    public string TopResultTitle => TopResult switch
    {
        HomeSectionItem.SongItem s => s.Song.Title,
        HomeSectionItem.AlbumItem a => a.Album.Title,
        HomeSectionItem.PlaylistItem p => p.Pl.Title,
        HomeSectionItem.ArtistItem ar => ar.Artist.Name,
        _ => string.Empty,
    };

    /// <summary>Human-readable kind label for the <see cref="TopResult"/> (e.g. "Song", "Artist").</summary>
    public string TopResultSubtitle => TopResult switch
    {
        HomeSectionItem.SongItem => "Song",
        HomeSectionItem.AlbumItem => "Album",
        HomeSectionItem.PlaylistItem => "Playlist",
        HomeSectionItem.ArtistItem => "Artist",
        _ => string.Empty,
    };

    /// <summary>Thumbnail for the <see cref="TopResult"/> regardless of its concrete kind.</summary>
    public Uri? TopResultThumbnail => TopResult switch
    {
        HomeSectionItem.SongItem s => s.Song.ThumbnailUrl,
        HomeSectionItem.AlbumItem a => a.Album.ThumbnailUrl,
        HomeSectionItem.PlaylistItem p => p.Pl.ThumbnailUrl,
        HomeSectionItem.ArtistItem ar => ar.Artist.ThumbnailUrl,
        _ => null,
    };

    // ── Query handling (debounce + suggestions, Req 12.2 / 12.3) ──────────────────────────────────

    /// <summary>
    /// Records a new partial <paramref name="text"/> and (re)starts the debounce timer. Called on
    /// every keystroke; only a pause longer than <see cref="DebounceMilliseconds"/> triggers the
    /// network calls, coalescing rapid typing into one request pair (Req 12.2).
    /// </summary>
    public void UpdateQuery(string text)
    {
        Query = text ?? string.Empty;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        // Empty query: clear everything immediately, no request, no pending timer.
        if (string.IsNullOrWhiteSpace(Query))
        {
            _debounceCts = null;
            _searchCts?.Cancel();
            Suggestions.Clear();
            ClearResults();
            HasSearched = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebounceAsync(cts.Token);
    }

    private async Task DebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer keystroke.
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await UpdateSuggestionsAsync(token).ConfigureAwait(true);
        await ExecuteSearchAsync(Query, token).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs the search for <paramref name="query"/> right away, bypassing the debounce timer. Used
    /// when the user commits a query (Enter / chosen suggestion) so there is no extra delay.
    /// </summary>
    public Task SearchImmediately(string query)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        Query = query ?? string.Empty;
        return ExecuteSearchAsync(Query, CancellationToken.None);
    }

    private async Task UpdateSuggestionsAsync(CancellationToken token)
    {
        var input = Query.Trim();
        if (input.Length == 0)
        {
            Suggestions.Clear();
            return;
        }

        await RunSafeAsync(async ct =>
        {
            var suggestions = await _client.GetSearchSuggestionsAsync(input, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            Suggestions.Clear();
            foreach (var suggestion in suggestions)
            {
                Suggestions.Add(suggestion);
            }
        }, token).ConfigureAwait(true);
    }

    private async Task ExecuteSearchAsync(string rawQuery, CancellationToken externalToken)
    {
        var query = (rawQuery ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            ClearResults();
            HasSearched = false;
            return;
        }

        // Supersede any older in-flight search so the latest query's results win.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _searchCts = cts;
        var token = cts.Token;

        IsLoading = true;
        try
        {
            await RunSafeAsync(async ct =>
            {
                // Base (universal) search for the top result, then per-type filtered searches in
                // parallel so every section carries a FULL list (the universal response only holds
                // a few items per shelf).
                var baseTask = _client.SearchAsync(query, filter: null, ct);
                var songsTask = _client.SearchAsync(query, SongsFilter, ct);
                var videosTask = _client.SearchAsync(query, VideosFilter, ct);
                var albumsTask = _client.SearchAsync(query, AlbumsFilter, ct);
                var artistsTask = _client.SearchAsync(query, ArtistsFilter, ct);
                var playlistsTask = _client.SearchAsync(query, PlaylistsFilter, ct);

                var response = await baseTask.ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                ApplyResults(response);
                HasSearched = true;

                // Each deep list lands as it arrives; a failed filter keeps the base list.
                await ApplyDeepAsync(songsTask, r => Replace(Songs, r.Songs), ct).ConfigureAwait(true);
                await ApplyDeepAsync(videosTask, r => Replace(MusicVideos, r.Songs), ct).ConfigureAwait(true);
                await ApplyDeepAsync(albumsTask, r => Replace(Albums, r.Albums), ct).ConfigureAwait(true);
                await ApplyDeepAsync(artistsTask, r => Replace(Artists, r.Artists), ct).ConfigureAwait(true);
                await ApplyDeepAsync(playlistsTask, r => Replace(Playlists, r.Playlists), ct).ConfigureAwait(true);
                RaiseResultFlags();
            }, token).ConfigureAwait(true);
        }
        finally
        {
            // Only the current (non-superseded) search clears the loading affordance.
            if (ReferenceEquals(_searchCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    // ── Result clicks (Req 12.4) ──────────────────────────────────────────────────────────────────

    /// <summary>Plays a selected song result, replacing the queue with that track (Req 12.4).</summary>
    public Task PlaySongAsync(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return _player.PlaySongAsync(song);
    }

    // ── Result application ────────────────────────────────────────────────────────────────────────

    /// <summary>Applies one per-type deep search; failures keep the base (universal) list.</summary>
    private static async Task ApplyDeepAsync(Task<SearchResponse> task, Action<SearchResponse> apply, CancellationToken ct)
    {
        try
        {
            var response = await task.ConfigureAwait(true);
            if (!ct.IsCancellationRequested)
            {
                apply(response);
            }
        }
        catch (Exception)
        {
            // Best-effort enrichment only.
        }
    }

    private void ApplyResults(SearchResponse response)
    {
        TopResult = response.TopResult;
        Replace(Songs, response.Songs);
        Replace(Albums, response.Albums);
        Replace(Artists, response.Artists);
        Replace(Playlists, response.Playlists);
        Replace(Podcasts, response.Podcasts);
        RaiseResultFlags();
    }

    private void ClearResults()
    {
        TopResult = null;
        Songs.Clear();
        Albums.Clear();
        Artists.Clear();
        Playlists.Clear();
        Podcasts.Clear();
        MusicVideos.Clear();
        RaiseResultFlags();
    }

    private void RaiseResultFlags()
    {
        OnPropertyChanged(nameof(HasSongs));
        OnPropertyChanged(nameof(HasAlbums));
        OnPropertyChanged(nameof(HasArtists));
        OnPropertyChanged(nameof(HasPlaylists));
        OnPropertyChanged(nameof(HasPodcasts));
        OnPropertyChanged(nameof(HasMusicVideos));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
