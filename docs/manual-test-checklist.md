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

Isi kolom **Hasil** dengan ✅ / ❌ / ⚠️ + catatan. Tanggal & versi diisi tiap putaran uji.

**Cara membaca kolom Langkah:** tiap langkah ditulis sebagai instruksi harfiah — klik apa, di mana,
lalu lihat apa. Kalau sebuah langkah terasa ambigu, itu cacat pada checklist-nya; perbaiki
langkahnya, jangan tebak maksudnya.

---

## Riwayat putaran uji

**Putaran 1 — 2026-07-22.** Terhenti di tengah: halaman Pengaturan crash begitu dibuka
(`ApplyLabels()` berjalan sebelum ViewModel dibuat) dan memblokir sisa pengujian. Sudah diperbaiki
dan diverifikasi. Langkah 2, 9, 16, 32, 53, 58b lulus.

**Putaran 2 — 2026-07-22.** Checklist dijalankan hampir penuh. Hasil ringkas:

- **Lulus:** 1–7, 9, 10, 11, 14, 16, 21, 24–27, 31–42, 52–58b, 60–63, 66, 68, 69, 71, 72, 74
- **Cacat ditemukan:** 12, 15, 20, 22, 23, 28, 58c, 59, 64, 65, 67, 73
- **Langkah checklist yang salah tulis** (bukan cacat aplikasi): 8, 10, 13, 17, 18, 19, 29, 30, 70, 73
  — semuanya sudah ditulis ulang di bawah
- **Belum diuji:** A6 (Discord, 43–51b) — ditunda atas permintaan pemilik repo

---

## A. Perubahan 2026-07-22

### A1. Mini player — risiko tertinggi

Alasannya: pemutaran ditambatkan pada **satu** WebView2 tersembunyi di `RootGrid`. Desainnya sengaja
memakai ulang jendela yang sama agar elemen itu tidak pernah pindah induk. Kalau ada yang putus di
sini, gejalanya adalah **audio mati**, bukan sekadar UI jelek.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 1 | Putar lagu, tunggu stabil, klik tombol mini player (ikon PiP, cluster kanan player bar) | Jendela mengecil ke ±400×150, selalu di atas jendela lain; sidebar & title bar hilang | ✅ 2026-07-22 |
| 2 | **Dengarkan saat transisi** | Audio **tidak putus, tidak tersendat, tidak mengulang** | ✅ 2026-07-22 |
| 3 | Di mini player: tekan prev / play-pause / next | Semua bekerja; sampul + judul ikut berubah | ✅ 2026-07-22 |
| 4 | Geser scrubber mini player | Posisi berpindah; thumb tidak menyentak balik | ✅ 2026-07-22 |
| 5 | Klik tombol restore | Jendela penuh kembali; sidebar + title bar muncul; audio tetap jalan | ✅ 2026-07-22 |
| 6 | Buka panel antrean → masuk mini player → keluar | Panel antrean terbuka lagi seperti semula | ✅ 2026-07-22 |
| 7 | Ulangi dengan panel **lirik** terbuka | Panel lirik yang terbuka lagi, bukan antrean | ✅ 2026-07-22 |
| 8 | Saat di mini player, arahkan kursor ke tepi/pojok jendela dan coba tarik untuk mengubah ukuran | **Tidak bisa diubah ukurannya, dan itu benar.** `CompactOverlay` ukurannya dikelola Windows, bukan aplikasi — tidak ada gagang resize. Langkah ini hanya memastikan jendelanya tidak jadi aneh saat dicoba | ✅ 2026-07-22 (langkah lama salah: menuntut jendela bisa diperkecil) |
| 9 | Klik restore untuk keluar dari mini player. Lalu tarik pojok kanan-bawah jendela sekecil mungkin | Berhenti di sekitar 980×600 dan tidak mau lebih kecil — batas minimum aktif lagi setelah sempat dilepas untuk mini player | ✅ 2026-07-22 (mentok, floor aktif lagi) |
| 10 | Masuk mini player. Lalu klik tombol **✕** (bukan Quit dari tray) | Dua hal: (a) audio **lanjut** di latar; (b) saat dibuka lagi dari tray, jendelanya kembali **normal**, bukan mini player. Yang (b) memang disengaja — lihat riwayat 58 di bawah | ✅ 2026-07-22 |

