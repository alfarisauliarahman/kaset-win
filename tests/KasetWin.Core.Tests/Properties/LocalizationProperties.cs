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
    /// <summary>RTL-script locales the app ships no translation for.</summary>
    private static readonly string[] UntranslatedRtlLocales = ["ar", "ar-SA", "ar-EG", "he-IL", "fa-IR"];

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

    /// <summary>
    /// A language the app has no strings for must not drag the layout into its writing direction.
    ///
    /// Regression for a shipped bug: <c>SupportedLanguages.All</c> listed <c>ar</c> (plus fr/ko/tr)
    /// on the strength of <c>.resw</c> stubs that nothing bound, while every visible string came
    /// from <c>UiStrings</c> — English or Indonesian only. On an Arabic system the selector returned
    /// <c>"ar"</c>, <c>MainWindow</c> flipped the window to right-to-left, and the mirrored layout
    /// then filled with Indonesian text.
    ///
    /// The invariant that prevents it: direction is derived from the <b>selected</b> language, never
    /// the raw system locale, so an unsupported locale is LTR because its fallback is LTR.
    /// </summary>
    [Fact]
    public void Unsupported_rtl_locale_falls_back_to_english_and_stays_ltr()
    {
        var supported = SupportedLanguages.All;

        foreach (var locale in UntranslatedRtlLocales)
        {
            var selected = LanguageSelector.Select(locale, supported);

            Assert.Equal(SupportedLanguages.Fallback, selected);
            Assert.False(
                LayoutDirection.IsRtl(selected),
                $"'{locale}' has no translation, so the layout must follow its LTR fallback.");
        }

        // The RTL machinery itself is intact and ready for the first RTL translation — this is a
        // claim about coverage, not about LayoutDirection being broken.
        Assert.True(LayoutDirection.IsRtl("ar"));
    }

    /// <summary>
    /// Every advertised language must be one the app can actually render. <c>UiStrings</c> is the
    /// real source of visible text and is English-or-Indonesian, so the supported list may not grow
    /// past those two until <c>UiStrings</c> itself gains a language (see Strings/README.md).
    /// </summary>
    [Fact]
    public void Supported_languages_match_the_strings_that_exist()
    {
        Assert.Equal(new[] { "en", "id" }, SupportedLanguages.All);
        Assert.Contains(SupportedLanguages.Fallback, SupportedLanguages.All);
    }
}
