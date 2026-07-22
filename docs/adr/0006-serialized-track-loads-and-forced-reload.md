# ADR 0006 — Pemuatan track diserialisasi, dan pemuatan paksa untuk pilihan pengguna

## Status

Diterima — 2026-07-23. Menggantikan sebagian perilaku penjaga pemuatan yang dicatat di ADR 0003/0004
(bagian `_expectedVideoId`).

## Context

Dua cacat dari uji manual putaran 4, yang ternyata berakar pada asumsi yang sama:

1. **Menekan Next berkali-kali dengan cepat menghentikan pemutaran total.** `LoadTrackAsync` tidak
   pernah diserialisasi. Dua pemuatan yang tumpang tindih saling menyelak, dan yang lebih tua bisa
   menyelesaikan `finally`-nya **setelah** yang lebih baru memasang penjaga `_expectedVideoId` —
   sehingga penjaga itu tertinggal menunjuk track yang sudah ditinggalkan. Semua `STATE_UPDATE`
   berikutnya diabaikan, dan pemutar berakhir tanpa memutar apa pun.

2. **Setelah internet kembali, lagu yang sama tidak mau diputar.** `WebView2PlaybackController.LoadVideoAsync`
   keluar lebih awal ketika `_currentVideoId` sudah sama dengan yang diminta. Idempotensi itu memang
   disengaja — ia meredam `TRACK_ENDED` basi supaya lagu yang sedang berjalan tidak diputar ulang.
   Tapi ia juga membuat "klik lagu yang sama" menjadi tanpa efek, padahal halamannya sudah mati.

Keduanya adalah kasus di mana penjaga yang benar untuk **peristiwa otomatis** menjadi salah ketika
sumbernya adalah **niat pengguna**.

## Decision

1. **Pemuatan track diserialisasi dengan tiket generasi.** `LoadTrackAsync` mengambil `SemaphoreSlim`
   dan menaikkan `_loadGeneration`; pemuatan yang sudah tersalip berubah jadi no-op **sebelum**
   menyentuh controller. Pola yang sama sudah dipakai `LyricsService`, jadi ini bukan mekanisme baru
   di repo ini.
2. **Kegagalan pemuatan yang tersalip tidak boleh membersihkan `_expectedVideoId`.** Ini inti
   perbaikan cacat pertama: yang berhak melepas penjaga hanyalah pemuatan yang masih terkini.
3. **`LoadVideoAsync` menerima `forceReload`.** Hanya jalur yang berasal dari niat pengguna yang
   mengirim `true` (`PlayCollectionAsync` / `PlayMixAsync` / `PlaySongRadioAsync`, dan lewat mereka
   `PlayAsync` / `PlaySongAsync`). Auto-advance dan jalur "replay expected" tetap mendapat no-op
   idempoten — diuji, supaya event basi tidak mengulang lagu yang sedang berjalan.
4. **Navigasi yang gagal diingat.** Controller mencatat `NavigationCompleted` dengan
   `IsSuccess == false` dan bernavigasi ulang walau tanpa `forceReload`.

## Consequences

**Konsekuensi yang terlihat pengguna, dan disengaja:** mengklik lagu yang **sedang** diputar kini
mengulanginya dari 0:00, bukan tidak melakukan apa-apa. Itu harga dari perbaikan nomor 3 — dan
sekaligus perilaku yang diharapkan kebanyakan pemutar musik. Kalau suatu saat ini dianggap
mengganggu, yang perlu diubah cukup `forceReload: true` di tiga titik masuk itu.

**Yang TIDAK diselesaikan:** pemutaran tidak *otomatis* melanjut sendiri saat Wi-Fi kembali —
pengguna masih harus menekan play. Memantau status jaringan lalu melanjutkan sendiri adalah mekanisme
terpisah yang sengaja tidak ditambahkan di sini.

**Belum terverifikasi runtime:** cabang `IsSuccess == false` butuh WebView2 sungguhan dan putus
jaringan sungguhan; ia inert kecuali sebuah navigasi benar-benar melapor gagal. Lihat langkah 130-132
di `docs/manual-test-checklist.md`.
