namespace KasetWin.Core.Services.Localization;

/// <summary>
/// Pure layout-direction policy (Req 19.2): decide whether a language must be laid out
/// right-to-left (RTL).
/// </summary>
/// <remarks>
/// Kept in <c>Core</c> (no WinUI dependency) so it is exercised headless by Property 42: <em>layout
/// direction is RTL if and only if the language is in the RTL set</em>. The WinUI layer maps the
/// boolean to <c>FlowDirection.RightToLeft</c>/<c>LeftToRight</c> on the window's root element.
/// </remarks>
public static class LayoutDirection
{
    /// <summary>
    /// Languages laid out right-to-left. Per Req 19.1/19.2 the only supported RTL UI language is
    /// Arabic (<c>ar</c>). Compared on the primary subtag, so <c>ar-SA</c> etc. are covered.
    /// </summary>
    public static IReadOnlyList<string> RtlLanguages { get; } = ["ar"];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="language"/> is written right-to-left
    /// (i.e. its primary subtag is in <see cref="RtlLanguages"/>); otherwise <see langword="false"/>.
    /// Blank/<see langword="null"/> input is treated as left-to-right.
    /// </summary>
    public static bool IsRtl(string? language)
    {
        var primary = LanguageTag.PrimarySubtag(language);
        if (primary.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < RtlLanguages.Count; i++)
        {
            if (string.Equals(LanguageTag.PrimarySubtag(RtlLanguages[i]), primary, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
