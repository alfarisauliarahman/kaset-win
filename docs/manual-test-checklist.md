# Checklist Uji Manual — KasetWin

Hal-hal yang **tidak bisa** dijamin oleh 424 test headless dan job build CI: apa pun yang melibatkan
WebView2 sungguhan, sesi login sungguhan, presenter jendela, SMTC, atau mata manusia.

**Cara menyiapkan** (dari `M:\kaset\kaset\KasetWin`):

```powershell
Get-Process -Name "KasetWin.App" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/KasetWin.App/KasetWin.App.csproj -c Debug
Start-Process "shell:AppsFolder\Kaset.KasetWin_kjgd17zy2bc08!App"
```

> Jangan pakai `-p:Platform=x64` untuk alur dev ini — paket yang ter-register menunjuk
> `bin\Debug\...\win-x64`, sedangkan flag x64 menulis ke `bin\x64\Debug`, sehingga kamu akan menguji
> build lama. Nama prosesnya `KasetWin.App`, bukan `KasetWin`.

Isi kolom **Hasil** dengan ✅ / ❌ + catatan. Tanggal & versi diisi tiap putaran uji.

---

## A. Perubahan 2026-07-22

> **Putaran uji 1 (2026-07-22).** Langkah 2, 9, 16, 32, 53, 58b lulus. Langkah 58 menemukan cacat visual (sudah diperbaiki, perlu uji ulang bersama 58c). Halaman Pengaturan sempat
> crash begitu dibuka dan memblokir sisa pengujian — sudah diperbaiki dan diverifikasi
> (`ApplyLabels()` berjalan sebelum ViewModel dibuat; lihat task 36b). Sisa langkah belum dijalankan.

### A1. Mini player — risiko tertinggi

Alasannya: pemutaran ditambatkan pada **satu** WebView2 tersembunyi di `RootGrid`. Desainnya sengaja
memakai ulang jendela yang sama agar elemen itu tidak pernah pindah induk. Kalau ada yang putus di
sini, gejalanya adalah **audio mati**, bukan sekadar UI jelek.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 1 | Putar lagu, tunggu stabil, klik tombol mini player (ikon PiP, cluster kanan player bar) | Jendela mengecil ke ±400×150, selalu di atas jendela lain; sidebar & title bar hilang | |
| 2 | **Dengarkan saat transisi** | Audio **tidak putus, tidak tersendat, tidak mengulang** | ✅ 2026-07-22 |
| 3 | Di mini player: tekan prev / play-pause / next | Semua bekerja; sampul + judul ikut berubah | |
| 4 | Geser scrubber mini player | Posisi berpindah; thumb tidak menyentak balik | |
| 5 | Klik tombol restore | Jendela penuh kembali; sidebar + title bar muncul; audio tetap jalan | |
| 6 | Buka panel antrean → masuk mini player → keluar | Panel antrean terbuka lagi seperti semula | |
| 7 | Ulangi dengan panel **lirik** terbuka | Panel lirik yang terbuka lagi, bukan antrean | |
| 8 | Di mini player, coba tarik tepi jendela mengecil | Boleh kecil (floor 980×600 dilepas sementara) | |
| 9 | Setelah restore, coba tarik jendela mengecil | Berhenti di 980×600 — **floor harus kembali aktif** | ✅ 2026-07-22 (mentok, floor aktif lagi) |
| 10 | Masuk mini player, lalu tutup jendela (bukan quit) | Audio lanjut di latar seperti mode biasa | |

> **Riwayat 58 (2026-07-22).** Perbaikan pertama benar secara perilaku tetapi jelek dilihat: jendela
> tampak membesar kembali ke ukuran penuh sesaat sebelum hilang ke tray, karena mode mini player
> dilepas **sebelum** jendela disembunyikan. Urutannya dibalik — `Hide()` dulu, baru lepas mini
> player saat jendela sudah tidak terlihat — dan ukuran yang disimpan diambil dari frame sebelum
> mengecil, bukan dari frame 400×150 yang sedang aktif. **Perlu diuji ulang.**

