# Contributing to KasetWin

Thanks for your interest in contributing! This document covers development setup, project structure, and guidelines.

## Getting Started

### Requirements

- Windows 10 (1809 / build 17763) or later — Windows 11 recommended
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Windows App SDK 1.6 build tooling (restored via NuGet)
- WebView2 Runtime (preinstalled on Windows 11)
- **Developer Mode** enabled (Settings → Privacy & security → For developers) to run a local unsigned build
- Optional: Visual Studio 2022 with the **Windows App SDK** workload

### Build & Run

```powershell
# Clone
git clone https://github.com/alfarisauliarahman/kaset-win.git
cd kaset-win

# Dev build (packaged / MSIX)
dotnet build src/KasetWin.App/KasetWin.App.csproj -c Debug

# Headless unit + property tests (no WinUI needed)
dotnet test tests/KasetWin.Core.Tests/KasetWin.Core.Tests.csproj
```

Or open `KasetWin.sln` in Visual Studio 2022, set **KasetWin.App** as the startup project, pick **x64**, and press **F5**.

> Tip: if a parallel build hits an XamlCompiler file lock, run `dotnet build-server shutdown` and add `-p:UseSharedCompilation=false`.

## Project Structure

| Project | Role |
|---------|------|
| `KasetWin.Core` | Headless domain core — models, InnerTube client + parsers, queue/player/auth logic, lyrics, settings. No WinUI dependency; fully unit/property-testable. |
| `KasetWin.Platform` | WinRT adapters — WebView2 playback, SMTC, DPAPI credentials, image cache, network monitor, cross-mode storage. |
| `KasetWin.App` | WinUI 3 app — pages, ViewModels (MVVM), DI/Generic Host bootstrap, the hidden playback WebView2 host. |
| `KasetWin.ApiExplorer` | Console tool to explore InnerTube endpoints. |
| `tests/KasetWin.Core.Tests` | xUnit + CsCheck property-based tests over the headless core. |

Business logic lives in `KasetWin.Core` (headless, testable). Keep WinUI/WinRT dependencies in `KasetWin.App` / `KasetWin.Platform`.

## Guidelines

- **Write it like the surrounding code** — match existing naming, comment density, and idioms.
- **Add tests** for new core logic in `KasetWin.Core.Tests`; run them before opening a PR.
- **Verify UI/runtime changes** by actually running the app, not just building.
- **Never commit secrets** — no cookies, tokens, `SAPISID` / `__Secure-3PAPISID`, or account identifiers. Test fixtures use sanitized placeholders only.
- Follow **[Conventional Commits](https://www.conventionalcommits.org/)** for commit messages (e.g. `feat(search): ...`, `fix(player): ...`).
- Use **[Semantic Versioning](https://semver.org/)** (`MAJOR.MINOR.PATCH`) for releases.

## AI-Assisted Contributions & Prompt Requests

AI-assisted contributions are welcome. You can submit a traditional PR, and optionally share the prompt that generated your changes (see the PR template) so reviewers can understand the intent and iterate on it.

## Disclaimer

KasetWin is an unofficial application and is not affiliated with YouTube or Google Inc. Do not use it to violate YouTube's Terms of Service.
