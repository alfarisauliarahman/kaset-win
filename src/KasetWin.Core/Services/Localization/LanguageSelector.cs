namespace KasetWin.Core.Services.Localization;

/// <summary>
/// Pure language-selection policy (Req 19.3): given a locale (typically the system/UI culture) and
/// the set of supported languages, pick the supported language when the locale matches, otherwise
/// fall back to English.
/// </summary>
/// <remarks>
/// <para>
/// Matching is performed on the normalized <em>primary language subtag</em> (see
/// <see cref="LanguageTag.PrimarySubtag"/>), so <c>"en-US"</c>, <c>"en-GB"</c> and <c>"en"</c> all
/// resolve to the supported <c>"en"</c>, and <c>"id-ID"</c> resolves to the supported <c>"id"</c>.
/// A locale whose subtag is not in the supported set resolves to the fallback, not to itself:
/// <c>"ar-SA"</c> gives <c>"en"</c> while Arabic has no translation. That is what keeps an Arabic
/// system on an LTR layout — direction is derived from the <em>selected</em> language.
/// </para>
/// <para>
/// Kept in <c>Core</c> (no WinUI dependency) so it is exercised headless by Property 42: <em>for any
/// locale code, the selected language equals the locale when supported, otherwise the fallback</em>.
/// The WinUI layer feeds the result to <c>ApplicationLanguages.PrimaryLanguageOverride</c>.
/// </para>
/// </remarks>
public static class LanguageSelector
{
    /// <summary>
    /// Selects the UI language for <paramref name="locale"/>.
    /// </summary>
    /// <param name="locale">
    /// The requested locale (e.g. the system culture name such as <c>"fr-FR"</c>). May be
    /// <see langword="null"/> or blank, in which case the result is <paramref name="fallback"/>.
    /// </param>
    /// <param name="supported">
    /// The set of supported language codes (primary subtags), e.g. <see cref="SupportedLanguages.All"/>.
    /// </param>
    /// <param name="fallback">
    /// The language returned when <paramref name="locale"/> is not supported. Defaults to English
    /// (<c>"en"</c>); a blank/null value is treated as <see cref="SupportedLanguages.Fallback"/>.
    /// </param>
    /// <returns>
    /// The normalized primary subtag of <paramref name="locale"/> when that subtag is present in
    /// <paramref name="supported"/>; otherwise the normalized <paramref name="fallback"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="supported"/> is <see langword="null"/>.</exception>
    public static string Select(string? locale, IReadOnlyList<string> supported, string fallback = "en")
    {
        ArgumentNullException.ThrowIfNull(supported);

        var normalizedFallback = LanguageTag.PrimarySubtag(fallback);
        if (normalizedFallback.Length == 0)
        {
            normalizedFallback = SupportedLanguages.Fallback;
        }

        var requested = LanguageTag.PrimarySubtag(locale);
        if (requested.Length == 0)
        {
            return normalizedFallback;
        }

        for (var i = 0; i < supported.Count; i++)
        {
            if (string.Equals(LanguageTag.PrimarySubtag(supported[i]), requested, StringComparison.Ordinal))
            {
                return requested;
            }
        }

        return normalizedFallback;
    }
}
