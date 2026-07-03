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
```

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
suite green. The spec that drives this project lives at the **workspace root** under
`../.kiro/specs/kaset-winui3/` (requirements / design / tasks + `upstream-sync.md`) — this is the
single canonical copy read by the Kiro tooling; do not re-create a copy inside `KasetWin/`.

## Credits

Windows port of [Kaset](https://github.com/sozercan/kaset) (macOS, Swift) by Sertaç Özercan.
