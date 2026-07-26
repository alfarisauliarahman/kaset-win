# Kaset untuk Windows (KasetWin)

[English](README.md) · **Bahasa Indonesia**

Klien **Windows** native untuk YouTube Music, dibangun dengan **C# / .NET 8 + WinUI 3** (Windows App SDK). Port Windows dari awal untuk aplikasi macOS [Kaset](https://github.com/sozercan/kaset) — ide yang sama (klien musik native yang bersih untuk YouTube Music) dengan desain terinspirasi Apple Music, dibangun ulang memakai UI native Windows dan integrasi platform.

<table>
  <tr>
    <th>Beranda</th>
    <th>Sedang Diputar + Lirik</th>
  </tr>
  <tr>
    <td><img src="docs/screenshot-home.png" alt="Tangkapan layar Beranda KasetWin"></td>
    <td><img src="docs/screenshot-nowplaying.png" alt="Tangkapan layar panel sedang-diputar KasetWin"></td>
  </tr>
</table>

## Fitur

- 🪟 **Pengalaman Native Windows 11** — Desain WinUI 3 / Fluent dengan latar Mica, player bar gaya Apple Music, sidebar yang bersih, dan toggle sumber Music ↔ YouTube
- 🎧 **Dukungan YouTube Music** — Pemutaran penuh konten YouTube Music berproteksi DRM lewat langganan Premium yang kamu punya (di-host di WebView2 latar belakang yang tersembunyi)
- 🔊 **Audio Latar Belakang** — Musik tetap jalan saat jendela ditutup; berhenti hanya saat keluar secara eksplisit
- 🧭 **Jelajah** — Rilis baru, tangga lagu (dengan peringkat dan panah tren naik/turun), serta mood & genre
- 🎙️ **Podcast** — Jelajahi acara dan channel kreator, putar episode, serta lacak progres per-episode dan status sudah-diputar
- 📚 **Perpustakaan** — Playlist, lagu disukai, unggahan, artis yang diikuti, dan album tersimpan; buat, ubah, dan hapus playlist milikmu, tambahkan lagu ke playlist, dan pasang sampul playlist lokal kustom
- 🕓 **Riwayat** — Buka kembali lagu yang baru diputar
- 🔍 **Pencarian** — Hasil gaya Apple Music (hasil teratas, artis, album, lagu, video musik, playlist, podcast, episode) dengan saran kaya dan riwayat pencarian lokal
- 🎚️ **Panel Sedang Diputar** — Panel samping ter-dok gaya Apple Music untuk antrean putar dan lirik tersinkron
- 📜 **Lirik** — Lirik tersinkron waktu dengan sorotan baris-per-baris, bergulir gaya Apple Music (baris aktif naik ke atas). **YouTube Music jadi sumber pertama**: ia mencocokkan lewat videoId, bukan menebak dari judul/artis, dan katanya adalah salinan resmi berlisensi dari label — dikreditkan per lagu ke LyricFind atau Musixmatch, mana pun yang dikirim YouTube. LRCLib dan NetEase (cakupan Asia/K-pop bagus) tetap jadi cadangan, dan sumbernya selalu ditampilkan sehingga ketahuan lirik itu datang dari mana. Tidak semua lagu punya versi tersinkron. Episode podcast menampilkan teks (CC) YouTube sebagai "lirik" tersinkron dengan opsi sorotan karaoke kata-per-kata
- 📃 **Kelola Antrean** — Lihat, susun ulang, acak, dan isi-ulang otomatis (radio) antrean pemutaran
- 🎛️ **Equalizer** — Equalizer 9-band dengan preset, diterapkan ke keluaran pemutaran WebView2
- 🪟 **Mini Player** — Kecilkan jendela jadi overlay ringkas yang selalu di atas (CompactOverlay Windows) berisi sampul, judul, transport, dan scrubber; pemutaran tidak terputus saat berpindah mode
- 😴 **Timer Tidur** — Hentikan pemutaran setelah 15/30/45/60 menit atau di akhir lagu yang sedang diputar, lengkap dengan hitung mundur di player bar
- 🎮 **Discord Rich Presence** — Opsional: tampilkan lagu yang sedang kamu dengar di profil Discord. Cukup satu toggle, tanpa setup; mati secara default karena ini menyiarkan apa yang kamu dengar ke publik
- ⌨️ **Pintasan Global** — Opsional: kendali pemutaran dari aplikasi mana pun (Ctrl+Alt+↓/→/←/↑), berguna kalau keyboardmu tidak punya tombol media
- 📐 **Ukuran Jendela Diingat** — Dibuka lagi di ukuran dan posisi terakhir, dicek dulu apakah masih muat di layar yang terpasang
- 🌗 **Mode Terang / Gelap** — Ikuti Windows, atau paksa terang atau gelap
- ⌨️ **Pintasan Keyboard** — Kendali keyboard penuh (skema gaya YouTube Music) untuk pemutaran, geser posisi, volume, suka/tidak-suka, lirik, dan navigasi — tekan **Shift + /** untuk contekan pintasan di dalam app
- 🖥️ **Integrasi Sistem** — Kontrol Now Playing / transport media Windows (SMTC), dukungan tombol media, kontrol thumbbar taskbar, dan notifikasi pergantian lagu
- 📣 **Bagikan** — Bagikan lagu, playlist, album, dan artis lewat share sheet Windows
- 🔗 **Skema URL** — Buka lagu langsung dengan `kaset://play?v=VIDEO_ID`
- 🧩 **Ekstensi (Adblock)** — Memuat ekstensi browser tak-terpaket ke WebView2 pemutaran, dengan uBlock Origin diunduh otomatis dan selalu diperbarui
- 🌍 **Terlokalisasi** — Antarmuka terlokalisasi penuh dalam **Bahasa Indonesia** dan **Inggris** (chrome, menu, dialog, dan toast mengikuti bahasa app; konten YouTube Music mengikuti penyematan bahasa yang sama)
- ▶️ **Mode YouTube** — *Dalam pengerjaan.* Toggle Music ↔ YouTube dan kerangka untuk permukaan YouTube penuh (beranda / tonton / langganan / shorts / riwayat) sudah ada, tapi mode ini **belum selesai**.

> Di luar cakupan port Windows (hanya-macOS di aplikasi asli): Apple Intelligence / AI on-device, haptics, AppleScript, dan share sheet macOS.

## Persyaratan

- Windows 10 versi 1809 (build 17763) atau lebih baru — Windows 11 disarankan
- WebView2 Runtime (sudah terpasang di Windows 11)
- Akun [Google](https://accounts.google.com) untuk personalisasi YouTube Music (langganan Premium diperlukan untuk pemutaran penuh tanpa iklan)

## Instalasi

Unduh rilis terbaru dari halaman [Releases](https://github.com/alfarisauliarahman/kaset-win/releases).

- **`Kaset-win-Setup.exe`** — installer. Klik dua kali untuk memasang; membuat pintasan Desktop dan Start-menu serta mendukung pembaruan di tempat.
- **`Kaset-win-Portable.zip`** — versi tanpa instal. Ekstrak ke mana saja lalu jalankan `KasetWin.App.exe` langsung.

> **Catatan:** app ini **tidak bersertifikat kode**, jadi Windows SmartScreen mungkin memperingatkan saat pertama dijalankan — pilih *More info → Run anyway*. Sesi login dan pengaturanmu tersimpan per-pengguna di `%LOCALAPPDATA%\Kaset`.

## Membangun dari sumber

Membutuhkan [.NET 8 SDK](https://dotnet.microsoft.com/download) dan tooling build Windows App SDK (dipulihkan lewat NuGet). Untuk menjalankan build terpaket (MSIX) lokal, aktifkan **Mode Pengembang** (Settings → Privacy & security → For developers).

```powershell
# Build dev (terpaket / MSIX):
dotnet build src/KasetWin.App/KasetWin.App.csproj -c Debug

# Tes (core headless, tanpa WinUI):
dotnet test tests/KasetWin.Core.Tests/KasetWin.Core.Tests.csproj

# .exe mandiri (tak-terpaket, self-contained):
dotnet publish src/KasetWin.App/KasetWin.App.csproj -c Release -r win-x64 --self-contained true `
  -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true

# Installer + feed auto-update (butuh: dotnet tool install -g vpk):
vpk pack --packId Kaset --packVersion 0.3.0 --mainExe KasetWin.App.exe --packTitle "Kaset" `
  --packDir src/KasetWin.App/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish --outputDir dist
```

> Tips: jika build paralel kena file lock XamlCompiler, jalankan `dotnet build-server shutdown` dan tambahkan `-p:UseSharedCompilation=false`.

> Test hanya mencakup core headless. Apa pun yang melibatkan WebView2 sungguhan, sesi login, presenter jendela, atau SMTC harus diuji manual — lihat [`docs/manual-test-checklist.md`](docs/manual-test-checklist.md).
>
> Kalau ada yang aneh saat aplikasinya jalan, mulai dari [`docs/troubleshooting.md`](docs/troubleshooting.md) — build hijau dan test hijau tidak berarti aplikasinya jalan. Cacat yang sudah diketahui dan keputusan yang sudah diambil ada di [`docs/known-issues.md`](docs/known-issues.md).

## Tata letak proyek

| Proyek | Peran |
|--------|-------|
| `KasetWin.Core` | Core domain headless — model, klien InnerTube + parser, logika queue / player / auth, lirik, pengaturan. Tanpa dependensi WinUI; diuji unit dan property. |
| `KasetWin.Platform` | Adapter WinRT — controller pemutaran WebView2, SMTC, kredensial DPAPI, decode/cache gambar, monitor jaringan, penyimpanan lintas-mode (`AppData`). |
| `KasetWin.App` | App WinUI 3 — halaman, ViewModel (MVVM), bootstrap DI / Generic Host, host WebView2 pemutaran tersembunyi, pintasan keyboard, lokalisasi. |
| `KasetWin.ApiExplorer` | Alat konsol untuk menjelajah endpoint InnerTube. |
| `tests/KasetWin.Core.Tests` | Tes berbasis-property xUnit + **CsCheck** atas core headless. |

## Keamanan

Jangan pernah commit cookie asli, token, nilai `SAPISID` / `__Secure-3PAPISID`, atau kredensial apa pun. Fixture tes hanya memakai placeholder yang sudah disterilkan. Rahasia saat runtime disimpan lewat DPAPI / Windows Credential Locker, tidak pernah di repo.

## Kredit

Berbasis [Kaset](https://github.com/sozercan/kaset) oleh Sertaç Özercan. Port Windows ini adalah reimplementasi independen dalam C# / WinUI 3.

## Penafian

KasetWin adalah aplikasi tidak resmi dan tidak berafiliasi dengan YouTube atau Google Inc. dengan cara apa pun. "YouTube", "YouTube Music", dan "Logo YouTube" adalah merek dagang terdaftar milik Google Inc.
