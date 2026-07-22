namespace KasetWin.Core.Services.Localization;

/// <summary>
/// The set of UI languages Kaset ships translations for (Req 19.1) and the fallback used when the
/// active/system locale is not among them (Req 19.3).
/// </summary>
/// <remarks>
/// <para>
/// Codes are primary language subtags (BCP-47). Keeping the canonical list here (pure, WinUI-free)
/// lets <see cref="LanguageSelector"/> and <see cref="LayoutDirection"/> be tested headless
/// (Property 42).
/// </para>
/// <para>
/// This list must only name languages the app can actually <b>render</b>. It previously also
/// claimed <c>fr</c>, <c>ko</c>, <c>tr</c> and <c>ar</c> on the strength of <c>.resw</c> folders
/// that were never wired up (no <c>x:Uid</c> anywhere), while every visible string in fact comes
/// from <c>UiStrings</c>, which is English-or-Indonesian. The mismatch was user-visible: on an
/// Arabic system <c>MainWindow.ApplyLanguageAndFlowDirection</c> selected <c>ar</c> and flipped the
/// whole window to right-to-left, and then filled that RTL layout with Indonesian text.
/// </para>
/// <para>
/// To add a language, translate <c>UiStrings</c> first, then add its subtag here — in that order.
/// The RTL machinery (<see cref="LayoutDirection"/>) is retained and tested, so an RTL language
/// works the moment its strings exist.
/// </para>
/// </remarks>
public static class SupportedLanguages
{
    /// <summary>Fallback language used when the requested locale is not supported (English, Req 19.3).</summary>
    public const string Fallback = "en";

    /// <summary>Supported UI languages: English and Indonesian — the two <c>UiStrings</c> renders (Req 19.1).</summary>
    public static IReadOnlyList<string> All { get; } = ["en", "id"];
}