### A2. Timer tidur

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 11 | Klik tombol timer tidur → pilih 15 menit | Toast "Pemutaran berhenti dalam 15 menit"; ikon jam menyala warna aksen | |
| 12 | Hover tombolnya | Tooltip menyebut sisa waktu | |
| 13 | Pilih 15 menit lalu ganti 30 menit | Yang berlaku 30 menit saja — bukan dua timer berjalan | |
| 14 | Pilih "Nonaktif" | Toast dibatalkan; ikon kembali normal | |
| 15 | **Uji ujung**: putar lagu pendek, pilih "Akhir lagu ini" | Di akhir lagu pemutaran **berhenti**, antrean **tidak** maju | |
| 16 | Arm "Akhir lagu ini" lalu tekan **Next manual** | Timer **tetap aktif**, pemutaran **tidak** langsung terjeda (ini justru bug yang dihindari desainnya) | ✅ 2026-07-22 |
| 17 | Arm 15 menit, biarkan beberapa lagu berganti | Timer bertahan melewati batas antar-lagu | |
| 18 | Untuk menguji tanpa menunggu: arm 15 menit, cek Task Manager | CPU idle wajar (tick 1 detik hanya saat aktif) | |
| 19 | Nonaktifkan timer, cek lagi | Tick berhenti sepenuhnya | |

> Ingin verifikasi cepat tanpa menunggu 15 menit? Ubah sementara `SleepTimerPresets` di
> `PlayerBar.xaml.cs` menjadi `[1]`, build, uji, lalu kembalikan.

### A3. Aksesibilitas (Narrator)

Nyalakan Narrator: **Ctrl + Win + Enter**. Matikan dengan kombinasi yang sama.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 20 | Tab menyusuri player bar dari kiri ke kanan | Setiap tombol disebut namanya: Acak, Sebelumnya, Putar/Jeda, Berikutnya, Ulangi, Suka, Timer tidur, Mini player, Lirik, Antrean, Bisukan — **tidak ada** yang cuma "tombol" | |
| 21 | Fokus ke slider seek dan volume | Disebut "Posisi pemutaran" dan "Volume", bukan sekadar angka | |
| 22 | Suka sebuah lagu, fokus ulang tombolnya | Namanya berubah "Suka" → "Batal suka" | |
| 23 | Ganti mode ulangi tiga kali | Nama tombol mengikuti: nonaktif / semua / satu | |
| 24 | Fokus tombol lirik saat memutar **podcast** | Disebut "Subtitel (CC)", bukan "Lirik" | |
| 25 | Fokus tautan judul/artis pada baris lagu | Menyebut **judul lagunya**, bukan "Buka album" (ini pengecualian yang disengaja) | |
| 26 | Navigasi ke Pengaturan → Ekualiser, Tab antar slider | Tiap slider menyebut frekuensinya ("Penguatan 1 kHz"), tidak sembilan slider anonim | |
| 27 | Buka panel antrean, masuk ke daftarnya | Disebut "Antrean pemutaran"; tab Riwayat → "Riwayat pemutaran" | |
| 28 | Ketik di kotak cari, fokus tombol X pada baris riwayat | Menyebut "Hapus dari riwayat: \<query\>" | |
| 29 | Fokus toggle sumber saat sidebar menyempit | Menyebut sumber yang sedang aktif **dan** bahwa klik akan menggantinya | |
| 30 | Lewati sampul album saat Tab | Narrator **tidak** berhenti di gambar sampul | |

### A4. Lokalisasi

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 31 | Pengaturan → Bahasa → English | Seluruh chrome jadi Inggris | |
| 32 | Dalam mode English, hover tombol **lirik** | "Lyrics" — **bukan** "Lirik" (ini cacat yang baru diperbaiki) | ✅ 2026-07-22 |
| 33 | Dalam mode English, hover tombol **kembali** di title bar | "Back" — bukan "Kembali" | |
| 34 | Dalam mode English, hover tombol hapus riwayat pencarian | "Remove from history" | |
| 35 | Dalam mode **Indonesia**, matikan Wi-Fi | Banner offline berbahasa **Indonesia** (dulu selalu Inggris) | |
| 36 | Ganti bahasa bolak-balik, lalu cek player bar & panel | Tidak ada label yang tertinggal di bahasa lama | |

