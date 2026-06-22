# Kaset for Windows (KasetWin)

A native **Windows** YouTube Music client built with **C# / .NET 8 + WinUI 3** (Windows App SDK).
It is a ground-up Windows port of the macOS [Kaset](https://github.com/sozercan/kaset) app: the
same idea (a clean, native music client for YouTube Music), rebuilt with Windows-native UI and
platform integrations.

> Status: pre-release / work in progress. Core music experience + most advanced features are
> implemented and the app builds and runs. See **Feature status** below.

## What it is

- **Native Windows shell** — Mica backdrop, `NavigationView` sidebar, bottom player bar (Fluent / WinUI 3).
- **Playback via a hidden WebView2** for DRM-protected audio; all data via the InnerTube API
  (`YTMusicClient`) using `SAPISIDHASH` auth — no scraping where an API exists.
- **System integration** — System Media Transport Controls (SMTC / Now Playing + media keys),
  toast notifications, DPAPI-protected credentials, `kaset://` protocol activation.

## Requirements

- Windows 10 (1809 / build 17763) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Windows App SDK 1.6 build tooling (restored via NuGet)
- WebView2 Runtime (preinstalled on Windows 11)
- For running an unsigned local build: **Developer Mode** enabled
  (Settings → Privacy & security → For developers → Developer Mode)

## Build

```powershell
# from the KasetWin/ folder
dotnet build KasetWin.sln -c Debug

# unit + property tests (headless, no WinUI)
dotnet test tests/KasetWin.Core.Tests/KasetWin.Core.Tests.csproj
```

The class libraries (`KasetWin.Core`, `KasetWin.Platform`, `KasetWin.ApiExplorer`, tests) restore
and build without MSIX tooling. The packaged WinUI app (`KasetWin.App`) needs the Windows App SDK
build tools.

> Tip: if a parallel build hits an XamlCompiler file lock, run
> `dotnet build-server shutdown` and add `-p:UseSharedCompilation=false`.

## Run (local, unsigned MSIX)

```powershell
# build the packaged app for x64
dotnet build src/KasetWin.App/KasetWin.App.csproj -c Debug -p:Platform=x64

# register the loose layout (Developer Mode must be ON), then launch from Start menu ("Kaset")
Add-AppxPackage -Register src/KasetWin.App/bin/x64/Debug/net8.0-windows10.0.19041.0/AppxManifest.xml
```

Or open `KasetWin.sln` in Visual Studio 2022, set **KasetWin.App** as startup, pick **x64**, press **F5**.

First launch shows the shell; signing in with a Google account (via the embedded WebView2) populates
Home / Library and enables playback.

## Project layout

| Project | Role |
|---------|------|
| `KasetWin.Core` | Headless domain core — models, InnerTube client + parsers, queue/player/auth logic, lyrics, settings. No WinUI dependency, fully unit/property-testable. |
| `KasetWin.Platform` | WinRT adapters — WebView2 playback controller, SMTC, DPAPI credentials, image decode/cache, network monitor. |
| `KasetWin.App` | WinUI 3 app — pages, ViewModels (MVVM), DI/Generic Host bootstrap, the hidden playback WebView2 host. |
| `KasetWin.ApiExplorer` | Console tool to explore InnerTube endpoints (`auth` / `list` / `browse`). |
| `tests/KasetWin.Core.Tests` | xUnit + **CsCheck** property-based tests over the headless core. |

## Feature status

**Implemented:** Home / Explore (Charts, Moods & Genres, New Releases) / Search / Library (with
optimistic mutations) / Playlist / Album / Artist / Queue / Lyrics (synced + plain) / Settings /
History; Infinite Mix & radio; Favorites; Podcasts (region-aware); Share; toast notifications +
offline indicator; floating/PiP video (OMV detection); a parallel **YouTube mode** (Home /
Subscriptions / History / Explore / Watch + comments / Shorts) with a `PlaybackArbiter` that keeps a
single audio source active; `kaset://` protocol activation; localization (en/fr/ko/id/tr/ar) + RTL.

**Deferred / not yet:** Last.fm scrobbling; the live YouTube watch WebView2 surface (currently shows
metadata + thumbnail); Equalizer, haptics, AI features (intentionally out of scope for the Windows port).

## Security

Never commit real cookies, tokens, `SAPISID`/`__Secure-3PAPISID` values, or any credentials. Test
fixtures use sanitized placeholders only. Secrets at runtime are stored via DPAPI / the Windows
Credential Locker, never in the repo.

## Credits

Based on [Kaset](https://github.com/sozercan/kaset) by Sertaç Özercan. This Windows port is an
independent reimplementation in C#/WinUI 3.