> **Riwayat 58 (2026-07-22).** Perbaikan pertama benar secara perilaku tetapi jelek dilihat: jendela
> tampak membesar kembali ke ukuran penuh sesaat sebelum hilang ke tray, karena mode mini player
> dilepas **sebelum** jendela disembunyikan. Urutannya dibalik — `Hide()` dulu, baru lepas mini
> player saat jendela sudah tidak terlihat — dan ukuran yang disimpan diambil dari frame sebelum
> mengecil. Putaran 2 membuktikan animasi membesar itu hilang, tapi memunculkan gejala lain
> (lihat 58c).

### A2. Timer tidur

> **Cara menguji tanpa menunggu 15 menit:** ubah sementara `SleepTimerPresets` di
> `src/KasetWin.App/Controls/PlayerBar.xaml.cs` (cari `private static readonly int[] SleepTimerPresets`)
> menjadi `[1]`, build, uji, lalu kembalikan ke `[15, 30, 45, 60]`.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 11 | Klik tombol timer tidur (ikon bulan, cluster kanan player bar) → pilih **15 menit** | Toast "Pemutaran berhenti dalam 15 menit" muncul; ikon bulan berubah jadi warna aksen (biru) | ✅ 2026-07-22 |
| 12 | Tanpa mengklik apa pun, arahkan kursor ke tombol timer tidur dan diamkan ±1 detik | Tooltip yang muncul menyebut **sisa waktu** (mis. "Timer tidur — 14 menit"), bukan sekadar "Timer tidur" | ❌ 2026-07-22: tooltip tidak menyebut sisa waktu |
| 13 | Klik tombol timer tidur → pilih **15 menit**. Tunggu toastnya hilang. Klik tombol itu lagi → pilih **30 menit**. Sekarang diamkan lebih dari 15 menit | Musik **masih jalan** setelah menit ke-15 dan baru berhenti di menit ke-30. Kalau berhenti di menit 15, berarti timer lama tidak dibatalkan dan ada dua timer berjalan | |
| 14 | Klik tombol timer tidur → pilih **Nonaktif** | Toast "Timer tidur dibatalkan"; ikon bulan kembali ke warna normal (putih/abu) | ✅ 2026-07-22 |
| 15 | **Uji ujung.** Putar lagu, lompat ke ±10 detik sebelum lagu habis. Klik tombol timer tidur → **Akhir lagu ini**. Biarkan lagunya habis sendiri | Pemutaran **berhenti** saat lagu selesai, dan antrean **tidak** maju ke lagu berikutnya | ❌ 2026-07-22: ikon timer padam (timer terpakai) tapi **musik lanjut ke lagu berikutnya** |
| 16 | Klik tombol timer tidur → **Akhir lagu ini**. Lalu tekan tombol **Next** di player bar (skip manual) | Musik pindah ke lagu berikutnya seperti biasa, dan ikon timer **tetap menyala** — timer tidak boleh ikut terpakai oleh skip manual | ✅ 2026-07-22 (ikon masih menyala) |
| 17 | Klik tombol timer tidur → **15 menit**. Biarkan 2–3 lagu berganti sendiri | Ikon timer **tetap menyala** melewati tiap pergantian lagu. Kalau padam saat lagu berganti, berarti timer ikut tereset di batas antar-lagu | |

> Dua langkah lama (18, 19 — "cek CPU di Task Manager") dihapus di putaran 2: itu memeriksa detail
> internal (`DispatcherTimer` berhenti saat timer mati) yang tidak bisa dinilai dari luar dan tidak
> berarti apa-apa bagi pengguna.

### A3. Aksesibilitas (Narrator)

