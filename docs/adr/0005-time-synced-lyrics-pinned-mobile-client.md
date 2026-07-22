# ADR 0005 — Lirik tersinkron waktu via klien InnerTube seluler yang di-pin

## Status

Accepted (2026-07-22)

## Context

Panel lirik menampilkan lirik dari beberapa penyedia. Keluhan pemilik repo: liriknya **sering tidak
lengkap** — LRCLib dan NetEase dicari secara *fuzzy* (judul/artis/durasi), jadi untuk katalog non-Barat,
rilis ulang, remaster, dan judul lokal mereka sering meleset atau kosong. Penyedia yang tidak pernah
meleset adalah YouTube Music sendiri, karena pencariannya berkunci pada `videoId` yang sedang diputar
— tetapi selama ini ia hanya mengembalikan teks polos, sehingga hanya bisa mengisi tier "plain".

Eksplorasi langsung (tanpa autentikasi) dengan perintah baru `ApiExplorer lyrics <videoId>`
menunjukkan bahwa **fidelitas jawaban `browse` untuk id lirik (`MPLYt…`) ditentukan oleh identitas
klien InnerTube yang bertanya**, bukan oleh id-nya:

| Klien | Bentuk jawaban |
|-------|----------------|
| `WEB_REMIX` (yang dipakai aplikasi) | `musicDescriptionShelfRenderer` — teks polos + footer sumber |
| `IOS_MUSIC 6.33.3` + konteks iOS | sama, teks polos |
| `ANDROID_MUSIC 6.33.52` (dengan/tanpa konteks Android) | sama, teks polos |
| **`ANDROID_MUSIC 7.21.50` + konteks Android** | **`elementRenderer` → `timedLyricsModel`, lirik ber-cue** |

Dua variabel sama-sama wajib: `6.33.52` tetap polos walau konteks Android dikirim, dan `7.21.50`
jatuh kembali ke bentuk web bila field konteks (`androidSdkVersion`, `osName`, `osVersion`,
`platform`) dihilangkan. Jadi konteks Android adalah bagian dari identitas, bukan hiasan.

Bentuk data yang **diamati langsung** (bukan dugaan), pada `mZsHggY8G6M` dan 12 lagu lain,
2026-07-22:

```
contents.elementRenderer.newElement.type.componentType.model
  .timedLyricsModel.lyricsData.timedLyricsData[]
```

```json
{"lyricLine":"Ayy","cueRange":{"startTimeMilliseconds":"1980","endTimeMilliseconds":"3750","metadata":{"id":"0"}}}
```

- `lyricLine` — string.
- `cueRange.startTimeMilliseconds` / `endTimeMilliseconds` — **string berisi bilangan bulat
  milidetik**, bukan angka JSON. Harus di-parse defensif (`long.TryParse`, InvariantCulture).
- `cueRange.metadata.id` — string, indeks baris; tidak dipakai.

Dua temuan lain yang membentuk keputusan di bawah:

1. **Jawaban ber-timing tidak membawa atribusi sama sekali.** Setiap bentuk polos memuat footer
   `"Source: Musixmatch"` / `"Source: LyricFind"`; tidak satu pun jawaban `7.21.50` memuatnya
   (tidak ada `sourceMessage`, tidak ada `footer`). Kredit lisensor itu wajib ditampilkan.
2. **Bentuk ber-timing ≠ lagu tersinkron.** Sebuah lagu bisa mengembalikan `timedLyricsData` penuh
   dengan **nol** `cueRange` (`BiQIc7fG9pA`, 34–80 baris, 0 cue). Itu artinya lagunya memang tidak
   punya versi tersinkron.

## Decision

1. **Pin `ANDROID_MUSIC 7.21.50` + konteks Android hanya untuk satu permintaan `browse` lirik.**
   Konstanta ada di `InnerTubeSupport`; penyamaran tidak menyentuh endpoint lain, origin, cookie,
   maupun `SAPISIDHASH`. Id `MPLYt…` diambil dari `next` biasa (`WEB_REMIX`) karena id itu sama untuk
   semua klien (diverifikasi).
