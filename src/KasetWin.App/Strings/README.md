# Localization & RTL (Req 19)

This folder holds Kaset's PRI string resources and documents how the app selects a language at
runtime and applies right-to-left (RTL) layout. Implements Requirement 19 (Lokalisasi dan RTL).

## 0. Where the UI text actually comes from — read this first

Kaset ships **two** languages, English and Indonesian, and almost every visible string comes from
`KasetWin.App/Localization/UiStrings.cs`, **not** from the `.resw` files in this folder. `UiStrings`
is a static class of `IsIndonesian ? "…" : "…"` properties; controls read it in their constructor
and in `ApplyLanguage()` / `ApplyLabels()`, which are re-invoked when the language setting changes.

The `.resw` mechanism below is retained but currently carries only a small legacy key set and is
bound by nothing (`x:Uid` count in the app: **0**). Treat `UiStrings` as the source of truth.

> **History.** This folder used to hold `fr-FR`, `ko-KR`, `tr-TR` and `ar` stubs, and
> `SupportedLanguages.All` listed all six. None of them were reachable — no `x:Uid` bound to them —
> so the app advertised languages it could not render. That was user-visible for Arabic: language
> selection picked `ar` and flipped the window to RTL, then filled the mirrored layout with
> Indonesian text from `UiStrings`. The stubs were removed and `SupportedLanguages.All` narrowed to
> `["en", "id"]` so the promise matches the strings that exist.

**To add a language:** translate `UiStrings` first (today that means converting its two-way ternaries
into a lookup keyed by language), then add the subtag to `SupportedLanguages.All`, then — if you also
want `.resw`-driven strings — add `Strings/<tag>/Resources.resw`. In that order. The RTL machinery is
kept and property-tested, so an RTL language works the moment its strings exist.

## 1. Resource layout (`.resw`)

```
Strings/
  en-US/Resources.resw   ← default / neutral language (Req 19.3 fallback)
  id-ID/Resources.resw   ← Indonesian
```

Both files share the same stable, English-named keys (`Nav_Home`, `Nav_Explore`, `Nav_Search`,
`Nav_Library`, `Nav_Settings`, `Player_Play`, `Player_Pause`, `Player_Next`, `Player_Previous`,
`Common_Loading`, `Common_Retry`, `Common_Cancel`, `Settings_Language`). Only the values are
translated. Any new key must be added to **both** files.

Build wiring:

- The .NET SDK globs `Strings\**\*.resw` as `PRIResource` automatically (`EnableDefaultPriItems`), so
  each file is compiled into the app's PRI (Package Resource Index). Do **not** re-declare them in
  the csproj — that triggers NETSDK1022 duplicate-item errors.
- `<DefaultLanguage>en-US</DefaultLanguage>` (csproj) makes English the neutral/default candidate.
- `Package.appxmanifest` declares `<Resource Language="x-generate" />`, which expands at build time
  to one entry per language present in the PRI.

## 2. Language selection (Req 19.3) — pure logic in Core

The selection policy lives in `KasetWin.Core` (no WinUI dependency) so it is unit/property testable
headless (Property 42):

- `KasetWin.Core.Services.Localization.SupportedLanguages.All` → `["en","id"]`,
  `SupportedLanguages.Fallback` → `"en"`.
- `LanguageSelector.Select(string? locale, IReadOnlyList<string> supported, string fallback = "en")`
  → returns the locale's primary subtag when supported, otherwise the fallback. Matching is on the
  primary BCP-47 subtag, so `en-US`→`en`, `id-ID`→`id`, `fr-FR`→`en` (unsupported → fallback).
- `LayoutDirection.IsRtl(string? language)` → `true` iff the language's primary subtag is in
  `LayoutDirection.RtlLanguages` (currently `["ar"]`). No shipping language is RTL today, so this
  evaluates to `false` in practice — it is kept ready for the first RTL translation.

## 3. Runtime resolution (WinUI layer)

`MainWindow.ApplyLanguageAndFlowDirection()` performs both steps at startup:

```csharp
using KasetWin.Core.Services.Localization;
using Microsoft.Windows.Globalization; // ApplicationLanguages

// System UI language, e.g. "id-ID".
var systemLocale = System.Globalization.CultureInfo.CurrentUICulture.Name;

// Pure Core policy → "id" if supported, else "en" (Req 19.3).
var language = LanguageSelector.Select(systemLocale, SupportedLanguages.All);

// Drive PRI resolution for any x:Uid / ResourceLoader strings.
ApplicationLanguages.PrimaryLanguageOverride = language;
```

Note that the **user-facing** language is the one picked in Settings and read by
`SettingsViewModel.LoadLanguageSetting()`, which is what `UiStrings` consults. The call above governs
PRI resolution and layout direction only.

Reading a `.resw` string, if you ever add one, is either declarative (`x:Uid="Nav_Home"` on a
`TextBlock` binds its `Text` to `Nav_Home`) or imperative:

```csharp
var loader = Microsoft.Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();
string home = loader.GetString("Nav_Home");
```

## 4. RTL layout (Req 19.2) — applying `FlowDirection`

`FlowDirection` is inherited down the visual tree, so it is set **once on the window's root element**
and every child (NavigationView, lists, player controls) flips automatically:

```csharp
RootGrid.FlowDirection = LayoutDirection.IsRtl(language)
    ? FlowDirection.RightToLeft
    : FlowDirection.LeftToRight;
```

Notes:

- `MainWindow` is a `Window`, which has no `FlowDirection` property itself — set it on the root
  `FrameworkElement` (here `RootGrid`). Any element swapped in as the window content should have its
  `FlowDirection` set the same way.
- Apply the direction to the **selected** language, never to the raw system locale. Selecting first
  is what keeps an Arabic system on an LTR layout while Arabic has no translation.
- Because selection and direction are pure functions in Core, the mapping
  *language → (resources, FlowDirection)* is fully covered by Property 42 without launching the UI.
