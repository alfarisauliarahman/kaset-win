namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure helpers for the moods/genres category browse identity (Req 31.2/31.3). The
/// <see cref="HomeResponseParser"/> projects a moods/genres navigation button into a
/// <c>PlaylistItem</c> whose id combines the browse id and its params token
/// (<c>"{browseId}_{params}"</c>) so the same browse id can repeat across sections without
/// colliding for list virtualization. This helper recognises such ids and splits them back into
/// the <c>(browseId, params)</c> pair the <c>browse</c> request needs.
/// </summary>
/// <remarks>
/// Mirrors the macOS <c>MoodCategory</c> model: a mood/genre category always browses through the
/// canonical <c>FEmusic_moods_and_genres_category</c> endpoint with a category-specific
/// <c>params</c> token; everything after <c>"FEmusic_moods_and_genres_category_"</c> in the
/// combined id is the params token. All comparisons are ordinal and the methods never throw.
/// </remarks>
public static class MoodCategoryId
{
    /// <summary>Prefix shared by every moods/genres surface and category browse id.</summary>
    public const string Prefix = "FEmusic_moods_and_genres";

    /// <summary>Canonical browse id for a single mood/genre category page.</summary>
    public const string CategoryBrowseId = "FEmusic_moods_and_genres_category";

    /// <summary>
    /// True when <paramref name="id"/> is (or encodes) a moods/genres category — i.e. it starts
    /// with <see cref="Prefix"/>. <c>null</c>/empty yields <see langword="false"/>.
    /// </summary>
    public static bool IsMoodCategory(string? id) =>
        !string.IsNullOrEmpty(id) && id.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Splits a combined moods/genres id into the <c>(browseId, params)</c> pair for a
    /// <c>browse</c> request. The canonical <see cref="CategoryBrowseId"/> base is returned as the
    /// browse id and everything after <c>"{CategoryBrowseId}_"</c> becomes the params token; an id
    /// equal to the base (or any other mood id without a params suffix) yields a <c>null</c> params.
    /// </summary>
    /// <param name="combinedId">The combined id produced by <see cref="HomeResponseParser"/>.</param>
    /// <returns>The browse id and optional params token to send to the <c>browse</c> endpoint.</returns>
    public static (string BrowseId, string? Params) Parse(string combinedId)
    {
        ArgumentException.ThrowIfNullOrEmpty(combinedId);

        if (combinedId == CategoryBrowseId)
        {
            return (CategoryBrowseId, null);
        }

        var withSeparator = CategoryBrowseId + "_";
        if (combinedId.StartsWith(withSeparator, StringComparison.Ordinal))
        {
            var paramsToken = combinedId[withSeparator.Length..];
            return (CategoryBrowseId, paramsToken.Length == 0 ? null : paramsToken);
        }

        // A moods/genres id that is not the category endpoint (e.g. the FEmusic_moods_and_genres
        // landing itself) browses as-is with no params.
        return (combinedId, null);
    }
}