2. **Selalu ambil juga `browse` `WEB_REMIX`**, untuk dua alasan: sebagai *fallback* isi bila jalur
   Android gagal, **dan** sebagai satu-satunya sumber atribusi lisensor untuk hasil tersinkron.
   Permintaan itu di-cache (`ApiCacheTtl.Lyrics`) dan penyedia mem-memoize per `videoId`, jadi
   biayanya paling banyak satu permintaan tambahan per lagu per sesi. Alternatif "cukup tulis
   *YouTube Music* saja" ditolak: kredit lisensor baru saja dirilis atas permintaan pemilik repo,
   dan menghapusnya diam-diam sambil "memperbaiki" lirik adalah pertukaran yang salah.
3. **Permintaan `next` tidak ditambah — ia sudah dipakai bersama lewat `ApiCache`.** Badan permintaan
   `next` di jalur lirik identik byte-per-byte dengan yang dipakai `GetSongMetadataAsync` /
   `GetSongRelatedAsync` (`videoId`, `enablePersistentPlaylistPanel`, `isAudioOnly`,
   `tunerSettingValue`) dan TTL-nya sama (`ApiCacheTtl.SongMetadata`); kunci cache adalah badan yang
   dikanonkan, dan TTL bukan bagian dari kunci. Jadi `next` untuk lirik dilayani dari cache yang
   sudah diisi pemutar — tidak ada round trip tambahan, dan **tidak perlu** menyalurkan respons
   `next` antar-layanan (yang justru akan mengikat panel lirik ke siklus hidup pemutar). Yang
   dilakukan hanyalah mengunci fakta itu dengan tes
   (`The_lyrics_next_round_trip_is_shared_with_the_one_the_player_already_issues`), karena mengubah
   salah satu badan permintaan saja akan diam-diam menggandakan trafik.
4. **Nol `cueRange` = hasil polos, bukan hasil tersinkron yang rusak.** Parser mengembalikan
   `Text` gabungan bila tidak ada satu pun cue; hasil `Synced` hanya dibentuk bila minimal satu baris
   ber-cue.
5. **Setiap mode gagal turun kelas, tidak pernah melempar.** Rantainya:
   `Synced → Plain → Unavailable`.
6. **Permintaan Android dikirim ANONIM — tanpa cookie, tanpa `SAPISIDHASH`.** Ini bukan pilihan gaya;
   tanpa ini fiturnya **tidak jalan sama sekali**. InnerTube menolak konteks klien seluler yang
   membawa `SAPISIDHASH` beroriginkan web, jadi setiap permintaan berbalas HTTP 400 dan diam-diam
   turun ke teks polos. Gejalanya menyesatkan: eksplorasi lewat `ApiExplorer` berhasil (CLI-nya tidak
   punya cookie) sementara aplikasi yang sudah login gagal 100%, dan satu-satunya jejaknya adalah
   `ytm-lyrics browse android failed: ApiError` di log diagnostik. Lirik tidak butuh identitas — sama
   untuk semua orang — dan jalur tanpa login itu justru yang sudah diverifikasi hidup. **Jangan
   menghapus `anonymous: true` pada panggilan itu**; hasilnya adalah lirik tersinkron yang mati tanpa
   satu pun pesan error.
7. **Kredit lisensor ada di LABEL sumber, bukan di dalam lirik.** Sebelumnya kredit ditempel ke akhir
   teks (polos) dan ditambahkan sebagai satu baris bertimestamp setelah lirik terakhir (tersinkron).
   Dua-duanya menyembunyikannya — baru terlihat setelah digulir sampai habis — dan bentuk tersinkron
   lebih buruk lagi setelah gulir gaya Apple Music masuk: sorot baris aktif ikut berpindah ke baris
   palsu itu dan meluncurkannya ke puncak panel, seolah lagunya berakhir dengan kata "Musixmatch".
   Labelnya menyebut **keduanya** (`YouTube Music — LyricFind`): yang pertama menjawab penyedia mana
   yang menang (itu yang diatur di Settings), yang kedua menjawab siapa yang melisensi kata-katanya
   (itu yang diwajibkan YouTube tampil, dan berganti per lagu sehingga tidak bisa disimpulkan sendiri).
   Prefiks `Source:` / `Sumber:` dipangkas di parser: YouTube mengirimnya **sudah diterjemahkan**
   mengikuti bahasa konten, sehingga membiarkannya menghasilkan "Sumber: Sumber: LyricFind" dan
   membuat kata "Sumber" datang dari YouTube alih-alih dari pengaturan bahasa aplikasi.

