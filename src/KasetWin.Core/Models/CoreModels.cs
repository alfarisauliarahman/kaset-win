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

    public int? TrackNumber { get; init; }

    /// <summary>
    /// 1-based chart position when this song appears in a chart playlist row (parsed from the row's
    /// <c>customIndexColumn</c>); <c>0</c> outside charts. Distinct from <see cref="TrackNumber"/>,
    /// which is the album/playlist ordinal.
    /// </summary>
    public int Rank { get; init; }

    /// <summary>Week-over-week chart movement arrow (up/down/steady); <see cref="TrendDirection.None"/> outside charts.</summary>
    public TrendDirection Trend { get; init; }

    public string? ListenerCountText { get; init; }

    /// <summary>Whether a non-empty <see cref="ListenerCountText"/> (plays / views line) is available.</summary>
    public bool HasListenerCount => !string.IsNullOrWhiteSpace(ListenerCountText);

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

    /// <summary>
    /// Whether this track is a podcast episode. Checks <see cref="VideoType"/> first, then the
    /// podcast-show browse ids some surfaces put in the album/artist slots (<c>MPSP…</c>) — tracks
    /// re-materialised by the queue/web bridge can lose <see cref="VideoType"/>, so a single
    /// signal is not enough.
    /// </summary>
    public bool IsPodcastEpisode =>
        VideoType == MusicVideoType.PodcastEpisode
        || (Album?.Id?.StartsWith("MPSP", StringComparison.Ordinal) ?? false)
        || (PrimaryArtistId?.StartsWith("MPSP", StringComparison.Ordinal) ?? false);
}

/// <summary>
/// An artist / channel. Identity is the channelId (typically <c>UC...</c>).
/// </summary>
public sealed record Artist
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public Uri? ThumbnailUrl { get; init; }

    /// <summary>
    /// Optional subtitle line carried by an artist card, e.g. the "44.2M monthly listeners" text on
    /// a "Fans might also like" related-artist tile. <c>null</c> when the source carried none.
    /// </summary>
    public string? SubtitleText { get; init; }

    /// <summary>Whether a non-empty <see cref="SubtitleText"/> is available to show.</summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(SubtitleText);

    /// <summary>
    /// 1-based chart position when this artist appears in a chart ("Top artists") row, parsed from
    /// the <c>customIndexColumn</c>; <c>0</c> when the artist did not come from a chart.
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// The week-over-week movement arrow shown beside a chart rank (up / down / steady), parsed from
    /// the chart row's trend icon; <see cref="TrendDirection.None"/> outside charts.
    /// </summary>
    public TrendDirection Trend { get; init; }
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

    public string? ReleaseDateText { get; init; }

    public string? ContentType { get; init; }

    public string? Description { get; init; }

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

    public string? ReleaseDateText { get; init; }

    public string? ContentType { get; init; }

    public string? Description { get; init; }

    /// <summary>Whether the current user owns the playlist (delete affordance, Req 14.3).</summary>
    public bool IsOwnedByUser { get; init; }

    /// <summary>Whether the album/playlist is flagged explicit (header badge).</summary>
    public bool IsExplicit { get; init; }
}

/// <summary>
/// Playlist metadata plus its tracks and an optional pagination token (Req 8.4).
/// </summary>
public sealed record PlaylistDetail
{
    public required Playlist Playlist { get; init; }

    public IReadOnlyList<Song> Tracks { get; init; } = [];

    public string? ContinuationToken { get; init; }

    /// <summary>
    /// The likeable playlist id for "add to library" (an album's <c>OLAK…</c> audio-playlist id, or a
    /// real <c>VL/PL</c> playlist id). The <c>like/like</c> endpoint rejects an album's <c>MPRE…</c>
    /// browseId with HTTP 400, so this is the id to send when saving an album/playlist to the
    /// collection. <c>null</c> when the response carried none (callers fall back to the browseId).
    /// </summary>
    public string? LikePlaylistId { get; init; }

    /// <summary>
    /// Whether the shelf rows are podcast episodes (<c>musicMultiRowListItemRenderer</c>) rather
    /// than music tracks. The track parser cannot represent episodes, so callers should route the
    /// surface to the podcast show page instead of rendering an empty track list.
    /// </summary>
    public bool IsPodcastPlaylist { get; init; }
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

    /// <summary>
    /// "Live performances" shelf items — a separate video carousel YouTube Music titles distinctly
    /// from the plain "Videos" shelf. Empty when the artist page carries no such shelf.
    /// </summary>
    public IReadOnlyList<Song> LivePerformances { get; init; } = [];

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
/// The classified items of an artist "See all" browse (albums/singles, videos, playlists, related
/// artists). Each rail's "See all" resolves to one of these buckets.
/// </summary>
public sealed record ArtistSectionResult
{
    public IReadOnlyList<Album> Albums { get; init; } = [];

    public IReadOnlyList<Song> Videos { get; init; } = [];

    public IReadOnlyList<Playlist> Playlists { get; init; } = [];

    public IReadOnlyList<Artist> Artists { get; init; } = [];
}

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

    /// <summary>"See all" browse target for the Live performances shelf, when present.</summary>
    public string? LiveBrowseId { get; init; }

    /// <summary>"See all" browse target for the Featured/playlists shelf, when present.</summary>
    public string? FeaturedBrowseId { get; init; }

    /// <summary>"See all" browse target for the Related/similar artists shelf, when present.</summary>
    public string? RelatedBrowseId { get; init; }
}
