using CsCheck;
using KasetWin.Core.Services.Localization;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests for the pure localization policy of the kaset-winui3 feature: language
/// selection with English fallback and right-to-left layout direction (Property 42). A single
/// <see cref="FactAttribute"/> running a minimum of 100 CsCheck iterations covers both facets.
/// </summary>
public class LocalizationProperties
{
    /// <summary>Region/script suffixes appended to a primary subtag to exercise normalization.</summary>
    private static readonly Gen<string> RegionSuffix = Gen.OneOfConst(
        string.Empty, "-US", "-GB", "-FR", "-KR", "-ID", "-TR", "-SA", "_Latn", "-419");

    // Feature: kaset-winui3, Property 42: Pemilihan bahasa dan arah tata letak
    // Validates: Requirements 19.2, 19.3
    [Fact]
    public void Property42_Language_selection_and_layout_direction()
    {
        var supported = SupportedLanguages.All;

        // (a) A supported locale (optionally with a region/script suffix) selects its own primary
        //     subtag, and layout direction is RTL iff that language is Arabic.
        var supportedScenario =
            from language in Gen.OneOfConst(supported.ToArray())
            from suffix in RegionSuffix
            select (language, locale: language + suffix);

        supportedScenario.Sample(
            s =>
            {
                var (language, locale) = s;

                Assert.Equal(language, LanguageSelector.Select(locale, supported));

                // Arabic is the only RTL UI language; every other supported language is LTR.
                Assert.Equal(language == "ar", LayoutDirection.IsRtl(locale));
            },
            iter: 100);

        // (b) Any unsupported/garbage locale falls back to English, and the fallback is LTR.
        //     A primary subtag of length >= 3 can never collide with the 2-letter supported codes.
        var garbageScenario =
            from baseTag in Gen.Char['a', 'z'].Array[3, 8].Select(chars => new string(chars))
            from suffix in RegionSuffix
            select baseTag + suffix;

        garbageScenario.Sample(
            locale =>
            {
                Assert.Equal(SupportedLanguages.Fallback, LanguageSelector.Select(locale, supported));
                Assert.False(LayoutDirection.IsRtl(locale));
            },
            iter: 100);

        // Blank/null locales also resolve to the English fallback (LTR).
        Assert.Equal(SupportedLanguages.Fallback, LanguageSelector.Select(null, supported));
        Assert.Equal(SupportedLanguages.Fallback, LanguageSelector.Select(string.Empty, supported));
        Assert.Equal(SupportedLanguages.Fallback, LanguageSelector.Select("   ", supported));
        Assert.False(LayoutDirection.IsRtl(null));
    }
}
