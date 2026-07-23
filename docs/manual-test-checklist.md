# Checklist Uji Manual — KasetWin

Hal-hal yang **tidak bisa** dijamin oleh 497 test headless dan job build CI: apa pun yang melibatkan
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

**Putaran 4 — 2026-07-23.** Bagian D + E dijalankan hampir penuh, ditambah sebagian A.

- **Lulus:** 12, 15, 17, 30, 58c, 59, 65, 65b, 67, 75, 76, 77, 80–87, 89, 94–98, 101, 104–108,
  114–120, 123–126. Lirik tersinkron **terkonfirmasi jalan** (label `Sumber: YouTube Music — LyricFind`).
- **Tidak diuji:** 13, 60, 79, 110, 122 (dilewati); 70 (Podcast tidak tersedia di region ID);
  112–113 (aplikasi tidak pernah mati sendiri lagi sejak perbaikan crash).
- **Cacat baru — lihat seksi F di bawah.**

> **Koreksi 2026-07-23.** Hasil putaran 4 sempat cuma ditulis sebagai ringkasan di atas dan diangkat
> ke seksi F — **kolom Hasil per baris dibiarkan kosong**, padahal penguji sudah menjawab tiap nomor
> satu per satu. Commit `24101da` mengklaim "tercatat semua" lebih besar dari kenyataannya. Ke-72
> jawaban itu sekarang ada di barisnya masing-masing, bertanggal 2026-07-23. Ringkasan tidak
> menggantikan kolom Hasil: ringkasan hilang di antara putaran, barisnya tidak.

---

## G. Uji perbaikan putaran 5 (2026-07-23)

**Ini yang harus diuji sekarang** — 20 langkah, 127–146. Menutup sebagian besar seksi F. **Belum
satu pun diuji dengan tangan**: build hijau dan 497 test lulus, tapi seksi F sendiri lahir dari
hal-hal yang lolos kedua gerbang itu.

Kalau waktumu sedikit, enam ini yang paling menentukan: **142** (Ulangi — gejala yang sudah dua kali
dilaporkan), **129** (`kaset://` lengkap), **130** (next cepat tidak berhenti total), **133** (ikon
taskbar), **137** (bunyi timer "akhir lagu ini"), **140** (like/love sinkron — satu-satunya yang
perbaikannya belum terbukti menyasar sebab yang benar).

### G0. Regresi wajib — jalankan lima ini SEBELUM 127

