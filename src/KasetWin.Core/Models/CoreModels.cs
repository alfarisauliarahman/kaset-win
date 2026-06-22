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

    public IReadOnlyList<Song> TopSongs { get; init; } = [];

    public IReadOnlyList<Album> Albums { get; init; } = [];

    public IReadOnlyList<Album> SinglesAndEps { get; init; } = [];

    /// <summary>Podcast / show episodes on the artist page (advanced phase, Req 18 ADR-0018).</summary>
    public IReadOnlyList<ArtistEpisode> Episodes { get; init; } = [];

    public bool IsSubscribed { get; init; }

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
}