Nyalakan Narrator: **Ctrl + Win + Enter**. Matikan dengan kombinasi yang sama.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 20 | Klik di area player bar, lalu tekan **Tab** berulang dari kiri ke kanan sambil mendengarkan Narrator | Tiap tombol disebut **namanya saja**: Acak, Sebelumnya, Putar/Jeda, Berikutnya, Ulangi, Suka, Timer tidur, Mini player, Lirik, Antrean, Bisukan. Tidak ada yang cuma "tombol", tidak ada yang membacakan simbol/kode, dan tidak ada nama berbahasa Indonesia saat aplikasi berbahasa Inggris | ❌ 2026-07-22: banyak elemen tak jelas di sekitar sidebar ("navigation item expanded", "menu tindakan button", Pengaturan disebut "pop out button"); masih campur bahasa Indonesia |
| 21 | Tab sampai fokus berada di slider posisi lagu, lalu ke slider volume | Disebut "Posisi pemutaran" dan "Volume", bukan sekadar angka | ✅ 2026-07-22 |
| 22 | Klik tombol ❤️ (Suka) pada lagu yang sedang diputar, lalu tekan Shift+Tab / Tab sampai fokus kembali ke tombol itu | Namanya berubah dari "Suka" jadi "Batal suka" — dan **tidak** ada karakter/kode aneh yang ikut dibacakan | ❌ 2026-07-22: statusnya benar ("suka") tapi Narrator ikut membacakan kode simbol |
| 23 | Fokuskan tombol Ulangi (Tab), tekan **Spasi** untuk mengganti mode, lalu Shift+Tab dan Tab lagi supaya fokus kembali ke tombol itu. Ulangi 3× | Nama tombol mengikuti modenya: "Ulangi nonaktif" → "Ulangi semua" → "Ulangi satu lagu" | ❌ 2026-07-22: nama tidak berubah saat mode berganti |
| 24 | Putar sebuah **podcast**, lalu Tab sampai fokus ke tombol lirik | Disebut "Subtitel (CC)", bukan "Lirik" | ✅ 2026-07-22 |
| 25 | Buka panel antrean, Tab sampai fokus ke tautan judul/artis salah satu baris lagu | Narrator menyebut **judul lagunya**, bukan "Buka album". Ini pengecualian yang disengaja: tautan itu membungkus judul, jadi memberinya nama sendiri justru menghapus judulnya | ✅ 2026-07-22 |
| 26 | Buka Pengaturan → gulir ke Ekualiser → Tab menyusuri kesembilan slider | Tiap slider menyebut frekuensinya ("Penguatan 62 Hz", "Penguatan 125 Hz", …), bukan sembilan slider anonim yang identik | ✅ 2026-07-22 |
| 27 | Buka panel antrean, Tab sampai fokus masuk ke daftarnya. Lalu pindah ke tab Riwayat, ulangi | Daftarnya disebut "Antrean pemutaran", dan yang di tab Riwayat disebut "Riwayat pemutaran" | ✅ 2026-07-22 |
| 28 | Klik kotak cari, ketik sesuatu dan tekan Enter (supaya masuk riwayat). Klik kotak cari lagi sampai daftar riwayat muncul. Tab sampai fokus ke tombol **✕** di salah satu baris riwayat | Narrator menyebut "Hapus dari riwayat: \<kata yang dicari\>" — jadi sepuluh baris yang identik bisa dibedakan | ❌ 2026-07-22: tidak disebut |
| 29 | Buka panel antrean sampai sidebar ikut menyempit jadi ikon saja. Tombol sumber Music/YouTube di bawah sidebar berubah bentuk jadi tombol bundar. Tab sampai fokus ke tombol itu | Narrator menyebut sumber yang **sedang aktif** dan bahwa mengklik akan menggantinya (mis. "Sumber: Music — klik untuk pindah ke YouTube") — bukan cuma "tombol" | |
| 30 | Tab menyusuri player bar dari awal | Narrator **melewati** gambar sampul album — tidak berhenti di situ sama sekali. Ini kebalikan dari langkah lain: yang benar justru **tidak** disebut, karena judul lagunya sudah dibacakan di sebelahnya | |

### A4. Lokalisasi

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 31 | Pengaturan → Bahasa → English | Seluruh chrome jadi Inggris | ✅ 2026-07-22 |
| 32 | Dalam mode English, hover tombol **lirik** | "Lyrics" — **bukan** "Lirik" | ✅ 2026-07-22 |
| 33 | Dalam mode English, hover tombol **kembali** di title bar | "Back" — bukan "Kembali" | ✅ 2026-07-22 |
| 34 | Dalam mode English, hover tombol hapus riwayat pencarian | "Remove from history" | ✅ 2026-07-22 |
| 35 | Dalam mode **Indonesia**, matikan Wi-Fi | Banner offline berbahasa **Indonesia** (dulu selalu Inggris) | ✅ 2026-07-22 |
| 36 | Ganti bahasa bolak-balik, lalu cek player bar & panel | Tidak ada label yang tertinggal di bahasa lama | ✅ 2026-07-22 |

