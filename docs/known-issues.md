# Cacat yang diketahui & keputusan yang sudah diambil

Dua daftar, dan keduanya sengaja ada:

- **Diketahui & diterima** — sudah dilihat, sudah ditimbang, dibiarkan. Kalau kamu menemukannya,
  bukan temuan baru. Jangan "memperbaiki" tanpa membaca alasannya dulu: beberapa di antaranya adalah
  pertukaran yang disengaja, dan membalikkannya menghidupkan bug lain.
- **Ditolak / dibatalkan** — pernah dipertimbangkan, diputuskan tidak. Ditulis supaya tidak ada yang
  mengerjakannya lagi dari nol, lalu heran kenapa di-revert.

Terakhir diperbarui: 2026-07-23.

---

## Diketahui & diterima

| Hal | Kenapa dibiarkan |
|---|---|
| **Baris lirik sebelumnya tampak separuh** di atas baris aktif | Tinggi baris yang melipat dibaca sebelum layout selesai, jadi target gulir meleset beberapa piksel. Kosmetik; baris aktifnya sendiri sudah tidak terpotong lagi. Perbaikan sesungguhnya berarti menunda gulir sampai layout settle, yang bisa membuat glide-nya tersendat — dan tersendat lebih terasa daripada separuh baris. |
| **`Tyler, The Creator` bisa terbaca dua artis** | Pemisahan koma pada byline datar terjadi di `observer.js`, di hulu Core. Aturan konjungsi "dan"/"and" tidak diperlebar ke koma justru supaya kasus ini tidak jadi lebih parah. Menebak salah lebih buruk daripada nama yang kepanjangan. |
| **Lirik tidak selalu tersinkron** | Ketersediaan versi tersinkron ditentukan per lagu oleh YouTube, bukan oleh penyedia lisensinya. Terverifikasi: satu lagu Musixmatch bisa tersinkron penuh, satu lagi tidak sama sekali. Bukan bug. |
| **Versi klien `ANDROID_MUSIC 7.21.50` yang di-pin pasti basi** | Tidak bisa dihindari; bisa dipulihkan. Kegagalannya turun ke teks polos (tidak pernah ke "lirik hilang"), log memperingatkan setelah 12 lagu beruntun tanpa timing, dan versinya bisa diganti lewat setting `lyrics.androidClientVersion` **tanpa rilis baru**. Lihat ADR 0005. |
| **ViewModel & lapisan Platform nol test** | 487 test seluruhnya `KasetWin.Core`. `SearchViewModel` (724 baris) dan `PlaylistDetailViewModel` (859 baris) hanya diuji dengan tangan. Gap struktural terbesar yang tersisa. |
| **Tidak ada tampilan error + tombol "coba lagi"** | 16 halaman punya `ProgressRing`, tapi kalau panggilan API gagal user hanya melihat halaman kosong. Rekomendasi prioritas tertinggi yang belum dikerjakan. |
| **`MainWindow` ~3.000 baris di 8 partial** | `NavigationService` yang direncanakan masih TODO sejak task 14.2. Tiap fitur shell baru menumpuk di sini. |
| **Podcast di sidebar tidak memuat** | YouTube Music tidak menyediakan Podcast untuk region Indonesia. Bukan cacat Kaset. |
| **Mode YouTube penuh masih WIP** | `YouTube*Page`, Shorts, watch — di luar cakupan checklist uji manual. |

## Menunggu pemilik repo (bukan bug)

| Hal | Yang dibutuhkan |
|---|---|
| **Discord Rich Presence tidak aktif** | `DiscordRichPresenceOptions.DefaultApplicationId` masih kosong. Perlu satu Application ID dari Discord Developer Portal (sekali seumur hidup, bukan per-user). Sampai diisi, kartu Pengaturan menampilkan penjelasannya alih-alih diam-diam gagal. |
| **Checklist bagian D & E belum dijalankan** | Bagian D sempat terhenti karena aplikasinya crash saat itu; crash-nya sudah diperbaiki. Lihat `docs/manual-test-checklist.md`. |

## Ditolak / dibatalkan — jangan dikerjakan ulang tanpa membaca ini

| Hal | Alasan |
|---|---|
| **Last.fm scrobbling** | Dibatalkan atas permintaan pemilik repo. Task 21 & Requirement 28 ditandai dibatalkan, bukan dihapus, supaya tetap ketemu kalau dicari. |
| **Crossfade & fade in/out** | Dibatalkan atas permintaan pemilik repo. Catatan teknisnya: crossfade sungguhan butuh dua pemutar audio sekaligus, sedangkan Kaset hanya punya satu WebView2. |
| **Memindahkan toggle sumber ke atas sidebar** | Dicoba, lalu **dibatalkan** setelah dilihat langsung. Itu murni opini tata letak, bukan perbaikan cacat. Posisinya tetap di bawah pane. |
| **Versi klien lirik lewat konfigurasi jarak jauh** | Ditolak. Menambah ketergantungan jaringan dan membuat berkas di luar repo menentukan identitas klien yang kita samar — terlalu mahal untuk fitur yang sudah gagal dengan anggun. Setting lokal + peringatan di log sudah cukup. |
| **Memaksa pilihan sumber lirik ke "YouTube Music"** | Ditolak. "Otomatis" sudah mencoba YouTube Music lebih dulu **dan** mempertahankan cadangan LRCLib/NetEase; memaksanya justru menghapus cadangan itu diam-diam dan mengurangi cakupan. |
| **Memberi nama aksesibilitas pada `TrackInfo`** | Sengaja tidak. Tautannya membungkus judul lagu, jadi namanya sudah datang dari isinya; menimpanya dengan "Buka album" justru menghapus judulnya dari pembacaan Narrator. |

---

Kalau sebuah baris di sini sudah tidak benar lagi, hapus barisnya — daftar cacat yang basi lebih
berbahaya daripada tidak ada daftar sama sekali, karena orang akan memercayainya.