### A5. Tata letak

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 37 | Matikan Wi-Fi saat menatap daftar lagu | Banner offline muncul **menimpa** konten — daftar **tidak bergeser turun** | |
| 38 | Nyalakan Wi-Fi lagi | Banner hilang tanpa konten menyentak | |
| 39 | Lihat sidebar | Toggle Music↔YouTube ada di **bawah** pane, di atas baris akun (posisi asli — percobaan memindahkannya ke atas dibatalkan) | |
| 40 | Klik toggle ke YouTube lalu balik | Indikator meluncur mulus; isi sidebar berganti; navigasi ke Home sumber itu | |
| 41 | Buka panel antrean (sidebar menyempit ke ikon) | Pill toggle berganti jadi tombol bundar ikon-saja, tidak terpotong | |
| 42 | Kecilkan jendela sampai batas minimum (980 px) | Cluster kanan player bar (kini 6 tombol + slider) tidak terpotong/tumpang tindih | |

### A6. Discord Rich Presence

Butuh Discord terpasang & login. **Tidak perlu setup dari pengguna** — Kaset memakai satu Application
ID bersama yang sudah ditanam di kode (`DiscordRichPresenceOptions.DefaultApplicationId`). Kolom
Application ID di bagian "Lanjutan" hanya untuk yang ingin nama lain muncul di profilnya.

> **Prasyarat build:** kalau `DefaultApplicationId` masih kosong, toggle-nya tidak akan berfungsi dan
> kartu Pengaturan menampilkan pesan penjelasannya. Isi dulu konstanta itu sebelum menguji A6.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 43 | Toggle "Tampilkan di Discord" dalam keadaan mati (default) | Tidak ada apa pun di profil Discord | |
| 44 | Nyalakan toggle-nya, putar lagu | Profil Discord menampilkan judul + artis, **tanpa perlu restart dan tanpa paste apa pun** | |
| 45 | Lihat penghitung waktu di Discord | Menghitung dari posisi lagu saat ini, bukan dari 00:00 | |
| 46 | Geser scrubber ke tengah lagu | Penghitung menyesuaikan, tidak mengulang dari nol | |
| 47 | Jeda pemutaran | Penghitung waktu **hilang** (lagu jeda tidak boleh tampak terus jalan) | |
| 48 | Putar lagu berjudul sangat panjang | Judul terpotong dengan "…", presence tetap muncul (bukan hilang) | |
| 49 | Tutup Discord sambil Kaset tetap memutar | Kaset tidak crash, tidak ada pesan error | |
| 50 | Buka Discord lagi, ganti lagu | Presence muncul lagi sendiri | |
| 51 | Matikan toggle-nya | Presence hilang dari profil | |
| 51b | Isi Application ID sendiri di bagian Lanjutan | Nama aplikasi di profil berubah mengikuti ID itu | |

