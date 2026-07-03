# ADR-0001: Pemutaran DRM via WebView2 + Projection/Packaging

## Status

Accepted

## Context

Kaset memutar konten YouTube Music ber-DRM (Widevine). Data (home, search, library, dll.) diambil lewat InnerTube API (`YTMusicClient` + `SAPISIDHASH`), tetapi **audio/video ber-DRM tidak bisa diputar lewat API** — harus lewat mesin browser yang mendukung Widevine. Di macOS asli hal ini ditangani oleh `WKWebView` tersembunyi (`SingletonPlayerWebView`). Port Windows butuh padanan.

Selain itu, memaketkan WebView2 untuk WinUI 3 (Windows App SDK) rawan dua kelas kegagalan build/packaging:
- **CS0012** ketika projeksi C#/WinRT WebView2 tidak konsisten (mis. menambah `PackageReference` WebView2 standalone di proyek App di samping projeksi milik Platform).
- **NETSDK1152 / APPX1101** karena payload WebView2 core terduplikasi saat publish MSIX.

## Decision

1. **Pemutaran lewat WebView2 tersembunyi** yang dimiliki `KasetWin.App` (satu instance, ukuran 1×1, mode Hidden/Mini/Video). Audio latar tetap berjalan saat window ditutup, berhenti saat quit. Kontrol (play/pause/seek/volume/mute) lewat `ExecuteScriptAsync`; state kembali lewat `WebMessageReceived` (pesan divalidasi karena untrusted). Deteksi DRM via `IsDrmAvailable`. Adapter ada di `KasetWin.Platform` (`WebView2PlaybackController` + `observer.js`).
2. **Projeksi WebView2 di-pin di `KasetWin.Platform`** (`<WebView2EnableCsWinRTProjection>true`, versi pinned). `KasetWin.App` **tidak boleh** menambah `PackageReference Microsoft.Web.WebView2` standalone — App memakai WebView2 bawaan Windows App SDK melalui projeksi Platform.
3. **Dedup payload publish**: App memakai MSBuild target `_DedupWebView2CorePayload` + `<ErrorOnDuplicatePublishOutputFiles>false>` untuk mencegah NETSDK1152/APPX1101.

## Consequences

- ✅ Pemutaran ber-DRM berjalan native di Windows tanpa API resmi; arsitektur "API-first, WebView hanya untuk playback + auth" tetap terjaga.
- ✅ Build MSIX konsisten; kelas error CS0012/NETSDK1152/APPX1101 dihindari selama aturan projeksi/dedup dipatuhi.
- ⚠️ Aturan packaging ini **rapuh** — mengubah referensi WebView2 di App atau menghapus target dedup akan memunculkan kembali error tersebut. Dicatat juga di `AGENTS.md` ("do not break").
- ⚠️ Interaksi playback bersifat asinkron via jembatan JS; pesan dari WebView diperlakukan untrusted dan harus divalidasi.

## Rujukan

- ADR asli macOS: `../../../docs/adr/0001-webview-playback.md`
- Aturan packaging: `KasetWin/AGENTS.md` (bagian "WebView2 projection / MSIX packaging")
- Spec: `.kiro/specs/kaset-winui3/design.md`
