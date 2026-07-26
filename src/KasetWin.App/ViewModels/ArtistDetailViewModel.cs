using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the Artist detail surface (Task 14.7, Req 15.1â€“15.4).
/// </summary>
/// <remarks>
/// <para>
/// Loads an artist via <see cref="IYTMusicClient.GetArtistAsync(string, CancellationToken)"/> and
/// projects the <see cref="ArtistDetail"/> into bindable header fields plus three rails â€” Top songs,
/// Albums, and Singles &amp; EPs (Req 15.1). Playing from the page loads songs into the queue through
/// <see cref="IPlayerService.PlayCollectionAsync"/> (Req 15.4). Follow/unfollow toggles the
/// subscription optimistically and reconciles on failure (Req 15.3). The "See all" browseIds are
/// surfaced so the page can navigate to the full list for a rail when one exists (Req 15.2).
/// </para>
/// <para>
/// Navigation itself lives in the page code-behind (it owns the <c>Frame</c>); this ViewModel only
/// exposes the data and the play/subscribe commands so it stays free of any WinUI dependency beyond
/// the MVVM toolkit.
/// </para>
/// </remarks>
public sealed partial class ArtistDetailViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;
    private readonly IQueueService? _queue;
    private readonly Notifications.IInAppNotifier? _notifier;

    private string? _channelId;

    /// <summary>
    /// The authoritative channel id for the subscribe/unsubscribe mutation, taken from the artist
    /// header's subscribe button (Bug 4). Falls back to the browse id (<see cref="_channelId"/>)
    /// when the header carries none, mirroring the macOS client. Using the browse id alone is what
    /// produced the HTTP 400.
    /// </summary>
    private string? _subscribeChannelId;

    /// <summary>Creates the ViewModel with the data client and player resolved from DI.</summary>
    public ArtistDetailViewModel(
        IYTMusicClient client,
        IPlayerService player,
        ISingleFlight? singleFlight = null,
        IQueueService? queue = null,
        Notifications.IInAppNotifier? notifier = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _queue = queue;
        _notifier = notifier;
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
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.InsertNext([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastPlayingNext(song.Title));
    }

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
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.AppendDeduplicated([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastAddedToQueue(song.Title));
    }

    // â”€â”€ Header (Req 15.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Uri? _thumbnailUrl;

    /// <summary>Wide banner / cover image shown behind the artist name (YT-Music-style header).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeaderImage))]
    private Uri? _headerImageUrl;

    /// <summary>True when a header banner image is available to show.</summary>
    public bool HasHeaderImage => HeaderImageUrl is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(CanExpandDescription))]
    private string? _description;

    /// <summary>True when there is a non-empty artist description to show.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Subscriber count line (e.g. "1.2M subscribers").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetaLine))]
    [NotifyPropertyChangedFor(nameof(MetaLine))]
    private string? _subscriberText;

    /// <summary>Monthly-listeners line (e.g. "93.2M monthly listeners").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetaLine))]
    [NotifyPropertyChangedFor(nameof(MetaLine))]
    private string? _monthlyListenersText;

    /// <summary>
    /// The single metadata line shown under the artist name. Prefers monthly listeners (YT Music's
    /// primary metric) and falls back to the subscriber count.
    /// </summary>
    public string? MetaLine => !string.IsNullOrWhiteSpace(MonthlyListenersText)
        ? MonthlyListenersText
        : SubscriberText;

    /// <summary>True when there is a monthly-listeners / subscriber line to show.</summary>
    public bool HasMetaLine => !string.IsNullOrWhiteSpace(MetaLine);

    // â”€â”€ Description expander (Req 15.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionMaxLines))]
    [NotifyPropertyChangedFor(nameof(DescriptionToggleLabel))]
    private bool _isDescriptionExpanded;

    /// <summary>Collapsed shows ~3 lines; expanded removes the cap.</summary>
    public int DescriptionMaxLines => IsDescriptionExpanded ? 0 : 3;

    /// <summary>Toggle label: "Show more" when collapsed, "Show less" when expanded.</summary>
    public string DescriptionToggleLabel => IsDescriptionExpanded ? "Ringkas" : "Selengkapnya";

    /// <summary>
    /// True only when the collapsed description is actually clipped (measured by the view via
    /// <c>TextBlock.IsTextTrimmed</c>), so "Show more" appears only when the text really overflows —
    /// never for a short description that already fits.
    /// </summary>
    [ObservableProperty]
    private bool _canExpandDescription;

    /// <summary>Toggles the description between collapsed (~3 lines) and fully expanded.</summary>
    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    // â”€â”€ Subscription toggle (Req 15.3) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeLabel))]
    [NotifyPropertyChangedFor(nameof(SubscribeGlyph))]
    private bool _isSubscribed;

    /// <summary>Subscribe/Subscribed label that reflects <see cref="IsSubscribed"/> (YouTube Music wording).</summary>
    public string SubscribeLabel => IsSubscribed ? Localization.UiStrings.FollowingLabel : Localization.UiStrings.FollowLabel;

    /// <summary>Segoe Fluent glyph: checkmark when following, add otherwise.</summary>
    public string SubscribeGlyph => IsSubscribed ? "\uE73E" : "\uE710";

    // â”€â”€ Rails (Req 15.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Top songs rail; identity is <c>Song.Id</c> (videoId) for stable list virtualization (Req 16.1).</summary>
    public ObservableCollection<Song> TopSongs { get; } = [];

    /// <summary>Albums rail; identity is <c>Album.Id</c> (browseId).</summary>
    public ObservableCollection<Album> Albums { get; } = [];

    /// <summary>Singles &amp; EPs rail; identity is <c>Album.Id</c> (browseId).</summary>
    public ObservableCollection<Album> SinglesAndEps { get; } = [];

    /// <summary>Videos rail; identity is <c>Song.Id</c> (videoId).</summary>
    public ObservableCollection<Song> Videos { get; } = [];

    /// <summary>Live-performances rail; identity is <c>Song.Id</c> (videoId).</summary>
    public ObservableCollection<Song> LivePerformances { get; } = [];

    /// <summary>Featured-on / playlists-by-artist rail; identity is <c>Playlist.Id</c> (browseId).</summary>
    public ObservableCollection<Playlist> FeaturedPlaylists { get; } = [];

    /// <summary>Similar / related artists rail; identity is <c>Artist.Id</c> (channelId).</summary>
    public ObservableCollection<Artist> RelatedArtists { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTopSongs))]
    private bool _hasTopSongsValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlbums))]
    private bool _hasAlbumsValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSingles))]
    private bool _hasSinglesValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideos))]
    private bool _hasVideosValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLive))]
    private bool _hasLiveValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeatured))]
    private bool _hasFeaturedValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelated))]
    private bool _hasRelatedValue;

    /// <summary>True when the Top songs rail has content (section visibility).</summary>
    public bool HasTopSongs => HasTopSongsValue;

    /// <summary>True when the Albums rail has content (section visibility).</summary>
    public bool HasAlbums => HasAlbumsValue;

    /// <summary>True when the Singles &amp; EPs rail has content (section visibility).</summary>
    public bool HasSingles => HasSinglesValue;

    /// <summary>True when the Videos rail has content (section visibility).</summary>
    public bool HasVideos => HasVideosValue;

    /// <summary>True when the Live performances rail has content (section visibility).</summary>
    public bool HasLive => HasLiveValue;

    /// <summary>True when the Featured rail has content (section visibility).</summary>
    public bool HasFeatured => HasFeaturedValue;

    /// <summary>True when the Related artists rail has content (section visibility).</summary>
    public bool HasRelated => HasRelatedValue;

    // â”€â”€ "See all" destinations (Req 15.2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllSongs))]
    private string? _songsSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllAlbums))]
    private string? _albumsSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllSingles))]
    private string? _singlesSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllVideos))]
    private string? _videosSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllLive))]
    private string? _liveSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllFeatured))]
    private string? _featuredSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllRelated))]
    private string? _relatedSeeAllBrowseId;

    /// <summary>True when a "See all" target exists for the Top songs rail.</summary>
    public bool CanSeeAllSongs => !string.IsNullOrEmpty(SongsSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Albums rail.</summary>
    public bool CanSeeAllAlbums => !string.IsNullOrEmpty(AlbumsSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Singles &amp; EPs rail.</summary>
    public bool CanSeeAllSingles => !string.IsNullOrEmpty(SinglesSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Videos rail.</summary>
    public bool CanSeeAllVideos => !string.IsNullOrEmpty(VideosSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Live performances rail.</summary>
    public bool CanSeeAllLive => !string.IsNullOrEmpty(LiveSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Featured rail.</summary>
    public bool CanSeeAllFeatured => !string.IsNullOrEmpty(FeaturedSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Related artists rail.</summary>
    public bool CanSeeAllRelated => !string.IsNullOrEmpty(RelatedSeeAllBrowseId);

    // â”€â”€ Radio (Req 15.4 / 25) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private string? _radioPlaylistId;
    private string? _radioVideoId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRadio))]
    private bool _hasRadioValue;

    /// <summary>True when the artist exposes a Radio/mix seed (header start-radio button).</summary>
    public bool HasRadio => HasRadioValue;

    // â”€â”€ Loading (Req 15.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Loads the artist identified by <paramref name="channelId"/> (a <c>UCâ€¦</c> id). Coalesced
    /// per-id via the single-flight base so a re-entrant navigation joins the in-flight load
    /// (Req 16.3). Safe to call from <c>OnNavigatedTo</c>.
    /// </summary>
    public Task LoadArtistAsync(string channelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelId);
        _channelId = channelId;
        return LoadAsync($"artist:{channelId}", c => LoadCoreAsync(channelId, c), ct);
    }

    private async Task LoadCoreAsync(string channelId, CancellationToken ct)
    {
        var detail = await _client.GetArtistAsync(channelId, ct).ConfigureAwait(true);

        Name = detail.Artist.Name;
        ThumbnailUrl = detail.Artist.ThumbnailUrl;
        HeaderImageUrl = detail.HeaderImageUrl ?? detail.Artist.ThumbnailUrl;
        Description = detail.Description;
        IsDescriptionExpanded = false;
        // Recomputed by the view once the collapsed text is measured (IsTextTrimmed).
        CanExpandDescription = false;
        SubscriberText = detail.SubscriberText;
        MonthlyListenersText = detail.MonthlyListenersText;
        IsSubscribed = detail.IsSubscribed;
        // Prefer the subscribe button's channel id for the mutation; fall back to the browse id used
        // to load the page when the header carries none (Bug 4).
        _subscribeChannelId = detail.SubscribeChannelId;

        _radioPlaylistId = detail.RadioPlaylistId;
        _radioVideoId = detail.RadioVideoId;
        HasRadioValue = !string.IsNullOrEmpty(_radioPlaylistId) || !string.IsNullOrEmpty(_radioVideoId);

        // The inline artist "Top songs" shelf only carries ~5 rows; when a "See all" songs target
        // exists, load the full song list so the 5-row grid actually wraps into multiple columns
        // (the shelf's own "See all" button still points at the same target). Falls back to the
        // inline five when the full list can't be loaded.
        var topSongs = detail.TopSongs;
        if (!string.IsNullOrEmpty(detail.SeeAll.SongsBrowseId))
        {
            try
            {
                var full = await _client.GetPlaylistAsync(detail.SeeAll.SongsBrowseId, ct).ConfigureAwait(true);
                if (full.Tracks.Count > topSongs.Count)
                {
                    topSongs = full.Tracks;
                }
            }
            catch (Core.Errors.KasetError)
            {
                // Keep the inline five on failure.
            }
        }

        ReplaceAll(TopSongs, topSongs);
        ReplaceAll(Albums, detail.Albums);
        ReplaceAll(SinglesAndEps, detail.SinglesAndEps);
        ReplaceAll(Videos, detail.Videos);
        ReplaceAll(LivePerformances, detail.LivePerformances);
        ReplaceAll(FeaturedPlaylists, detail.FeaturedPlaylists);
        ReplaceAll(RelatedArtists, detail.RelatedArtists);

        HasTopSongsValue = TopSongs.Count > 0;
        HasAlbumsValue = Albums.Count > 0;
        HasSinglesValue = SinglesAndEps.Count > 0;
        HasVideosValue = Videos.Count > 0;
        HasLiveValue = LivePerformances.Count > 0;
        HasFeaturedValue = FeaturedPlaylists.Count > 0;
        HasRelatedValue = RelatedArtists.Count > 0;

        SongsSeeAllBrowseId = detail.SeeAll.SongsBrowseId;
        AlbumsSeeAllBrowseId = detail.SeeAll.AlbumsBrowseId;
        SinglesSeeAllBrowseId = detail.SeeAll.SinglesBrowseId;
        VideosSeeAllBrowseId = detail.SeeAll.VideosBrowseId;
        LiveSeeAllBrowseId = detail.SeeAll.LiveBrowseId;
        FeaturedSeeAllBrowseId = detail.SeeAll.FeaturedBrowseId;
        RelatedSeeAllBrowseId = detail.SeeAll.RelatedBrowseId;
    }

    /// <summary>
    /// Plays the Live-performances rail starting at <paramref name="video"/> (Req 15.4); the whole
    /// rail is loaded into the queue so playback can continue past the clicked clip.
    /// </summary>
    [RelayCommand]
    private Task PlayLivePerformanceAsync(Song? video)
    {
        if (video is null || LivePerformances.Count == 0)
        {
            return Task.CompletedTask;
        }

        var index = 0;
        for (var i = 0; i < LivePerformances.Count; i++)
        {
            if (string.Equals(LivePerformances[i].Id, video.Id, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        var live = LivePerformances.ToList();
        return RunSafeAsync(_ => _player.PlayCollectionAsync(live, index));
    }

    // â”€â”€ Play (Req 15.4) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Plays the artist's top songs from the top of the rail into the queue (Req 15.4).</summary>
    [RelayCommand]
    private Task PlayTopSongsAsync() => PlayFromTopSongsAsync(0);

    /// <summary>
    /// Shuffle-plays the artist's top songs (Req 5.7/15.4): loads the rail into the queue and turns
    /// shuffle on so playback starts from a randomized order.
    /// </summary>
    [RelayCommand]
    private Task ShuffleTopSongsAsync()
    {
        // THROWAWAY DIAGNOSTIC (Bug A): confirm the artist Shuffle command reaches the player.
        if (TopSongs.Count == 0)
        {
            return Task.CompletedTask;
        }

        var songs = TopSongs.ToList();
        return RunSafeAsync(async _ =>
        {
            await _player.PlayCollectionAsync(songs).ConfigureAwait(true);
            if (!_player.IsShuffled)
            {
                _player.ToggleShuffle();
            }
        });
    }

    /// <summary>
    /// Starts the artist's Radio/mix (Req 25): fetches a mix queue from the header start-radio seed
    /// (playlist mix preferred, song radio as fallback) and plays it into the queue.
    /// </summary>
    [RelayCommand]
    private Task StartRadioAsync()
    {
        // THROWAWAY DIAGNOSTIC (Bug A): confirm the artist Radio command reaches the client/player.

        if (!HasRadio)
        {
            return Task.CompletedTask;
        }

        return RunSafeAsync(async ct =>
        {
            RadioQueueResult radio;
            if (!string.IsNullOrEmpty(_radioPlaylistId))
            {
                radio = await _client.GetMixQueueAsync(_radioPlaylistId, ct).ConfigureAwait(true);
            }
            else
            {
                radio = await _client.GetRadioQueueAsync(_radioVideoId!, ct).ConfigureAwait(true);
            }

            if (radio.Songs.Count > 0)
            {
                await _player.PlayCollectionAsync(radio.Songs).ConfigureAwait(true);
            }
        });
    }

    /// <summary>
    /// Plays the top songs starting at <paramref name="song"/> (Req 15.4); the whole rail is loaded
    /// into the queue so the user can continue past the clicked track.
    /// </summary>
    [RelayCommand]
    private Task PlaySongAsync(Song? song)
    {
        if (song is null)
        {
            return Task.CompletedTask;
        }

        var index = IndexOfSong(song);
        return PlayFromTopSongsAsync(index < 0 ? 0 : index);
    }

    /// <summary>
    /// Plays the Videos rail starting at <paramref name="video"/> (Req 15.4); the whole rail is
    /// loaded into the queue so the user can continue past the clicked video.
    /// </summary>
    [RelayCommand]
    private Task PlayVideoAsync(Song? video)
    {
        if (video is null || Videos.Count == 0)
        {
            return Task.CompletedTask;
        }

        var index = 0;
        for (var i = 0; i < Videos.Count; i++)
        {
            if (string.Equals(Videos[i].Id, video.Id, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        var videos = Videos.ToList();
        return RunSafeAsync(_ => _player.PlayCollectionAsync(videos, index));
    }

    private Task PlayFromTopSongsAsync(int startIndex)
    {
        // THROWAWAY DIAGNOSTIC (Bug A): confirm artist "Play"/top-song commands reach the player.
        if (TopSongs.Count == 0)
        {
            return Task.CompletedTask;
        }

        var songs = TopSongs.ToList();
        return RunSafeAsync(_ => _player.PlayCollectionAsync(songs, startIndex));
    }

    private int IndexOfSong(Song song)
    {
        for (var i = 0; i < TopSongs.Count; i++)
        {
            if (string.Equals(TopSongs[i].Id, song.Id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // â”€â”€ Follow / unfollow (Req 15.3) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Toggles the artist subscription optimistically (Req 15.3): flips <see cref="IsSubscribed"/>
    /// immediately, calls the matching InnerTube mutation, and reverts the flag if the call fails so
    /// the UI converges back to the real backend state.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSubscribeAsync()
    {
        // Use the subscribe button's channel id (Bug 4); fall back to the browse id when absent.
        var channelId = _subscribeChannelId;
        if (string.IsNullOrEmpty(channelId))
        {
            channelId = _channelId;
        }

        if (string.IsNullOrEmpty(channelId))
        {
            return;
        }

        var wasSubscribed = IsSubscribed;

        // Optimistic flip.
        IsSubscribed = !wasSubscribed;

        try
        {
            ErrorMessage = null;
            if (wasSubscribed)
            {
                await _client.UnsubscribeArtistAsync(channelId).ConfigureAwait(true);
            }
            else
            {
                await _client.SubscribeArtistAsync(channelId).ConfigureAwait(true);
            }
        }
        catch (Core.Errors.KasetError ex)
        {
            // Roll back the optimistic change and surface the error.
            IsSubscribed = wasSubscribed;
            ErrorMessage = ex.Message;
        }
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
