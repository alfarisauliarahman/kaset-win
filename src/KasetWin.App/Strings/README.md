# Localization & RTL (Req 19)

This folder holds Kaset's UI string resources and documents how the app selects a language at
runtime and applies right-to-left (RTL) layout. Implements Requirement 19 (Lokalisasi dan RTL) and
follows ADR-0013's "single source of truth per language" intent, adapted to the WinUI 3 / PRI model.

## 1. Resource layout (`.resw`)

```
Strings/
  en-US/Resources.resw   ← default / neutral language (Req 19.3 fallback)
  fr-FR/Resources.resw   ← French
  ko-KR/Resources.resw   ← Korean
  id-ID/Resources.resw   ← Indonesian
  tr-TR/Resources.resw   ← Turkish
  ar/Resources.resw      ← Arabic (RTL test language, Req 19.2)
```

Every file shares the same stable, English-named keys (e.g. `Nav_Home`, `Nav_Explore`,
`Nav_Search`, `Nav_Library`, `Nav_Settings`, `Player_Play`, `Player_Pause`, `Player_Next`,
`Player_Previous`, `Common_Loading`, `Common_Retry`, `Common_Cancel`, `Settings_Language`). Only the
values are translated. Add new strings to **all** language files using the same key.

Build wiring:

- `KasetWin.App.csproj` includes `Strings\**\*.resw` as `PRIResource`, so each file is compiled into
  the app's PRI (Package Resource Index).
- `<DefaultLanguage>en-US</DefaultLanguage>` (csproj) makes English the neutral/default candidate.
- `Package.appxmanifest` declares `<Resource Language="x-generate" />`, which expands at build time
  to one entry per language present in the PRI.

## 2. Language selection (Req 19.3) — pure logic in Core

The selection policy lives in `KasetWin.Core` (no WinUI dependency) so it is unit/property testable
headless (Property 42):

- `KasetWin.Core.Services.Localization.SupportedLanguages.All` → `["en","fr","ko","id","tr","ar"]`,
  `SupportedLanguages.Fallback` → `"en"`.
- `LanguageSelector.Select(string? locale, IReadOnlyList<string> supported, string fallback = "en")`
  → returns the locale's primary subtag when supported, otherwise the fallback. Matching is on the
  primary BCP-47 subtag, so `en-US`→`en`, `ar-SA`→`ar`.
- `LayoutDirection.IsRtl(string? language)` → `true` iff the language's primary subtag is in
  `LayoutDirection.RtlLanguages` (currently `["ar"]`).

## 3. Runtime resolution (WinUI layer)

At startup (e.g. in `App.OnLaunched`/`AppHost` bootstrap), choose the language from the system UI
culture and tell the Windows Resource Management System to use it:

```csharp
using KasetWin.Core.Services.Localization;
using Microsoft.Windows.Globalization; // ApplicationLanguages

// System UI language, e.g. "fr-FR". GlobalizationPreferences.Languages[0] is also valid.
var systemLocale = System.Globalization.CultureInfo.CurrentUICulture.Name;

// Pure Core policy → "fr" if supported, else "en" (Req 19.3).
var language = LanguageSelector.Select(systemLocale, SupportedLanguages.All);

// Drive PRI resolution. Empty string = follow system; an explicit tag overrides it
// (also used when the user picks a language in Settings, Req 18.4 / Settings_Language).
ApplicationLanguages.PrimaryLanguageOverride = language;
```

Strings are then read either declaratively via `x:Uid` in XAML (e.g. a `TextBlock` with
`x:Uid="Nav_Home"` binds its `Text` to the `Nav_Home` resource) or imperatively:

```csharp
var loader = Microsoft.Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();
string home = loader.GetString("Nav_Home");
```

## 4. RTL layout (Req 19.2) — applying `FlowDirection`

`FlowDirection` is inherited down the visual tree, so it is set **once on the window's root element**
and every child (NavigationView, lists, player controls) flips automatically.

In `MainWindow` (after `InitializeComponent`, or whenever the language changes), set the root
element's `FlowDirection` from the pure Core helper:

```csharp
using KasetWin.Core.Services.Localization;
using Microsoft.UI.Xaml;

// 'language' is the value chosen in step 3 (or read back from PrimaryLanguageOverride).
RootGrid.FlowDirection = LayoutDirection.IsRtl(language)
    ? FlowDirection.RightToLeft
    : FlowDirection.LeftToRight;
```

Notes:

- `MainWindow` is a `Window`, which has no `FlowDirection` property itself — set it on the root
  `FrameworkElement` (here `RootGrid`, or the root `Frame`/`NavigationView` once added). Any element
  swapped in as the window content should have its `FlowDirection` set the same way.
- For Arabic (`ar`), `LayoutDirection.IsRtl("ar")` is `true`, so the layout mirrors to RTL; all other
  supported languages stay `LeftToRight`.
- Because selection and direction are pure functions in Core, the mapping
  *language → (resources, FlowDirection)* is fully covered by Property 42 without launching the UI.

> The exact call sites in `MainWindow.xaml.cs` / `AppHost.cs` are added by the UI wiring task; this
> file is the contract those call sites follow.
