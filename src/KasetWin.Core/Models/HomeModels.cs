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
}

/// <summary>
/// Grouped search results. <see cref="TopResult"/> is the highlighted card
/// (<c>musicCardShelfRenderer</c>), the remaining lists are per-type groups.
/// </summary>
public sealed record SearchResponse
{
    public HomeSectionItem? TopResult { get; init; }

    public IReadOnlyList<Song> Songs { get; init; } = [];

    public IReadOnlyList<Album> Albums { get; init; } = [];

    public IReadOnlyList<Artist> Artists { get; init; } = [];

    public IReadOnlyList<Playlist> Playlists { get; init; } = [];

    /// <summary>Podcast shows surfaced in search (advanced phase, Req 27).</summary>
    public IReadOnlyList<Playlist> Podcasts { get; init; } = [];
}

/// <summary>
/// Minimal placeholder for a search filter (e.g. Songs / Albums / Artists).
/// Carries the display label and the InnerTube params token; expanded in the
/// Search parser task.
/// </summary>
public sealed record SearchFilter(string Label, string? Params = null);