### A5. Tata letak

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 37 | Matikan Wi-Fi saat menatap daftar lagu | Banner offline muncul **menimpa** konten — daftar **tidak bergeser turun** | ✅ 2026-07-22 |
| 38 | Nyalakan Wi-Fi lagi | Banner hilang tanpa konten menyentak | ✅ 2026-07-22 |
| 39 | Lihat sidebar | Toggle Music↔YouTube ada di **bawah** pane, di atas baris akun (posisi asli — percobaan memindahkannya ke atas dibatalkan) | ✅ 2026-07-22 |
| 40 | Klik toggle ke YouTube lalu balik | Indikator meluncur mulus; isi sidebar berganti; navigasi ke Home sumber itu | ✅ 2026-07-22 |
| 41 | Buka panel antrean (sidebar menyempit ke ikon) | Pill toggle berganti jadi tombol bundar ikon-saja, tidak terpotong | ✅ 2026-07-22 |
| 42 | Kecilkan jendela sampai batas minimum (980 px) | Cluster kanan player bar (6 tombol + slider) tidak terpotong/tumpang tindih | ⚠️ 2026-07-22: tombol aman, tapi **nama artis & album di bagian tengah terpotong** |

### A6. Discord Rich Presence

Butuh Discord terpasang & login. **Tidak perlu setup dari pengguna** — Kaset memakai satu Application
ID bersama yang sudah ditanam di kode (`DiscordRichPresenceOptions.DefaultApplicationId`). Kolom
Application ID di bagian "Lanjutan" hanya untuk yang ingin nama lain muncul di profilnya.

> **Prasyarat build:** `DefaultApplicationId` **masih kosong** per 2026-07-22, jadi toggle-nya belum
> berfungsi dan kartu Pengaturan menampilkan pesan penjelasannya. Isi dulu konstanta itu sebelum
> menguji A6. Bagian ini belum pernah dijalankan sama sekali.

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
| 52 | Pengaturan → nyalakan Pintasan global | Aktif langsung, tanpa restart | ✅ 2026-07-22 |
| 53 | Buka Notepad/browser (Kaset di belakang), tekan `Ctrl+Alt+→` | Lagu ganti ke berikutnya | ✅ 2026-07-22 |
| 54 | Tekan `Ctrl+Alt+↓`, lalu `Ctrl+Alt+↑` | Putar/jeda, lalu bisu/tidak bisu | ✅ 2026-07-22 |
| 55 | Tutup Kaset ke tray, tekan `Ctrl+Alt+→` | Tetap jalan walau jendela tersembunyi | ✅ 2026-07-22 |
| 56 | Matikan toggle-nya, tekan `Ctrl+Alt+→` | Tidak terjadi apa-apa (pintasan dilepas) | ✅ 2026-07-22 |
| 57 | Resize jendela ke ukuran aneh, tutup, buka lagi | Terbuka di ukuran & posisi yang sama | ✅ 2026-07-22 |
| 58 | **Uji ujung**: masuk mini player, tutup dari situ, buka lagi dari tray | Terbuka di ukuran **normal**, bukan 400×150 | ✅ 2026-07-22 |
| 58b | Masuk mini player, klik restore | Kembali ke ukuran & posisi persis sebelum masuk mini player | ✅ 2026-07-22 |
| 58c | Masuk mini player. Klik **✕**, dan perhatikan baik-baik apa yang terjadi di layar selama jendela menghilang | Jendela langsung hilang ke tray. Tidak boleh terlihat membesar, tidak boleh berkedip jadi layar penuh, dan tidak boleh melompat ke pojok layar | ❌ 2026-07-22: tidak membesar lagi, tapi jendela sempat **jadi layar penuh**, lalu mini player **melompat ke pojok kiri atas**, baru menghilang |
| 59 | Klik tombol **Maximize**. Tutup aplikasi lewat ✕ lalu Quit dari tray. Buka lagi | Terbuka **dalam keadaan maximize** — sama seperti saat ditutup | ❌ 2026-07-22: terbuka windowed. Perilaku lama sengaja hanya menyimpan ukuran windowed; itu keputusan yang salah dan sudah diubah |

---

## B. Regresi inti (jalankan sebelum tiap rilis)

