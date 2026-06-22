namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// The navigable kind a YouTube Music <c>browseId</c> resolves to, derived purely from
/// its prefix. Used by navigation routing (Property 25) and by several per-surface parsers
/// (Home, Library, Search) to decide whether an item is a playlist, album, artist, or
/// podcast show.
/// </summary>
public enum BrowseIdKind
{
    /// <summary>Playlist (incl. Liked Music <c>VLLM</c> and radio <c>RDCLAK</c>).</summary>
    Playlist,

    /// <summary>Album or single/EP.</summary>
    Album,

    /// <summary>Artist / channel / library-artist / user profile.</summary>
    Artist,

    /// <summary>Podcast show.</summary>
    Podcast,

    /// <summary>Prefix not recognised (e.g. a feature endpoint or generated id).</summary>
    Unknown,
}

/// <summary>
/// Pure, dependency-free classification of a YouTube Music <c>browseId</c> by prefix.
/// Mirrors the prefix table in <c>api-discovery.md</c> / <c>design.md</c> and is reused for
/// navigation routing (Property 25) and item typing inside the modular parsers.
/// </summary>
/// <remarks>
/// <para>Prefix table:</para>
/// <list type="bullet">
///   <item><description><c>VL</c>, <c>PL</c>, <c>RDCLAK</c> (and <c>VLLM</c> via <c>VL</c>) → <see cref="BrowseIdKind.Playlist"/></description></item>
///   <item><description><c>MPRE</c>, <c>OLAK</c> → <see cref="BrowseIdKind.Album"/></description></item>
///   <item><description><c>UC</c>, <c>MPLAUC</c> → <see cref="BrowseIdKind.Artist"/></description></item>
///   <item><description><c>MPSPP</c> → <see cref="BrowseIdKind.Podcast"/></description></item>
///   <item><description>anything else → <see cref="BrowseIdKind.Unknown"/></description></item>
/// </list>
/// <para>
/// All comparisons are ordinal. The method is total and deterministic: <c>null</c>/empty and
/// unrecognised prefixes return <see cref="BrowseIdKind.Unknown"/> (never throws).
/// </para>
/// </remarks>
public static class BrowseIdClassifier
{
    /// <summary>
    /// Classifies <paramref name="browseId"/> into a <see cref="BrowseIdKind"/> by prefix.
    /// </summary>
    /// <param name="browseId">The InnerTube browse id. <c>null</c>/empty yields <see cref="BrowseIdKind.Unknown"/>.</param>
    public static BrowseIdKind Classify(string? browseId)
    {
        if (string.IsNullOrEmpty(browseId))
        {
            return BrowseIdKind.Unknown;
        }

        // Podcast show ids (MPSPP) are checked before the album MP* family so the more
        // specific prefix wins.
        if (browseId.StartsWith("MPSPP", StringComparison.Ordinal))
        {
            return BrowseIdKind.Podcast;
        }

        // Library-artist (MPLAUC) before the generic UC channel check; both are artists.
        if (browseId.StartsWith("MPLAUC", StringComparison.Ordinal)
            || browseId.StartsWith("UC", StringComparison.Ordinal))
        {
            return BrowseIdKind.Artist;
        }

        if (browseId.StartsWith("MPRE", StringComparison.Ordinal)
            || browseId.StartsWith("OLAK", StringComparison.Ordinal))
        {
            return BrowseIdKind.Album;
        }

        // VL covers VLLM (Liked Music). RDCLAK is the radio/auto playlist prefix.
        if (browseId.StartsWith("VL", StringComparison.Ordinal)
            || browseId.StartsWith("PL", StringComparison.Ordinal)
            || browseId.StartsWith("RDCLAK", StringComparison.Ordinal))
        {
            return BrowseIdKind.Playlist;
        }

        return BrowseIdKind.Unknown;
    }
}
