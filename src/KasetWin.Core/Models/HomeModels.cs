using System.Text.Json.Serialization;

namespace KasetWin.Core.Models;

/// <summary>
/// A section of the Home / Explore surface, holding a typed list of items.
/// </summary>
public sealed record HomeSection
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<HomeSectionItem> Items { get; init; } = [];

    public bool IsChart { get; init; }
}

/// <summary>
/// Discriminated union of items that can appear in a Home section or as a search
/// top result. Modelled as an abstract record with sealed subtypes so the parser
/// can pattern-match on the concrete kind.
/// <para>
/// Annotated for <see cref="System.Text.Json"/> polymorphic round-trip via a
/// <c>$type</c> discriminator so it survives serialize/deserialize cycles.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SongItem), "song")]
[JsonDerivedType(typeof(AlbumItem), "album")]
[JsonDerivedType(typeof(PlaylistItem), "playlist")]
[JsonDerivedType(typeof(ArtistItem), "artist")]
public abstract record HomeSectionItem
{
    /// <summary>Stable, kind-prefixed identity for UI virtualization.</summary>
    public abstract string Id { get; }

    public sealed record SongItem(Song Song) : HomeSectionItem
    {
        public override string Id => $"song-{Song.Id}";
    }

    public sealed record AlbumItem(Album Album) : HomeSectionItem
    {
        public override string Id => $"album-{Album.Id}";
    }

    public sealed record PlaylistItem(Playlist Pl) : HomeSectionItem
    {
        public override string Id => $"playlist-{Pl.Id}";
    }

    public sealed record ArtistItem(Artist Artist) : HomeSectionItem
    {
        public override string Id => $"artist-{Artist.Id}";
    }
}

/// <summary>
/// Parsed Home / Explore response (section-based) with optional continuation token.
/// </summary>
public sealed record HomeResponse
{
    public IReadOnlyList<HomeSection> Sections { get; init; } = [];

    public string? ContinuationToken { get; init; }

    /// <summary>Header of a trailing description shelf (e.g. "Tentang artis" on the Related surface).</summary>
    public string? AboutTitle { get; init; }

    /// <summary>Body text of the trailing description shelf (artist bio on the Related surface).</summary>
    public string? AboutText { get; init; }
}

/// <summary>
/// Grouped search results. <see cref="TopResult"/> is the highlighted card
/// (<c>musicCardShelfRenderer</c>), the remaining lists are per-type groups.
/// </summary>
/// <summary>
/// One shelf of a universal search response, in the exact order and grouping YT Music sent it
/// (e.g. "Podcasts", "Episodes", "Community playlists"). Backs the unified (unclassified)
/// search presentation.
/// </summary>
public sealed record SearchSection(string Title, IReadOnlyList<HomeSectionItem> Items);

public sealed record SearchResponse
{
    public HomeSectionItem? TopResult { get; init; }

    /// <summary>The response's shelves verbatim (title + rows), preserving YT's order.</summary>
    public IReadOnlyList<SearchSection> Sections { get; init; } = [];

    public IReadOnlyList<Song> Songs { get; init; } = [];

    public IReadOnlyList<Album> Albums { get; init; } = [];

    public IReadOnlyList<Artist> Artists { get; init; } = [];

    public IReadOnlyList<Playlist> Playlists { get; init; } = [];

    /// <summary>Podcast shows surfaced in search (advanced phase, Req 27).</summary>
    public IReadOnlyList<Playlist> Podcasts { get; init; } = [];

    /// <summary>
    /// Token for the next page of a filtered search shelf, or <see langword="null"/> when the shelf
    /// has no more results. Only populated for single-type (filtered) searches, which return one
    /// pageable <c>musicShelfRenderer</c>.
    /// </summary>
    public string? ContinuationToken { get; init; }
}

/// <summary>
/// Minimal placeholder for a search filter (e.g. Songs / Albums / Artists).
/// Carries the display label and the InnerTube params token; expanded in the
/// Search parser task.
/// </summary>
public sealed record SearchFilter(string Label, string? Params = null);

/// <summary>
/// One search-as-you-type suggestion from <c>music/get_search_suggestions</c>: either a plain query
/// completion / history entry, or a rich entity row (song/artist/album) with a thumbnail and a
/// subtitle like "Lagu • DAY6 • 351 jt pemutaran • Every DAY6 February".
/// </summary>
public sealed record SearchSuggestion(
    string Query,
    string? Subtitle = null,
    Uri? ThumbnailUrl = null,
    bool IsHistory = false)
{
    /// <summary>Browse target for a rich row (artist <c>UC…</c>, album <c>MPRE…</c>, playlist), when present.</summary>
    public string? BrowseId { get; init; }

    /// <summary>InnerTube page type of <see cref="BrowseId"/> (e.g. <c>MUSIC_PAGE_TYPE_ARTIST</c>).</summary>
    public string? PageType { get; init; }

    /// <summary>Video id for a rich song row, when present.</summary>
    public string? VideoId { get; init; }

    /// <summary>Whether this is a rich entity row (thumbnail + subtitle) rather than a plain query.</summary>
    public bool IsRich => ThumbnailUrl is not null || !string.IsNullOrEmpty(Subtitle);

    /// <summary>Whether a subtitle line is available to show.</summary>
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
}