| # | Area | Langkah | Harapan | Hasil |
|---|------|---------|---------|-------|
| 60 | Login | Sign in dengan akun Google | Berhasil; nama akun muncul di sidebar | ⚠️ 2026-07-22: berhasil tapi lambat & kadang nyangkut — halaman login sempat muncul walau sudah login, dan baru masuk setelah dibatalkan lalu diklik lagi |
| 61 | Pemutaran | Putar lagu dari Beranda | Audio jalan, sampul + judul benar | ✅ 2026-07-22 |
| 62 | Antrean | Putar album, tekan Next beberapa kali | Maju sesuai urutan album | ✅ 2026-07-22 |
| 63 | **Ulangi satu + Next** | Set Repeat One, tekan Next | **Maju ke lagu berikutnya** (Repeat One hanya untuk akhir lagu otomatis — Tugas 30.7) | ✅ 2026-07-22 |
| 64 | Media key | Minimalkan app, tekan tombol next di keyboard | Maju, tidak mengulang lagu yang sama | ❌ 2026-07-22: tidak bereaksi sama sekali (regresi — putaran sebelumnya jalan) |
| 65 | SMTC | Buka panel volume Windows | Kartu Now Playing menampilkan lagu + sampul yang benar | ❌ 2026-07-22: kartunya benar, tapi menemukan cacat antrean yang lebih besar — lihat di bawah |
| 66 | Latar belakang | Tutup jendela (bukan quit) | Audio lanjut; ikon tray ada | ✅ 2026-07-22 |
| 67 | Lirik | Buka panel lirik saat memutar | Lirik tersinkron, baris aktif menyala | ⚠️ 2026-07-22: jalan, tapi label "Lirik" di header menjorok ke kiri, lirik sering tidak lengkap, dan sumber liriknya tidak pernah ditampilkan |
| 68 | Cari | Cari artis | Hasil bertipe (teratas/artis/album/lagu) | ✅ 2026-07-22 |
| 69 | Playlist | Buat playlist, tambah lagu, hapus | Semua tersimpan di sisi server | ✅ 2026-07-22 |
| 70 | Podcast | Buka **satu playlist/koleksi podcast** dan putar sebuah episode | Progres tersimpan; tombol ±10s/30s muncul | ⚠️ 2026-07-22: item **Podcast di sidebar** memang tidak bisa memuat — YouTube Music tidak menyediakan Podcast untuk region Indonesia. Bukan cacat Kaset; langkah diubah agar tidak menyesatkan |
| 71 | Ekualiser | Ubah preset saat memutar | Suara berubah | ✅ 2026-07-22 |
| 72 | Adblock | Putar beberapa lagu | Tidak ada iklan (uBlock aktif) | ✅ 2026-07-22 |
| 73 | Protokol | Dari PowerShell jalankan `start "kaset://play?v=BiQIc7fG9pA"` (Rejoice — Official HIGE DANDism) | Kaset muncul ke depan dan memutar lagu itu, **lengkap dengan nama artis di player bar dan lirik yang ikut termuat** | ❌ 2026-07-22: lagu diputar, tapi player bar hanya menampilkan judul (nama artis kosong) dan panel lirik tetap berkata "Putar lagu untuk melihat lirik di sini" |
| 74 | Pintasan | Tekan Shift + / | Contekan pintasan muncul, dan **muat di jendela** — kalau jendelanya kecil, isinya bisa digulir, bukan terpotong atas-bawah | ⚠️ 2026-07-22: muncul, tapi memenuhi layar atas-bawah saat jendela kecil dan tidak bisa digulir |

### 65b — Cacat antrean yang ditemukan saat menguji SMTC

Cacat terpisah dan lebih besar dari langkah 65 sendiri, dicatat sebagai langkah uji tersendiri:

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 65b | Putar album (mis. *Editorial* — Official HIGE DANDism) mulai dari track 1. Buka panel antrean. Tekan **Next** | Pindah ke track 2 album itu; antrean tetap berisi album yang sama; **Previous** mengembalikan ke track 1 | ❌ 2026-07-22: antrean tiba-tiba berganti jadi mix/queue baru; Previous tidak bisa kembali (dianggap memutar dari awal); track 1 tidak bisa diputar ulang (nyangkut); Next berikutnya mengganti antrean lagi |

---

## D. Uji ulang perbaikan putaran 2 (2026-07-22, commit `faf5896`)

Semua langkah di bawah menguji cacat yang **sudah diperbaiki tapi belum pernah diklik**. Build hijau
dan 445 test headless lulus, tapi tak satu pun dari ini bisa dibuktikan tanpa tangan manusia.

Urutan sengaja: yang paling berisiko dan paling sering dipakai dulu.

