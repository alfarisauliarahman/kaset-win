# Architecture Decision Records — KasetWin

Direktori ini berisi Architecture Decision Records (ADR) untuk port **KasetWin** (Windows / WinUI 3). AGENTS.md mewajibkan: untuk perubahan desain signifikan, tambahkan ADR di sini.

ADR khusus port ini melengkapi ADR repo asli (macOS) di `../../../docs/adr/`. Bila sebuah keputusan port menggantikan padanan Apple-nya (mis. Sparkle→MSIX, Keychain→DPAPI, AppleScript→protocol activation), sebutkan ADR asli yang menjadi rujukan.

## Apa itu ADR?

Dokumen yang menangkap satu keputusan arsitektur penting beserta konteks dan konsekuensinya — agar konteks terjaga, onboarding cepat, dan diskusi lama tidak berulang.

## Format

```markdown
# ADR-NNNN: Judul

## Status
[Proposed | Accepted | Deprecated | Superseded by ADR-XXXX]

## Context
Masalah apa yang memotivasi keputusan ini?

## Decision
Perubahan apa yang diusulkan/dilakukan?

## Consequences
Apa yang jadi lebih mudah atau lebih sulit karena keputusan ini?
```

## Index

| ADR | Judul | Status |
|-----|-------|--------|
| [0001](0001-webview2-playback-and-projection.md) | Pemutaran DRM via WebView2 + Projection/Packaging | Accepted |

## Padanan platform (ringkas)

Peta keputusan port terhadap teknologi asli macOS. Detail penuh ada di `.kiro/specs/kaset-winui3/design.md`.

| Aspek | macOS (asli) | KasetWin (Windows) |
|-------|--------------|--------------------|
| Pemutaran DRM | `WKWebView` (`SingletonPlayerWebView`) | **WebView2** tersembunyi |
| Tampilan bahan | Liquid Glass / `.glassEffect()` | **Mica / Acrylic** (Fluent) |
| Penyimpanan kredensial | Keychain | **DPAPI** (`DpapiCredentialStore`) |
| Now Playing / media key | `MPNowPlayingInfoCenter` | **SMTC** (`SmtcController`) |
| Auto-update | Sparkle (appcast) | **MSIX / AppInstaller** (future work) |
| Otomasi | AppleScript | **Protocol activation** `kaset://` + arg CLI |
| AI / Apple Intelligence | Foundation Models | **Dihilangkan** dari lingkup |
