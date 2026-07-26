# Cacat yang diketahui & keputusan yang sudah diambil

Dua daftar, dan keduanya sengaja ada:

- **Diketahui & diterima** — sudah dilihat, sudah ditimbang, dibiarkan. Kalau kamu menemukannya,
  bukan temuan baru. Jangan "memperbaiki" tanpa membaca alasannya dulu: beberapa di antaranya adalah
  pertukaran yang disengaja, dan membalikkannya menghidupkan bug lain.
- **Ditolak / dibatalkan** — pernah dipertimbangkan, diputuskan tidak. Ditulis supaya tidak ada yang
  mengerjakannya lagi dari nol, lalu heran kenapa di-revert.

Terakhir diperbarui: 2026-07-27 (putaran uji 17).

---

## Diketahui & diterima

| Hal | Kenapa dibiarkan |
|---|---|
| **Baris lirik sebelumnya tampak separuh** di atas baris aktif | Tinggi baris yang melipat dibaca sebelum layout selesai, jadi target gulir meleset beberapa piksel. Kosmetik; baris aktifnya sendiri sudah tidak terpotong lagi. Perbaikan sesungguhnya berarti menunda gulir sampai layout settle, yang bisa membuat glide-nya tersendat — dan tersendat lebih terasa daripada separuh baris. |
| **Pemutaran tidak otomatis melanjut saat internet kembali** | Yang diperbaiki putaran 5 adalah "klik lagu yang sama tidak berefek"; melanjutkan sendiri butuh pemantauan status jaringan, mekanisme terpisah yang sengaja belum ditambahkan. Pengguna menekan play. Lihat ADR 0006. |
| **Mengklik lagu yang sedang diputar mengulanginya dari 0:00** | Bukan cacat — konsekuensi yang disengaja dari perbaikan di atas, dan perilaku yang lazim di pemutar musik. Membalikkannya berarti mengembalikan "lagu mati tidak bisa dimuat ulang". |
| **Menu klik kanan: "Simpan ke playlist" & "Hapus dari riwayat" belum ada** | Klik kanan kini ada di semua halaman lagu (putaran 9+). Dua item ini lanjutan: picker playlist butuh plumbing per halaman; hapus riwayat butuh feedback-token di `HistoryViewModel`. |
| **Mini player: resize lanjutan ke 450px belum terbukti di CompactOverlay** | Masuk mini sudah terbukti bisa resize; expand panel memakai API yang sama tapi belum diuji manual (langkah 187). Kalau OS menolak: degradasi aman, fallback Opsi B (tanpa resize) sudah disiapkan. |
| **Narrator: tombol Ulangi & tombol sumber ringkas** (#128/#142) | Perbaikan `ToString()` putaran 5 menutup satu kelas masalah, tapi bukan kelas yang memuat kedua tombol ini — diuji tangan putaran 7, nol perubahan. Butuh Narrator hidup; pemilik repo menghentikan pengujian Narrator untuk sekarang. |
| **Race laten di `LoadTrackAsync`** | `_expectedVideoId`/`CurrentTrack` diset di luar `_loadGate`, tidak atomik dengan increment generasi. Kelas bug yang sama dengan ADR 0006 di baris berbeda. **Tidak terbukti** menyebabkan gejala apa pun, dan jendelanya nanodetik sehingga tidak ada test deterministik yang bisa ditulis. Jangan "perbaiki" tanpa test yang benar-benar membuktikannya. |
| **Scroll roda mouse di shelf horizontal masih campur** | **DIPARKIR setelah 3 generasi perbaikan gagal** (per-shelf enumeration → kalah virtualisasi; central-only → kalah rute event; PointerMoved pre-attach → masih dilaporkan kacau, penguji menyerah). Generasi berikutnya WAJIB debugging live dengan penguji (shelf mana persisnya, arah gerak, satu input satu efek atau dua) — bukan hipotesis keempat dari kode. |
| **`Tyler, The Creator` bisa terbaca dua artis** | Pemisahan koma pada byline datar terjadi di `observer.js`, di hulu Core. Aturan konjungsi "dan"/"and" tidak diperlebar ke koma justru supaya kasus ini tidak jadi lebih parah. Menebak salah lebih buruk daripada nama yang kepanjangan. |
| **Lirik tidak selalu tersinkron** | Ketersediaan versi tersinkron ditentukan per lagu oleh YouTube, bukan oleh penyedia lisensinya. Terverifikasi: satu lagu Musixmatch bisa tersinkron penuh, satu lagi tidak sama sekali. Bukan bug. |
| **Versi klien `ANDROID_MUSIC 7.21.50` yang di-pin pasti basi** | Tidak bisa dihindari; bisa dipulihkan. Kegagalannya turun ke teks polos (tidak pernah ke "lirik hilang"), log memperingatkan setelah 12 lagu beruntun tanpa timing, dan versinya bisa diganti lewat setting `lyrics.androidClientVersion` **tanpa rilis baru**. Lihat ADR 0005. |
| **ViewModel & lapisan Platform nol test** | 503 test seluruhnya `KasetWin.Core`. `SearchViewModel` (724 baris) dan `PlaylistDetailViewModel` (859 baris) hanya diuji dengan tangan. Gap struktural terbesar yang tersisa. |
| **Tidak ada tampilan error + tombol "coba lagi"** | 16 halaman punya `ProgressRing`, tapi kalau panggilan API gagal user hanya melihat halaman kosong. Rekomendasi prioritas tertinggi yang belum dikerjakan. |
| **`MainWindow` ~3.000 baris di 8 partial** | `NavigationService` yang direncanakan masih TODO sejak task 14.2. Tiap fitur shell baru menumpuk di sini. |
| **Podcast di sidebar tidak memuat** | YouTube Music tidak menyediakan Podcast untuk region Indonesia. Bukan cacat Kaset. |
| **Mode YouTube penuh masih WIP** | `YouTube*Page`, Shorts, watch — di luar cakupan checklist uji manual. |

## Menunggu pemilik repo (bukan bug)

| Hal | Yang dibutuhkan |
|---|---|
| **Discord Rich Presence tidak aktif** | `DiscordRichPresenceOptions.DefaultApplicationId` masih kosong. Perlu satu Application ID dari Discord Developer Portal (sekali seumur hidup, bukan per-user). Sampai diisi, kartu Pengaturan menampilkan penjelasannya alih-alih diam-diam gagal. |
| **Checklist seksi H5b sudah dijalankan (putaran 8); yang belum: langkah putaran 9 (167–170) + langkah uji sidebar/klik-kanan** | Untuk arsip: seksi G dijalankan putaran 7, H5b putaran 8 (11 lulus, 4 gagal → seksi I). Untuk arsip: seksi G semula **20 langkah** (127–146) yang menguji perbaikan putaran 5: aksesibilitas, `kaset://`, nyangkut, ikon taskbar, antrean, bunyi timer. Seksi D & E sudah dijalankan pada putaran 4. Langkah 142–146 ditambahkan menyusul — seksi G semula tidak punya langkah untuk tombol Ulangi, tombol hapus riwayat, maupun Narrator di sidebar, padahal ketiganya justru gejala yang dilaporkan dua putaran berturut-turut. |

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