### D1. Antrean & pemutaran — kerjakan ini duluan

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 75 | Buka sebuah album (mis. *Editorial*), putar dari track 1. Buka panel antrean. Tekan **Next** | Pindah ke track 2 **album yang sama**; isi panel antrean **tidak berubah** — tidak ada lagu asing yang muncul | |
| 76 | Lanjutkan: tekan Next 3–4 kali lagi, perhatikan panel antrean tiap kali | Tetap album yang sama, urut. Tidak pernah berganti jadi mix | |
| 77 | Sekarang tekan **Previous** | Kembali ke track sebelumnya. (Ini yang dulu mati total) | |
| 78 | Klik track 1 di panel antrean | Track 1 diputar ulang dari awal — tidak nyangkut | |
| 79 | Ulangi 75–78 dari **playlist**, bukan album | Perilaku sama | |
| 80 | Putar 1 lagu saja dari kartu Beranda (bukan album), biarkan sampai habis | Boleh mengikuti autoplay YouTube — ini memang perilaku yang benar saat antrean habis | |

### D2. Timer tidur

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 81 | Putar lagu, lompat ke ±10 detik sebelum habis. Klik timer tidur → **Akhir lagu ini**. Biarkan habis | Musik **berhenti**. Antrean **tidak** maju. Ini cacat #15 yang dulu diam-diam gagal | |
| 82 | Lanjutan 81: tunggu 30 detik setelah berhenti | Tetap diam. Tidak ada lagu yang tiba-tiba jalan sendiri (YouTube sempat memulai lagu berikutnya di belakang layar) | |
| 83 | Lanjutan 82: tekan tombol Play | Musik jalan lagi normal — penjagaannya lepas begitu kamu sendiri yang minta | |
| 84 | Klik timer tidur → 15 menit. Lihat **ikon bulannya** | Ada badge angka **15** menempel di ikon. Tidak perlu hover | |
| 85 | Tunggu ±1 menit, lihat lagi | Badge turun jadi 14 | |
| 86 | Klik timer tidur → Nonaktif | Badge hilang, ikon kembali normal | |
| 87 | Klik timer tidur → Akhir lagu ini | Badge menampilkan **♪** (bukan angka — mode ini tidak punya hitungan mundur) | |

### D3. Media key & protokol

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 88 | Putar lagu. Minimalkan Kaset. Tekan tombol **⏭ di keyboard** | Lagu maju. (Dulu tidak bereaksi — Chromium di dalam WebView2 yang merebut tombolnya) | |
| 89 | Tekan tombol **⏯** di keyboard | Jeda, lalu tekan lagi → jalan | |
| 90 | Buka PowerShell, jalankan `start "kaset://play?v=t82Q3f4pNUY"` | Kaset ke depan, lagu diputar, dan player bar menampilkan **nama artis** — bukan cuma judul | |

### D4. Aksesibilitas (Narrator: Ctrl + Win + Enter)

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 91 | Tab menyusuri player bar | Tiap tombol disebut namanya saja. **Tidak ada lagi bunyi kode/simbol** setelah nama tombol | |
| 92 | Fokuskan tombol Ulangi, tekan **Spasi**, lalu Shift+Tab dan Tab lagi supaya fokus kembali ke tombol itu. Ulangi 3× | Namanya mengikuti mode: nonaktif → semua → satu. **Fokus harus benar-benar pindah lalu balik** — Narrator tidak membacakan ulang nama tombol yang sedang difokus | |
| 93 | Ketik di kotak cari, tekan Enter, klik kotak cari lagi, Tab ke tombol ✕ pada baris riwayat | Disebut "Hapus dari riwayat: \<kata\>" | |

### D5. Jendela

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 94 | Maximize jendela. Tutup ✕, lalu **Quit dari tray**. Buka Kaset lagi | Terbuka dalam keadaan **maximize** | |
| 95 | Lanjutan 94: klik restore (un-maximize) | Kembali ke ukuran & posisi sebelum di-maximize, bukan ukuran acak | |
| 96 | Maximize, tutup ✕ (ke tray, bukan quit), buka lagi dari tray | Masih maximize — tidak diam-diam jadi windowed | |
| 97 | Masuk mini player, klik ✕, **perhatikan layar saat jendela menghilang** | Langsung hilang. Tidak membesar, tidak berkedip jadi layar penuh, tidak melompat ke pojok | |
| 98 | Lanjutan 97: buka lagi dari tray | Shell penuh, ukuran sebelum mini player, chrome kembali, audio tidak putus | |

