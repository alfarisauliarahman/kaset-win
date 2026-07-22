# Upstream Sync — Kaset (macOS) → Kaset WinUI 3

Dokumen ini melacak **delta** antara repo asli `sozercan/kaset` (macOS/Swift) dan port **KasetWin (WinUI 3)**. Spec WinUI 3 (`requirements.md` / `design.md` / `tasks.md`) dibuat dari snapshot Kaset **sebelum** batch commit di bawah. Dokumen ini mencatat fitur/fix upstream baru dan status portingnya, supaya port tidak drift.

- **Dibuat:** 2026-07-03
- **Baseline port:** snapshot Kaset sebelum PR #314
- **Sinkron terakhir sampai commit:** `bd68513` (PR #341), tag rilis **v0.12.0**
- **Sumber kebenaran:** kode Swift di `Sources/` = referensi; port hidup di `KasetWin/`

> Catatan: spec **kanonik tunggal** ada di `KasetWin/.kiro/specs/kaset-winui3/` — di dalam repo git port ini, jadi ikut ter-commit dan ter-push. Salinan lama di root workspace sudah dinonaktifkan (2026-07-22, diganti nama jadi `M:\kaset\kaset\.kiro.duplicate-removed-20260722\` — boleh dihapus kapan saja): root itu milik repo Kaset macOS upstream dan spec di sana tidak pernah ter-version-control, sehingga sempat drift (requirements/tasks di root lebih baru daripada salinan di sini). Jangan membuat salinan di luar repo ini lagi.

---

## Legenda status

- **PERLU TASK** — fitur/perilaku baru yang belum ada di spec WinUI 3; perlu requirement/task baru.
- **CERMINKAN** — perbaikan perilaku/logika yang perlu ditiru di port bila area terkait sudah dibangun.
- **CATATAN PERF** — optimasi; tidak wajib, tapi baik ditiru saat menyentuh area yang sama.
- **N/A** — spesifik Apple/Swift atau docs/CI; tidak relevan untuk port.

---

## A. Fitur baru — PERLU TASK

| Upstream | Fitur | Padanan Windows / catatan port | Posisi di spec kita |
|---|---|---|---|
| #341 | Nama artis di header album/playlist jadi **link yang bisa diklik** → buka halaman artis | Jadikan nama artis di `AlbumPage`/`PlaylistPage` sebuah `HyperlinkButton`/klik → `NavigateToArtist(browseId)` | Belum ada. Req 14 punya detail album/playlist, Req 15 punya halaman artis, tapi afordans link ini belum ditulis |
| #326 | **Seek maju/mundur 30 detik** (mode YouTube video) | Tombol +30s/−30s di player bar video; clamp `[0, Duration]` (sudah ada Property 11). Ref: `YouTubePlayerService+Seeking.swift` | Belum ada task; area seek ada di Player (Tugas 11) |
| #334 | Entri **Like/Unlike di Dock menu** | Padanan: tombol **SMTC thumbbutton** / Jump List taskbar Windows untuk toggle like lagu yang sedang diputar | Belum ada; SMTC ada di Tugas 10 (Now Playing) — tambah aksi like |
| #314, #327, #331 | **Redesign player bar** (scrubber ala Apple Music, layout + kontrol baru, interaksi antar-halaman) | Redesain visual `PlayerBar.xaml`: scrubber, marquee judul, artwork glow, vertical volume slider, seek-hold. Ref file baru: `AppleMusicScrubber`, `PlayerBarProgressLane`, `PlayerBarMarqueeText`, `PlayerBarVerticalSlider`, dll | Player bar sudah dibangun (Tugas 12), tapi ini redesign besar — pertimbangkan task "polish UI player bar" |

## B. Perbaikan perilaku — CERMINKAN

| Upstream | Fix | Dampak ke port |
|---|---|---|
| #318 | History tidak tercatat di **Brand Account** (musik + video) — perlu switch sesi Brand account. Upstream menambah **ADR 0023** (`0023-brand-account-history-session-switch.md`) | Logika `AuthService`/multi-akun + History di port harus tahu konsep sesi Brand account. **Baca ADR 0023** sebelum menyentuh auth/history |
| #345 | API key gagal di balik **EU consent wall** YouTube. Upstream tambah `APISessionConfiguration.swift` | `YTMusicClient`/InnerTube di port harus menangani consent wall EU saat resolusi API key/cookie |
| #319 | Media-key **"next" mengulang lagu yang sama** saat app di background | Cek jalur SMTC/media-key di `SmtcController` + `PlayerService` port agar next benar-benar maju (videoId otoritatif) |
| #322 | Kontrak **ukuran main window** dipaksakan. Upstream tambah `MainWindowLayout.swift` | Terapkan aturan sizing yang setara di `MainWindow` WinUI (min/max/persist ukuran) |
| #336 | Ikon sidebar **kehilangan branding warna** setelah navigasi | Pastikan `NavigationView`/sidebar port mempertahankan warna ikon aktif setelah navigasi |

## C. Optimasi — CATATAN PERF

| Upstream | Optimasi | Catatan |
|---|---|---|
| #335 | Optimasi parser + **load coalescing** | Saat menyentuh parser/single-flight di port, bandingkan dengan pola coalescing upstream |
| #346 | Optimasi **parser + queue hot paths** | Relevan untuk `QueueService`/parser Core port |

## D. Tidak relevan untuk port — N/A

- #329 `fix(build): support SwiftPM build layout` — build Swift.
- #315 expose `.agents/skills` via `.claude/skills` symlink — tooling repo.
- #337 `host playback webviews for web extensions` — infra **WebExtensions** (spesifik macOS/WebKit); ekstensi web di luar scope port WinUI 3 saat ini.
- #317 bump `actions/checkout`, #330 bump `actions/cache` — CI GitHub Actions.
- #321, #328 docs YouTube + screenshot README — dokumentasi.
- #323, #324 update appcast v0.12.0 — auto-update Sparkle (padanan port = MSIX/AppInstaller, sudah dicatat di design).

---

## Status implementasi port (per 2026-07-03, fokus YouTube Music)

| Task | Fitur | Status |
|---|---|---|
| 30.1 | Nama artis clickable di album/playlist | ✅ **Selesai** (build hijau) |
| 30.7 | Fix Next mengulang lagu di Repeat One | ✅ **Selesai + teruji** (6 test, 381 lulus) |
| 30.8 | Kontrak ukuran main window | ✅ **Selesai** (build hijau) |
| 30.9 | Warna ikon sidebar | ⚪ **N/A** (tak ada ikon berwarna brand di port) |
| 30.2 | Seek ±30 detik | ⏸️ Ditunda — mode YouTube video (di luar fokus) |
| 30.3 | Like/Unlike lagu aktif | 🟡 **Sebagian** — tombol Like in-app di player bar (build hijau); surface taskbar ditunda; verifikasi butuh app berjalan |
| 30.4 | Redesain player bar | ⏸️ Ditunda — UI besar/subjektif |
| 30.5 | History Brand Account | ⏸️ Ditunda — butuh akun/sesi nyata |
| 30.6 | EU consent wall | ⏸️ Ditunda — butuh API region EU nyata |

## Ringkasan tindakan yang disarankan

1. **Tambah task PERLU TASK (bag. A)** ke `tasks.md` (dan requirement bila perlu):
   - Nama artis clickable di album/playlist (#341)
   - Seek ±30 detik (#326)
   - Like/Unlike via SMTC/Jump List (#334)
   - Polish/redesain player bar (#314/#327/#331)
2. **Baca ADR 0023** (Brand account history) sebelum menyentuh auth/history di port (#318).
3. **Tangani EU consent wall** di jalur API key/cookie port (#345).
4. Saat menyentuh area terkait, **cerminkan fix** #319/#322/#336 dan **optimasi** #335/#346.
