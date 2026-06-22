namespace KasetWin.Core.Services.Localization;

/// <summary>
/// Pure helpers for working with BCP-47 language tags (e.g. <c>en</c>, <c>en-US</c>, <c>ar-SA</c>).
/// </summary>
/// <remarks>
/// Lives in <c>Core</c> with no WinUI/WinRT dependency so language selection and layout-direction
/// logic can be exercised headless (Property 42) without <c>ApplicationLanguages</c>/PRI.
/// </remarks>
internal static class LanguageTag
{
    /// <summary>
    /// Returns the normalized primary language subtag of <paramref name="tag"/>: the portion before
    /// the first region/script separator (<c>-</c> or <c>_</c>), trimmed and lowercased
    /// (invariant). Returns <see cref="string.Empty"/> for <see langword="null"/>/blank input.
    /// </summary>
    /// <example><c>"en-US" → "en"</c>, <c>"ar"  → "ar"</c>, <c>"zh_Hans_CN" → "zh"</c>.</example>
    public static string PrimarySubtag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var trimmed = tag.Trim();
        var separator = trimmed.IndexOfAny(['-', '_']);
        var primary = separator >= 0 ? trimmed[..separator] : trimmed;
        return primary.ToLowerInvariant();
    }
}
