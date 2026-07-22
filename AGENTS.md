# AGENTS.md — KasetWin (Windows)

Guidance for AI coding assistants working on the **Windows** port of Kaset (`KasetWin/`).

## Role

You are a Senior .NET Engineer specializing in **C# 12 / .NET 8**, **WinUI 3 (Windows App SDK)**,
and Windows desktop development. Follow the Windows app design guidance (Fluent / WinUI). KasetWin is
a native Windows YouTube Music client: playback through a hidden **WebView2** (DRM audio) and all
data via the InnerTube API (`YTMusicClient`) using `SAPISIDHASH`.

## Critical rules

> 🚨 **NEVER leak secrets** — no real cookies, tokens, API keys, `SAPISID`/`__Secure-3PAPISID`, or
> credentials in code, comments, logs, fixtures, or output. Use placeholders (`"REDACTED"`,
> `"mock-token"`, `"test-cookie"`). Violation is a critical security incident.

> ⚠️ **Prefer API over WebView** — use `YTMusicClient` (InnerTube) wherever the functionality exists.
> WebView2 is only for playback (DRM audio) and authentication.

> ⚠️ **No third-party frameworks** without asking first.

> 🧩 **WebView2 projection / MSIX packaging (do not break):**
> - `KasetWin.Platform` provides the WebView2 C#/WinRT projection (pinned). **Do NOT** add a
>   standalone `Microsoft.Web.WebView2` `PackageReference` to `KasetWin.App` — it causes CS0012.
> - The App relies on the `_DedupWebView2CorePayload` target + `<ErrorOnDuplicatePublishOutputFiles>false>`
>   to avoid APPX1101 / NETSDK1152. Leave these intact.

> 📝 **Document architectural decisions** — for significant design changes, add an ADR under `docs/adr/`.

## Build & quality

```powershell
dotnet build KasetWin.sln -c Debug                                   # build
dotnet test tests/KasetWin.Core.Tests/KasetWin.Core.Tests.csproj     # headless unit + property tests
dotnet build src/KasetWin.App/KasetWin.App.csproj -c Debug           # XAML compile (CI runs this too)
```

- **Always build `KasetWin.App`, not just the tests.** A large class of errors — bad `x:Bind` paths,
  missing `x:Name`, unknown `StaticResource`, wrong namespace on an attached property — exists only
  at XAML-compile time and is invisible to `dotnet test`. CI has a dedicated `build-app` job for this.

- **A green build and a green suite do not mean the app runs.** Every defect in the 2026-07-23 round
  — a crash on launch, a player bar with the like button marooned mid-bar, synced lyrics that never
  once worked — passed both gates. Launch it before claiming a UI or playback change works.

- **Start at `docs/troubleshooting.md`** when anything misbehaves at runtime, and check
  `docs/known-issues.md` before "fixing" something — several entries there are deliberate trade-offs
  whose reversal reintroduces a different bug.

- **When it crashes, read `crash.log` first.** WinUI reports every unhandled exception as the same
  opaque `0xc000027b` in the Windows event log, with no type, message, or stack. The handler in
  `App.xaml.cs` writes the real exception to
  `%LOCALAPPDATA%\Packages\Kaset.KasetWin_…\LocalState\crash.log`. Do not bisect commits before
  looking there; the stack usually names the line outright.

- If a build hits an XamlCompiler file lock during parallel/iterated builds:
  `dotnet build-server shutdown` and pass `-p:UseSharedCompilation=false`.
- Stop a running instance before rebuilding the app:
  `Get-Process -Name "KasetWin.App","XamlCompiler" | Stop-Process -Force`.

## Architecture

- **`KasetWin.Core`** — headless, WinUI-free. Domain models (immutable records), InnerTube client +
  modular static parsers, queue/player/auth state, lyrics, settings. Must stay testable without WinUI.
- **`KasetWin.Platform`** — WinRT adapters (WebView2 playback, SMTC, DPAPI, imaging, network).
- **`KasetWin.App`** — WinUI 3 UI + MVVM ViewModels + Generic Host / DI; owns the single hidden
  playback WebView2 (background audio). Pages use a parameterless ctor and resolve services from
  `((App)Application.Current).Services`; detail navigation via `Frame` + `Type.GetType(...)` guard.
- **`KasetWin.ApiExplorer`** — console InnerTube explorer; improve it instead of writing one-off scripts.

## Coding rules

| ❌ Avoid | ✅ Use | Why |
|----------|--------|-----|
| `print` / ad-hoc `Console` | `ILogger` (Serilog) + secret redaction | Structured, redacted logging |
| `DispatcherQueue` misuse off-thread | marshal UI work via `DispatcherQueue.TryEnqueue` | UI-thread safety |
| Force-unwrap / `!` everywhere | nullable handling, `is { }` patterns | Nullable is enabled |
| Throwing parsers | return ignore/empty or `KasetError(ParseError)` | Resilient parsing |
| bare `ToolTipService.SetToolTip` | `A11y.Label(element, text)` | Sets the accessible name too (see below) |

> ♿ **Accessibility is not optional.** A tooltip is **not** read by Narrator, so an icon-only control
> with only a tooltip is announced as an unnamed "button". Use `KasetWin.App.Accessibility.A11y`:
> `Label` (name + tooltip from one string), `Name` (name only — sliders), `Decorative` (hide artwork
> that repeats adjacent text). Two rules with teeth:
> - Anything **icon-only or value-only** needs a name. XAML carries an English fallback;
>   `ApplyLanguage()` / `ApplyLabels()` re-applies it via `A11y` so the name follows the app language.
> - Do **not** name a control whose accessible name already comes from its text content — setting
>   `AutomationProperties.Name="Go to album"` on a link wrapping a song title *replaces* the title.
> See Req 38 in the spec and `Controls/TrackInfo.xaml.cs` for the documented exception.

> 🌐 **Localization reality check.** Visible text comes from `Localization/UiStrings.cs`
> (English/Indonesian), **not** from the `.resw` files — `x:Uid` usage in the app is 0. Never widen
> `SupportedLanguages.All` before `UiStrings` can render that language: the list drives
> `PrimaryLanguageOverride` *and* RTL layout direction, and a language with no strings produced an
> RTL window full of Indonesian text. Read `src/KasetWin.App/Strings/README.md` first.

- Map HTTP 401/403 → `KasetError(AuthExpired)`.
- `@Observable`-style VMs use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- Use **Swift Testing-equivalent**: xUnit (`[Fact]`) + **CsCheck** for property-based tests
  (min 100 iterations, one property per test, comment `// Feature: kaset-winui3, Property N: ...`).

## API discovery

Before implementing any new/changed API call, explore the endpoint first — do not guess response shapes:

```powershell
dotnet run --project src/KasetWin.ApiExplorer -- auth          # auth status
dotnet run --project src/KasetWin.ApiExplorer -- list          # known endpoints
dotnet run --project src/KasetWin.ApiExplorer -- browse FEmusic_home -v
```

## Task planning

For non-trivial work: **Research → Plan → Implement → Verify**. Build continuously; keep the test
suite green. The spec that drives this project lives **inside this repo** at
`.kiro/specs/kaset-winui3/` (requirements / design / tasks + `upstream-sync.md`) — the single
canonical copy. A second copy used to sit at the workspace root (`M:\kaset\kaset\.kiro\`), outside
this repo and therefore never committed; the two drifted and the root one was retired on
2026-07-22 (renamed to `.kiro.duplicate-removed-20260722`). Do not re-create a copy outside this repo.

## Credits

Windows port of [Kaset](https://github.com/sozercan/kaset) (macOS, Swift) by Sertaç Özercan.