### A7. Pintasan global & ukuran jendela

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 52 | Pengaturan → nyalakan Pintasan global | Aktif langsung, tanpa restart | |
| 53 | Buka Notepad/browser (Kaset di belakang), tekan `Ctrl+Alt+→` | Lagu ganti ke berikutnya | ✅ 2026-07-22 |
| 54 | Tekan `Ctrl+Alt+↓`, lalu `Ctrl+Alt+↑` | Putar/jeda, lalu bisu/tidak bisu | |
| 55 | Tutup Kaset ke tray, tekan `Ctrl+Alt+→` | Tetap jalan walau jendela tersembunyi | |
| 56 | Matikan toggle-nya, tekan `Ctrl+Alt+→` | Tidak terjadi apa-apa (pintasan dilepas) | |
| 57 | Resize jendela ke ukuran aneh, tutup, buka lagi | Terbuka di ukuran & posisi yang sama | |
| 58 | **Uji ujung**: masuk mini player, tutup dari situ, buka lagi dari tray | Terbuka di ukuran **normal**, bukan 400×150 | ⚠️ 2026-07-22: ukuran benar, tapi animasi membesar terlihat sebelum menutup → diperbaiki, **uji ulang** |
| 58b | Masuk mini player, klik restore | Kembali ke ukuran & posisi persis sebelum masuk mini player | ✅ 2026-07-22 |
| 58c | Tutup dari mode mini player, **perhatikan animasinya** | Jendela langsung hilang ke tray — **tidak** terlihat membesar dulu | |
| 59 | Maximize, tutup, buka lagi | Terbuka di ukuran normal terakhir, bukan ukuran maximize | |

---

## B. Regresi inti (jalankan sebelum tiap rilis)

| # | Area | Langkah | Harapan | Hasil |
|---|------|---------|---------|-------|
| 60 | Login | Sign in dengan akun Google | Berhasil; nama akun muncul di sidebar | |
| 61 | Pemutaran | Putar lagu dari Beranda | Audio jalan, sampul + judul benar | |
| 62 | Antrean | Putar album, tekan Next beberapa kali | Maju sesuai urutan album | |
| 63 | **Ulangi satu + Next** | Set Repeat One, tekan Next | **Maju ke lagu berikutnya** (Repeat One hanya untuk akhir lagu otomatis — Tugas 30.7) | |
| 64 | Media key | Minimalkan app, tekan tombol next di keyboard | Maju, tidak mengulang lagu yang sama | |
| 65 | SMTC | Buka panel volume Windows | Kartu Now Playing menampilkan lagu + sampul yang benar | |
| 66 | Latar belakang | Tutup jendela (bukan quit) | Audio lanjut; ikon tray ada | |
| 67 | Lirik | Buka panel lirik saat memutar | Lirik tersinkron, baris aktif menyala | |
| 68 | Cari | Cari artis | Hasil bertipe (teratas/artis/album/lagu) | |
| 69 | Playlist | Buat playlist, tambah lagu, hapus | Semua tersimpan di sisi server | |
| 70 | Podcast | Buka Podcast, putar episode | Progres tersimpan; tombol ±10s/30s muncul | |
| 71 | Ekualiser | Ubah preset saat memutar | Suara berubah | |
| 72 | Adblock | Putar beberapa lagu | Tidak ada iklan (uBlock aktif) | |
| 73 | Protokol | `start kaset://play?v=VIDEO_ID` dari terminal | Lagu itu diputar | |
| 74 | Pintasan | Tekan Shift + / | Contekan pintasan muncul | |

---

## C. Yang masih belum tercakup uji apa pun

Dicatat jujur, bukan diklaim aman:

- **ViewModel dan lapisan Platform tidak punya test sama sekali.** 424 test seluruhnya `KasetWin.Core`.
  `SearchViewModel` (724 baris) dan `PlaylistDetailViewModel` (859 baris) hanya diuji dengan tangan.
- **Wiring timer tidur.** `SleepTimer` sendiri diuji 9 test; bahwa `PlayerService` benar-benar
  menjeda dan ticker benar-benar hidup/mati mengikuti state hanya bisa dibuktikan lewat langkah
  11–19 di atas.
- **Klien IPC Discord.** Pemetaan aktivitasnya diuji 12 test, tapi frame named-pipe yang benar-benar
  diterima Discord hanya bisa dibuktikan lewat langkah 43–51.
- **Pintasan global & geometri jendela** sepenuhnya interop Win32 — nol cakupan test otomatis.
- **Mode YouTube penuh** (`YouTube*Page`, Shorts, watch) masih WIP dan tidak dicakup checklist ini.
- **Brand Account** dan **EU consent wall** (Tugas 30.5 / 30.6) butuh akun & region sungguhan.
