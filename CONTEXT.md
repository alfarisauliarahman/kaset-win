# KasetWin Domain Language

Glosarium domain untuk **KasetWin** (port Windows / WinUI 3). Diadaptasi dari `CONTEXT.md` repo asli (macOS) dengan nama komponen versi Windows. Baca ini sebelum mengubah logika Library/Queue/Playback agar penamaan & aturan tetap konsisten.

> Sumber kebenaran perilaku = kode Swift di `Sources/` (referensi). Port hidup di `KasetWin/`.

## App Source

Pengalaman konten aktif yang dipilih lewat toggle sumber di sidebar. **Music** (YouTube Music) adalah default; **Video** (YouTube biasa) adalah pengalaman kedua. Pergantian sumber menukar permukaan navigasi tanpa menggabungkan kedua model data.

## Music Experience

Permukaan YouTube Music KasetWin: lagu, album, artis, playlist, podcast, lirik, manajemen antrian, dan pemutaran audio ber-DRM lewat **WebView2 tersembunyi** (`WebView2PlaybackController` / playback host). Pengambilan data milik `YTMusicClient` dan parser khusus musik di `KasetWin.Core`.

## YouTube Experience

Permukaan YouTube biasa (Fase Lanjutan): rekomendasi, pencarian, subscriptions, Shorts, channel, playlist, Watch Later, history, komentar, dan pemutaran video. Pengambilan data milik `YouTubeClient` dan parser khusus YouTube.

## Playback Arbiter

Koordinator yang menjaga YouTube Music dan YouTube biasa agar tidak saling menimpa saat memutar. Ia menjeda musik saat video YouTube mulai, menjeda video saat musik mulai, dan membiarkan penanganan media-key (SMTC) mengikuti sumber yang terakhir diputar.

## Library

Koleksi tersimpan pengguna yang login. KasetWin menampilkan Library musik sebagai playlist, artis yang di-follow, podcast yang di-subscribe, dan lagu yang di-upload.

## Library Content Identity

Aturan identitas untuk menentukan apakah dua item Library merujuk konten yang sama. Playlist bisa muncul sebagai `VL...` atau ID playlist mentah; artis yang di-follow bisa muncul sebagai `MPLAUC...` (library browse ID) atau `UC...` (channel publik). KasetWin memperlakukan bentuk-bentuk ekuivalen ini sebagai satu item Library. (Lihat `BrowseIdClassifier`.)

## Library Content Reconciliation

Aturan yang menggabungkan mutasi Library lokal yang optimistik dengan snapshot Library yang *eventually consistent*. Item yang baru ditambahkan tetap terlihat dan item yang dihapus tetap disembunyikan sampai respons backend stabil. (Lihat `LibraryContentReconciler`.)

## Library Mutation Orchestration

Alur yang menerapkan perubahan Library ke YouTube Music, menginvalidasi cache respons Library yang basi, memperbarui state Library secara optimistik, dan menjadwalkan rekonsiliasi saat snapshot backend tertinggal.

## Queue Song Metadata

Aturan menyiapkan lagu sebelum masuk antrian native: menghapus label album generik dari metadata artis, menerapkan nilai fallback artist/album/thumbnail untuk aksi berbasis album, dan mempertahankan metadata pemutaran saat membangun ulang nilai lagu.

## Album Playback Actions

Alur yang mengambil track album dari playlist detail YouTube Music, menyiapkannya sebagai lagu siap-antri, lalu menyisipkannya ke antrian atau mengganti pemutaran dengan album tersebut. (Lihat `PlayerService.PlayCollection`.)

## Playlist Playback Actions

Alur yang mengubah data browse playlist menjadi antrian pemutaran native: fallback antrian playlist radio, koreksi playability browse, fallback artwork playlist, pemuatan continuation, penyaringan duplikat, dan pembuangan continuation saat antrian aktif berubah.
