using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the Artist detail surface (Task 14.7, Req 15.1–15.4).
/// </summary>
/// <remarks>
/// <para>
/// Loads an artist via <see cref="IYTMusicClient.GetArtistAsync(string, CancellationToken)"/> and
/// projects the <see cref="ArtistDetail"/> into bindable header fields plus three rails — Top songs,
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

    private string? _channelId;

    /// <summary>Creates the ViewModel with the data client and player resolved from DI.</summary>
    public ArtistDetailViewModel(IYTMusicClient client, IPlayerService player, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    // ── Header (Req 15.1) ──────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Uri? _thumbnailUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    /// <summary>True when there is a non-empty artist description to show.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // ── Subscription toggle (Req 15.3) ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeLabel))]
    [NotifyPropertyChangedFor(nameof(SubscribeGlyph))]
    private bool _isSubscribed;

    /// <summary>Follow/Following label that reflects <see cref="IsSubscribed"/>.</summary>
    public string SubscribeLabel => IsSubscribed ? "Following" : "Follow";

    /// <summary>Segoe Fluent glyph: checkmark when following, add otherwise.</summary>
    public string SubscribeGlyph => IsSubscribed ? "\uE73E" : "\uE710";

    // ── Rails (Req 15.1) ───────────────────────────────────────────────────────────────────────

    /// <summary>Top songs rail; identity is <c>Song.Id</c> (videoId) for stable list virtualization (Req 16.1).</summary>
    public ObservableCollection<Song> TopSongs { get; } = [];

    /// <summary>Albums rail; identity is <c>Album.Id</c> (browseId).</summary>
    public ObservableCollection<Album> Albums { get; } = [];

    /// <summary>Singles &amp; EPs rail; identity is <c>Album.Id</c> (browseId).</summary>
    public ObservableCollection<Album> SinglesAndEps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTopSongs))]
    private bool _hasTopSongsValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlbums))]
    private bool _hasAlbumsValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSingles))]
    private bool _hasSinglesValue;

    /// <summary>True when the Top songs rail has content (section visibility).</summary>
    public bool HasTopSongs => HasTopSongsValue;

    /// <summary>True when the Albums rail has content (section visibility).</summary>
    public bool HasAlbums => HasAlbumsValue;

    /// <summary>True when the Singles &amp; EPs rail has content (section visibility).</summary>
    public bool HasSingles => HasSinglesValue;

    // ── "See all" destinations (Req 15.2) ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllSongs))]
    private string? _songsSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllAlbums))]
    private string? _albumsSeeAllBrowseId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSeeAllSingles))]
    private string? _singlesSeeAllBrowseId;

    /// <summary>True when a "See all" target exists for the Top songs rail.</summary>
    public bool CanSeeAllSongs => !string.IsNullOrEmpty(SongsSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Albums rail.</summary>
    public bool CanSeeAllAlbums => !string.IsNullOrEmpty(AlbumsSeeAllBrowseId);

    /// <summary>True when a "See all" target exists for the Singles &amp; EPs rail.</summary>
    public bool CanSeeAllSingles => !string.IsNullOrEmpty(SinglesSeeAllBrowseId);

    // ── Loading (Req 15.1) ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the artist identified by <paramref name="channelId"/> (a <c>UC…</c> id). Coalesced
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
        Description = detail.Description;
        IsSubscribed = detail.IsSubscribed;

        ReplaceAll(TopSongs, detail.TopSongs);
        ReplaceAll(Albums, detail.Albums);
        ReplaceAll(SinglesAndEps, detail.SinglesAndEps);

        HasTopSongsValue = TopSongs.Count > 0;
        HasAlbumsValue = Albums.Count > 0;
        HasSinglesValue = SinglesAndEps.Count > 0;

        SongsSeeAllBrowseId = detail.SeeAll.SongsBrowseId;
        AlbumsSeeAllBrowseId = detail.SeeAll.AlbumsBrowseId;
        SinglesSeeAllBrowseId = detail.SeeAll.SinglesBrowseId;
    }

    // ── Play (Req 15.4) ──────────────────────────────────────────────────────────────────────────

    /// <summary>Plays the artist's top songs from the top of the rail into the queue (Req 15.4).</summary>
    [RelayCommand]
    private Task PlayTopSongsAsync() => PlayFromTopSongsAsync(0);

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

    private Task PlayFromTopSongsAsync(int startIndex)
    {
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

    // ── Follow / unfollow (Req 15.3) ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the artist subscription optimistically (Req 15.3): flips <see cref="IsSubscribed"/>
    /// immediately, calls the matching InnerTube mutation, and reverts the flag if the call fails so
    /// the UI converges back to the real backend state.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSubscribeAsync()
    {
        if (string.IsNullOrEmpty(_channelId))
        {
            return;
        }

        var channelId = _channelId;
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
