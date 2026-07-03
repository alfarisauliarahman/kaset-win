namespace KasetWin.Core.Models;

/// <summary>
/// A playable track. Identity is the YouTube <c>videoId</c> (mirrored into
/// <see cref="Id"/> for the stable-identity convention, Req 16.1).
/// </summary>
public sealed record Song
{
    /// <summary>Stable identity == <see cref="VideoId"/>.</summary>
    public required string Id { get; init; }

    /// <summary>YouTube videoId.</summary>
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<Artist> Artists { get; init; } = [];

    public Album? Album { get; init; }

    public TimeSpan? Duration { get; init; }

    public Uri? ThumbnailUrl { get; init; }

    public bool IsPlayable { get; init; } = true;

    public bool? HasVideo { get; init; }

    /// <summary>OMV / ATV / UGC / PodcastEpisode.</summary>
    public MusicVideoType? VideoType { get; init; }

    public LikeStatus? LikeStatus { get; init; }

    public bool? IsInLibrary { get; init; }

    public FeedbackTokens? FeedbackTokens { get; init; }

    public bool? IsExplicit { get; init; }

    /// <summary>Comma-joined artist names for display.</summary>
    public string ArtistsDisplay => string.Join(", ", Artists.Select(a => a.Name));

    /// <summary>Deterministic thumbnail fallback derived from <see cref="VideoId"/>.</summary>
    public Uri FallbackThumbnailUrl => new($"https://i.ytimg.com/vi/{VideoId}/hqdefault.jpg");

    /// <summary>
    /// The primary artist with a navigable channel id (the first artist carrying a non-empty
    /// <see cref="Artist.Id"/>), or <c>null</c> when no artist has one. Backs the clickable
    /// artist-name affordance (only rendered when a real id exists, never fabricated).
    /// </summary>
    public Artist? PrimaryArtist => Artists.FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));

    /// <summary>Channel id of <see cref="PrimaryArtist"/>, or <c>null</c> when none is available.</summary>
    public string? PrimaryArtistId => PrimaryArtist?.Id;

    /// <summary>Whether the artist name can navigate to an artist page (a real channel id exists).</summary>
    public bool HasArtistLink => PrimaryArtist is not null;

    /// <summary>
    /// The album browseId this song belongs to, or <c>null</c> when the song carries no album.
    /// Backs the clickable song-title affordance (navigates to the album/single page when present).
    /// </summary>
    public string? AlbumBrowseId => string.IsNullOrEmpty(Album?.Id) ? null : Album!.Id;

    /// <summary>Whether the song title can navigate to an album page (a real album browseId exists).</summary>
    public bool HasAlbumLink => AlbumBrowseId is not null;
}

/// <summary>
/// An artist / channel. Identity is the channelId (typically <c>UC...</c>).
/// </summary>
public sealed record Artist
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public Uri? ThumbnailUrl { get; init; }
}

/// <summary>
/// An album or single/EP. Identity is the browseId (<c>MPRE.../OLAK...</c>).
/// </summary>
public sealed record Album
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<Artist> Artists { get; init; } = [];

    public Uri? ThumbnailUrl { get; init; }

    public string? Year { get; init; }

    public IReadOnlyList<Song> Tracks { get; init; } = [];

    /// <summary>Comma-joined artist names for display.</summary>
    public string ArtistsDisplay => string.Join(", ", Artists.Select(a => a.Name));
}

/// <summary>
/// A playlist summary. Identity is the browseId (<c>VL.../PL...</c>).
/// </summary>
public sealed record Playlist
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public Artist? Author { get; init; }

    public Uri? ThumbnailUrl { get; init; }

    public int? TrackCount { get; init; }

    /// <summary>Whether the current user owns the playlist (delete affordance, Req 14.3).</summary>
    public bool IsOwnedByUser { get; init; }
}

/// <summary>
/// Playlist metadata plus its tracks and an optional pagination token (Req 8.4).
/// </summary>
public sealed record PlaylistDetail
{
    public required Playlist Playlist { get; init; }

    public IReadOnlyList<Song> Tracks { get; init; } = [];

    public string? ContinuationToken { get; init; }
}

/// <summary>
/// A single continuation page of playlist tracks (Req 8.4). Holds the next batch of
/// <see cref="Tracks"/> and the optional <see cref="ContinuationToken"/> for the page after
/// it. The client layer concatenates these onto an existing <see cref="PlaylistDetail"/>.
/// </summary>
public sealed record PlaylistContinuation
{
    public IReadOnlyList<Song> Tracks { get; init; } = [];

