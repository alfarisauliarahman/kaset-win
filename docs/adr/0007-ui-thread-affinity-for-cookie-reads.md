# ADR 0007 — Pembacaan cookie di-marshal ke thread UI

## Status

Diterima — 2026-07-23. Menutup satu kelas kegagalan yang menyentuh **seluruh** lapisan API, bukan
satu fitur.

## Context

Album dan sampul tidak pernah muncul saat lagu diputar lewat `kaset://play?v=…` (checklist #73, #90,
#115, #129). Putaran uji 5 menyimpulkan penyebabnya ada di lapisan metadata — respons watch-next
hanya membawa browse id album tanpa judul — lalu menambahkan `TrackMetadataEnricher` untuk
menyusulkan judulnya. Test seam-nya lulus. Gejalanya tidak berubah sama sekali.

Putaran 7 menginstrumentasi kedua fetcher-nya. `diag.log` menjawab dalam satu baris, berulang untuk
**setiap** lagu:

```
enrich song videoId=BiQIc7fG9pA FAILED: COMException:
This method can only be called from the thread that created the object.
```

`CoreWebView2.CookieManager` adalah objek COM dengan afinitas thread: ia hanya boleh disentuh dari
thread yang membuatnya, yaitu thread UI. Dan **setiap** permintaan InnerTube menandatangani dirinya
dengan cookie (`SAPISIDHASH`), jadi setiap panggilan API yang berjalan di thread latar selalu gagal
untuk pengguna yang sedang login.

Kegagalannya senyap di mana pun pemanggil menganggap hasilnya opsional — dan enrichment memang
dirancang begitu: metadata itu kenyamanan, dan kenyamanan yang melempar tidak boleh mengganggu
pemutaran. Jadi kode yang benar, di atas panggilan yang tidak pernah sampai ke jaringan, lulus test
dan lolos dua putaran perbaikan.

## Decision

Pembacaan cookie di-marshal ke thread UI oleh dekorator `UiThreadCookieSource`, yang tinggal di
**lapisan App**, bukan di Platform.

Alasan penempatannya: `KasetWin.Platform` sengaja tidak bergantung pada WinUI, dan menyeret
`DispatcherQueue` ke sana demi satu pemanggil adalah arah yang salah. Pemanggil yang sudah berada di
thread UI tidak membayar apa pun — jalur cepatnya pass-through langsung.

Dispatcher-nya ditangkap saat host dibangun (`AppHost.Build`, dipanggil dari `App.OnLaunched`),
**bukan** di dalam factory DI. Factory berjalan di thread mana pun yang pertama kali me-resolve
layanan itu; untuk pemanggil latar, itu justru menangkap thread yang salah — persis bug yang
dokumen ini ada untuk mencegahnya.

Saat thread UI sudah tidak ada (shutdown), yang dikembalikan adalah snapshot kosong, bukan exception
— kontrak yang sama dengan yang sudah dijanjikan `WebView2CookieSource` ketika WebView2 belum ada.

## Consequences

- Panggilan API dari thread latar kini berhasil untuk pengguna login. Yang paling terlihat adalah
  baris album dan sampul di player bar, tapi cakupannya jauh lebih luas: **semua** kode latar yang
  memanggil InnerTube ikut berhenti gagal diam-diam.
- Ada satu hop dispatcher per pembacaan cookie dari thread latar. Diabaikan dibanding round-trip
  jaringan yang menyusulnya.
- **Pelajaran yang lebih penting dari perbaikannya:** kegagalan yang ditelan secara sengaja butuh
  jejak. Dua putaran terbuang karena satu-satunya bukti — sebuah exception — dibuang tanpa suara.
  Kedua fetcher metadata sekarang menulis hasilnya ke `diag.log`, berhasil maupun gagal. Kalau kelak
  ada `catch` yang menelan kegagalan lagi, tulis alasannya **dan** jejaknya.
- Jangan membalik ini dengan memindahkan marshalling ke `WebView2CookieSource`: itu mengharuskan
  Platform mengenal WinUI. Kalau suatu saat Platform butuh dispatcher untuk alasan lain, keputusan
  ini boleh ditinjau ulang — tapi bukan demi satu pemanggil.
