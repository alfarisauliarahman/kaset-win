namespace KasetWin.Core.Models;

/// <summary>
/// A signed-in Google / brand account. <see cref="OnBehalfOfUser"/> equivalents
/// are resolved elsewhere; this record describes display + selection state.
/// <paramref name="AvatarUrl"/> is the account profile photo when the response carries one.
/// </summary>
public sealed record UserAccount(
    string Name,
    string? Handle,
    string? BrandId,
    bool IsPrimary,
    bool IsCurrent,
    Uri? AvatarUrl = null);

/// <summary>
/// A user-pinned favorite item shown on Home (advanced phase, Req 29). Identity is
/// the underlying content id (videoId/browseId) together with its <see cref="Type"/>.
/// </summary>
public sealed record FavoriteItem(
    string ContentId,
    FavoriteItemType Type,
    string Title,
    string? Subtitle,
    Uri? ThumbnailUrl)
{
    /// <summary>Projects a <see cref="Song"/> into a song favorite (identity == videoId).</summary>
    public static FavoriteItem From(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return new FavoriteItem(
            song.Id,
            FavoriteItemType.Song,
            song.Title,
            string.IsNullOrEmpty(song.ArtistsDisplay) ? null : song.ArtistsDisplay,
            song.ThumbnailUrl ?? song.FallbackThumbnailUrl);
    }

    /// <summary>Projects an <see cref="Album"/> into an album favorite (identity == browseId).</summary>
    public static FavoriteItem From(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        return new FavoriteItem(
            album.Id,
            FavoriteItemType.Album,
            album.Title,
            string.IsNullOrEmpty(album.ArtistsDisplay) ? null : album.ArtistsDisplay,
            album.ThumbnailUrl);
    }

    /// <summary>Projects a <see cref="Playlist"/> into a playlist favorite (identity == browseId).</summary>
    public static FavoriteItem From(Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        return new FavoriteItem(
            playlist.Id,
            FavoriteItemType.Playlist,
            playlist.Title,
            playlist.Author?.Name,
            playlist.ThumbnailUrl);
    }

    /// <summary>Projects an <see cref="Artist"/> into an artist favorite (identity == channelId).</summary>
    public static FavoriteItem From(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return new FavoriteItem(
            artist.Id,
            FavoriteItemType.Artist,
            artist.Name,
            null,
            artist.ThumbnailUrl);
    }
}

/// <summary>
/// A podcast show. Identity is the <c>MPSPP...</c> browseId (converted to <c>P...</c>
/// for subscribe mutations). Advanced phase (Req 27).
/// </summary>
public sealed record PodcastShow
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Optional author / publisher line for display.</summary>
    public string? Author { get; init; }

    /// <summary>Optional show/playlist description from the header (truncated in the UI).</summary>
    public string? Description { get; init; }

    /// <summary>Optional author channel id (<c>UC…</c>) for artist-page navigation.</summary>
    public string? AuthorChannelId { get; init; }

    /// <summary>Whether the show/playlist is already saved to the user's library (header bookmark toggle).</summary>
    public bool IsSaved { get; init; }

    /// <summary>Optional best thumbnail for the show card.</summary>
    public Uri? ThumbnailUrl { get; init; }

    public IReadOnlyList<PodcastEpisode> Episodes { get; init; } = [];

    /// <summary>Whether <see cref="Id"/> is a navigable podcast show browse id (<c>MPSPP...</c>).</summary>
    public bool HasNavigableId => Id.StartsWith("MPSPP", StringComparison.Ordinal);
}

/// <summary>
/// A single podcast episode with playback progress / played state (advanced phase).
/// </summary>
/// <remarks>
/// Identity is the YouTube <c>videoId</c>. <see cref="Progress"/> is a 0–1 fraction of the
/// episode duration (mirrors the macOS <c>playbackProgress</c>); per-episode position/played state
/// is persisted by <c>EpisodeProgressStore</c> (Req 27.3). The optional display fields default to
/// <see langword="null"/> so existing call sites that build the minimal positional shape still
/// compile.
/// </remarks>
public sealed record PodcastEpisode(string Id, string Title, TimeSpan? Duration, double Progress, bool IsPlayed)
{
    /// <summary>Optional best thumbnail for the episode card.</summary>
    public Uri? ThumbnailUrl { get; init; }