### D6. Tata letak & lirik

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 99 | Kecilkan jendela sampai mentok (980 px). Lihat bagian tengah player bar | Judul, artis, dan album **tidak terpotong**. Tombol suka tetap terlihat | |
| 100 | Di jendela mentok itu, tekan **Shift + /** | Contekan pintasan muat di jendela dan **bisa digulir** — tidak terpotong atas-bawah | |
| 101 | Buka panel lirik saat memutar lagu | Judul "Lirik" di header sejajar dengan teks liriknya | |
| 102 | Gulir ke bawah sampai akhir lirik | Ada baris **"Sumber: …"** yang menyebut penyedianya (LRCLib / NetEase / YouTube Music) | |
| 103 | Putar lagu yang liriknya tidak ada | Baris "Sumber:" **tidak muncul** (bukan "Sumber: " kosong) | |

### D7. Lirik tersinkron dari YouTube Music

YouTube Music kini jadi penyedia **pertama**, di depan LRCLib dan NetEase — LRCLib sering kosong untuk
katalog Indonesia, dan lirik YouTube datang dari katalog yang sama dengan lagunya (tidak ada
pencocokan judul/artis/durasi yang bisa meleset).

Tiga videoId di bawah sudah diverifikasi lewat API dan bisa dipakai sebagai kasus uji tetap:

| videoId | Sumber | Baris | Tersinkron |
|---|---|---|---|
| `t82Q3f4pNUY` | Musixmatch | 33 | ya (semua baris) |
| `mZsHggY8G6M` | LyricFind | 51 | ya (semua baris) |
| `BiQIc7fG9pA` | Musixmatch | 34 | **tidak** — teks polos |

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 104 | `start "kaset://play?v=t82Q3f4pNUY"`, buka panel lirik | Lirik muncul dan **baris aktifnya menyala mengikuti lagu** | |
| 105 | Lanjutan 104: geser scrubber ke tengah lagu | Baris yang menyala ikut melompat ke posisi itu | |
| 106 | Lanjutan 104: lihat akhir lirik | "Sumber: YouTube Music" | |
| 107 | `start "kaset://play?v=mZsHggY8G6M"` (lagu LyricFind) | Sama — tersinkron. Penyedia hulu tidak boleh mengubah perilaku | |
| 108 | `start "kaset://play?v=BiQIc7fG9pA"` (lagu tanpa versi sync) | Lirik tetap muncul sebagai **teks polos** yang bisa digulir — tidak kosong, tidak error | |
| 109 | Putar lagu Indonesia yang dulu liriknya kosong gara-gara LRCLib | Sekarang ada liriknya. **Ini alasan utama urutannya dibalik** — kalau masih kosong, pembalikannya tidak menyelesaikan apa pun | |
| 110 | Putar lagu yang benar-benar tidak punya lirik di mana pun | Panel bilang lirik tidak tersedia, aplikasi tidak menggantung | |
| 111 | Matikan Wi-Fi, ganti lagu, buka panel lirik | Gagal dengan pesan, bukan crash atau loading selamanya | |

---

## C. Yang masih belum tercakup uji apa pun

Dicatat jujur, bukan diklaim aman:

- **ViewModel dan lapisan Platform tidak punya test sama sekali.** 424 test seluruhnya `KasetWin.Core`.
  `SearchViewModel` (724 baris) dan `PlaylistDetailViewModel` (859 baris) hanya diuji dengan tangan.
- **Wiring timer tidur.** `SleepTimer` sendiri diuji 9 test; bahwa `PlayerService` benar-benar
  menjeda dan ticker benar-benar hidup/mati mengikuti state hanya bisa dibuktikan lewat langkah
  11–17 di atas — dan langkah 15 membuktikan wiring itu **belum** benar.
- **Klien IPC Discord.** Pemetaan aktivitasnya diuji 12 test, tapi frame named-pipe yang benar-benar
  diterima Discord hanya bisa dibuktikan lewat langkah 43–51.
- **Pintasan global & geometri jendela** sepenuhnya interop Win32 — nol cakupan test otomatis.
- **Mode YouTube penuh** (`YouTube*Page`, Shorts, watch) masih WIP dan tidak dicakup checklist ini.
- **Brand Account** dan **EU consent wall** (Tugas 30.5 / 30.6) butuh akun & region sungguhan.