    public string? ContinuationToken { get; init; }
}
public sealed record ArtistDetail
{
    public required Artist Artist { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Wide banner / cover image from the artist header (<c>musicImmersiveHeaderRenderer</c> /
    /// <c>musicVisualHeaderRenderer</c> thumbnail). Used as the YouTube-Music-style header banner.
    /// Falls back to <see cref="Artist"/>.<see cref="Artist.ThumbnailUrl"/> when not separately set.
    /// </summary>
    public Uri? HeaderImageUrl { get; init; }

    /// <summary>
    /// Subscriber count line from the subscribe button (e.g. <c>"1.2M subscribers"</c>), or
    /// <c>null</c> when the header carries none.
    /// </summary>
    public string? SubscriberText { get; init; }

    /// <summary>
    /// Monthly-listeners / monthly-audience line from the immersive header (e.g.
    /// <c>"93.2M monthly listeners"</c>), or <c>null</c> when absent.
    /// </summary>
    public string? MonthlyListenersText { get; init; }

    /// <summary>
    /// Playlist id for the artist's "Radio"/mix (from the header <c>startRadioButton</c>
    /// <c>watchPlaylistEndpoint</c>), or <c>null</c> when the header exposes none.
    /// </summary>
    public string? RadioPlaylistId { get; init; }

    /// <summary>
    /// Seed videoId for the artist's radio (from the header <c>startRadioButton</c>
    /// <c>watchEndpoint</c>), or <c>null</c>.
    /// </summary>
    public string? RadioVideoId { get; init; }

    public IReadOnlyList<Song> TopSongs { get; init; } = [];

    public IReadOnlyList<Album> Albums { get; init; } = [];

    public IReadOnlyList<Album> SinglesAndEps { get; init; } = [];

    /// <summary>Video shelf items (carousel items whose endpoint is a <c>watchEndpoint</c>).</summary>
    public IReadOnlyList<Song> Videos { get; init; } = [];

    /// <summary>"Featured on" / playlists-by-artist carousels (playlist browse ids).</summary>
    public IReadOnlyList<Playlist> FeaturedPlaylists { get; init; } = [];

    /// <summary>"Fans might also like" / similar/related artists (channel browse ids).</summary>
    public IReadOnlyList<Artist> RelatedArtists { get; init; } = [];

    /// <summary>Podcast / show episodes on the artist page (advanced phase, Req 18 ADR-0018).</summary>
    public IReadOnlyList<ArtistEpisode> Episodes { get; init; } = [];

    public bool IsSubscribed { get; init; }

    /// <summary>
    /// The channel id YouTube Music expects for the subscribe/unsubscribe mutation, taken from the
    /// header's <c>subscribeButtonRenderer.channelId</c> (Bug 4). This is the authoritative id for
    /// the <c>subscription/subscribe</c> endpoint and can differ from the browse id used to load the
    /// page; sending the browse id instead is what produced the HTTP 400. <c>null</c> when the
    /// header carries no subscribe button channel id, in which case callers fall back to the
    /// navigable browse id (mirroring the macOS client).
    /// </summary>
    public string? SubscribeChannelId { get; init; }

    public ArtistSeeAllDestinations SeeAll { get; init; } = new();
}

/// <summary>
/// Minimal placeholder for episodes surfaced on an artist page (advanced phase).
/// Identity is the browseId/videoId of the episode.
/// </summary>
public sealed record ArtistEpisode(string Id, string Title, Uri? ThumbnailUrl = null);

/// <summary>
/// Minimal placeholder for the "See all" navigation targets on an artist page.
/// Holds optional browseIds for each rail; expanded in the Artist parser task.
/// </summary>
public sealed record ArtistSeeAllDestinations
{
    public string? SongsBrowseId { get; init; }

    public string? AlbumsBrowseId { get; init; }

    public string? SinglesBrowseId { get; init; }

    /// <summary>"See all" browse target for the Videos shelf, when present.</summary>
    public string? VideosBrowseId { get; init; }

    /// <summary>"See all" browse target for the Featured/playlists shelf, when present.</summary>
    public string? FeaturedBrowseId { get; init; }

    /// <summary>"See all" browse target for the Related/similar artists shelf, when present.</summary>
    public string? RelatedBrowseId { get; init; }
}
