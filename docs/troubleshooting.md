# Diagnosis — kalau Kaset crash, ngambek, atau "kok gak ngefek"

Dokumen ini ada karena satu sesi terbuang untuk mem-bisect commit padahal jawabannya sudah tertulis
lengkap di satu berkas log. Baca urutan di bawah **sebelum** menebak.

---

## 0. Menjalankan aplikasinya

```powershell
cd M:\kaset\kaset\KasetWin
Get-Process -Name "KasetWin.App" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/KasetWin.App/KasetWin.App.csproj -c Debug
Start-Process "shell:AppsFolder\Kaset.KasetWin_kjgd17zy2bc08!App"
```

Tiga jebakan yang sudah memakan korban:

| Gejala | Sebab |
|---|---|
| "Perubahanku tidak muncul" | Dipakai `-p:Platform=x64`. Paket yang ter-register menunjuk `bin\Debug\...\win-x64`, sedangkan flag itu menulis ke `bin\x64\Debug` — jadi yang diuji build lama. |
| "Aplikasinya crash, `Get-Process KasetWin` kosong" | Nama prosesnya **`KasetWin.App`**, bukan `KasetWin`. |
| Build gagal `MSB3027 / file locked` | Aplikasinya masih jalan. Stop dulu. Kalau XamlCompiler yang mengunci: `dotnet build-server shutdown` lalu tambahkan `-p:UseSharedCompilation=false`. |

Menjalankan `KasetWin.App.exe` langsung **tidak** berguna untuk diagnosis: build unpackaged butuh
bootstrapper Windows App Runtime dan gagal dengan `REGDB_E_CLASSNOTREG` sebelum kode kita jalan.
Selalu lewat `shell:AppsFolder`.

---

## 1. Aplikasi crash → baca `crash.log` DULU

```
%LOCALAPPDATA%\Packages\Kaset.KasetWin_kjgd17zy2bc08\LocalState\crash.log
```

Berisi exception .NET lengkap beserta stack-nya, ditulis oleh handler
`Application.UnhandledException` di `App.xaml.cs`.

**Kenapa ini wajib:** WinUI melaporkan *setiap* exception tak tertangkap sebagai kode yang sama —
`0xc000027b` di Windows Event Log — tanpa tipe, pesan, atau stack. Kode itu tidak memberi tahu
apa-apa; ia sama persis untuk null reference, cast gagal, dan kegagalan interop. Sebelum handler ini
ada, satu-satunya cara adalah mem-bisect commit satu per satu.

> **Jebakan yang sudah terjadi:** pemicu sebuah crash bisa berupa **data, bukan kode** — crash 2026-07-23
> hanya menyala kalau sudah ada geometri jendela tersimpan di settings. Akibatnya commit-commit lama
> pun ikut crash saat di-bisect, dan sempat terlihat seolah bukan ulah perubahan terbaru. Kalau
> bisect memberi hasil yang "tidak masuk akal", curigai state di `LocalState`, bukan kodenya.

Kalau `crash.log` kosong padahal aplikasinya mati, crash-nya terjadi sebelum handler terpasang
(sangat awal di `App` ctor). Baru saat itu Event Log berguna, sekadar untuk memastikan memang crash:

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-10)} |
  Where-Object { $_.Message -match 'KasetWin' } | Select-Object -First 3 TimeCreated, Id, Message
```

---

## 2. Sesuatu "tidak jalan" tapi tidak crash → `diag.log`

```
%LOCALAPPDATA%\Packages\Kaset.KasetWin_kjgd17zy2bc08\LocalState\diag.log
```

Di sinilah jawaban paling banyak ditemukan, karena kegagalan yang *anggun* tidak menampilkan apa pun
di layar. Contoh nyata: lirik tersinkron tidak pernah jalan, dan satu-satunya buktinya adalah baris
ini berulang di setiap lagu —

```
ytm-lyrics browse android failed: ApiError
ytm-lyrics videoId=... plain chars=1281
```

Aplikasinya "baik-baik saja": ia turun ke teks polos persis seperti dirancang. Tanpa log itu, gejala
yang terlihat pengguna hanyalah "kok liriknya tidak nyala".

Cari dengan pola, jangan dibaca semua:

```powershell
Select-String -Path "$env:LOCALAPPDATA\Packages\Kaset.KasetWin_kjgd17zy2bc08\LocalState\diag.log" `
  -Pattern "ytm-lyrics" | Select-Object -Last 15
```

---

## 3. Curiga masalahnya di API YouTube → pakai ApiExplorer

Jangan menebak bentuk respons; AGENTS.md melarangnya. Panggil endpoint-nya sungguhan:

```powershell
dotnet run --project src/KasetWin.ApiExplorer -- auth            # status login
dotnet run --project src/KasetWin.ApiExplorer -- list            # endpoint yang dikenal
dotnet run --project src/KasetWin.ApiExplorer -- browse FEmusic_home -v
dotnet run --project src/KasetWin.ApiExplorer -- lyrics t82Q3f4pNUY   # + per klien InnerTube
```

> **Perbedaan yang pernah menyesatkan:** ApiExplorer berjalan **tanpa cookie**, aplikasi berjalan
> **sudah login**. Sebuah permintaan bisa berhasil di explorer dan gagal 100% di aplikasi justru
> *karena* identitasnya terbawa — itulah persis kasus lirik tersinkron (lihat ADR 0005 keputusan 6).
> Kalau explorer bilang "jalan" tapi aplikasi bilang "gagal", curigai autentikasi lebih dulu.

videoId yang sudah terverifikasi dan enak dipakai sebagai kasus uji tetap:

| videoId | Atribusi | Tersinkron |
|---|---|---|
| `t82Q3f4pNUY` | Musixmatch | ya |
| `mZsHggY8G6M` | LyricFind | ya |
| `BiQIc7fG9pA` | Musixmatch | **tidak** (teks polos) |

---

## 4. Urutan yang disarankan

1. `crash.log` — kalau aplikasinya mati.
2. `diag.log` — kalau aplikasinya hidup tapi fiturnya diam.
3. ApiExplorer — kalau dugaannya di sisi YouTube.
4. Baru bisect commit. Hampir tidak pernah perlu sampai sini; dan kalau hasilnya aneh, baca lagi
   catatan "data, bukan kode" di bagian 1.

Lihat juga: `docs/known-issues.md` (jangan-jangan itu memang sudah diketahui dan diterima),
`docs/manual-test-checklist.md`, dan `docs/adr/`.