    /// <summary>Optional show name shown under the episode title.</summary>
    public string? ShowTitle { get; init; }

    /// <summary>Optional show browse id (<c>MPSPP...</c>) for back-navigation.</summary>
    public string? ShowBrowseId { get; init; }

    /// <summary>Optional episode description/summary (truncated in the UI).</summary>
    public string? Description { get; init; }

    /// <summary>Optional publish date, as YT sends it ("5 days ago" / "Jun 13, 2025").</summary>
    public string? PublishedText { get; init; }

    /// <summary>Whether a non-empty <see cref="Description"/> is available.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Whether a non-empty <see cref="PublishedText"/> is available.</summary>
    public bool HasPublished => !string.IsNullOrWhiteSpace(PublishedText);

    /// <summary>1-based position within its show/playlist (assigned when the list is built).</summary>
    public int Number { get; init; }

    /// <summary>The number as display text (empty when unset).</summary>
    public string NumberText => Number > 0 ? Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// The duration exactly as YT displayed it ("49 min" / "1 hr 5 min"). YT only sends a rounded
    /// value here, so showing it verbatim is honest — formatting the parsed TimeSpan as "49:00"
    /// implied a precision the data doesn't have.
    /// </summary>
    public string? DurationText { get; init; }

    /// <summary>Display duration: YT's own text, falling back to the parsed TimeSpan.</summary>
    public string DurationDisplay => DurationText
        ?? (Duration is { } d ? (d.Hours > 0 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss")) : string.Empty);

    /// <summary>Whether a duration (text or parsed) is available to show.</summary>
    public bool HasDuration => !string.IsNullOrWhiteSpace(DurationText) || (Duration is { } dur && dur > TimeSpan.Zero);

    /// <summary>Whether any playback progress exists (drives the red progress strip).</summary>
    public bool HasProgress => Progress > 0;

    /// <summary>
    /// Server tokens for the "Mark as played" (<see cref="FeedbackTokens.Add"/>) and "Mark as
    /// unplayed" (<see cref="FeedbackTokens.Remove"/>) mutations, when the row's menu carried them.
    /// Lets the played state sync to the YT Music account instead of staying local-only.
    /// </summary>
    public FeedbackTokens? PlayedFeedback { get; init; }
}

/// <summary>
/// Minimal placeholder for the library landing surface (Req 13.1). Holds the
/// top-level collections shown on the library page; expanded in the library
/// parser / view-model tasks.
/// </summary>
public sealed record LibraryContent
{
    public IReadOnlyList<Playlist> Playlists { get; init; } = [];

    public IReadOnlyList<Album> Albums { get; init; } = [];

    public IReadOnlyList<Artist> Artists { get; init; } = [];

    public IReadOnlyList<Song> Songs { get; init; } = [];
}

/// <summary>
/// The "add to playlist" menu (Req 8.4), parsed from <c>playlist/get_add_to_playlist</c>.
/// Lists the (de-duplicated) playlists a track can be added to and whether the user can
/// create a new playlist (only when the payload exposes a <c>createPlaylistEndpoint</c>).
/// </summary>
public sealed record AddToPlaylistMenu
{
    /// <summary>Existing playlists the track can be added to, de-duplicated by playlist id.</summary>
    public IReadOnlyList<Playlist> Playlists { get; init; } = [];

    /// <summary>
    /// Whether a new playlist can be created from this menu. <see langword="true"/> only when
    /// the response contains a <c>createPlaylistEndpoint</c> affordance; otherwise
    /// <see langword="false"/>.
    /// </summary>
    public bool CanCreate { get; init; }
}
