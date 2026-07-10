using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
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
    [NotifyPropertyChangedFor(nameof(TopResultThumbWidth))]
    [NotifyPropertyChangedFor(nameof(TopResultThumbHeight))]
    [NotifyPropertyChangedFor(nameof(TopResultThumbCorner))]
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

    /// <summary>Podcast-episode results (play on click; searching an episode title must hit).</summary>
    public ObservableCollection<Song> Episodes { get; } = [];

    /// <summary>
    /// Unified (unclassified) results for podcast-flavoured queries: the response shelves verbatim,
    /// in YT's own order and grouping. When populated, the typed sections stay empty/hidden.
    /// </summary>
    public ObservableCollection<SearchUnifiedSection> UnifiedSections { get; } = [];

    /// <summary>Whether the current results are presented unified (podcast-flavoured query).</summary>
    [ObservableProperty]
    private bool _isUnifiedResults;

    /// <summary>Whether any unified sections exist (section visibility).</summary>
    public bool HasUnifiedSections => UnifiedSections.Count > 0;

    // ── Per-type filter params (ported from the macOS reference / ytmusicapi) ────────────────────
    // Pattern: EgWKAQ (filtered) + type code + AWoMEA4QChADEAQQCRAF (no spelling correction).
    private static readonly SearchFilter SongsFilter = new("Songs", "EgWKAQIIAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter VideosFilter = new("Videos", "EgWKAQIQAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter AlbumsFilter = new("Albums", "EgWKAQIYAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter ArtistsFilter = new("Artists", "EgWKAQIgAWoMEA4QChADEAQQCRAF");
    private static readonly SearchFilter PlaylistsFilter = new("Playlists", "EgWKAQIoAWoMEA4QChADEAQQCRAF");

    // Podcast-show filter (type code JQ). From the macOS reference; distinct trailing token.
    private static readonly SearchFilter PodcastsFilter = new("Podcasts", "EgWKAQJQAWoQEBAQCRAEEAMQBRAKEBUQEQ==");

    // Episode filter (type code JI, same trailing token as podcasts — ytmusicapi's "episodes").
    private static readonly SearchFilter EpisodesFilter = new("Episodes", "EgWKAQJIAWoQEBAQCRAEEAMQBRAKEBUQEQ==");

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

    /// <summary>Whether any podcast-episode results exist (section visibility).</summary>
    public bool HasEpisodes => Episodes.Count > 0;

    /// <summary>Whether a completed search yielded nothing (empty-state visibility).</summary>
    public bool ShowNoResults =>
        HasSearched && !HasTopResult && !HasSongs && !HasAlbums && !HasArtists && !HasPlaylists && !HasPodcasts && !HasMusicVideos && !HasEpisodes && !HasUnifiedSections;

    /// <summary>Display title for the <see cref="TopResult"/> regardless of its concrete kind.</summary>
    public string TopResultTitle => TopResult switch
    {
        HomeSectionItem.SongItem s => s.Song.Title,
        HomeSectionItem.AlbumItem a => a.Album.Title,
        HomeSectionItem.PlaylistItem p => p.Pl.Title,
        HomeSectionItem.ArtistItem ar => ar.Artist.Name,
        _ => string.Empty,
    };

    /// <summary>
    /// Human-readable kind label for the <see cref="TopResult"/> (e.g. "Song", "Artist"). An artist
    /// appends its monthly-listener / subscriber count when the search payload carried one; a video
    /// top result reads "Music Video".
    /// </summary>
    public string TopResultSubtitle => TopResult switch
    {
        HomeSectionItem.SongItem s => s.Song.VideoType == MusicVideoType.Omv ? "Music Video" : "Song",
        HomeSectionItem.AlbumItem => "Album",
        HomeSectionItem.PlaylistItem => "Playlist",
        HomeSectionItem.ArtistItem ar => ar.Artist.HasSubtitle ? $"Artist • {ar.Artist.SubtitleText}" : "Artist",
        _ => string.Empty,
    };

    // ── Adaptive Top-Result cover geometry ───────────────────────────────────────────────────────
    // Video → 16:9 wide; artist → circle; album / song / playlist → square (Apple-Music-style).

    /// <summary>Cover width for the Top-Result card, adapted to the result kind.</summary>
    public double TopResultThumbWidth => IsTopResultVideo ? 156d : 104d;

    /// <summary>Cover height for the Top-Result card, adapted to the result kind.</summary>
    public double TopResultThumbHeight => IsTopResultVideo ? 88d : 104d;

    /// <summary>Cover corner radius — a full circle for artists, a rounded square otherwise.</summary>
    public CornerRadius TopResultThumbCorner =>
        TopResult is HomeSectionItem.ArtistItem ? new CornerRadius(52) : new CornerRadius(8);

    private bool IsTopResultVideo =>
        TopResult is HomeSectionItem.SongItem s && s.Song.VideoType == MusicVideoType.Omv;

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
                // Podcast-flavoured queries are shown UNIFIED: the base response's shelves verbatim
                // (YT's own order/grouping, like the web), with no per-type classification and no
                // filtered enrichment. Everything else keeps the typed sections.
                if (IsPodcastQuery(query))
                {
                    // The universal response from this client is nearly empty (web gets full
                    // shelves — under investigation via the search-base.json dump), so the unified
                    // page is composed from the type searches that ARE complete, in YT-like order.
                    var unifiedBaseTask = _client.SearchAsync(query, filter: null, ct);
                    var unifiedPodcastsTask = _client.SearchAsync(query, PodcastsFilter, ct);
                    var unifiedEpisodesTask = _client.SearchAsync(query, EpisodesFilter, ct);
                    var unifiedPlaylistsTask = _client.SearchAsync(query, PlaylistsFilter, ct);
                    // The suggestion engine is the ONLY surface that reliably resolves the query to
                    // the right entity (the search endpoints answer this client with unrelated
                    // results — see the search-*.json dumps), so its entity rows lead the page.
                    var unifiedSuggestionsTask = _client.GetRichSearchSuggestionsAsync(query, ct);

                    var unified = await unifiedBaseTask.ConfigureAwait(true);
                    ct.ThrowIfCancellationRequested();
                    // The universal response from this client is garbage for podcast queries (dump
                    // verdict: profiles/random playlists, target show absent) while the FILTERED
                    // searches are accurate — so the accurate sections lead and the universal list
                    // trails as "Lainnya". Sequential awaits keep the display order deterministic
                    // (the requests themselves already run concurrently).
                    StartUnifiedMode(unified.TopResult);
                    HasSearched = true;

                    await AppendUnifiedSuggestionEntitiesAsync(unifiedSuggestionsTask).ConfigureAwait(true);
                    await AppendUnifiedFilteredAsync("Podcasts", unifiedPodcastsTask,
                        r => r.Podcasts.Select(p => (HomeSectionItem)new HomeSectionItem.PlaylistItem(p)), ct).ConfigureAwait(true);
                    await AppendUnifiedFilteredAsync("Episodes", unifiedEpisodesTask,
                        r => r.Songs.Select(s => (HomeSectionItem)new HomeSectionItem.SongItem(s)), ct).ConfigureAwait(true);
                    await AppendUnifiedFilteredAsync("Playlists", unifiedPlaylistsTask,
                        r => r.Playlists.Select(p => (HomeSectionItem)new HomeSectionItem.PlaylistItem(p)), ct).ConfigureAwait(true);
                    AppendUnifiedBaseSections(unified);
                    return;
                }

                IsUnifiedResults = false;
                UnifiedSections.Clear();

                // Base (universal) search for the top result, then per-type filtered searches in
                // parallel so every section carries a FULL list (the universal response only holds
                // a few items per shelf).
                var baseTask = _client.SearchAsync(query, filter: null, ct);
                var songsTask = _client.SearchAsync(query, SongsFilter, ct);
                var videosTask = _client.SearchAsync(query, VideosFilter, ct);
                var albumsTask = _client.SearchAsync(query, AlbumsFilter, ct);
                var artistsTask = _client.SearchAsync(query, ArtistsFilter, ct);
                var playlistsTask = _client.SearchAsync(query, PlaylistsFilter, ct);
                var podcastsTask = _client.SearchAsync(query, PodcastsFilter, ct);
                var episodesTask = _client.SearchAsync(query, EpisodesFilter, ct);

                var response = await baseTask.ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                ApplyResults(response);
                HasSearched = true;

                // Each deep list lands as it arrives, then pages a few continuations to pull as many
                // items as the shelf offers; a failed filter keeps the base list. The sections load
                // CONCURRENTLY — sequential awaits made the last sections (podcasts/episodes) appear
                // many seconds after the first, which read as "no results" while scrolling.
                await Task.WhenAll(
                    LoadFilteredAsync(songsTask, Songs, r => r.Songs, s => s.VideoId, ct),
                    LoadFilteredAsync(videosTask, MusicVideos, r => r.Songs, s => s.VideoId, ct),
                    LoadFilteredAsync(albumsTask, Albums, r => r.Albums, a => a.Id, ct),
                    LoadFilteredAsync(artistsTask, Artists, r => r.Artists, a => a.Id, ct),
                    LoadFilteredAsync(playlistsTask, Playlists, r => r.Playlists, p => p.Id, ct),
                    LoadFilteredAsync(podcastsTask, Podcasts, r => r.Podcasts, p => p.Id, ct),
                    // Filtered episode rows classify as songs (they carry a videoId), so pick r.Songs.
                    LoadFilteredAsync(episodesTask, Episodes, r => r.Songs, s => s.VideoId, ct)).ConfigureAwait(true);
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

    /// <summary>Maximum continuation pages fetched per filtered section (keeps paging bounded).</summary>
    private const int MaxContinuationPages = 4;

    /// <summary>
    /// Applies one per-type filtered search and then follows up to <see cref="MaxContinuationPages"/>
    /// continuation pages, appending de-duplicated items so each section carries as many results as
    /// the shelf offers. Failures keep whatever landed (best-effort); a superseded search stops early.
    /// </summary>
    private async Task LoadFilteredAsync<T>(
        Task<SearchResponse> initialTask,
        ObservableCollection<T> target,
        Func<SearchResponse, IReadOnlyList<T>> pick,
        Func<T, string> key,
        CancellationToken ct)
    {
        try
        {
            var response = await initialTask.ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // NEVER clear here: the collection already holds the BASE (universal) search results —
            // the exact relevance-ordered data YT Music web shows. The filtered search only knows
            // its own type shelf (e.g. "community playlists" excludes podcast playlists), so
            // clearing silently erased base hits the user could see moments earlier. Keep base
            // items first, then append the filtered extras deduplicated.
            var seen = new HashSet<string>(target.Select(key), StringComparer.Ordinal);

            void AddAll(IReadOnlyList<T> items)
            {
                foreach (var item in items)
                {
                    if (seen.Add(key(item)))
                    {
                        target.Add(item);
                    }
                }
            }

            AddAll(pick(response));
            // Reveal this section as soon as its first page lands, instead of waiting for every
            // other filter + all continuations to finish (which left the page looking empty).
            RaiseResultFlags();

            var token = response.ContinuationToken;
            var pages = 0;
            while (!string.IsNullOrEmpty(token) && pages < MaxContinuationPages && !ct.IsCancellationRequested)
            {
                var next = await _client.SearchContinuationAsync(token, ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                AddAll(pick(next));
                token = next.ContinuationToken;
                pages++;
            }
        }
        catch (Exception)
        {
            // Best-effort enrichment / paging only.
        }
    }

    /// <summary>A query is podcast-flavoured when it mentions podcasts (any language variant).</summary>
    private static bool IsPodcastQuery(string query) =>
        query.Contains("podcast", StringComparison.OrdinalIgnoreCase)
        || query.Contains("podkast", StringComparison.OrdinalIgnoreCase)
        || query.Contains("siniar", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cross-section dedupe for the unified page (by underlying item id).</summary>
    private readonly HashSet<string> _unifiedSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// Appends one filtered search as its own unified section, deduplicated against everything the
    /// page already shows. Best-effort — a failed filter contributes nothing.
    /// </summary>
    private async Task AppendUnifiedFilteredAsync(
        string title,
        Task<SearchResponse> task,
        Func<SearchResponse, IEnumerable<HomeSectionItem>> pick,
        CancellationToken ct)
    {
        try
        {
            var response = await task.ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var rows = pick(response)
                .Select(SearchRow.FromItem)
                .Where(r => r is not null)
                .Cast<SearchRow>()
                .Where(r => _unifiedSeen.Add(r.Item.Id))
                .ToList();
            if (rows.Count > 0)
            {
                UnifiedSections.Add(new SearchUnifiedSection(title, rows));
                OnPropertyChanged(nameof(HasUnifiedSections));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
        catch (Exception)
        {
            // Best-effort enrichment only.
        }
    }

    /// <summary>Enters unified mode (podcast queries): clears the typed sections so their UI collapses.</summary>
    private void StartUnifiedMode(HomeSectionItem? topResult)
    {
        TopResult = topResult;
        Replace(Songs, []);
        Replace(Albums, []);
        Replace(Artists, []);
        Replace(Playlists, []);
        Replace(Podcasts, []);
        MusicVideos.Clear();
        Episodes.Clear();

        UnifiedSections.Clear();
        _unifiedSeen.Clear();
        IsUnifiedResults = true;
        OnPropertyChanged(nameof(HasUnifiedSections));
        RaiseResultFlags();
    }

    /// <summary>
    /// Leads the unified page with the suggestion engine's ENTITY rows ("Teratas") — podcasts,
    /// playlists, artists, albums, videos it resolved for the query. Best-effort.
    /// </summary>
    private async Task AppendUnifiedSuggestionEntitiesAsync(Task<IReadOnlyList<SearchSuggestion>> task)
    {
        try
        {
            var suggestions = await task.ConfigureAwait(true);
            var rows = new List<SearchRow>();
            foreach (var s in suggestions)
            {
                if (!s.IsRich)
                {
                    continue;
                }

                HomeSectionItem? item = null;
                if (!string.IsNullOrEmpty(s.VideoId))
                {
                    item = new HomeSectionItem.SongItem(new Song
                    {
                        Id = s.VideoId!,
                        VideoId = s.VideoId!,
                        Title = s.Query,
                        ThumbnailUrl = s.ThumbnailUrl,
                    });
                }
                else if (!string.IsNullOrEmpty(s.BrowseId))
                {
                    var browseId = s.BrowseId!;
                    item = browseId.StartsWith("UC", StringComparison.Ordinal)
                        ? new HomeSectionItem.ArtistItem(new Artist
                        {
                            Id = browseId,
                            Name = s.Query,
                            ThumbnailUrl = s.ThumbnailUrl,
                            SubtitleText = s.Subtitle,
                        })
                        : browseId.StartsWith("MPRE", StringComparison.Ordinal)
                            ? new HomeSectionItem.AlbumItem(new Album
                            {
                                Id = browseId,
                                Title = s.Query,
                                ThumbnailUrl = s.ThumbnailUrl,
                            })
                            : new HomeSectionItem.PlaylistItem(new Playlist
                            {
                                Id = browseId,
                                Title = s.Query,
                                ThumbnailUrl = s.ThumbnailUrl,
                                Author = string.IsNullOrEmpty(s.Subtitle)
                                    ? null
                                    : new Artist { Id = string.Empty, Name = s.Subtitle! },
                            });
                }

                if (item is not null
                    && SearchRow.FromItem(item) is { } row
                    && _unifiedSeen.Add(row.Item.Id))
                {
                    rows.Add(row);
                }
            }

            if (rows.Count > 0)
            {
                UnifiedSections.Add(new SearchUnifiedSection("Teratas", rows));
                OnPropertyChanged(nameof(HasUnifiedSections));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
        catch (Exception)
        {
            // Best-effort lead section only.
        }
    }

    /// <summary>
    /// Appends the universal response's rows as a trailing "Lainnya" section, deduplicated against
    /// the accurate filtered sections that lead the page.
    /// </summary>
    private void AppendUnifiedBaseSections(SearchResponse response)
    {
        var rows = response.Sections
            .SelectMany(s => s.Items)
            .Select(SearchRow.FromItem)
            .Where(r => r is not null)
            .Cast<SearchRow>()
            .Where(r => _unifiedSeen.Add(r.Item.Id))
            .ToList();
        if (rows.Count > 0)
        {
            UnifiedSections.Add(new SearchUnifiedSection("Lainnya", rows));
            OnPropertyChanged(nameof(HasUnifiedSections));
            OnPropertyChanged(nameof(ShowNoResults));
        }

        KasetWin.Core.Diag.Write(
            $"unified search: respSections=[{string.Join(" | ", response.Sections.Select(s => $"{s.Title}({s.Items.Count})"))}] ui={UnifiedSections.Count}");
    }

    private void ApplyResults(SearchResponse response)
    {
        TopResult = response.TopResult;
        Replace(Songs, response.Songs);
        Replace(Albums, response.Albums);
        Replace(Artists, response.Artists);
        Replace(Playlists, response.Playlists);
        Replace(Podcasts, response.Podcasts);
        // These two have no base-search source, so a fresh search must reset them here (the
        // filtered loaders no longer clear — they append onto whatever the base search seeded).
        MusicVideos.Clear();
        Episodes.Clear();
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
        Episodes.Clear();
        UnifiedSections.Clear();
        IsUnifiedResults = false;
        OnPropertyChanged(nameof(HasUnifiedSections));
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
        OnPropertyChanged(nameof(HasEpisodes));
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

/// <summary>One unified-search shelf: YT's shelf title plus its rows, verbatim order.</summary>
public sealed record SearchUnifiedSection(string Title, IReadOnlyList<SearchRow> Rows);

/// <summary>
/// A display row of the unified search list. Wraps the underlying <see cref="HomeSectionItem"/>
/// (activation routes through <c>FeedNavigation</c>) with uniform title / subtitle / thumbnail.
/// </summary>
public sealed record SearchRow(HomeSectionItem Item, string Title, string? Subtitle, Uri? Thumbnail)
{
    /// <summary>Builds the display row for <paramref name="item"/>; null for unrenderable items.</summary>
    public static SearchRow? FromItem(HomeSectionItem item) => item switch
    {
        HomeSectionItem.SongItem s => new SearchRow(
            item, s.Song.Title, s.Song.ArtistsDisplay, s.Song.ThumbnailUrl),
        HomeSectionItem.AlbumItem a => new SearchRow(
            item, a.Album.Title, "Album", a.Album.ThumbnailUrl),
        HomeSectionItem.ArtistItem ar => new SearchRow(
            item, ar.Artist.Name, ar.Artist.SubtitleText ?? "Artist", ar.Artist.ThumbnailUrl),
        HomeSectionItem.PlaylistItem p => new SearchRow(
            item, p.Pl.Title, p.Pl.Author?.Name, p.Pl.ThumbnailUrl),
        _ => null,
    };
}
