# Requirements Document

## Introduction

Dokumen ini mendefinisikan kebutuhan untuk **Kaset WinUI 3**, yaitu pembangunan ulang (port) aplikasi "Kaset" — saat ini klien YouTube Music native macOS (Swift/SwiftUI) — menjadi aplikasi **native Windows** menggunakan **WinUI 3 (C#/.NET)**. Tampilan harus terasa native Windows mengikuti **Fluent Design** (Mica/Acrylic), dengan referensi pengalaman pengguna mirip Groove Music / Apple Music. Aplikasi ini **tidak resmi (unofficial)** dan bergantung pada API internal YouTube Music yang dapat berubah sewaktu-waktu.

Arsitektur inti yang menentukan kelayakan teknis tetap dipertahankan dari versi macOS:
- Pemutaran konten ber-DRM (Widevine) melalui WebView tersembunyi (WebView2 di Windows).
- Seluruh pengambilan data melalui API internal InnerTube YouTube Music (pendekatan API-first), dengan autentikasi SAPISIDHASH.
- Login Google melalui WebView2 dan pembacaan sesi dari cookie.

Kebutuhan dikelompokkan menjadi tiga tingkat lingkup:
- **Inti Rilis-1**: fondasi dan fitur minimum yang membuat aplikasi musik dapat digunakan.
- **Fase Lanjutan**: fitur yang dikerjakan setelah inti stabil.
- **Ditunda**: fitur di luar lingkup rilis awal (dicatat sebagai future work).

### Pengelompokan Lingkup (Ringkasan)

- **Inti Rilis-1**: Autentikasi & SAPISIDHASH (Req 1–4), Pemutaran musik WebView2 & antrian (Req 5–9), Now Playing/SMTC (Req 10), Browsing dasar Home/Search/Library/Playlist/Album/Artist (Req 11–16), Lyrics (Req 17), Settings dasar & lokalisasi (Req 18–19), Penanganan error/logging/keamanan/performa & API Explorer (Req 20–24).
- **Fase Lanjutan**: Multi-akun lanjutan, Infinite Mix & radio (Req 25), Video & mini/floating player (Req 26), Podcasts (Req 27), ~~Scrobbling Last.fm (Req 28 — dibatalkan)~~, Favorites/pinned & History (Req 29–30), Explore detail Moods/Charts/New Releases (Req 31), Mode YouTube penuh (Req 32), Protocol activation & share & notifikasi (Req 33–35).
- **Ditunda**: Haptic feedback, AppleScript, fitur AI (Req 36). _(Equalizer, Web Extensions dan Auto-update semula di sini tetapi sudah diimplementasikan — lihat catatan pada Req 36.)_

## Glossary

- **Kaset**: Aplikasi klien YouTube Music native Windows berbasis WinUI 3 yang dibangun dalam spec ini.
- **WebView2**: Komponen Microsoft Edge WebView2 yang menyematkan mesin Chromium ke aplikasi Windows; digunakan untuk pemutaran ber-DRM dan login.
- **Playback_WebView**: Instance WebView2 tersembunyi tunggal (singleton) yang memuat `music.youtube.com/watch?v={id}` untuk memutar audio.
- **JS_Bridge**: Jembatan komunikasi dua arah antara JavaScript di Playback_WebView dan kode native melalui mekanisme pesan WebView2 (`postMessage` / `WebMessageReceived`).
- **YTMusic_Client**: Komponen native yang melakukan permintaan terautentikasi ke API internal InnerTube YouTube Music.
- **InnerTube**: API internal (tidak resmi) YouTube Music pada endpoint `youtubei/v1/*`.
- **SAPISIDHASH**: Skema autentikasi berbasis header berbentuk `SAPISIDHASH {timestamp}_{hash}`, di mana `hash = SHA1("{timestamp} {SAPISID} {origin}")`.
- **SAPISID**: Nilai cookie Google (`__Secure-3PAPISID` / `SAPISID`) yang dibaca dari Playback_WebView setelah login.
- **WEB_REMIX**: Nilai `clientName` konteks InnerTube yang digunakan untuk origin musik (`https://music.youtube.com`).
- **Origin_Musik**: Nilai origin `https://music.youtube.com` yang wajib digunakan untuk perhitungan SAPISIDHASH pada permintaan musik.
- **Auth_Service**: Komponen native yang mengelola state autentikasi (loggedOut, loggingIn, loggedIn) dan re-auth.
- **Queue_Service**: Komponen native yang menjadi sumber kebenaran (source of truth) untuk antrian pemutaran.
- **Player_Service**: Komponen native yang mengelola state dan kontrol pemutaran (play/pause, next/prev, seek, volume, shuffle, repeat).
- **SMTC**: SystemMediaTransportControls Windows untuk integrasi Now Playing dan tombol media.
- **Credential_Store**: Windows Credential Locker atau DPAPI untuk menyimpan kredensial sensitif secara aman.
- **LRCLib**: Penyedia lirik eksternal yang menyediakan lirik plain dan synced dalam format LRC.
- **Parser**: Modul native murni (pure function) yang mengubah respons JSON InnerTube menjadi model data aplikasi.
- **KasetError**: Tipe error terpadu aplikasi (padanan `YTMusicError` di macOS).
- **API_Explorer**: Tooling CLI versi Windows untuk eksplorasi endpoint InnerTube.
- **Brand_Account**: Akun merek (brand) YouTube dengan identitas 21-digit yang diakses melalui index `X-Goog-AuthUser` dan `context.user.onBehalfOfUser`.
- **Protocol_Activation**: Mekanisme Windows untuk menangani URI scheme `kaset://` melalui registrasi protokol aplikasi.

---

## Requirements

### Requirement 1: Pemutaran Konten Ber-DRM melalui WebView2 (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengguna, saya ingin memutar lagu YouTube Music yang dilindungi DRM, sehingga saya dapat mendengarkan musik Premium di aplikasi native Windows.

#### Acceptance Criteria

1. THE Kaset SHALL memelihara tepat satu instance Playback_WebView selama siklus hidup aplikasi.
2. WHEN pengguna memulai pemutaran sebuah lagu, THE Player_Service SHALL memuat URL `https://music.youtube.com/watch?v={videoId}` ke dalam Playback_WebView.
3. WHEN konten yang diminta dilindungi Widevine, THE Playback_WebView SHALL memutar audio menggunakan dukungan DRM Widevine WebView2.
4. WHILE jendela utama aplikasi tertutup tetapi aplikasi belum keluar, THE Playback_WebView SHALL melanjutkan pemutaran audio di latar belakang.
5. WHEN pengguna keluar (quit) dari aplikasi, THE Kaset SHALL menghentikan pemutaran audio dan melepaskan Playback_WebView.
6. WHEN pengguna memutar videoId yang berbeda dari yang sedang dimuat, THE Player_Service SHALL menjeda audio saat ini sebelum memuat URL baru.
7. IF dukungan DRM Widevine tidak tersedia pada runtime WebView2, THEN THE Kaset SHALL menampilkan pesan kesalahan yang menjelaskan bahwa pemutaran tidak dapat dilakukan.

### Requirement 2: Jembatan JavaScript-Native untuk State Pemutaran (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengguna, saya ingin kontrol dan tampilan pemutaran di UI native selalu sinkron dengan pemutar web, sehingga status pemutaran yang saya lihat akurat.

#### Acceptance Criteria

1. WHILE sebuah lagu sedang dimuat di Playback_WebView, THE JS_Bridge SHALL mengirim pembaruan state berisi `isPlaying`, `progress`, `duration`, `videoId`, dan `title` ke Player_Service.
2. WHEN JS_Bridge mendeteksi pergantian track, THE JS_Bridge SHALL mengirim sinyal `trackChanged` ke Player_Service.
3. WHEN sebuah track selesai secara alami, THE JS_Bridge SHALL mengirim event `TRACK_ENDED` beserta `videoId` track yang berakhir.
4. WHEN Player_Service menerima event `TRACK_ENDED`, THE Player_Service SHALL memvalidasi bahwa `videoId` yang berakhir cocok dengan lagu yang diharapkan pada antrian sebelum melanjutkan ke track berikutnya.
5. WHERE Queue_Service memiliki antrian aktif, THE Queue_Service SHALL menjadi sumber kebenaran untuk track berikutnya alih-alih mengikuti autoplay YouTube.
6. WHEN Playback_WebView melaporkan `videoId` baru sementara metadata DOM masih kosong atau usang, THE Player_Service SHALL memperlakukan `videoId` yang dilaporkan sebagai otoritatif.

### Requirement 3: Autentikasi InnerTube dengan SAPISIDHASH (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengguna, saya ingin aplikasi mengakses data akun YouTube Music saya, sehingga saya dapat melihat pustaka dan rekomendasi pribadi.

#### Acceptance Criteria

1. WHEN YTMusic_Client menyusun permintaan terautentikasi, THE YTMusic_Client SHALL menghitung header otorisasi berbentuk `SAPISIDHASH {timestamp}_{hash}` dengan `hash = SHA1("{timestamp} {SAPISID} {origin}")`.
2. WHEN YTMusic_Client melakukan permintaan data musik, THE YTMusic_Client SHALL menggunakan Origin_Musik `https://music.youtube.com` dan `clientName` WEB_REMIX pada konteks permintaan.
3. WHEN YTMusic_Client membutuhkan nilai SAPISID, THE YTMusic_Client SHALL membaca cookie `__Secure-3PAPISID` atau `SAPISID` dari Playback_WebView.
4. THE YTMusic_Client SHALL menyertakan header Cookie, Authorization, dan Origin yang konsisten dengan Origin_Musik pada setiap permintaan musik.
5. IF origin yang digunakan tidak sesuai dengan Origin_Musik, THEN THE YTMusic_Client SHALL memperlakukan respons sebagai kegagalan autentikasi tanpa mengubah origin secara diam-diam.
6. WHEN YTMusic_Client menerima respons HTTP 401 atau 403, THEN THE YTMusic_Client SHALL melempar KasetError tipe authExpired.

### Requirement 4: Login Google dan State Sesi (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengguna, saya ingin masuk dengan akun Google saya, sehingga aplikasi dapat memutar dan menampilkan konten akun saya.

#### Acceptance Criteria

1. WHILE tidak ada sesi valid, THE Auth_Service SHALL berada pada state loggedOut.
2. WHEN pengguna memulai login, THE Auth_Service SHALL berpindah ke state loggingIn dan menampilkan alur login Google di WebView2.
3. WHEN cookie `__Secure-3PAPISID` valid terdeteksi setelah login, THE Auth_Service SHALL berpindah ke state loggedIn.
4. WHEN cookie sesi berubah pada WebView2, THE Auth_Service SHALL mengevaluasi ulang status login.
5. WHEN YTMusic_Client melaporkan KasetError authExpired, THE Auth_Service SHALL berpindah ke state loggedOut dan memicu alur re-auth.
6. WHEN aplikasi diluncurkan, THE Auth_Service SHALL memeriksa cookie tersimpan untuk menentukan apakah sesi valid masih ada.

### Requirement 5: Kontrol Pemutaran Dasar (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin mengendalikan pemutaran, sehingga saya dapat mengatur pengalaman mendengarkan saya.

#### Acceptance Criteria

1. WHEN pengguna menekan play atau pause, THE Player_Service SHALL mengubah state pemutaran sesuai aksi tersebut.
2. WHEN pengguna menekan next, THE Player_Service SHALL memutar track berikutnya pada Queue_Service.
3. WHEN pengguna menekan previous, THE Player_Service SHALL memutar track sebelumnya pada Queue_Service.
4. WHEN pengguna melakukan seek ke posisi tertentu, THE Player_Service SHALL mengatur posisi pemutaran ke posisi yang diminta.
5. WHEN pengguna mengubah volume, THE Player_Service SHALL menerapkan tingkat volume yang diminta pada rentang 0 sampai 100.
6. WHEN pengguna mengaktifkan mute, THE Player_Service SHALL membisukan audio sambil mempertahankan tingkat volume sebelumnya untuk pemulihan.
7. WHEN pengguna mengaktifkan shuffle, THE Player_Service SHALL mengacak urutan pemutaran antrian.
8. WHEN pengguna menyetel mode repeat, THE Player_Service SHALL menerapkan salah satu mode Off, All, atau One.

### Requirement 6: Manajemen Antrian (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin melihat dan mengatur antrian pemutaran, sehingga saya dapat menyusun lagu yang akan diputar.

#### Acceptance Criteria

1. WHEN pengguna membuka panel antrian, THE Queue_Service SHALL menampilkan daftar track yang sedang antri beserta track yang sedang diputar.
2. WHEN pengguna menyeret sebuah track ke posisi baru, THE Queue_Service SHALL menyusun ulang antrian sesuai posisi target.
3. WHEN pengguna menghapus (clear) antrian, THE Queue_Service SHALL mengosongkan antrian dan menghentikan pemutaran berikutnya.
4. WHEN pengguna mengacak antrian, THE Queue_Service SHALL mengacak urutan track tanpa mengganggu track yang sedang diputar.
5. WHEN pengguna memutar sebuah album atau playlist, THE Queue_Service SHALL mengisi antrian dengan track dari sumber tersebut.

### Requirement 7: Preferensi Kualitas Audio (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin memilih kualitas audio, sehingga saya dapat menyeimbangkan kualitas suara dan penggunaan data.

#### Acceptance Criteria

1. WHERE pengguna memilih preferensi kualitas audio, THE Player_Service SHALL meneruskan preferensi tersebut ke Playback_WebView sebagai permintaan kualitas.
2. WHEN preferensi kualitas audio berubah saat lagu sedang diputar, THE Player_Service SHALL menerapkan ulang preferensi pada pemutar yang sedang berjalan.
3. THE Kaset SHALL menyediakan pilihan kualitas audio low, medium, dan high.

### Requirement 8: Pemutaran Album, Playlist, dan Artist (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin memutar album, playlist, dan lagu populer artis, sehingga saya dapat mendengarkan kumpulan lagu dengan satu aksi.

#### Acceptance Criteria

1. WHEN pengguna memutar sebuah album, THE Player_Service SHALL memuat seluruh track album ke Queue_Service dan memutar track pertama.
2. WHEN pengguna memutar sebuah playlist, THE Player_Service SHALL memuat track playlist ke Queue_Service dan memutar track pertama.
3. WHEN pengguna memutar top songs sebuah artis, THE Player_Service SHALL memuat top songs artis tersebut ke Queue_Service.
4. WHERE sebuah playlist berukuran besar dengan paginasi, THE YTMusic_Client SHALL memuat track tambahan melalui token continuation.

### Requirement 9: Penanganan Live Stream (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin mengetahui ketika konten adalah siaran langsung, sehingga saya memahami mengapa kontrol tertentu tidak tersedia.

#### Acceptance Criteria

1. WHEN konten yang diputar adalah live stream, THE Kaset SHALL menampilkan indikator LIVE.
2. WHILE sebuah live stream sedang diputar, THE Player_Service SHALL menonaktifkan kontrol seek.
3. WHILE sebuah live stream sedang diputar, THE Queue_Service SHALL menonaktifkan operasi antrian untuk konten tersebut.

### Requirement 10: Integrasi Now Playing dan Tombol Media (SMTC) (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin mengontrol pemutaran melalui kontrol media sistem Windows, sehingga saya dapat mengelola musik tanpa membuka aplikasi.

#### Acceptance Criteria

1. WHILE sebuah track sedang diputar, THE Kaset SHALL memperbarui SMTC dengan judul, artis, dan artwork track.
2. WHEN pengguna menekan tombol media play, pause, next, atau previous, THE SMTC SHALL meneruskan perintah tersebut ke Player_Service.
3. WHEN state pemutaran berubah, THE Kaset SHALL memperbarui status pemutaran pada SMTC.

### Requirement 11: Halaman Home (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin melihat beranda yang dipersonalisasi, sehingga saya dapat menemukan musik dengan cepat.

#### Acceptance Criteria

1. WHEN pengguna membuka Home, THE YTMusic_Client SHALL mengambil bagian-bagian beranda (`FEmusic_home`) melalui InnerTube.
2. WHEN respons Home berisi token continuation, THE YTMusic_Client SHALL mendukung pemuatan bagian tambahan melalui paginasi.
3. WHEN pengguna memilih sebuah item pada Home, THE Kaset SHALL menavigasi ke halaman detail yang sesuai (lagu, album, playlist, atau artis).

### Requirement 12: Pencarian (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin mencari konten, sehingga saya dapat menemukan lagu, album, artis, playlist, dan podcast tertentu.

#### Acceptance Criteria

1. WHEN pengguna mengirim kueri pencarian, THE YTMusic_Client SHALL mengembalikan hasil yang dikelompokkan menjadi lagu, album, artis, playlist, dan podcast.
2. WHILE pengguna mengetik kueri, THE Kaset SHALL menunda (debounce) eksekusi pencarian untuk mengurangi permintaan berlebih.
3. WHEN pengguna mengetik kueri parsial, THE Kaset SHALL menampilkan saran pencarian.
4. WHEN pengguna memilih sebuah hasil pencarian, THE Kaset SHALL menavigasi ke halaman detail yang sesuai.

### Requirement 13: Pustaka (Library) (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin mengelola pustaka pribadi, sehingga saya dapat mengakses dan mengatur koleksi musik saya.

#### Acceptance Criteria

1. WHEN pengguna membuka Library, THE YTMusic_Client SHALL mengambil playlist pengguna, liked songs, artis yang di-follow, dan lagu yang diunggah.
2. WHEN pengguna membuat playlist baru, THE YTMusic_Client SHALL membuat playlist melalui InnerTube dengan judul yang diberikan.
3. WHEN pengguna menambahkan lagu ke sebuah playlist, THE YTMusic_Client SHALL menambahkan lagu tersebut melalui endpoint `browse/edit_playlist`.
4. WHEN pengguna menghapus playlist miliknya, THE YTMusic_Client SHALL menghapus playlist tersebut melalui InnerTube.
5. WHEN pengguna menerapkan filter pada Library, THE Kaset SHALL menampilkan hanya item yang sesuai dengan filter.
6. WHEN sebuah mutasi pustaka berhasil, THE Kaset SHALL menerapkan pembaruan optimistik pada UI dan menjadwalkan rekonsiliasi dengan snapshot backend.
7. IF sebuah mutasi pustaka gagal, THEN THE Kaset SHALL mengembalikan state UI ke kondisi sebelum mutasi.

### Requirement 14: Halaman Playlist dan Album (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin melihat detail playlist dan album, sehingga saya dapat menelaah dan memutar isinya.

#### Acceptance Criteria

1. WHEN pengguna membuka sebuah playlist, THE YTMusic_Client SHALL mengambil metadata playlist dan daftar track-nya.
2. WHEN pengguna membuka sebuah album, THE YTMusic_Client SHALL mengambil metadata album dan daftar track-nya.
3. WHERE sebuah playlist dimiliki oleh pengguna, THE Kaset SHALL menampilkan afordans untuk menghapus playlist tersebut.
4. WHEN pengguna memutar dari halaman playlist atau album, THE Player_Service SHALL memuat track ke Queue_Service.

### Requirement 15: Halaman Artist (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin melihat halaman artis, sehingga saya dapat menelusuri karya artis tersebut.

#### Acceptance Criteria

1. WHEN pengguna membuka halaman artis, THE YTMusic_Client SHALL mengambil top songs, albums, serta singles & EPs artis tersebut.
2. WHEN pengguna memilih "See all" pada sebuah bagian artis, THE Kaset SHALL menampilkan daftar lengkap untuk bagian tersebut.
3. WHEN pengguna menekan follow atau unfollow pada artis, THE YTMusic_Client SHALL memperbarui status subscription artis melalui InnerTube.
4. WHEN pengguna memutar dari halaman artis, THE Player_Service SHALL memuat lagu artis ke Queue_Service.

### Requirement 16: Identitas Stabil dan Pemuatan Daftar (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin daftar panjang tampil mulus, sehingga aplikasi tetap responsif saat menelusuri banyak konten.

#### Acceptance Criteria

1. WHEN Kaset menampilkan daftar konten, THE Kaset SHALL menggunakan identitas item yang stabil untuk menghindari render ulang yang tidak perlu.
2. WHEN sebuah daftar panjang ditampilkan, THE Kaset SHALL memuat dan merender item secara lazy.
3. WHEN pemuatan data yang sama dipicu beberapa kali secara bersamaan, THE Kaset SHALL menggabungkannya menjadi satu permintaan tunggal (single-flight).

### Requirement 17: Lirik Plain dan Synced (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin melihat lirik lagu, sehingga saya dapat mengikuti lagu yang sedang diputar.

#### Acceptance Criteria

1. WHEN pengguna membuka panel lirik untuk sebuah track, THE Kaset SHALL mengambil lirik dari LRCLib menggunakan informasi judul dan artis track.
2. WHERE lirik synced tersedia, THE Kaset SHALL menampilkan lirik synced dengan menyorot baris sesuai posisi pemutaran.
3. IF lirik synced tidak tersedia, THEN THE Kaset SHALL menampilkan lirik plain sebagai fallback.
4. WHEN lirik untuk sebuah videoId telah diambil, THE Kaset SHALL menyimpannya dalam cache berdasarkan videoId.
5. WHEN Kaset mem-parsing payload LRC, THE Parser SHALL menghasilkan struktur lirik synced; dan untuk seluruh payload LRC yang valid, parsing kemudian pencetakan kembali kemudian parsing ulang SHALL menghasilkan struktur lirik synced yang setara (properti round-trip).

### Requirement 18: Pengaturan Dasar (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin menyesuaikan preferensi aplikasi, sehingga aplikasi berperilaku sesuai keinginan saya.

#### Acceptance Criteria

1. WHEN pengguna mengubah halaman peluncuran default, THE Kaset SHALL membuka halaman tersebut saat aplikasi berikutnya diluncurkan.
2. WHERE preferensi "ingat pengaturan pemutaran" diaktifkan, THE Kaset SHALL memulihkan state shuffle dan repeat pada peluncuran berikutnya.
3. WHERE preferensi lirik synced diaktifkan, THE Kaset SHALL mencari lirik synced sebelum melakukan fallback ke lirik plain.
4. WHEN pengguna mengubah sebuah pengaturan, THE Kaset SHALL menyimpan preferensi tersebut secara persisten.

### Requirement 19: Lokalisasi dan RTL (Inti Rilis-1)

**User Story:** Sebagai pengguna, saya ingin menggunakan aplikasi dalam bahasa saya, sehingga aplikasi mudah dipahami.

#### Acceptance Criteria

1. THE Kaset SHALL menyediakan antarmuka dalam bahasa **English dan Indonesian**. _(Direvisi 2026-07-22 — lihat catatan di bawah.)_
2. WHERE bahasa aktif ditulis dari kanan ke kiri, THE Kaset SHALL menampilkan tata letak dengan arah kanan-ke-kiri (RTL).
3. WHEN bahasa sistem termasuk salah satu bahasa yang didukung, THE Kaset SHALL memilih bahasa tersebut sebagai default; WHERE bahasa sistem tidak didukung, THE Kaset SHALL memakai fallback English **beserta arah tata letaknya** (LTR).

> **Catatan revisi (2026-07-22).** Kriteria 1 semula menyebut enam bahasa (English, French, Korean,
> Indonesian, Turkish, Arabic) atas dasar enam folder `.resw`. Folder itu tidak pernah tersambung —
> jumlah `x:Uid` di seluruh aplikasi adalah **0**, dan semua teks yang terlihat berasal dari
> `KasetWin.App/Localization/UiStrings.cs` yang hanya English/Indonesian. Klaim enam bahasa
> menimbulkan cacat yang terlihat pengguna: di sistem berbahasa Arab, pemilihan bahasa memilih `ar`,
> jendela dibalik ke RTL, lalu diisi teks bahasa Indonesia. `SupportedLanguages.All` dipersempit ke
> `["en", "id"]` dan stub `.resw` yang mati dihapus, sehingga janji spec cocok dengan string yang
> benar-benar ada. Kriteria 2 dan mesin RTL-nya dipertahankan dan tetap diuji (Property 42 +
> `Unsupported_rtl_locale_falls_back_to_english_and_stays_ltr`), siap dipakai begitu ada terjemahan
> RTL. Urutan menambah bahasa: terjemahkan `UiStrings` dulu, baru tambah subtag — lihat
> `KasetWin/src/KasetWin.App/Strings/README.md`.

### Requirement 20: Penanganan Error Terpadu (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengguna, saya ingin aplikasi menangani kegagalan dengan baik, sehingga saya memahami apa yang terjadi dan dapat mengambil tindakan.

#### Acceptance Criteria

1. THE Kaset SHALL merepresentasikan kegagalan menggunakan KasetError dengan kategori authExpired, notAuthenticated, networkError, parseError, apiError, playbackError, dan unknown.
2. WHEN sebuah permintaan jaringan gagal karena masalah konektivitas, THE YTMusic_Client SHALL melempar KasetError tipe networkError.
3. WHEN sebuah respons gagal di-parse, THE Parser SHALL melempar KasetError tipe parseError.
4. WHEN sebuah operasi yang dapat dicoba ulang gagal, THE Kaset SHALL mencoba ulang dengan backoff eksponensial hingga batas percobaan yang ditentukan.

### Requirement 21: Logging Terstruktur (Inti Rilis-1)

**User Story:** Sebagai pengembang, saya ingin log terstruktur, sehingga saya dapat mendiagnosis masalah.

#### Acceptance Criteria

1. WHEN sebuah peristiwa penting terjadi, THE Kaset SHALL mencatatnya melalui logger terstruktur dengan kategori dan level.
2. THE Kaset SHALL mendukung level log debug, info, warning, dan error.
3. WHEN Kaset mencatat informasi, THE Kaset SHALL mengecualikan cookie, token, dan nilai SAPISID dari keluaran log.

### Requirement 22: Keamanan Kredensial dan Rahasia (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengguna, saya ingin kredensial saya aman, sehingga akun saya tidak terekspos.

#### Acceptance Criteria

1. WHEN Kaset menyimpan kredensial sensitif, THE Kaset SHALL menyimpannya di Credential_Store (Windows Credential Locker atau DPAPI).
2. THE Kaset SHALL mengecualikan cookie, token, kunci API, dan nilai SAPISID dari kode, komentar, dokumentasi, dan fixture pengujian.
3. WHEN Kaset menampilkan diagnostik, THE Kaset SHALL menyamarkan (redact) nilai kredensial.

### Requirement 23: Arsitektur API-First dan Parser Modular (Inti Rilis-1, Foundational)

**User Story:** Sebagai pengembang, saya ingin pengambilan data melalui API dan parser yang teruji, sehingga aplikasi andal dan mudah dipelihara.

#### Acceptance Criteria

1. WHERE sebuah fungsi tersedia melalui InnerTube, THE Kaset SHALL menggunakan YTMusic_Client alih-alih Playback_WebView, kecuali untuk pemutaran dan autentikasi.
2. THE Kaset SHALL mengimplementasikan parsing respons sebagai modul Parser murni (pure function) yang dapat diuji secara independen.
3. WHEN sebuah respons InnerTube di-parse, THE Parser SHALL menghasilkan model data yang setara untuk masukan yang setara secara deterministik (idempoten).

### Requirement 24: Tooling API Explorer (Inti Rilis-1)

**User Story:** Sebagai pengembang, saya ingin tooling untuk mengeksplorasi endpoint InnerTube di Windows, sehingga saya dapat memverifikasi struktur respons sebelum implementasi.

#### Acceptance Criteria

1. THE Kaset SHALL menyediakan API_Explorer berbasis CLI untuk Windows.
2. WHEN pengembang menjalankan API_Explorer dengan perintah auth, THE API_Explorer SHALL melaporkan status autentikasi saat ini.
3. WHEN pengembang menjalankan API_Explorer terhadap sebuah endpoint browse, THE API_Explorer SHALL menampilkan respons InnerTube untuk endpoint tersebut.

### Requirement 25: Infinite Mix dan Radio (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin mendengarkan mix dan radio tanpa henti, sehingga musik terus diputar berdasarkan selera saya.

#### Acceptance Criteria

1. WHEN pengguna memulai sebuah artist mix (`RDEM...`), THE Player_Service SHALL memuat kumpulan lagu awal melalui endpoint `next`.
2. WHILE sebuah mix sedang diputar dan tersisa sepuluh lagu atau kurang dalam antrian, THE Player_Service SHALL memuat lagu tambahan melalui token continuation.
3. WHEN lagu tambahan dimuat untuk sebuah mix, THE Queue_Service SHALL memfilter lagu duplikat sebelum menambahkannya ke antrian.
4. WHEN pengguna memulai pemutaran antrian reguler, song radio, atau menghapus antrian, THE Player_Service SHALL menghapus token continuation mix.

### Requirement 26: Video dan Mini/Floating Player (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin menonton video musik dalam jendela mengambang, sehingga saya dapat melihat video sambil menggunakan aplikasi lain.

#### Acceptance Criteria

1. WHEN sebuah track memiliki video yang tersedia, THE Kaset SHALL menandai ketersediaan video (OMV versus ATV/UGC).
2. WHEN pengguna mengaktifkan floating video window, THE Kaset SHALL menampilkan video dalam jendela picture-in-picture yang terpisah. _(Untuk **audio**, padanannya adalah mini player CompactOverlay — lihat Req 39, yang justru memakai ulang jendela utama karena WebView2 pemutaran tidak boleh di-re-parent.)_
3. WHERE preferensi pop-out video diaktifkan dan pengguna menavigasi keluar dari halaman video, THE Kaset SHALL memindahkan video yang sedang diputar ke jendela mengambang.
4. WHERE preferensi pop-out video dinonaktifkan dan pengguna menavigasi keluar dari halaman video, THE Kaset SHALL menghentikan pemutaran video.

### Requirement 27: Podcasts (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin mendengarkan podcast, sehingga saya dapat mengikuti episode favorit.

#### Acceptance Criteria

1. WHEN endpoint `FEmusic_podcasts` tersedia untuk region pengguna, THE Kaset SHALL menampilkan tab Podcasts.
2. IF endpoint `FEmusic_podcasts` mengembalikan 404 untuk region pengguna, THEN THE Kaset SHALL menyembunyikan tab Podcasts.
3. WHEN pengguna mendengarkan sebuah episode, THE Kaset SHALL menyimpan progress episode dan status sudah diputar.
4. WHEN pengguna subscribe atau unsubscribe sebuah podcast, THE YTMusic_Client SHALL memperbarui status langganan melalui InnerTube.

### Requirement 28 (DIBATALKAN): Scrobbling Last.fm (Fase Lanjutan)

> **Dibatalkan 2026-07-22** atas keputusan pemilik repo. Scrobbling Last.fm dikeluarkan dari
> lingkup KasetWin; tidak ada acceptance criteria yang berlaku. Task 21 di `tasks.md` ditandai
> dibatalkan, dan Property 38 & 39 tidak akan ditulis.

### Requirement 29: Favorites dan Item Tersemat (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin menyematkan item favorit, sehingga saya dapat mengaksesnya dengan cepat dari Home dan sidebar.

#### Acceptance Criteria

1. WHEN pengguna menyematkan sebuah lagu, album, playlist, atau artis, THE Kaset SHALL menambahkannya ke daftar Favorites tanpa duplikasi.
2. WHEN pengguna menyusun ulang Favorites melalui drag, THE Kaset SHALL menyimpan urutan baru secara persisten.
3. WHERE terdapat item Favorites, THE Kaset SHALL menampilkan bagian Favorites pada Home.
4. WHEN pengguna melepas sematan sebuah item, THE Kaset SHALL menghapusnya dari Favorites.

### Requirement 30: Riwayat (History) (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin melihat riwayat mendengarkan, sehingga saya dapat memutar ulang lagu yang baru saja saya dengar.

#### Acceptance Criteria

1. WHEN pengguna membuka History, THE YTMusic_Client SHALL mengambil riwayat mendengarkan pengguna melalui InnerTube.
2. WHEN pengguna memilih sebuah item dari History, THE Player_Service SHALL memutar item tersebut.

### Requirement 31: Explore Detail (Moods, Charts, New Releases) (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin menjelajahi rilisan baru, chart, serta mood & genre, sehingga saya dapat menemukan musik baru.

#### Acceptance Criteria

1. WHEN pengguna membuka Explore, THE YTMusic_Client SHALL mengambil bagian New Releases, Charts, serta Moods & Genres.
2. WHEN pengguna memilih sebuah kategori mood atau genre, THE YTMusic_Client SHALL mengambil konten untuk kategori tersebut.
3. WHEN pengguna memilih sebuah item pada Explore, THE Kaset SHALL menavigasi ke halaman detail yang sesuai.

### Requirement 32: Mode YouTube Penuh (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin beralih ke mode YouTube, sehingga saya dapat menonton video YouTube penuh dalam aplikasi yang sama.

#### Acceptance Criteria

1. WHEN pengguna mengaktifkan toggle sumber YouTube, THE Kaset SHALL menampilkan halaman Home, Explore, Subscriptions, dan History khusus YouTube.
2. WHEN pengguna membuka sebuah watch page YouTube, THE Kaset SHALL menampilkan video, metadata, dan komentar.
3. WHEN beberapa sumber audio berpotensi aktif, THE Kaset SHALL memastikan hanya satu sumber audio yang aktif pada satu waktu.
4. WHEN pengguna membuka Shorts, THE Kaset SHALL menampilkan Shorts dengan paging vertikal snap.
5. WHEN pengguna menekan like, dislike, subscribe, atau Watch Later pada konten YouTube, THE Kaset SHALL memperbarui status terkait melalui API YouTube.

### Requirement 33: Protocol Activation kaset:// (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin membuka konten melalui tautan `kaset://`, sehingga integrasi dengan sistem dan tautan eksternal berjalan mulus.

#### Acceptance Criteria

1. WHEN Kaset menerima URI `kaset://play?v={videoId}`, THE Kaset SHALL memutar lagu yang sesuai.
2. WHEN Kaset menerima URI `kaset://playlist?list={id}`, THE Kaset SHALL membuka playlist yang sesuai.
3. WHEN Kaset menerima URI `kaset://album?id={id}`, THE Kaset SHALL membuka album yang sesuai.
4. WHEN Kaset menerima URI `kaset://artist?id={id}`, THE Kaset SHALL membuka halaman artis yang sesuai.
5. IF URI yang diterima tidak valid atau tidak dikenal, THEN THE Kaset SHALL mengabaikan URI tersebut tanpa mengubah state pemutaran.

### Requirement 34: Berbagi Konten (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin membagikan lagu, playlist, album, dan artis, sehingga saya dapat merekomendasikan musik ke orang lain.

#### Acceptance Criteria

1. WHEN pengguna membagikan sebuah lagu, playlist, album, atau artis, THE Kaset SHALL membuka dialog berbagi native Windows dengan judul dan URL konten.
2. WHERE sebuah konten tidak memiliki URL yang dapat dibagikan, THE Kaset SHALL menonaktifkan aksi berbagi untuk konten tersebut.

### Requirement 35: Notifikasi Ganti Track dan Pemantauan Jaringan (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin mendapat notifikasi pergantian track dan aplikasi sadar status jaringan, sehingga saya tetap terinformasi.

#### Acceptance Criteria

1. WHERE notifikasi ganti track diaktifkan dan track berubah, THE Kaset SHALL menampilkan notifikasi berisi judul dan artis track baru.
2. WHEN konektivitas jaringan berubah, THE Kaset SHALL memperbarui status konektivitas internalnya.
3. WHILE perangkat offline, THE Kaset SHALL menampilkan indikasi bahwa konektivitas tidak tersedia.

### Requirement 36: Fitur yang Ditunda (Out of Scope Rilis Awal)

**User Story:** Sebagai pemangku kepentingan, saya ingin fitur tertentu dicatat sebagai ditunda, sehingga ekspektasi lingkup rilis awal jelas.

#### Acceptance Criteria

1. ~~THE Kaset SHALL mengecualikan equalizer dari rilis awal~~ — **DIIMPLEMENTASIKAN** (ekualiser 9-band + preset via Web Audio pada WebView2 pemutaran).
2. THE Kaset SHALL mengecualikan haptic feedback karena tidak ada padanan native Windows.
3. ~~THE Kaset SHALL mengecualikan dukungan ekstensi web~~ — **DIIMPLEMENTASIKAN** (`ExtensionsService` memuat ekstensi unpacked; uBlock Origin auto-unduh & auto-perbarui).
4. THE Kaset SHALL mengecualikan integrasi AppleScript dan menggantinya dengan Protocol_Activation atau argumen CLI pada fase lanjutan.
5. ~~THE Kaset SHALL mengecualikan mekanisme auto-update~~ — **DIIMPLEMENTASIKAN** (Velopack + GithubSource; aktif hanya pada build terinstal).
6. THE Kaset SHALL mengecualikan seluruh fitur berbasis AI/Apple Intelligence (Command Bar AI, penjelasan lirik AI, analisis antrian AI, dan refine playlist AI) dari lingkup.

### Requirement 37: Sinkronisasi Fitur Upstream v0.12.0 (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin port Windows mengejar fitur baru yang ditambahkan pada Kaset macOS (rilis v0.12.0), sehingga pengalaman kedua platform tetap setara. Rujukan lengkap ada di `upstream-sync.md`.

#### Acceptance Criteria

1. WHEN pengguna melihat header album atau playlist, THE Kaset SHALL menampilkan nama artis sebagai afordans yang dapat diklik, dan WHEN diklik, THE Kaset SHALL menavigasi ke halaman artis terkait. _(upstream #341)_
2. WHERE sebuah video sedang diputar (mode YouTube), THE Player_Service SHALL menyediakan kontrol lompat mundur dan maju 30 detik, dengan posisi hasil di-clamp ke rentang `[0, Duration]`. _(upstream #326)_
3. WHILE sebuah lagu sedang diputar, THE Kaset SHALL menyediakan aksi Like/Unlike dari permukaan sistem (SMTC thumbbutton atau Jump List taskbar) yang memperbarui status like lagu tersebut. _(upstream #334)_
4. THE Kaset SHALL menyelaraskan tata letak dan kontrol Player_Bar dengan desain upstream terbaru (scrubber, marquee judul, kontrol volume, seek-hold). _(upstream #314, #327, #331)_
5. WHEN pengguna masuk dengan Brand Account, THE Kaset SHALL mencatat riwayat pemutaran (musik dan video) menggunakan sesi Brand account yang benar. _(upstream #318; lihat ADR terkait di repo asli)_
6. WHERE resolusi API key terhalang consent wall Uni Eropa, THE YTMusic_Client SHALL menyelesaikan alur consent tersebut sehingga API key tetap dapat diperoleh. _(upstream #345)_
7. WHILE aplikasi berada di latar belakang, WHEN pengguna menekan media-key "next", THE Player_Service SHALL memajukan ke track berikutnya (bukan mengulang track yang sama). _(upstream #319)_
8. WHEN main window ditampilkan, THE Kaset SHALL menegakkan kontrak ukuran window (batas minimum/maksimum dan persistensi ukuran) yang konsisten. _(upstream #322)_
9. WHEN pengguna bernavigasi antar-halaman, THE Kaset SHALL mempertahankan warna ikon sidebar yang ter-branding. _(upstream #336)_

### Requirement 38: Aksesibilitas Antarmuka (Inti)

**User Story:** Sebagai pengguna pembaca layar (Narrator/NVDA), saya ingin setiap kontrol menyebutkan namanya, sehingga saya dapat memakai Kaset tanpa melihat layar.

#### Acceptance Criteria

1. THE Kaset SHALL memberi `AutomationProperties.Name` pada setiap kontrol yang kontennya hanya ikon (tombol transport, suka/tidak suka, antrean, lirik, timer tidur, mini player, bisukan, toggle sumber, tombol kembali, hapus riwayat pencarian).
2. THE Kaset SHALL memberi nama aksesibilitas pada kontrol yang hanya memiliki nilai tanpa label terlihat (slider posisi pemutaran, slider volume, sembilan slider ekualiser).
3. WHERE sebuah kontrol memiliki tooltip, THE Kaset SHALL menetapkan nama aksesibilitas dari sumber string yang sama sehingga keduanya tidak pernah berbeda — tooltip **tidak** dibacakan pembaca layar dan bukan pengganti nama.
4. WHERE nama aksesibilitas sebuah kontrol sudah berasal dari teks kontennya (mis. tautan judul/artis pada `TrackInfo`), THE Kaset SHALL **tidak** menimpanya dengan label generik.
5. THE Kaset SHALL menandai konten dekoratif (sampul album, ikon aplikasi di title bar) sebagai `AccessibilityView="Raw"` agar pembaca layar tidak berhenti pada gambar yang mengulang teks di sebelahnya.
6. WHEN bahasa aplikasi berubah, THE Kaset SHALL memperbarui nama aksesibilitas mengikuti bahasa tersebut.

### Requirement 39: Mini Player dan Timer Tidur (Fase Lanjutan)

**User Story:** Sebagai pengguna, saya ingin mengecilkan Kaset menjadi overlay ringkas sambil bekerja, dan menjadwalkan pemutaran berhenti sendiri, sehingga aplikasi tetap berguna di latar dan saat tidur.

#### Acceptance Criteria

1. WHEN pengguna menekan tombol mini player, THE Kaset SHALL mengubah jendela utama ke presenter `CompactOverlay` (selalu di atas) dan menampilkan permukaan ringkas berisi sampul, judul/artis, transport (sebelumnya/putar-jeda/berikutnya), scrubber, dan tombol kembali.
2. WHILE berpindah masuk atau keluar mode mini player, THE Kaset SHALL mempertahankan pemutaran tanpa terputus — WebView2 pemutaran tidak boleh di-re-parent.
3. WHEN keluar dari mode mini player, THE Kaset SHALL memulihkan presenter, chrome (title bar + sidebar), batas ukuran minimum, dan mode panel now-playing yang terbuka sebelumnya.
4. WHEN pengguna memilih durasi timer tidur (15/30/45/60 menit), THE Kaset SHALL menjeda pemutaran setelah durasi tersebut habis, **tepat satu kali**, dan tidak lebih cepat dari durasi yang dipilih.
5. WHEN pengguna memilih timer "akhir lagu ini", THE Player_Service SHALL menjeda pemutaran pada akhir track yang diharapkan alih-alih memajukan antrean, dan SHALL mengabaikan event `TRACK_ENDED` yang tidak cocok dengan track tersebut.
6. WHILE timer tidur aktif, THE Kaset SHALL menampilkan indikasi visual pada Player_Bar beserta sisa waktu pada nama aksesibilitasnya; WHEN timer dibatalkan atau selesai, THE Kaset SHALL menghentikan tick-nya.
7. THE Kaset SHALL menempatkan keputusan "kapan berhenti" pada logika murni di `KasetWin.Core` yang dapat diuji headless, terpisah dari aksi menjeda.

### Requirement 40: Integritas Continuous Integration (Foundational)

**User Story:** Sebagai kontributor, saya ingin CI menangkap kesalahan XAML pada pull request, sehingga main tidak pernah hijau saat aplikasi sebenarnya gagal dibangun.

#### Acceptance Criteria

1. THE CI SHALL membangun `KasetWin.App` (kompilasi XAML) pada setiap push dan pull request ke `main`, terpisah dari job core headless.
2. WHERE sebuah perubahan mengandung kesalahan yang hanya muncul saat kompilasi XAML (jalur `x:Bind` salah, `x:Name` hilang, `StaticResource` tak dikenal, namespace attached property keliru), THE CI SHALL gagal.

### Requirement 41: Discord Rich Presence (Fase Lanjutan, Opsional)

**User Story:** Sebagai pengguna, saya ingin lagu yang sedang saya dengar tampil di profil Discord, sehingga teman-teman melihat apa yang saya putar.

#### Acceptance Criteria

1. WHERE pengguna menyalakan toggle Discord di Pengaturan, THE Kaset SHALL menerbitkan aktivitas "Listening" berisi judul lagu, artis, dan gambar sampul ke klien Discord lokal — **tanpa langkah setup apa pun dari pengguna**.
2. THE Kaset SHALL memakai satu Discord Application ID bersama yang ditanam di aplikasi. Application ID bersifat **publik** (ikut terkirim di tiap payload presence); yang rahasia adalah *Client Secret*, yang tidak dipakai jalur IPC lokal ini dan tidak pernah disentuh Kaset.
   - Meminta tiap pengguna mendaftarkan aplikasi Discord sendiri sebelum fitur ini berfungsi sama saja dengan meniadakan fitur ini bagi hampir semua orang.
2a. THE Kaset SHALL menyediakan override Application ID opsional di bagian "Lanjutan", hanya untuk pengguna yang ingin nama aplikasi berbeda muncul di profilnya.
2b. THE Kaset SHALL menonaktifkan fitur ini secara default, karena menyiarkan apa yang didengar ke profil publik adalah pilihan privasi pengguna.
2c. WHERE tidak ada Application ID yang tersedia (konstanta bersama kosong dan pengguna tidak mengisi override), THE Kaset SHALL menjelaskan kondisi itu di kartu Pengaturan alih-alih menampilkan toggle yang diam-diam tidak berfungsi.
3. WHEN pemutaran berjalan, THE Kaset SHALL menyertakan timestamp mulai yang sudah dikurangi posisi saat ini, sehingga penghitung waktu Discord tidak mengulang dari nol setiap seek atau reconnect.
4. WHILE pemutaran dijeda, THE Kaset SHALL menghilangkan timestamp agar lagu yang dijeda tidak tampak terus berjalan.
5. THE Kaset SHALL menjepit `details` dan `state` ke rentang 2–128 karakter yang diterima Discord, karena aktivitas di luar rentang itu ditolak diam-diam tanpa pesan error.
6. WHERE Discord tidak terpasang, tidak berjalan, atau ditutup di tengah sesi, THE Kaset SHALL memperlakukannya sebagai kondisi normal — tanpa error ke pengguna dan tanpa memengaruhi pemutaran.
7. THE Kaset SHALL membatasi frekuensi pembaruan presence (Discord membatasi ~1 per 15 detik) dan tidak mendorong pembaruan per-tik progres.

### Requirement 42: Pintasan Global dan Persistensi Geometri Jendela

**User Story:** Sebagai pengguna, saya ingin mengendalikan pemutaran dari aplikasi lain dan menemukan jendela Kaset di ukuran yang saya tinggalkan.

#### Acceptance Criteria

1. WHERE pintasan global diaktifkan di Pengaturan, THE Kaset SHALL mendaftarkan `Ctrl+Alt+↓` (putar/jeda), `Ctrl+Alt+→` (berikutnya), `Ctrl+Alt+←` (sebelumnya), dan `Ctrl+Alt+↑` (bisukan) secara sistem-wide.
2. WHERE sebuah kombinasi sudah diklaim aplikasi lain, THE Kaset SHALL melewati kombinasi itu saja dan tetap mendaftarkan sisanya.
3. THE Kaset SHALL menonaktifkan pintasan global secara default, karena mengklaim kombinasi tersebut dari seluruh aplikasi lain bukan keputusan yang boleh diambil sepihak.
4. WHEN pengguna mengubah toggle pintasan global, THE Kaset SHALL menerapkannya langsung tanpa perlu restart.
5. WHEN jendela ditutup, THE Kaset SHALL menyimpan ukuran dan posisi jendela, dan WHEN dibuka lagi SHALL memulihkannya.
6. WHERE jendela sedang dalam mode mini player, dimaksimalkan, atau diminimalkan, THE Kaset SHALL **tidak** menyimpan geometri tersebut — yang dipulihkan harus ukuran yang dipilih pengguna, bukan state sementara.
7. WHERE geometri tersimpan tidak lagi muat pada layar mana pun yang terpasang, THE Kaset SHALL mengabaikannya dan membuka pada ukuran default, agar jendela tidak muncul di luar area yang terlihat.