### Kegagalan yang harus ditanggung (versi yang di-pin PASTI basi)

Versi klien asing yang di-pin akan basi ketika YouTube menaikkannya, dan basinya **tidak seragam**
(semuanya diamati langsung):

| Versi | Akibat |
|-------|--------|
| `7.21.50` (kini) | `timedLyricsModel`, lirik ber-timing |
| `6.33.52` (lama) | HTTP 200 berisi bentuk **polos** — penurunan diam-diam, bukan error |
| `5.01` | HTTP 400 `FAILED_PRECONDITION` |
| `9.99.99` (karangan) | HTTP 404 `NOT_FOUND` |

Yang paling berbahaya adalah baris kedua: tidak ada yang dilempar, liriknya hanya kehilangan timing.
Karena itu jalur Android tidak pernah boleh menjadi satu-satunya sumber — ia hanya boleh *menambah*
timing, tidak pernah *menghilangkan* lirik yang tetap diberikan klien desktop.

### Urutan penyedia

`YouTubeMusicLyricsProvider` dipindah ke **urutan pertama** di `AppHost.cs`, di depan LRCLib dan
NetEase, dan `LyricsService` kini memakai urutan registrasi sebagai prioritas di dalam satu tier
(sebelumnya pemenangnya ditentukan oleh latensi jaringan). Alasannya: ia satu-satunya penyedia yang
mengidentifikasi lagu lewat `videoId` persis, jadi tidak bisa mengembalikan lirik rekaman lain;
teksnya salinan label berlisensi; dan kini ia ikut bersaing di tier tersinkron. Karena ia turun
kelas sendiri ke teks polos, menaruhnya pertama **tidak bisa** membuat kita kehilangan lirik
tersinkron: bila ia tidak punya timing, hasil tersinkron LRCLib/NetEase tetap menang tier.

## Cara memverifikasi ulang (saat lirik mendadak kehilangan timing)

```powershell
dotnet run --project src/KasetWin.ApiExplorer -- search "judul artis"     # cari videoId lagu asli
dotnet run --project src/KasetWin.ApiExplorer -- lyrics <videoId> -v      # semua klien, satu per satu
```

Perintah itu mengulang seluruh alur untuk setiap kandidat klien dan melaporkan
`timed lines: N, with cueRange: M` plus contoh entri mentah. Bila `7.21.50` sudah polos untuk lagu
yang jelas tersinkron di aplikasi YouTube Music resmi, naikkan
`InnerTubeSupport.ClientVersionAndroidMusic` ke versi Android Music terbaru dan jalankan ulang.
Catatan: gunakan `videoId` **lagu** (dari `search`), bukan id klip musik dari URL youtube.com —
id klip biasanya tidak punya tab Lyrics sama sekali.

## Consequences

**Lebih mudah**

- Lirik karaoke untuk katalog yang selama ini kosong; sweep 13 lagu → 12 tersinkron.
- Sumber lirik jadi bisa diprediksi (urutan registrasi), bukan lomba latensi.
- Kredit lisensor tetap tampil pada kedua bentuk.

**Lebih sulit / biaya**

- Satu versi klien yang di-pin harus dirawat; kegagalannya senyap (turun ke teks polos).
- Satu permintaan `browse` tambahan per lagu per sesi (ter-cache) demi atribusi.
- Aplikasi kini bicara dengan dua identitas klien; setiap perubahan pada `RequestAsync` harus
  menjaga `clientNameOverride` / `clientExtras` tetap terbatas pada permintaan lirik ini saja.
