using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Library;

/// <summary>
/// Pure, dependency-free text filtering for the Library landing surface (Req 13.5): given a
/// <see cref="LibraryContent"/> and a free-text query, keep only the items whose display text
/// (playlist/album/song <c>Title</c>, artist <c>Name</c>) contains the query as a case-insensitive
/// substring.
/// </summary>
/// <remarks>
/// <para>
/// Kept in <c>Core</c> (no WinUI dependency) so the Library page's filter affordance is exercised
/// headless by Property 29: <em>for any collection and query, the result is a subset of the original
/// whose every item matches the query; an empty/blank query returns everything.</em> The WinUI
/// view-model feeds the filtered collections to its observable lists.
/// </para>
/// <para>
/// Matching is total and never throws: a <see langword="null"/>/blank query matches every item, and
/// a <see langword="null"/>/empty candidate text never matches a non-blank query.
/// </para>
/// </remarks>
public static class LibraryFilter
{
    /// <summary>
    /// Whether <paramref name="text"/> matches <paramref name="query"/>: a blank/<see langword="null"/>
    /// query matches everything; otherwise the (trimmed) query must appear in <paramref name="text"/>
    /// as a case-insensitive substring.
    /// </summary>
    public static bool Matches(string? text, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return !string.IsNullOrEmpty(text)
            && text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a new <see cref="LibraryContent"/> whose collections retain only the items matching
    /// <paramref name="query"/> (case-insensitive substring on each item's title/name). A
    /// blank/<see langword="null"/> query returns <paramref name="content"/> unchanged (all items).
    /// </summary>
    /// <param name="content">The library landing content to filter.</param>
    /// <param name="query">The free-text filter; blank/<see langword="null"/> means "no filter".</param>
    public static LibraryContent Filter(LibraryContent content, string? query)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(query))
        {
            return content;
        }

        return new LibraryContent
        {
            Playlists = content.Playlists.Where(p => Matches(p.Title, query)).ToList(),
            Albums = content.Albums.Where(a => Matches(a.Title, query)).ToList(),
            Artists = content.Artists.Where(a => Matches(a.Name, query)).ToList(),
            Songs = content.Songs.Where(s => Matches(s.Title, query)).ToList(),
        };
    }
}
