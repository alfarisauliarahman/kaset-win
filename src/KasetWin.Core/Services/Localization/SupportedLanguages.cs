namespace KasetWin.Core.Services.Localization;

/// <summary>
/// The set of UI languages Kaset ships translations for (Req 19.1) and the fallback used when the
/// active/system locale is not among them (Req 19.3).
/// </summary>
/// <remarks>
/// Codes are primary language subtags (BCP-47). The on-disk <c>.resw</c> resource folders in
/// <c>KasetWin.App</c> use fuller tags (<c>en-US</c>, <c>fr-FR</c>, <c>ko-KR</c>, <c>id-ID</c>,
/// <c>tr-TR</c>, <c>ar</c>); the Windows Resource Management System resolves a primary subtag such
/// as <c>fr</c> to <c>fr-FR</c> at runtime. Keeping the canonical list here (pure, WinUI-free) lets
/// <see cref="LanguageSelector"/> and <see cref="LayoutDirection"/> be tested headless (Property 42).
/// </remarks>
public static class SupportedLanguages
{
    /// <summary>Fallback language used when the requested locale is not supported (English, Req 19.3).</summary>
    public const string Fallback = "en";

    /// <summary>
    /// Supported UI languages: English, French, Korean, Indonesian, Turkish, Arabic (Req 19.1).
    /// </summary>
    public static IReadOnlyList<string> All { get; } = ["en", "fr", "ko", "id", "tr", "ar"];
}