ADR 0006 mengubah jalur pemuatan track (tiket generasi + pemuatan paksa). Kelima langkah ini lulus
di putaran 4 **lewat jalur yang baru saja diubah itu**, jadi statusnya sekarang tidak diketahui —
bukan aman. Kalau salah satu pecah, tersangkanya ADR 0006, dan tidak ada gunanya melanjutkan ke 127.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 75 | Album → putar track 1 → Next (lihat panel antrean) | Track 2 album yang sama; antrean tidak berubah | ✅ 2026-07-23 (putaran 6, pasca ADR 0006) |
| 76 | Next 3–4 kali lagi | Tetap album yang sama, urut; tidak pernah jadi mix | ✅ 2026-07-23 (putaran 6) |
| 77 | Previous | Kembali ke track sebelumnya | ✅ 2026-07-23 (putaran 6) |
| 81 | Timer "Akhir lagu ini", biarkan lagu habis | Berhenti; antrean tidak maju | ⚠️ 2026-07-23 (putaran 6): musik BERHENTI dengan benar, tapi antrean melompat jauh — penguji memperkirakan 12 sampai 35 lagu terlewat <br> ✅ 2026-07-23 (putaran 7): **antrean aman** setelah perbaikan H1 <br> ❌ 2026-07-23 (putaran 7): tetap tidak ada bunyi maupun toast — lihat H3 |
| 115 | `start "kaset://play?v=t82Q3f4pNUY"`, buka panel lirik | Baris lirik menyala mengikuti lagu | ⚠️ 2026-07-23 (putaran 6): lirik tersinkron jalan, tapi **nama album hilang DAN sampul album hilang** di player bar. Lihat seksi H |

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 127 | Nyalakan Narrator, Tab ke daftar antrean / saran pencarian | Menyebut **"Judul — Artis"**. Tidak ada lagi dump `Id = …, VideoId = …` | ⏭️ 2026-07-23 (putaran 7): dilewati — penguji berhenti menguji Narrator (melelahkan, dan pelafalan Inggrisnya sulit dipahami) |
| 128 | Buka panel antrean sampai sidebar menyempit, Tab ke tombol sumber bundar | "Sumber: Musik — klik untuk pindah ke YouTube" (mengikuti sumber aktif) | ❌ 2026-07-23 (putaran 7): tidak berubah sama sekali dari sebelum perbaikan |
| 129 | `start "kaset://play?v=BEPSc8q6Bd8"` | Judul **dan album** muncul di player bar, dan kartu "Sedang diputar" di panel antrean **terisi** | ❌ 2026-07-23 (putaran 7): album, sampul di player bar, DAN sampul di antrean semuanya tidak muncul. Sebab ditemukan di putaran 7 — lihat H2 |
| 130 | Tekan **Next cepat 5–6 kali berturut-turut** | Selalu ada lagu yang jalan. Tidak pernah berhenti total | ✅ 2026-07-23 (putaran 7) |
| 131 | Matikan Wi-Fi saat memutar, nyalakan lagi, klik lagu **yang sama** | Lagu itu diputar ulang dari awal. (Dulu diam saja sampai kamu pilih lagu lain) | ⚠️ 2026-07-23 (putaran 7): pemutaran lanjut, tapi lirik tidak muncul; mengklik lagu yang sama membuat tampilan blank dan tidak memutar. Lihat H7 |
| 132 | Klik lagu yang **sedang** diputar | Mengulang dari 0:00 — **perubahan perilaku yang disengaja**, konsekuensi dari #131 | ❌ 2026-07-23 (putaran 7): lihat 131 |
| 133 | Lihat thumbnail taskbar (hover ikon Kaset di taskbar) | Tiga tombol (prev/play/next) **tampak ikonnya**, tidak kosong | ⚠️ 2026-07-23 (putaran 7): ikonnya ada tapi sangat tipis, nyaris tak terlihat — seperti tanpa outline |
| 134 | Ganti bahasa ke English, hover tombol thumbnail taskbar | Tooltipnya Inggris (dulu selalu Indonesia) | ⏭️ 2026-07-23 (putaran 7): tidak relevan — penguji tidak pernah melaporkan tooltip taskbar sebagai masalah. Langkah spekulatif, dihapus dari daftar wajib |
| 135 | Buka panel antrean setelah memutar beberapa lagu dari satu album | Ada bagian **"Sudah diputar"** yang redup di atas lagu yang sedang diputar | ✅ 2026-07-23 (putaran 7) |
| 136 | Klik salah satu lagu di bagian "Sudah diputar" | Lagu itu diputar lagi. Antrean tetap utuh | ✅ 2026-07-23 (putaran 7) |
| 137 | Set timer tidur **"Akhir lagu ini"**, biarkan lagunya habis | Musik berhenti, **toast muncul, DAN terdengar bunyi**. Dulu mode ini diam total | ❌ 2026-07-23 (putaran 7): tidak ada bunyi maupun toast sama sekali. Lihat H3 |
| 138 | Set timer 1 menit (ubah `SleepTimerPresets` jadi `[1]`), biarkan habis | Toast + bunyi yang sama | ⏭️ 2026-07-23 (putaran 7): tidak bisa dinilai selama 137 masih gagal |
| 139 | Set timer lalu **batalkan** | Tidak ada bunyi sama sekali | |
| 140 | Suka sebuah lagu dari player bar, lalu buka halaman albumnya | Baris lagu itu ikut tampil sebagai disukai **seketika**, tanpa menunggu | ✅ 2026-07-23 (putaran 7) — like/love akhirnya sinkron |
| 141 | Pengaturan → Sumber lirik | Keterangannya sekarang dua baris pendek | ❌ 2026-07-23 (putaran 7): keterangannya masih panjang sekali |
| 142 | Narrator hidup. Fokuskan tombol **Ulangi**, tekan Spasi, lalu Shift+Tab dan Tab lagi supaya fokus benar-benar pindah lalu balik. Ulangi 3× | "Ulangi nonaktif" → "Ulangi semua" → "Ulangi satu lagu". **Tidak ada lagi track id / song id** | ❌ 2026-07-23 (putaran 7): tidak berubah sama sekali |
| 143 | Ketik di kotak cari, tekan Enter, klik kotak cari lagi, Tab ke tombol **✕** pada baris riwayat | "Hapus dari riwayat: \<kata\>". **Bukan** judul lagu yang sedang diputar | ⏭️ 2026-07-23 (putaran 7): dilewati bersama 127 |
| 144 | Tab menyusuri **sidebar** dari atas ke bawah | Tiap item disebut namanya. Tidak ada "navigation item expanded", tidak ada "menu tindakan button", Pengaturan **bukan** "pop out button" | |
| 145 | Dalam mode **English**, ulangi 144 | Semuanya berbahasa Inggris. Tidak ada nama Indonesia yang menyelinap | |
| 146 | Minimalkan Kaset, tekan **⏭ keyboard** 5–6 kali berturut-turut | Tiap tekan lagunya maju. Tidak ada yang progress bar-nya jalan tapi **tanpa suara** (#88b) | ✅ 2026-07-23 (putaran 7) |

> **142–145 adalah inti dari perbaikan aksesibilitas putaran 5, dan sempat tidak punya langkah uji
> sama sekali.** Tugas 52 mengklaim menutup #20/23/28/91/92, tapi seksi ini cuma menguji daftar
> antrean & saran pencarian (#127) — tiga permukaan yang benar-benar dilaporkan pemilik repo dua
> putaran berturut-turut tidak pernah masuk daftar. Perbaikan `ToString()` itu satu rantai: kalau
> 142–144 masih membacakan objek yang salah, berarti diagnosisnya belum lengkap, bukan sekadar
> kurang menempel di satu tombol.

## H. Cacat dari putaran 6 (2026-07-23) — ditemukan oleh G0

G0 memang untuk ini: dua dari lima langkah regresi gagal, dan keduanya tidak terlihat oleh 497 test.

### H1. Timer tidur menghentikan musik, tapi antrean jalan terus — **diperbaiki**

Gejala (#81): musik berhenti benar, tapi antrean melompat belasan sampai puluhan lagu.

Sebab, dari kode: penjaga sleep-stop di `HandleStateUpdate` hanya mencegat laporan yang **sedang
memutar**. Setiap pause yang Kaset kirim membuat halaman melapor dirinya *paused* — pada video yang
sudah dia pindahi sendiri — dan laporan paused itu jatuh lurus ke adopsi antrean di bawahnya. Jadi
YouTube menyusuri rantai autoplay-nya, kita mem-pause tiap satu, dan tiap pause itu menambah satu
track lalu memajukan indeks. Komentar di kodenya bahkan sudah mengklaim "ia juga menekan adopsi
antrean" — perilaku yang tidak pernah benar-benar ditulis.

Perbaikan: selama stop berlaku, laporan bervideoId **lain** diabaikan seluruhnya, sementara laporan
untuk track yang dihentikan tetap dihormati (kalau tidak, posisi di UI beku). Dikunci 3 test baru.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 147 | Timer "Akhir lagu ini" di tengah album, biarkan habis, tunggu 2 menit, lalu buka panel antrean | Antrean **persis** seperti sebelum tidur; lagu yang berhenti masih yang ditandai sedang diputar | |
| 148 | Lanjutan 147: tekan Play | Lagu itu jalan lagi, dan antrean kembali mengikuti seperti biasa | |

### H2. `kaset://` masih tanpa album, dan sekarang tanpa sampul juga — **diperbaiki, sebab terbukti**

Gejala (#115): lirik tersinkron jalan, tapi player bar tidak menampilkan nama album **maupun sampul**.
Nama album adalah cacat yang sama dengan #73 putaran 4, yang putaran 5 klaim sudah ditutup; sampul
hilang itu **baru**.

Belum ada sebab yang terbukti, dan tidak akan ditebak. Yang sudah pasti: seam-nya sendiri benar —
`ProtocolLaunch_EnrichesTheQueueEntry_NotJustTheCurrentTrack` lulus, jadi begitu fetcher memberi
album, antrean dan player bar terisi. Yang belum terbukti: apakah fetcher itu berhasil **di dalam
aplikasi**. Enrichment menelan semua kegagalan by design, jadi gagalnya senyap — pola yang persis
sama dengan lirik tersinkron dulu (jalan di ApiExplorer tanpa login, gagal 100% di aplikasi
ber-login, ADR 0005). Karena itu kedua fetcher sekarang menulis ke `diag.log`.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 149 | `start "kaset://play?v=t82Q3f4pNUY"`, tunggu lagunya jalan, lalu buka `%LOCALAPPDATA%\Packages\Kaset.KasetWin_kjgd17zy2bc08\LocalState\diag.log` | Ada baris `enrich song …`. Isinya yang menentukan: `FAILED` berarti panggilannya ditolak, `album=<null>` berarti responsnya memang tidak membawa album | ✅ 2026-07-23 (putaran 7): log menjawabnya — `FAILED: COMException` di **setiap** lagu |

**Sebabnya terbukti, dan jauh lebih besar dari album.** Log menunjukkan 100% kegagalan, satu per lagu:

```
enrich song videoId=BiQIc7fG9pA FAILED: COMException:
This method can only be called from the thread that created the object.
```

`CoreWebView2.CookieManager` adalah objek COM yang terikat thread pembuatnya, dan **setiap** permintaan
InnerTube menandatangani dirinya dengan cookie. Jadi setiap panggilan API dari thread non-UI selalu
gagal untuk pengguna yang login — bukan hanya enrichment album. Gagalnya senyap di mana pun pemanggil
menganggap hasilnya opsional, dan itulah kenapa "album hilang" selamat dari satu putaran perbaikan:
kode di atasnya benar dan lulus test, tapi panggilannya tidak pernah sampai ke jaringan.

Perbaikan: pembacaan cookie di-marshal ke thread UI (`UiThreadCookieSource`). Sesudahnya, jalur yang
sama berbunyi:

```
enrich song videoId=t82Q3f4pNUY title=more than i like album=MPREb_I7Jb8zS9tBs/ thumb=yes
enrich album MPREb_I7Jb8zS9tBs title=Beautiful Mind
```

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 150 | `start "kaset://play?v=BEPSc8q6Bd8"` | Judul, **album**, dan **sampul** muncul di player bar, dan kartu "Sedang diputar" di antrean punya sampul | |
| 151 | Putar lagu dari kartu Beranda (bukan dari album) | Baris album ikut terisi — jalur yang sama, penerima manfaat yang sama | |

### H3. Timer tidur tidak berbunyi dan tidak memunculkan toast — **belum diperbaiki**

Gejala (#81, #137): musik berhenti benar, tapi tidak ada bunyi maupun toast — untuk kedua mode.
Putaran 5 mengklaim menutup ini (tugas 60, event `Expired`), jadi klaim itu salah.

Belum ditelusuri. Pertanyaan penguji yang harus dijawab lebih dulu, karena menentukan perilakunya:
**bunyinya sekali saat habis, atau ada peringatan sebelum habis?** Jawaban yang direncanakan: satu
bunyi tepat saat berhenti. Peringatan 10 detik sebelum tidur justru membangunkan orang yang sedang
tidur — kebalikan dari gunanya fitur ini.

### H4. Perbaikan aksesibilitas putaran 5 gagal di dua permukaan — **belum diperbaiki**

#128 (tombol sumber ringkas) dan #142 (tombol Ulangi) **tidak berubah sama sekali**. Tugas 52 & 53
mengklaim keduanya. `ToString()` pada `Song` menutup satu kelas masalah, tapi jelas bukan kelas yang
memuat kedua tombol ini. Perlu Narrator hidup — dan penguji sudah menyatakan berhenti mengujinya
untuk sekarang, jadi ini menunggu, bukan diam-diam dianggap beres.

### H5. Sisa yang dilaporkan putaran 7 — **belum diperbaiki**

| Asal | Gejala |
|---|---|
| 141 | Keterangan sumber lirik di Pengaturan **masih panjang** — pemangkasan putaran 5 tidak berpengaruh |
| 100 | Contekan pintasan **masih memenuhi layar** — sudah tiga putaran, tiga perbaikan, nol perubahan |
| 133 | Ikon thumbnail taskbar muncul tapi **sangat tipis**, nyaris tak terlihat |
| 110 | Lagu tanpa lirik menampilkan "Putar lagu untuk melihat lirik", bukan "lirik tidak tersedia" |
| 131 | Setelah internet kembali: lirik tidak muncul, dan mengklik lagu yang sama membuat tampilan **blank tanpa memutar** |
| — | **Klik toggle sumber Music dua kali → halaman "Home — Coming soon" (beranda mode YouTube) muncul**, padahal sumbernya Music |

### H6. Yang sudah beres menurut penguji

- **Sampul mini player tidak berkedip lagi.** Dihapus dari daftar cacat terbuka.
- **Like & love sinkron** (#140) — perbaikan parsial putaran 5 ternyata memang menyasar sebab yang benar.

### H7. Permintaan fitur baru

- **Panel lirik & antrean di dalam mini player**, seperti mini player Apple Music.



## F. Cacat terbuka dari putaran 4 (2026-07-23)

> **Status per putaran 5:** F1 (aksesibilitas), F2 (`kaset://`), F3 (nyangkut), dan bagian F4 (ikon
> taskbar, like/love, keterangan Settings) sudah ditangani — uji lewat seksi G di atas. Yang **masih
> terbuka**: contekan pintasan memenuhi layar, sidebar tidak menyempit, sampul berkedip di mini
> player, dan klik kanan mati di beberapa halaman (nomor 4–7 dalam triase, sengaja dilewati kali ini).

### F1. Aksesibilitas masih rusak — perbaikan glyph tidak menyelesaikannya

| # | Gejala |
|---|---|
| 20, 91 | Narrator masih menyebut hal-hal tak jelas di sekitar sidebar; masih campur bahasa |
| 23, 92 | Tombol Ulangi: **membacakan track id / song id**, bukan nama modenya |
| 28, 93 | Tombol hapus riwayat: **membacakan judul lagu yang sedang diputar**, bukan "Hapus dari riwayat: …" |
| 29 | Toggle sumber menyebut "music source button off, music source button on". Seharusnya menyatakan sumber **aktif** dan bahwa klik akan menggantinya |

> Menandai `FontIcon` sebagai dekoratif ternyata hanya menghilangkan bunyi kode glyph. Nama yang
> dibacakan masih salah objek — artinya `AutomationProperties.Name` tidak menempel pada elemen yang
> benar-benar menerima fokus. Perlu diselidiki dengan Narrator hidup, bukan dari kode.

### F2. Peluncuran `kaset://` tidak melengkapi metadata & antrean

| # | Gejala |
|---|---|
| 73, 90 | `start "kaset://play?v=BEPSc8q6Bd8"` → lagu diputar, tapi **album tidak muncul** di player bar dan panel antrean menampilkan **"Now playing" kosong**. Memutar track yang sama secara manual dari halaman album → semuanya muncul normal. Terjadi di semua lagu yang dicoba, bukan satu lagu tertentu |

### F3. Pemutaran nyangkut

| # | Gejala |
|---|---|
| 77b | Setelah menekan **Next berkali-kali dengan cepat**, pemutaran berhenti total — tidak ada lagu yang jalan |
| 88b | Media key sempat nyangkut: **progress bar berjalan tapi tidak ada suara**; dicoba lagi normal. Dugaan: hook-nya lambat |
| 111b | Setelah internet kembali, lagu **tidak lanjut sendiri**. Mengklik lagu yang sama tidak memutar; harus ganti lagu lain dulu |

### F4. UI

| # | Gejala |
|---|---|
| 74, 100 | Contekan pintasan **masih memenuhi layar** saat windowed — perbaikan tinggi maksimum belum berpengaruh |
| — | **Sidebar tidak ikut mengecil** saat jendela windowed/kecil; seharusnya menyempit apa pun situasinya |
| — | **Sampul album berkedip-kedip** saat mode mini player |
| — | **Klik kanan tidak berfungsi** di beberapa tempat: playlist, album, playlist podcast, dsb. |
| 88c | **Ikon taskbar/thumbnail kosong** (lihat tangkapan layar putaran 4) |
| — | **Like dan love tidak sinkron** satu sama lain |
| 121 | Keterangan sumber lirik di Pengaturan **terlalu panjang** |

### F5. Permintaan fitur (bukan cacat)

| Asal | Permintaan |
|---|---|
| 17 | Bunyi notifikasi saat timer tidur habis |
| 78 | Antrean menampilkan lagu yang **sudah** diputar, bukan memindahkannya ke Riwayat — kelompokkan jadi "telah diputar / sedang diputar / selanjutnya" seperti YouTube Music |
| 42, 99 | Teks now-playing kini berjalan (marquee); masih kurang sreg tapi diterima |

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
| 12 | Tanpa mengklik apa pun, arahkan kursor ke tombol timer tidur dan diamkan ±1 detik | Tooltip yang muncul menyebut **sisa waktu** (mis. "Timer tidur — 14 menit"), bukan sekadar "Timer tidur" | ❌ 2026-07-22: tooltip tidak menyebut sisa waktu <br> ✅ 2026-07-23 |
| 13 | Klik tombol timer tidur → pilih **15 menit**. Tunggu toastnya hilang. Klik tombol itu lagi → pilih **30 menit**. Sekarang diamkan lebih dari 15 menit | Musik **masih jalan** setelah menit ke-15 dan baru berhenti di menit ke-30. Kalau berhenti di menit 15, berarti timer lama tidak dibatalkan dan ada dua timer berjalan | ⏭️ 2026-07-23: tidak diuji (penguji tidak mau menunggu 30 menit) |
| 14 | Klik tombol timer tidur → pilih **Nonaktif** | Toast "Timer tidur dibatalkan"; ikon bulan kembali ke warna normal (putih/abu) | ✅ 2026-07-22 |
| 15 | **Uji ujung.** Putar lagu, lompat ke ±10 detik sebelum lagu habis. Klik tombol timer tidur → **Akhir lagu ini**. Biarkan lagunya habis sendiri | Pemutaran **berhenti** saat lagu selesai, dan antrean **tidak** maju ke lagu berikutnya | ❌ 2026-07-22: ikon timer padam (timer terpakai) tapi **musik lanjut ke lagu berikutnya** <br> ✅ 2026-07-23 |
| 16 | Klik tombol timer tidur → **Akhir lagu ini**. Lalu tekan tombol **Next** di player bar (skip manual) | Musik pindah ke lagu berikutnya seperti biasa, dan ikon timer **tetap menyala** — timer tidak boleh ikut terpakai oleh skip manual | ✅ 2026-07-22 (ikon masih menyala) |
| 17 | Klik tombol timer tidur → **15 menit**. Biarkan 2–3 lagu berganti sendiri | Ikon timer **tetap menyala** melewati tiap pergantian lagu. Kalau padam saat lagu berganti, berarti timer ikut tereset di batas antar-lagu | ✅ 2026-07-23 - memunculkan permintaan bunyi notifikasi, lihat F5 · **uji ulang: G #137–139** |

> Dua langkah lama (18, 19 — "cek CPU di Task Manager") dihapus di putaran 2: itu memeriksa detail
> internal (`DispatcherTimer` berhenti saat timer mati) yang tidak bisa dinilai dari luar dan tidak
> berarti apa-apa bagi pengguna.

### A3. Aksesibilitas (Narrator)

Nyalakan Narrator: **Ctrl + Win + Enter**. Matikan dengan kombinasi yang sama.

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 20 | Klik di area player bar, lalu tekan **Tab** berulang dari kiri ke kanan sambil mendengarkan Narrator | Tiap tombol disebut **namanya saja**: Acak, Sebelumnya, Putar/Jeda, Berikutnya, Ulangi, Suka, Timer tidur, Mini player, Lirik, Antrean, Bisukan. Tidak ada yang cuma "tombol", tidak ada yang membacakan simbol/kode, dan tidak ada nama berbahasa Indonesia saat aplikasi berbahasa Inggris | ❌ 2026-07-22: banyak elemen tak jelas di sekitar sidebar ("navigation item expanded", "menu tindakan button", Pengaturan disebut "pop out button"); masih campur bahasa Indonesia <br> ❌ 2026-07-23: masih sama persis (ada tangkapan layar) · **uji ulang: G #144–145** |
| 21 | Tab sampai fokus berada di slider posisi lagu, lalu ke slider volume | Disebut "Posisi pemutaran" dan "Volume", bukan sekadar angka | ✅ 2026-07-22 |
| 22 | Klik tombol ❤️ (Suka) pada lagu yang sedang diputar, lalu tekan Shift+Tab / Tab sampai fokus kembali ke tombol itu | Namanya berubah dari "Suka" jadi "Batal suka" — dan **tidak** ada karakter/kode aneh yang ikut dibacakan | ❌ 2026-07-22: statusnya benar ("suka") tapi Narrator ikut membacakan kode simbol |
| 23 | Fokuskan tombol Ulangi (Tab), tekan **Spasi** untuk mengganti mode, lalu Shift+Tab dan Tab lagi supaya fokus kembali ke tombol itu. Ulangi 3× | Nama tombol mengikuti modenya: "Ulangi nonaktif" → "Ulangi semua" → "Ulangi satu lagu" | ❌ 2026-07-22: nama tidak berubah saat mode berganti <br> ❌ 2026-07-23: malah membacakan track id / song id, bukan nama modenya · **uji ulang: G #142** |
| 24 | Putar sebuah **podcast**, lalu Tab sampai fokus ke tombol lirik | Disebut "Subtitel (CC)", bukan "Lirik" | ✅ 2026-07-22 |
| 25 | Buka panel antrean, Tab sampai fokus ke tautan judul/artis salah satu baris lagu | Narrator menyebut **judul lagunya**, bukan "Buka album". Ini pengecualian yang disengaja: tautan itu membungkus judul, jadi memberinya nama sendiri justru menghapus judulnya | ✅ 2026-07-22 |
| 26 | Buka Pengaturan → gulir ke Ekualiser → Tab menyusuri kesembilan slider | Tiap slider menyebut frekuensinya ("Penguatan 62 Hz", "Penguatan 125 Hz", …), bukan sembilan slider anonim yang identik | ✅ 2026-07-22 |
| 27 | Buka panel antrean, Tab sampai fokus masuk ke daftarnya. Lalu pindah ke tab Riwayat, ulangi | Daftarnya disebut "Antrean pemutaran", dan yang di tab Riwayat disebut "Riwayat pemutaran" | ✅ 2026-07-22 |
| 28 | Klik kotak cari, ketik sesuatu dan tekan Enter (supaya masuk riwayat). Klik kotak cari lagi sampai daftar riwayat muncul. Tab sampai fokus ke tombol **✕** di salah satu baris riwayat | Narrator menyebut "Hapus dari riwayat: \<kata yang dicari\>" — jadi sepuluh baris yang identik bisa dibedakan | ❌ 2026-07-22: tidak disebut <br> ❌ 2026-07-23: malah membacakan judul lagu yang sedang diputar · **uji ulang: G #143** |
| 29 | Buka panel antrean sampai sidebar ikut menyempit jadi ikon saja. Tombol sumber Music/YouTube di bawah sidebar berubah bentuk jadi tombol bundar. Tab sampai fokus ke tombol itu | Narrator menyebut sumber yang **sedang aktif** dan bahwa mengklik akan menggantinya (mis. "Sumber: Music — klik untuk pindah ke YouTube") — bukan cuma "tombol" | ❌ 2026-07-23: "music source button off / music source button on" - tidak menyebut sumber yang sedang aktif · **uji ulang: G #128** |
| 30 | Tab menyusuri player bar dari awal | Narrator **melewati** gambar sampul album — tidak berhenti di situ sama sekali. Ini kebalikan dari langkah lain: yang benar justru **tidak** disebut, karena judul lagunya sudah dibacakan di sebelahnya | ✅ 2026-07-23 (sampul memang dilewati) |

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
| 42 | Kecilkan jendela sampai batas minimum (980 px) | Cluster kanan player bar (6 tombol + slider) tidak terpotong/tumpang tindih | ⚠️ 2026-07-22: tombol aman, tapi **nama artis & album di bagian tengah terpotong** <br> ⚠️ 2026-07-23: sekarang jadi teks berjalan; penguji masih kurang sreg tapi menerima |

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
| 58c | Masuk mini player. Klik **✕**, dan perhatikan baik-baik apa yang terjadi di layar selama jendela menghilang | Jendela langsung hilang ke tray. Tidak boleh terlihat membesar, tidak boleh berkedip jadi layar penuh, dan tidak boleh melompat ke pojok layar | ❌ 2026-07-22: tidak membesar lagi, tapi jendela sempat **jadi layar penuh**, lalu mini player **melompat ke pojok kiri atas**, baru menghilang <br> ✅ 2026-07-23 |
| 59 | Klik tombol **Maximize**. Tutup aplikasi lewat ✕ lalu Quit dari tray. Buka lagi | Terbuka **dalam keadaan maximize** — sama seperti saat ditutup | ❌ 2026-07-22: terbuka windowed. Perilaku lama sengaja hanya menyimpan ukuran windowed; itu keputusan yang salah dan sudah diubah <br> ✅ 2026-07-23 |

---

## B. Regresi inti (jalankan sebelum tiap rilis)

| # | Area | Langkah | Harapan | Hasil |
|---|------|---------|---------|-------|
| 60 | Login | Sign in dengan akun Google | Berhasil; nama akun muncul di sidebar | ⚠️ 2026-07-22: berhasil tapi lambat & kadang nyangkut — halaman login sempat muncul walau sudah login, dan baru masuk setelah dibatalkan lalu diklik lagi <br> ⏭️ 2026-07-23: tidak diuji ulang |
| 61 | Pemutaran | Putar lagu dari Beranda | Audio jalan, sampul + judul benar | ✅ 2026-07-22 |
| 62 | Antrean | Putar album, tekan Next beberapa kali | Maju sesuai urutan album | ✅ 2026-07-22 |
| 63 | **Ulangi satu + Next** | Set Repeat One, tekan Next | **Maju ke lagu berikutnya** (Repeat One hanya untuk akhir lagu otomatis — Tugas 30.7) | ✅ 2026-07-22 |
| 64 | Media key | Minimalkan app, tekan tombol next di keyboard | Maju, tidak mengulang lagu yang sama | ❌ 2026-07-22: tidak bereaksi sama sekali (regresi — putaran sebelumnya jalan) <br> ⚠️ 2026-07-23: penguji tidak paham maksud langkahnya - duplikat langkah 88, perlu digabung atau ditulis ulang · **uji ulang: G #146** |
| 65 | SMTC | Buka panel volume Windows | Kartu Now Playing menampilkan lagu + sampul yang benar | ❌ 2026-07-22: kartunya benar, tapi menemukan cacat antrean yang lebih besar — lihat di bawah <br> ✅ 2026-07-23 |
| 66 | Latar belakang | Tutup jendela (bukan quit) | Audio lanjut; ikon tray ada | ✅ 2026-07-22 |
| 67 | Lirik | Buka panel lirik saat memutar | Lirik tersinkron, baris aktif menyala | ⚠️ 2026-07-22: jalan, tapi label "Lirik" di header menjorok ke kiri, lirik sering tidak lengkap, dan sumber liriknya tidak pernah ditampilkan <br> ✅ 2026-07-23 |
| 68 | Cari | Cari artis | Hasil bertipe (teratas/artis/album/lagu) | ✅ 2026-07-22 |
| 69 | Playlist | Buat playlist, tambah lagu, hapus | Semua tersimpan di sisi server | ✅ 2026-07-22 |
| 70 | Podcast | Buka **satu playlist/koleksi podcast** dan putar sebuah episode | Progres tersimpan; tombol ±10s/30s muncul | ⚠️ 2026-07-22: item **Podcast di sidebar** memang tidak bisa memuat — YouTube Music tidak menyediakan Podcast untuk region Indonesia. Bukan cacat Kaset; langkah diubah agar tidak menyesatkan <br> ⏭️ 2026-07-23: tetap tidak bisa diuji - Podcast tidak tersedia untuk region Indonesia |
| 71 | Ekualiser | Ubah preset saat memutar | Suara berubah | ✅ 2026-07-22 |
| 72 | Adblock | Putar beberapa lagu | Tidak ada iklan (uBlock aktif) | ✅ 2026-07-22 |
| 73 | Protokol | Dari PowerShell jalankan `start "kaset://play?v=BiQIc7fG9pA"` (Rejoice — Official HIGE DANDism) | Kaset muncul ke depan dan memutar lagu itu, **lengkap dengan nama artis di player bar dan lirik yang ikut termuat** | ❌ 2026-07-22: lagu diputar, tapi player bar hanya menampilkan judul (nama artis kosong) dan panel lirik tetap berkata "Putar lagu untuk melihat lirik di sini" <br> ❌ 2026-07-23: dengan `kaset://play?v=BEPSc8q6Bd8` album tidak muncul di player bar dan panel antrean menampilkan "Sedang diputar" kosong; lagu lain sama saja. Memutar track yang sama secara manual dari halaman albumnya membuat semuanya muncul normal · **uji ulang: G #129** |
| 74 | Pintasan | Tekan Shift + / | Contekan pintasan muncul, dan **muat di jendela** — kalau jendelanya kecil, isinya bisa digulir, bukan terpotong atas-bawah | ⚠️ 2026-07-22: muncul, tapi memenuhi layar atas-bawah saat jendela kecil dan tidak bisa digulir <br> ❌ 2026-07-23: masih memenuhi layar atas-bawah saat windowed (ada tangkapan layar) · **masih terbuka — tidak ada langkah G** |

### 65b — Cacat antrean yang ditemukan saat menguji SMTC

Cacat terpisah dan lebih besar dari langkah 65 sendiri, dicatat sebagai langkah uji tersendiri:

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 65b | Putar album (mis. *Editorial* — Official HIGE DANDism) mulai dari track 1. Buka panel antrean. Tekan **Next** | Pindah ke track 2 album itu; antrean tetap berisi album yang sama; **Previous** mengembalikan ke track 1 | ❌ 2026-07-22: antrean tiba-tiba berganti jadi mix/queue baru; Previous tidak bisa kembali (dianggap memutar dari awal); track 1 tidak bisa diputar ulang (nyangkut); Next berikutnya mengganti antrean lagi <br> ✅ 2026-07-23 |

---

## D. Uji ulang perbaikan putaran 2 (2026-07-22, commit `faf5896`)

Semua langkah di bawah menguji cacat yang **sudah diperbaiki tapi belum pernah diklik**. Build hijau
dan 445 test headless lulus, tapi tak satu pun dari ini bisa dibuktikan tanpa tangan manusia.

Urutan sengaja: yang paling berisiko dan paling sering dipakai dulu.

### D1. Antrean & pemutaran — kerjakan ini duluan

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 75 | Buka sebuah album (mis. *Editorial*), putar dari track 1. Buka panel antrean. Tekan **Next** | Pindah ke track 2 **album yang sama**; isi panel antrean **tidak berubah** — tidak ada lagu asing yang muncul | ✅ 2026-07-23 <br> ✅ 2026-07-23 (putaran 6, pasca ADR 0006) |
| 76 | Lanjutkan: tekan Next 3–4 kali lagi, perhatikan panel antrean tiap kali | Tetap album yang sama, urut. Tidak pernah berganti jadi mix | ✅ 2026-07-23 <br> ✅ 2026-07-23 (putaran 6) |
| 77 | Sekarang tekan **Previous** | Kembali ke track sebelumnya. (Ini yang dulu mati total) | ✅ 2026-07-23 - catatan penguji: frasa "mati total" di kolom Harapan salah konteks. Lihat juga 77b <br> ✅ 2026-07-23 (putaran 6) |
| 78 | Klik track 1 di panel antrean | Track 1 diputar ulang dari awal — tidak nyangkut | ⚠️ 2026-07-23: premis langkahnya salah - track 1 sudah tidak ada di antrean, sudah pindah ke Riwayat. Berubah jadi permintaan fitur, lihat F5 · **uji ulang: G #135–136** |
| 79 | Ulangi 75–78 dari **playlist**, bukan album | Perilaku sama | ⏭️ 2026-07-23: tidak diuji |
| 80 | Putar 1 lagu saja dari kartu Beranda (bukan album), biarkan sampai habis | Boleh mengikuti autoplay YouTube — ini memang perilaku yang benar saat antrean habis | ✅ 2026-07-23 |

### D2. Timer tidur

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 81 | Putar lagu, lompat ke ±10 detik sebelum habis. Klik timer tidur → **Akhir lagu ini**. Biarkan habis | Musik **berhenti**. Antrean **tidak** maju. Ini cacat #15 yang dulu diam-diam gagal | ✅ 2026-07-23 <br> ⚠️ 2026-07-23 (putaran 6): musik BERHENTI dengan benar, tapi antrean melompat jauh — penguji memperkirakan 12 sampai 35 lagu terlewat. Lihat seksi H |
| 82 | Lanjutan 81: tunggu 30 detik setelah berhenti | Tetap diam. Tidak ada lagu yang tiba-tiba jalan sendiri (YouTube sempat memulai lagu berikutnya di belakang layar) | ✅ 2026-07-23 |
| 83 | Lanjutan 82: tekan tombol Play | Musik jalan lagi normal — penjagaannya lepas begitu kamu sendiri yang minta | ✅ 2026-07-23 |
| 84 | Klik timer tidur → 15 menit. Lihat **ikon bulannya** | Ada badge angka **15** menempel di ikon. Tidak perlu hover | ✅ 2026-07-23 |
| 85 | Tunggu ±1 menit, lihat lagi | Badge turun jadi 14 | ✅ 2026-07-23 |
| 86 | Klik timer tidur → Nonaktif | Badge hilang, ikon kembali normal | ✅ 2026-07-23 |
| 87 | Klik timer tidur → Akhir lagu ini | Badge menampilkan **♪** (bukan angka — mode ini tidak punya hitungan mundur) | ✅ 2026-07-23 |

### D3. Media key & protokol

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 88 | Putar lagu. Minimalkan Kaset. Tekan tombol **⏭ di keyboard** | Lagu maju. (Dulu tidak bereaksi — Chromium di dalam WebView2 yang merebut tombolnya) | ⚠️ 2026-07-23: lagu maju, tapi lihat 88b (sempat nyangkut) dan 88c (ikon kosong) · **uji ulang: G #133, #146** |
| 89 | Tekan tombol **⏯** di keyboard | Jeda, lalu tekan lagi → jalan | ✅ 2026-07-23 |
| 90 | Buka PowerShell, jalankan `start "kaset://play?v=t82Q3f4pNUY"` | Kaset ke depan, lagu diputar, dan player bar menampilkan **nama artis** — bukan cuma judul | ❌ 2026-07-23: lihat 73 · **uji ulang: G #129** |

### D4. Aksesibilitas (Narrator: Ctrl + Win + Enter)

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 91 | Tab menyusuri player bar | Tiap tombol disebut namanya saja. **Tidak ada lagi bunyi kode/simbol** setelah nama tombol | ❌ 2026-07-23: lihat 20 · **uji ulang: G #144–145** |
| 92 | Fokuskan tombol Ulangi, tekan **Spasi**, lalu Shift+Tab dan Tab lagi supaya fokus kembali ke tombol itu. Ulangi 3× | Namanya mengikuti mode: nonaktif → semua → satu. **Fokus harus benar-benar pindah lalu balik** — Narrator tidak membacakan ulang nama tombol yang sedang difokus | ❌ 2026-07-23: masih tidak dibacakan · **uji ulang: G #142** |
| 93 | Ketik di kotak cari, tekan Enter, klik kotak cari lagi, Tab ke tombol ✕ pada baris riwayat | Disebut "Hapus dari riwayat: \<kata\>" | ❌ 2026-07-23: lihat 28 · **uji ulang: G #143** |

### D5. Jendela

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 94 | Maximize jendela. Tutup ✕, lalu **Quit dari tray**. Buka Kaset lagi | Terbuka dalam keadaan **maximize** | ✅ 2026-07-23 |
| 95 | Lanjutan 94: klik restore (un-maximize) | Kembali ke ukuran & posisi sebelum di-maximize, bukan ukuran acak | ✅ 2026-07-23 |
| 96 | Maximize, tutup ✕ (ke tray, bukan quit), buka lagi dari tray | Masih maximize — tidak diam-diam jadi windowed | ✅ 2026-07-23 |
| 97 | Masuk mini player, klik ✕, **perhatikan layar saat jendela menghilang** | Langsung hilang. Tidak membesar, tidak berkedip jadi layar penuh, tidak melompat ke pojok | ✅ 2026-07-23 |
| 98 | Lanjutan 97: buka lagi dari tray | Shell penuh, ukuran sebelum mini player, chrome kembali, audio tidak putus | ✅ 2026-07-23 |

### D6. Tata letak & lirik

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 99 | Kecilkan jendela sampai mentok (980 px). Lihat bagian tengah player bar | Judul, artis, dan album **tidak terpotong**. Tombol suka tetap terlihat | ⚠️ 2026-07-23: lihat 42 - sekarang teks berjalan, diterima |
| 100 | Di jendela mentok itu, tekan **Shift + /** | Contekan pintasan muat di jendela dan **bisa digulir** — tidak terpotong atas-bawah | ❌ 2026-07-23: lihat 74 · **masih terbuka — tidak ada langkah G** <br> ❌ 2026-07-23 (putaran 7): masih memenuhi layar, tidak berubah sama sekali |
| 101 | Buka panel lirik saat memutar lagu | Judul "Lirik" di header sejajar dengan teks liriknya | ✅ 2026-07-23 |
| 102 | Gulir ke bawah sampai akhir lirik | Ada baris **"Sumber: …"** yang menyebut penyedianya (LRCLib / NetEase / YouTube Music) | ✅ 2026-07-23: barisnya tidak ikut tergulir melainkan tetap di area bawah panel - penguji justru lebih suka begini |
| 103 | Putar lagu yang liriknya tidak ada | Baris "Sumber:" **tidak muncul** (bukan "Sumber: " kosong) | ✅ 2026-07-23: yang tampil placeholder "Play a song to see its lyrics"; baris "Sumber:" tidak muncul |

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
| 104 | `start "kaset://play?v=t82Q3f4pNUY"`, buka panel lirik | Lirik muncul dan **baris aktifnya menyala mengikuti lagu** | ✅ 2026-07-23 |
| 105 | Lanjutan 104: geser scrubber ke tengah lagu | Baris yang menyala ikut melompat ke posisi itu | ✅ 2026-07-23 |
| 106 | Lanjutan 104: lihat akhir lirik | "Sumber: YouTube Music" | ✅ 2026-07-23 |
| 107 | `start "kaset://play?v=mZsHggY8G6M"` (lagu LyricFind) | Sama — tersinkron. Penyedia hulu tidak boleh mengubah perilaku | ✅ 2026-07-23 |
| 108 | `start "kaset://play?v=BiQIc7fG9pA"` (lagu tanpa versi sync) | Lirik tetap muncul sebagai **teks polos** yang bisa digulir — tidak kosong, tidak error | ✅ 2026-07-23 |
| 109 | Putar lagu Indonesia yang dulu liriknya kosong gara-gara LRCLib | Sekarang ada liriknya. **Ini alasan utama urutannya dibalik** — kalau masih kosong, pembalikannya tidak menyelesaikan apa pun | ⏭️ 2026-07-23: terlewat saat pelaporan - tercakup oleh langkah 120 <br> ✅ 2026-07-23 (putaran 7) |
| 110 | Putar lagu yang benar-benar tidak punya lirik di mana pun | Panel bilang lirik tidak tersedia, aplikasi tidak menggantung | ⏭️ 2026-07-23: tidak diuji - penguji belum menemukan lagu yang cocok <br> ⚠️ 2026-07-23 (putaran 7): yang muncul placeholder "Putar lagu untuk melihat lirik", bukan pesan "lirik tidak tersedia" — pesannya salah untuk kasus ini |
| 111 | Matikan Wi-Fi, ganti lagu, buka panel lirik | Gagal dengan pesan, bukan crash atau loading selamanya | ✅ 2026-07-23: gagal dengan pesan "connectivity is unavailable" - tapi memunculkan 111b · **uji ulang: G #131** |

---

## E. Perbaikan putaran 3 (2026-07-23, commit `e4b3c73` dst.)

> Bagian D di atas **belum pernah dijalankan** — pengujiannya terhenti karena aplikasinya sendiri
> crash beberapa detik setelah dibuka. Itu sudah diperbaiki, jadi D dan E sekarang bisa dijalankan
> berurutan.

### E1. Aplikasi hidup (prasyarat — kalau ini gagal, sisanya percuma)

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 112 | Buka Kaset, diamkan **1 menit penuh** tanpa mengklik apa pun | Masih hidup. Dulu mati sendiri setelah ±20–40 detik | ✅ 2026-07-23: tidak pernah mati sendiri lagi |
| 113 | Kalau mati: buka `%LOCALAPPDATA%\Packages\Kaset.KasetWin_kjgd17zy2bc08\LocalState\crash.log` | Berisi exception lengkap + stack. File ini **baru ada** sejak putaran ini; sebelumnya crash tidak meninggalkan jejak apa pun | — 2026-07-23: tidak berlaku, tidak ada crash yang terjadi |
| 114 | Resize jendela ke ukuran aneh, tutup, buka lagi | Ukurannya kembali — dan tidak crash. Geometri tersimpan itu justru pemicu crash-nya dulu | ✅ 2026-07-23 |

### E2. Lirik tersinkron (fitur baru, belum pernah diuji manual)

Tiga videoId terverifikasi: `t82Q3f4pNUY` (Musixmatch, sync), `mZsHggY8G6M` (LyricFind, sync),
`BiQIc7fG9pA` (tanpa sync).

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 115 | `start "kaset://play?v=t82Q3f4pNUY"`, buka panel lirik | Baris lirik **menyala mengikuti lagu** | ✅ 2026-07-23 <br> ⚠️ 2026-07-23 (putaran 6): lirik tersinkron jalan, tapi **nama album hilang DAN sampul album hilang** di player bar. Lihat seksi H |
| 116 | Perhatikan posisi baris aktif saat lagu berjalan | Baris aktif naik ke **atas** panel dan diam di situ, baris berikutnya di bawahnya (gaya Apple Music) — **tidak terpotong** header | ✅ 2026-07-23 |
| 117 | Lihat label sumber di bawah panel | `Sumber: YouTube Music — Musixmatch` (atau LyricFind, tergantung lagu) | ✅ 2026-07-23 |
| 118 | Gulir lirik sampai baris terakhir | Baris terakhir masih bisa naik sampai atas, dan **tidak ada baris berisi "Source: …"** di dalam daftar liriknya | ✅ 2026-07-23 |
| 119 | `start "kaset://play?v=BiQIc7fG9pA"` (lagu tanpa versi sync) | Lirik tetap tampil sebagai teks polos yang bisa digulir; tidak kosong, tidak error | ✅ 2026-07-23 |
| 120 | Putar lagu Indonesia yang dulu liriknya kosong gara-gara LRCLib | Sekarang ada liriknya. **Ini alasan utama urutan penyedia dibalik** | ✅ 2026-07-23 |
| 121 | Pengaturan → Sumber lirik | Ada pilihan **YouTube Music**, default tetap **Otomatis**, dan ada keterangan LyricFind/Musixmatch + bahwa tidak semua lagu tersinkron | ⚠️ 2026-07-23: pilihannya ada dan defaultnya benar, tapi keterangannya terlalu panjang · **uji ulang: G #141** |
| 122 | Ganti ke LRCLib, putar lagu, lihat labelnya | `Sumber: LRCLib` — pilihannya benar-benar berpengaruh | ⏭️ 2026-07-23: tidak diuji |

### E3. Player bar & metadata

| # | Langkah | Harapan | Hasil |
|---|---------|---------|-------|
| 123 | Lihat player bar saat memutar lagu | Tombol **hati menempel di sebelah judul/artis**, tidak terdampar jauh ke kanan | ✅ 2026-07-23 |
| 124 | Kecilkan jendela sampai mentok (980 px) | Judul & artis tidak terpotong, tombol hati tetap terlihat | ✅ 2026-07-23 |
| 125 | Lebarkan jendela sampai maksimum | Grup cover+judul+hati tetap menyatu di tengah, tidak melar berjauhan | ✅ 2026-07-23 |
| 126 | Putar lagu dengan banyak artis (mis. *attached* — Tenxi, Anangga, Suisei) | Tertulis **tiga** artis. Tidak ada artis bernama "dan …" atau "and …" | ✅ 2026-07-23 |

---

## C. Yang masih belum tercakup uji apa pun

Dicatat jujur, bukan diklaim aman:

- **ViewModel dan lapisan Platform tidak punya test sama sekali.** 497 test seluruhnya `KasetWin.Core`.
  `SearchViewModel` (724 baris) dan `PlaylistDetailViewModel` (859 baris) hanya diuji dengan tangan.
- **Wiring timer tidur.** `SleepTimer` sendiri diuji 9 test; bahwa `PlayerService` benar-benar
  menjeda dan ticker benar-benar hidup/mati mengikuti state hanya bisa dibuktikan lewat langkah
  11–17 di atas — dan langkah 15 membuktikan wiring itu **belum** benar.
- **Klien IPC Discord.** Pemetaan aktivitasnya diuji 12 test, tapi frame named-pipe yang benar-benar
  diterima Discord hanya bisa dibuktikan lewat langkah 43–51.
- **Pintasan global & geometri jendela** sepenuhnya interop Win32 — nol cakupan test otomatis.
- **Mode YouTube penuh** (`YouTube*Page`, Shorts, watch) masih WIP dan tidak dicakup checklist ini.
- **Brand Account** dan **EU consent wall** (Tugas 30.5 / 30.6) butuh akun & region sungguhan.
