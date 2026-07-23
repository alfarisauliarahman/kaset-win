# Implementation Plan: Kaset WinUI 3

## Overview

Rencana implementasi bertahap untuk membangun **Kaset WinUI 3** (C#/.NET 8, WinUI 3) di solution **BARU** `KasetWin/` pada root workspace, terpisah dari kode Swift macOS (yang hanya menjadi referensi). Pendekatan bersifat **incremental & test-driven**: tiap tugas membangun di atas tugas sebelumnya, dimulai dari fondasi (solution, DI, logging, error) menuju lapisan API/parser, autentikasi, pemutaran WebView2, antrian/player, UI, lokalisasi, dan tooling CLI.

Konvensi:
- Setiap tugas hanya berisi pekerjaan **coding** (menulis/mengubah/menguji kode).
- Sub-tugas pengujian ditandai `*` (opsional untuk MVP, **tidak** diimplementasikan otomatis).
- **Property-based tests** memakai **CsCheck/FsCheck** (library, bukan implementasi sendiri), **minimum 100 iterasi**, **satu properti = satu test**, dan tiap test diberi komentar `// Feature: kaset-winui3, Property N: {judul}`.
- Properti murni diturunkan dari bagian *Correctness Properties* pada `design.md`.
- Referensi requirement memakai format `_Requirements: X.Y_`; referensi properti memakai `_Properties: N_`.

Fase:
- **INTI RILIS-1** (Tugas 1–17): wajib selesai agar aplikasi musik dapat digunakan.
- **FASE LANJUTAN** (Tugas 18–29): sub-tugas ditandai `*` dan epic diberi label `(Fase Lanjutan)`; dikerjakan setelah inti stabil.
- **DITUNDA**: dicatat di bagian Notes sebagai out-of-scope (tidak dibuat task).

---

## Tasks

### INTI RILIS-1

- [x] 1. Scaffold solution & fondasi (DI, logging, error)
  - [x] 1.1 Buat solution `KasetWin/` dan lima proyek
    - Buat `KasetWin.sln` + proyek `KasetWin.App` (WinUI 3), `KasetWin.Core` (class library, tanpa WinUI), `KasetWin.Platform` (adapter WinRT), `KasetWin.ApiExplorer` (console), `tests/KasetWin.Core.Tests` (xUnit + CsCheck/FsCheck)
    - Buat `Directory.Build.props` (`Nullable=enable`, `LangVersion=latest`, analyzers)
    - Atur referensi proyek: App→Core,Platform; Platform→Core; ApiExplorer→Core; Tests→Core
    - _Requirements: 23.1, 23.2_
  - [x] 1.2 Definisikan KasetError, kategori logging, dan redaksi rahasia
    - Implementasikan `KasetError` + `KasetErrorKind` (AuthExpired, NotAuthenticated, NetworkError, ParseError, ApiError, PlaybackError, Unknown) + `IsRetryable`
    - Konfigurasi `Microsoft.Extensions.Logging` + Serilog sink (file/debug) dengan kategori (Player/Auth/Api/WebView/Network/Notification) dan level
    - Implementasikan `RedactingEnricher`/destructuring policy yang menyensor cookie, token, SAPISID, dan header Authorization
    - _Requirements: 20.1, 21.1, 21.2, 21.3, 22.3_
  - [x] 1.3 Bootstrap Generic Host + Dependency Injection di App
    - Bangun `IHost` di `App.xaml.cs`, daftarkan logging + Serilog; daftarkan service singleton/transient secara incremental seiring implementasi
    - Resolve ViewModel via constructor injection
    - _Requirements: 23.1_
  - [x]* 1.4 Property test redaksi rahasia
    - **Property 33: Redaksi menghapus nilai sensitif dari output**
    - **Validates: Requirements 21.3, 22.3**
    - _Properties: 33_

- [x] 2. InnerTubeSupport (SAPISIDHASH origin-aware, logika murni)
  - [x] 2.1 Implementasikan ComputeSapisidHash + BuildContext
    - `InnerTubeSupport.ComputeSapisidHash(unixSeconds, sapisid, origin)` → `SAPISIDHASH {ts}_{SHA1hex}`
    - `BuildContext(onBehalfOfUser?)` dengan `clientName=WEB_REMIX`, versi, dan origin musik konstan
    - _Requirements: 3.1, 3.2, 3.4_
  - [x] 2.2 Implementasikan resolver SAPISID dari koleksi cookie
    - Pilih `__Secure-3PAPISID`, fallback `SAPISID`, kosong jika keduanya tidak ada (file terpisah `CookieSapisidResolver`)
    - _Requirements: 3.3_
  - [x]* 2.3 Property test SAPISIDHASH deterministik & well-formed
    - **Property 1: SAPISIDHASH deterministik dan well-formed**
    - **Validates: Requirements 3.1**
    - _Properties: 1_
  - [x]* 2.4 Property test konsistensi origin/clientName request musik
    - **Property 2: Header dan konteks request musik konsisten origin**
    - **Validates: Requirements 3.2, 3.4**
    - _Properties: 2_
  - [x]* 2.5 Property test resolusi SAPISID dari cookie
    - **Property 3: Resolusi SAPISID dari koleksi cookie**
    - **Validates: Requirements 3.3**
    - _Properties: 3_

- [x] 3. Model data (records immutable)
  - [x] 3.1 Definisikan records & enums domain
    - `Song/Artist/Album/Playlist/PlaylistDetail/HomeSection/HomeSectionItem/HomeResponse/SearchResponse/SongMetadata/RadioQueueResult` dan lirik (`TimedWord/SyncedLyricLine/SyncedLyrics/PlainLyrics/LyricResult`)
    - Enums `RepeatMode/AudioQuality/MusicVideoType/LikeStatus/PlaylistPrivacy`; identitas via `Id` (videoId/browseId); opsi `System.Text.Json` round-trip-safe
    - _Requirements: 16.1_
  - [x]* 3.2 Unit test value-equality & identitas stabil
    - Verifikasi `record` equality dan `Id` non-kosong untuk model inti
    - _Requirements: 16.1_

- [x] 4. APICache + RetryPolicy
  - [x] 4.1 Implementasikan APICache (TTL + LRU + invalidasi)
    - `IApiCache` dengan `TryGet/Set/ComputeKey` (SHA256 atas JSON terurut + endpoint + authuser/brand) + `InvalidateMutationCaches` (prefix `browse:/next:/like:/playlist/get_add_to_playlist:`); TTL per surface
    - _Requirements: 23.1_
  - [x] 4.2 Implementasikan ExponentialBackoffRetryPolicy
    - `IRetryPolicy.ExecuteAsync` dengan backoff eksponensial, `maxAttempts`, predikat retryable
    - _Requirements: 20.4_
  - [x]* 4.3 Property test batas percobaan & retryability
    - **Property 35: RetryPolicy mematuhi batas percobaan dan retryability**
    - **Validates: Requirements 20.4**
    - _Properties: 35_
  - [x]* 4.4 Unit test APICache TTL/LRU/invalidasi
    - Test ekspirasi TTL, eviksi LRU, dan invalidasi prefix mutasi
    - _Requirements: 23.1_

- [x] 5. Parser modular (pure static) + fixtures tersanitasi
  - [x] 5.1 Setup fixtures JSON tersanitasi per-surface + loader
    - Buat `tests/KasetWin.Core.Tests/Fixtures/{Home,Search,Library,Playlist,Artist,RadioQueue,SongMetadata,Lyrics}/` dengan respons InnerTube yang **disanitasi** (cookie/token/SAPISID/PII → placeholder); helper loader fixture
    - _Requirements: 22.2, 23.2_
  - [x] 5.2 Implementasikan ParsingHelpers + ResponseTreeSearch
    - Helper thumbnail/artists/durasi/isExplicit; pencarian renderer rekursif yang tahan reshuffle kontainer
    - _Requirements: 23.2_
  - [x] 5.3 Implementasikan HomeResponseParser + klasifikasi prefix browseId
    - Parse Home/Explore/Charts/Moods/NewReleases (section-based); helper `IdClassification` untuk rute navigasi via prefix browseId
    - _Requirements: 11.1, 11.3, 31.1_
  - [x] 5.4 Implementasikan SearchResponseParser
    - Top Result (`musicCardShelfRenderer`) + grup lagu/album/artis/playlist/podcast
    - _Requirements: 12.1, 12.4_
  - [x] 5.5 Implementasikan LibraryContentParser
    - `FEmusic_library_landing` (grid), identitas via prefix browseId
    - _Requirements: 13.1_
  - [x] 5.6 Implementasikan PlaylistParser + PlaylistEditability + continuation
    - Metadata + track playlist/album, deteksi kepemilikan (afordans hapus), ekstraksi token continuation, add-to-playlist & create id
    - _Requirements: 14.1, 14.2, 14.3, 8.4_
  - [x] 5.7 Implementasikan ArtistParser
    - Top songs, albums, singles/EP, status subscription, destinasi See all
    - _Requirements: 15.1_
  - [x] 5.8 Implementasikan RadioQueueParser
    - Ekstrak `playlistPanelVideo` (+ wrapper `playlistPanelVideoWrapperRenderer`) dan token continuation
    - _Requirements: 25.1_
  - [x] 5.9 Implementasikan SongMetadataParser
    - `musicVideoType` (OMV/ATV/UGC), feedback tokens, `isLive`, lyrics browseId, radio continuation
    - _Requirements: 9.1_
  - [x]* 5.10 Property test idempotensi/identitas parser (semua fixture)
    - **Property 23: Parser bersifat idempoten/deterministik dengan identitas stabil**
    - **Validates: Requirements 23.3, 11.1, 14.1, 14.2, 15.1, 16.1, 31.1**
    - _Properties: 23_
  - [x]* 5.11 Property test klasifikasi hasil pencarian
    - **Property 24: Klasifikasi hasil pencarian sesuai tipe**
    - **Validates: Requirements 12.1, 12.3**
    - _Properties: 24_
  - [x]* 5.12 Property test klasifikasi item library via prefix browseId
    - **Property 25: Identifikasi item library via prefix browseId**
    - **Validates: Requirements 11.3, 13.1, 12.4, 15.2**
    - _Properties: 25_
  - [x]* 5.13 Property test continuation gabung tanpa kehilangan/duplikasi
    - **Property 26: Continuation playlist/home menggabungkan tanpa kehilangan atau duplikasi**
    - **Validates: Requirements 8.4, 11.2**
    - _Properties: 26_
  - [x]* 5.14 Property test deteksi kepemilikan playlist
    - **Property 27: Deteksi kepemilikan playlist menentukan afordans hapus**
    - **Validates: Requirements 14.3**
    - _Properties: 27_
  - [x]* 5.15 Property test ParseError pada input rusak
    - **Property 34: Parser melempar ParseError pada input rusak**
    - **Validates: Requirements 20.3**
    - _Properties: 34_
  - [x]* 5.16 Property test parser radio queue
    - **Property 44: Parser radio queue mengekstrak lagu dan token**
    - **Validates: Requirements 25.1**
    - _Properties: 44_

- [x] 6. Lirik: LrcParser + LyricsService
  - [x] 6.1 Implementasikan LrcParser (round-trip)
    - Parse LRC → `SyncedLyrics` dan cetak `SyncedLyrics` → LRC (menangani metadata `[ar:]`, baris kosong, timestamp ganda)
    - _Requirements: 17.5_
  - [x] 6.2 Implementasikan LyricsService + LRCLibProvider
    - Ambil lirik dari LRCLib via judul/artis, cache per videoId, fallback synced→plain, logika sorot baris current
    - _Requirements: 17.1, 17.2, 17.3, 17.4_
  - [x]* 6.3 Property test round-trip parsing LRC
    - **Property 21: Round-trip parsing LRC**
    - **Validates: Requirements 17.5**
    - _Properties: 21_
  - [x]* 6.4 Property test penyorotan lirik synced monoton
    - **Property 22: Penyorotan lirik synced monoton terhadap waktu**
    - **Validates: Requirements 17.2**
    - _Properties: 22_

- [x] 7. YTMusicClient (HttpClient + endpoint InnerTube)
  - [x] 7.1 Implementasikan inti client + auth headers + error mapping + id helpers
    - Konfigurasi `HttpClient`/`SocketsHttpHandler` (maxConn 6, timeout 15s, header browser-style); `BuildAuthHeaders` (SAPISIDHASH + Cookie + Origin + X-Goog-AuthUser); mapper status HTTP→KasetError (401/403→AuthExpired, network→NetworkError, dll); helper id (`VL` strip untuk edit_playlist, `MPSPP`→`P`)
    - _Requirements: 3.1, 3.4, 3.5, 3.6, 20.2_
  - [x] 7.2 Implementasikan endpoint browse/library/detail (partial class)
    - `YTMusicClient.Browse.cs`: Home/Explore/Charts/Moods/NewReleases, Library landing/playlists/liked/uploaded, Playlist/Album/Artist + continuation; integrasi parser + cache
    - _Requirements: 11.1, 11.2, 13.1, 14.1, 14.2, 15.1, 8.4, 31.1_
  - [x] 7.3 Implementasikan endpoint search + suggestions (partial class)
    - `YTMusicClient.Search.cs`: `SearchAsync` (grup hasil) + `GetSearchSuggestionsAsync`
    - _Requirements: 12.1, 12.3_
  - [x] 7.4 Implementasikan endpoint detail playback & mutasi (partial class)
    - `YTMusicClient.Mutations.cs`: songMetadata/radio/mix continuation; rate/feedback/subscribe/unsubscribe; create/add/delete playlist; accounts_list
    - _Requirements: 13.2, 13.3, 13.4, 15.3_
  - [x]* 7.5 Property test pemetaan status HTTP ke KasetError
    - **Property 4: Pemetaan status HTTP ke KasetError**
    - **Validates: Requirements 3.6, 20.2**
    - _Properties: 4_
  - [x]* 7.6 Property test pembersihan id mutasi playlist
    - **Property 28: Pembersihan id mutasi playlist**
    - **Validates: Requirements 13.3**
    - _Properties: 28_
  - [x]* 7.7 Property test konversi id podcast MPSPP→P
    - **Property 36: Konversi ID podcast MPSPP→P**
    - **Validates: Requirements 27.4**
    - _Properties: 36_

- [x] 8. Abstraksi playback + WebView2 controller
  - [x] 8.1 Definisikan IPlaybackController/IJsBridge + helper audio quality
    - Antarmuka `IPlaybackController`, `IJsBridge`, records pesan (`PlaybackStateMessage`, `TrackEndedMessage`); helper pemetaan `AudioQuality`→string (`Low→small`, `Medium→medium`, `High→highres`)
    - _Requirements: 1.2, 2.1, 7.1, 7.3_
  - [x] 8.2 Implementasikan WebView2PlaybackController (Platform)
    - Singleton WebView2; observer.js + audioQuality.js (resource); injeksi via `AddScriptToExecuteOnDocumentCreatedAsync`; `WebMessageReceived` (validasi pesan untrusted); pause-before-load; seek/volume/mute via `ExecuteScriptAsync`; deteksi DRM (`IsDrmAvailable`)
    - _Requirements: 1.1, 1.2, 1.3, 1.6, 1.7, 2.1, 2.2, 2.3, 7.1, 7.2_
  - [x] 8.3 Implementasikan PlaybackWebViewHost (App)
    - WebView2 tersembunyi (1×1) dimiliki App; mode Hidden/Mini/Video seam
    - _Requirements: 1.1, 1.4_
  - [x]* 8.4 Property test pemetaan kualitas audio
    - **Property 19: Pemetaan kualitas audio bersifat total**
    - **Validates: Requirements 7.1, 7.3**
    - _Properties: 19_
  - [x]* 8.5 Smoke test singleton/lifecycle WebView2 + deteksi DRM
    - Verifikasi satu instance dibuat & pesan DRM tak tersedia (integration/smoke)
    - _Requirements: 1.1, 1.3, 1.5, 1.7_

- [x] 9. Autentikasi (cookie source, credential store, state machine, login)
  - [x] 9.1 Implementasikan WebView2CookieSource + DpapiCredentialStore
    - `ICookieSource` membaca cookie dari `CoreWebView2.CookieManager`; `DpapiCredentialStore` (DPAPI CurrentUser / Credential Locker) untuk simpan/muat rahasia
    - _Requirements: 3.3, 22.1_
  - [x] 9.2 Implementasikan AuthService (state machine)
    - State LoggedOut/LoggingIn/LoggedIn; `CheckLoginStatusAsync/StartLoginAsync/OnCookiesChanged/SessionExpired/SwitchAccountAsync`; authExpired→LoggedOut + NeedsReauth; origin-aware
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_
  - [x] 9.3 Implementasikan LoginDialog flow (App)
    - Tampilkan alur login Google di WebView2; transisi state via AuthService
    - _Requirements: 4.2, 4.3_
  - [x]* 9.4 Property test auth state machine
    - **Property 5: Auth state machine selalu valid dan mengikuti transisi**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6**
    - _Properties: 5_
  - [x]* 9.5 Unit test cookie source & credential store (fakes)
    - Verifikasi ekstraksi cookie & round-trip kredensial dengan implementasi in-memory
    - _Requirements: 22.1_

- [x] 10. QueueService (sumber kebenaran antrian)
  - [x] 10.1 Implementasikan QueueService
    - `SetQueue/Move/Clear/Shuffle/SetRepeatMode/PeekNext/AdvanceToNext/AdvanceToPrevious/AppendDeduplicated`; Shuffle menjaga track aktif; dedup berbasis videoId
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 5.7, 5.8, 8.1, 8.2, 8.3, 25.3_
  - [x]* 10.2 Property test shuffle permutasi menjaga track aktif
    - **Property 12: Shuffle adalah permutasi yang mempertahankan track aktif**
    - **Validates: Requirements 5.7, 6.4**
    - _Properties: 12_
  - [x]* 10.3 Property test PeekNext mengikuti mode repeat
    - **Property 13: PeekNext mengikuti mode repeat**
    - **Validates: Requirements 5.8, 2.5**
    - _Properties: 13_
  - [x]* 10.4 Property test Move mempertahankan multiset
    - **Property 14: Move mempertahankan multiset dan menempatkan item di target**
    - **Validates: Requirements 6.2**
    - _Properties: 14_
  - [x]* 10.5 Property test Clear mengosongkan antrian
    - **Property 15: Clear mengosongkan antrian dan menghentikan track berikutnya**
    - **Validates: Requirements 6.3**
    - _Properties: 15_
  - [x]* 10.6 Property test SetQueue/PlayCollection mengisi dari sumber
    - **Property 16: SetQueue/PlayCollection mengisi antrian dari sumber**
    - **Validates: Requirements 6.5, 8.1, 8.2, 8.3, 14.4, 15.4**
    - _Properties: 16_
  - [x]* 10.7 Property test AppendDeduplicated menjaga keunikan
    - **Property 17: AppendDeduplicated menjaga keunikan dan hanya menambah item baru**
    - **Validates: Requirements 25.3**
    - _Properties: 17_

- [x] 11. PlayerService + WebQueueSync
  - [x] 11.1 Implementasikan PlayerService + WebQueueSync
    - Kontrol play/pause/next/prev/seek/volume/mute/shuffle/repeat (clamp seek `[0,Duration]`, volume `[0,100]`); `HandleStateUpdate` (videoId otoritatif); `HandleTrackEndedAsync` (validasi videoId == expected, queue authority via WebQueueSync); live menonaktifkan seek; `PlayCollection` (album/playlist/artist) + terapkan preferensi kualitas audio
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 1.2, 1.6, 8.1, 8.2, 8.3, 9.2, 9.3_
  - [x]* 11.2 Property test pause-before-load & idempotensi pemuatan
    - **Property 6: Pause sebelum load dan idempotensi pemuatan video**
    - **Validates: Requirements 1.6, 1.2**
    - _Properties: 6_
  - [x]* 11.3 Property test STATE_UPDATE memetakan state player
    - **Property 7: STATE_UPDATE memetakan state player secara setia**
    - **Validates: Requirements 2.1, 2.2, 2.6**
    - _Properties: 7_
  - [x]* 11.4 Property test otoritas antrian pada akhir track
    - **Property 8: Otoritas antrian pada akhir track**
    - **Validates: Requirements 2.3, 2.4, 2.5**
    - _Properties: 8_
  - [x]* 11.5 Property test involusi play/pause & mute
    - **Property 9: Toggle play/pause dan mute adalah involusi**
    - **Validates: Requirements 5.1, 5.6**
    - _Properties: 9_
  - [x]* 11.6 Property test next→previous round-trip
    - **Property 10: Next lalu Previous adalah round-trip di tengah antrian**
    - **Validates: Requirements 5.2, 5.3**
    - _Properties: 10_
  - [x]* 11.7 Property test clamp seek & volume
    - **Property 11: Clamp seek dan volume**
    - **Validates: Requirements 5.4, 5.5**
    - _Properties: 11_
  - [x]* 11.8 Property test live menonaktifkan seek
    - **Property 20: Live menonaktifkan seek**
    - **Validates: Requirements 9.1, 9.2, 9.3**
    - _Properties: 20_

- [x] 12. Integrasi SMTC (Now Playing + tombol media)
  - [x] 12.1 Implementasikan SmtcController (Platform)
    - Update SMTC (judul/artis/artwork + status); teruskan tombol media play/pause/next/prev ke PlayerService
    - _Requirements: 10.1, 10.2, 10.3_
  - [x]* 12.2 Smoke test pembaruan SMTC
    - Verifikasi update metadata & status (integration/smoke)
    - _Requirements: 10.1, 10.3_

- [x] 13. Settings, ImageCache, single-flight, ColorExtractor, network seam
  - [x] 13.1 Implementasikan SettingsService (LocalSettings round-trip)
    - Persist halaman peluncuran, preferensi kualitas audio, ingat shuffle/repeat, preferensi lirik synced
    - _Requirements: 18.1, 18.2, 18.3, 18.4_
  - [x] 13.2 Implementasikan single-flight helper
    - `ConcurrentDictionary<string, Lazy<Task<T>>>` untuk menggabungkan request identik bersamaan
    - _Requirements: 16.3_
  - [x] 13.3 Implementasikan ImageCache + ColorExtractor
    - Memory + disk LRU, downsampling, prefetch dengan CancellationToken; ekstraksi warna aksen via BitmapDecoder averaging
    - _Requirements: 16.2_
  - [x] 13.4 Implementasikan INetworkMonitor seam (Platform)
    - `NetworkMonitor` berbasis `NetworkInformation`; update status konektivitas internal
    - _Requirements: 16.2_
  - [x]* 13.5 Property test round-trip pengaturan & kredensial
    - **Property 32: Round-trip persistensi pengaturan dan kredensial**
    - **Validates: Requirements 18.1, 18.2, 18.4, 22.1**
    - _Properties: 32_
  - [x]* 13.6 Property test single-flight menggabungkan request
    - **Property 31: Single-flight menggabungkan request identik bersamaan**
    - **Validates: Requirements 16.3**
    - _Properties: 31_

- [x] 14. UI WinUI 3 + mutasi Library
  - [x] 14.1 Implementasikan MainWindow (shell)
    - Mica backdrop, `NavigationView` + `Frame`, `PlayerBar`; keyboard accelerators (Ctrl+F, Space, Ctrl+Arrows, Ctrl+,); intercept close → hide window (background audio) + Quit eksplisit
    - _Requirements: 1.4, 1.5, 16.1_
  - [x] 14.2 Implementasikan ViewModel base + navigasi + identitas stabil/lazy
    - Base `ObservableObject`, navigation service, `ItemsRepeater`/virtualisasi dengan identitas stabil (videoId/browseId)
    - _Requirements: 16.1, 16.2_
  - [x] 14.3 Implementasikan Home & Explore page + ViewModel
    - Render section, navigasi ke detail; paginasi continuation
    - _Requirements: 11.1, 11.2, 11.3, 31.1_
  - [x] 14.4 Implementasikan Search page + ViewModel
    - Debounce kueri, saran pencarian, grup hasil, navigasi ke detail
    - _Requirements: 12.1, 12.2, 12.3, 12.4_
  - [x] 14.5 Implementasikan Library page + ViewModel + mutasi optimistik
    - Tampilkan playlists/liked/followed/uploaded; filter; `LibraryMutationActions` + `LibraryContentReconciler` (optimistic update + reconcile + rollback)
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7_
  - [x] 14.6 Implementasikan Playlist & Album page + ViewModel
    - Metadata + track, play ke Queue, afordans hapus untuk playlist milik pengguna
    - _Requirements: 14.1, 14.2, 14.3, 14.4_
  - [x] 14.7 Implementasikan Artist page + ViewModel
    - Top songs/albums/singles, follow/unfollow, See all, play ke Queue
    - _Requirements: 15.1, 15.2, 15.3, 15.4_
  - [x] 14.8 Implementasikan Queue page + ViewModel
    - Tampilkan antrian, reorder drag, clear, shuffle
    - _Requirements: 6.1, 6.2, 6.3, 6.4_
  - [x] 14.9 Implementasikan Lyrics page + ViewModel
    - Tampilkan synced (sorot baris) / plain fallback; indikator LIVE
    - _Requirements: 17.2, 17.3, 9.1_
  - [x] 14.10 Implementasikan Settings page + ViewModel
    - UI preferensi (halaman peluncuran, kualitas audio, lirik synced, ingat playback)
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 7.3_
  - [x]* 14.11 Property test filter library menghasilkan subset
    - **Property 29: Filter library menghasilkan subset yang cocok**
    - **Validates: Requirements 13.5**
    - _Properties: 29_
  - [x]* 14.12 Property test mutasi optimistik rollback & konvergen
    - **Property 30: Mutasi optimistik dapat di-rollback dan konvergen**
    - **Validates: Requirements 13.6, 13.7**
    - _Properties: 30_

- [x] 15. Lokalisasi & RTL
  - [x] 15.1 Implementasikan resource .resw + pemilihan bahasa + RTL
    - `.resw` untuk en/fr/ko/id/tr/ar; logika pemilihan bahasa (locale→didukung, fallback English); `FlowDirection` RTL untuk Arabic
    - _Requirements: 19.1, 19.2, 19.3_
  - [x]* 15.2 Property test pemilihan bahasa & arah tata letak
    - **Property 42: Pemilihan bahasa dan arah tata letak**
    - **Validates: Requirements 19.2, 19.3**
    - _Properties: 42_

- [x] 16. API Explorer CLI
  - [x] 16.1 Implementasikan Program.cs (auth/list/browse)
    - Perintah `auth` (status), `list` (endpoint dikenal), `browse <id> [-v] [--brand]`; reuse `YTMusicClient` + parser dari Core
    - _Requirements: 24.1, 24.2, 24.3_
  - [x]* 16.2 Smoke test perintah CLI
    - Verifikasi `auth`/`list` berjalan (integration/smoke)
    - _Requirements: 24.2_

- [x] 17. Checkpoint inti — pastikan semua test inti lulus
  - Pastikan seluruh test inti lulus, tanyakan ke user bila ada pertanyaan.

### FASE LANJUTAN

- [x] 18. (Fase Lanjutan) Infinite Mix & radio
  - [x]* 18.1 Implementasikan continuation mix + threshold + dedup wiring
    - Muat lagu awal via `next`; muat tambahan saat tersisa ≤10; dedup sebelum append; reset token mix saat antrian reguler/song radio/clear
    - _Requirements: 25.1, 25.2, 25.3, 25.4_
  - [x]* 18.2 Property test ambang pemuatan & reset token mix
    - **Property 18: Ambang pemuatan dan reset token mix**
    - **Validates: Requirements 25.2, 25.4**
    - _Properties: 18_

- [x] 19. (Fase Lanjutan) Video & floating/PiP player
  - [x]* 19.1 Implementasikan VideoWindow + SetDisplayModeAsync(Video) + deteksi OMV
    - Reparent elemen video ke jendela mengambang tanpa menghentikan pemutaran; pop-out behavior; deteksi ketersediaan video (OMV vs ATV/UGC)
    - _Requirements: 26.1, 26.2, 26.3, 26.4_
  - [x]* 19.2 Property test deteksi ketersediaan video
    - **Property 37: Deteksi ketersediaan video dari tipe video musik**
    - **Validates: Requirements 26.1**
    - _Properties: 37_

- [x] 20. (Fase Lanjutan) Podcasts
  - [x]* 20.1 Implementasikan PodcastParser + tab region-aware + progress + subscribe
    - Tampilkan tab Podcasts bila `FEmusic_podcasts` tersedia (sembunyikan jika 404); simpan progress/played; subscribe/unsubscribe (reuse konversi MPSPP→P)
    - _Requirements: 27.1, 27.2, 27.3, 27.4_
  - [x]* 20.2 Property test round-trip progres episode podcast
    - **Property 41: Round-trip progres episode podcast**
    - **Validates: Requirements 27.3**
    - _Properties: 41_

- [~] 21. (Fase Lanjutan) ~~Scrobbling Last.fm~~ — **DIBATALKAN (2026-07-22, keputusan user)**
  - Dikeluarkan dari lingkup atas permintaan pemilik repo. Task 21.1–21.3 beserta Property 38 & 39
    dan Requirement 28 tidak akan dikerjakan. Tidak ada kode yang perlu dihapus (belum pernah
    diimplementasikan). Jika suatu saat dihidupkan lagi, rujukan desainnya masih ada di riwayat git.

- [x] 22. (Fase Lanjutan) Favorites & item tersemat
  - [x]* 22.1 Implementasikan FavoritesService + bagian Home
    - Add (tanpa duplikasi), remove, reorder (persist), tampilkan bagian Favorites di Home
    - _Requirements: 29.1, 29.2, 29.3, 29.4_
  - [x]* 22.2 Property test keunikan & reversibilitas Favorites
    - **Property 40: Operasi Favorites menjaga keunikan dan reversibilitas**
    - **Validates: Requirements 29.1, 29.2, 29.3, 29.4**
    - _Properties: 40_

- [x] 23. (Fase Lanjutan) Riwayat (History)
  - [x]* 23.1 Implementasikan History page + ViewModel + GetHistoryAsync
    - Ambil riwayat via InnerTube; pilih item → putar
    - _Requirements: 30.1, 30.2_

- [x] 24. (Fase Lanjutan) Explore detail (Moods/Charts/New Releases)
  - [x]* 24.1 Implementasikan halaman detail Explore (reuse HomeResponseParser)
    - New Releases, Charts, Moods & Genres + konten per kategori; navigasi ke detail
    - _Requirements: 31.1, 31.2, 31.3_

- [x] 25. (Fase Lanjutan) Mode YouTube penuh + PlaybackArbiter
  - [x]* 25.1 Implementasikan client/parser/WebView YouTube paralel
    - Home/Explore/Subscriptions/History YouTube; watch page (video/metadata/komentar); like/dislike/subscribe/Watch Later
    - _Requirements: 32.1, 32.2, 32.5_
  - [x]* 25.2 Implementasikan PlaybackArbiter (satu sumber audio)
    - Pastikan hanya satu sumber audio aktif pada satu waktu
    - _Requirements: 32.3_
  - [x]* 25.3 Implementasikan Shorts (paging vertikal snap)
    - _Requirements: 32.4_

- [x] 26. (Fase Lanjutan) Protocol activation kaset://
  - [x]* 26.1 Implementasikan registrasi protokol + parser URI + handler aktivasi
    - `Package.appxmanifest` protocol `kaset://`; parse `play/playlist/album/artist`; abaikan URI tak valid tanpa mengubah state
    - _Requirements: 33.1, 33.2, 33.3, 33.4, 33.5_
  - [x]* 26.2 Property test parsing URL kaset://
    - **Property 43: Parsing URL kaset:// menghasilkan konten yang benar atau diabaikan**
    - **Validates: Requirements 33.1, 33.2, 33.3, 33.4, 33.5**
    - _Properties: 43_

- [x] 27. (Fase Lanjutan) Berbagi konten
  - [x]* 27.1 Implementasikan share via DataTransferManager
    - Dialog berbagi native (judul + URL); nonaktifkan saat tidak ada URL
    - _Requirements: 34.1, 34.2_

- [x] 28. (Fase Lanjutan) Notifikasi toast + indikator jaringan
  - [x]* 28.1 Implementasikan ToastNotificationService + indikator offline
    - Toast ganti track (judul/artis); indikasi konektivitas tidak tersedia saat offline
    - _Requirements: 35.1, 35.2, 35.3_

- [x] 29. Checkpoint fase lanjutan — pastikan semua test lulus
  - Pastikan seluruh test lulus, tanyakan ke user bila ada pertanyaan.

### UPSTREAM SYNC (v0.12.0)

> Fitur/fix baru dari repo asli `sozercan/kaset` sampai commit `bd68513` (tag v0.12.0) yang belum tercakup baseline spec. Rincian & pemetaan lengkap: `upstream-sync.md`. Semua sub-tugas ditandai `*` (belum diimplementasikan otomatis).

- [ ] 30. (Upstream Sync) Fitur & fix v0.12.0
  - [x]* 30.1 Nama artis clickable di header album/playlist → navigasi ke halaman artis
    - Jadikan nama artis di `AlbumPage`/`PlaylistPage` sebuah `HyperlinkButton`/afordans klik → `NavigateToArtist(browseId)`; guard bila `browseId` kosong. Ref upstream: `PlaylistDetailView.swift` (#341)
    - **SELESAI**: `PlaylistDetailViewModel` mengekspos `AuthorId` + `HasAuthorLink` (guard `ParsingHelpers.IsNavigableArtistId`, menolak id sintetis) + `NavigateToArtistCommand`; header `AlbumPage`/`PlaylistPage` pakai pola HyperlinkButton/plain seperti `TrackInfo`. Build hijau.
    - _Requirements: 37.1_
  - [ ]* 30.2 Kontrol seek ±30 detik (mode YouTube video)
    - Tombol +30s/−30s di player bar video; clamp `[0, Duration]`. Ref upstream: `YouTubePlayerService+Seeking.swift` (#326)
    - **DITUNDA (di luar fokus)**: fitur khusus **mode YouTube video** (Fase Lanjutan). User memutuskan fokus YouTube Music dulu. Kerjakan bersama Tugas 25 (mode YouTube penuh).
    - _Requirements: 37.2_
    - _Properties: 11_
  - [~]* 30.3 Aksi Like/Unlike lagu aktif
    - Toggle like lagu aktif via **SMTC thumbbutton** atau **Jump List** taskbar; reuse `SongLikeStatusManager`. Padanan dari Dock menu macOS. Ref upstream: #334
    - **SEBAGIAN (in-app selesai; surface sistem ditunda)**: tombol **Like** ditambahkan di `PlayerBar` (grup now-playing) — resolve `IYTMusicClient` dari DI, toggle `RateSongAsync` (Like ↔ removelike), update optimistik + revert bila gagal, visual opacity mengikuti konvensi Shuffle/Repeat. Build hijau. **Verifikasi fungsional butuh app berjalan + sesi login** (belum dijalankan; AGENTS.md mewajibkan izin sebelum menjalankan UI). Surface sistem (taskbar thumbbutton `ITaskbarList3`) tetap ditunda.
    - _Requirements: 37.3_
  - [ ]* 30.4 Polish/redesain Player Bar sesuai upstream
    - Scrubber ala Apple Music, marquee judul, artwork glow, vertical volume slider, seek-hold. Ref upstream file baru: `AppleMusicScrubber`, `PlayerBar*` (#314, #327, #331)
    - **DITUNDA (redesain UI besar & subjektif)**: perlu keputusan desain dengan user + verifikasi visual app berjalan. Bukan perbaikan fungsional.
    - _Requirements: 37.4_
  - [ ]* 30.5 History Brand Account (musik + video)
    - Pastikan pencatatan history memakai sesi Brand account yang benar; baca ADR `0023-brand-account-history-session-switch` di repo asli sebelum menyentuh auth/history. Ref upstream: #318
    - **DITUNDA (butuh API/sesi multi-akun nyata)**: tidak dapat diverifikasi headless; perlu akun Brand asli + observasi sesi WebView2. Baca ADR 0023 dulu.
    - _Requirements: 37.5_
  - [ ]* 30.6 Resolusi API key di balik EU consent wall
    - Tangani consent wall Uni Eropa saat resolusi API key/cookie di `YTMusicClient`. Ref upstream: `APISessionConfiguration.swift` (#345)
    - **DITUNDA (butuh respons API region EU nyata)**: tidak dapat diverifikasi tanpa consent wall EU asli. Rujuk `APISessionConfiguration.swift` saat dikerjakan.
    - _Requirements: 37.6_
  - [x]* 30.7 Fix media-key "next" mengulang track saat background
    - Pastikan next benar-benar maju (videoId otoritatif) di jalur SMTC/`PlayerService`. Ref upstream: #319
    - **SELESAI + TERUJI**: bug ditemukan di `QueueService.NextIndexLocked` — `RepeatMode.One` membuat Next eksplisit mengulang lagu sama. Perbaikan: `AdvanceToNext(bool ignoreRepeatOne)`; `PlayerService.NextAsync` pakai `ignoreRepeatOne:true` (skip maju/ wrap), jalur track-end tetap default (Repeat One dipertahankan). 6 unit test baru di `QueueServiceSkipTests.cs`; **381 test lulus**.
    - _Requirements: 37.7_
  - [x]* 30.8 Kontrak ukuran main window
    - Terapkan batas min/max + persistensi ukuran window. Ref upstream: `MainWindowLayout.swift` (#322)
    - **SELESAI**: `MainWindowLayout.Configure(this)` di ctor MainWindow — min 980×600 (DPI-scaled) via subclass `WM_GETMINMAXINFO` (WinAppSDK 1.6 belum punya `PreferredMinimum*`), buka default 1100×760. Build hijau.
    - _Requirements: 37.8_
  - [~]* 30.9 Pertahankan warna ikon sidebar setelah navigasi
    - Ikon `NavigationView`/sidebar tetap ter-branding setelah pindah halaman. Ref upstream: #336
    - **N/A untuk port saat ini**: sidebar WinUI pakai `NavigationView` standar dengan `FontIcon` monokrom (tanpa override `Foreground`) — tidak ada konsep "ikon berwarna brand" seperti `KasetSidebarRow` macOS, dan NavigationView mengelola foreground state selected/unselected otomatis. Bug #336 tak punya analog di sini. Ditinjau ulang bila sidebar berwarna brand ditambahkan.
    - _Requirements: 37.9_

### KUALITAS & FITUR TAMBAHAN (2026-07-22)

> Hasil audit menyeluruh atas basis kode. Tiga temuan cacat (aksesibilitas nol, klaim bahasa yang
> tidak sesuai kenyataan, CI yang tidak pernah mengompilasi XAML) plus dua fitur yang hilang
> dibanding klien musik desktop lain. Detail keputusan: `docs/adr/0003` (mini player & timer tidur)
> dan `docs/adr/0004` (Discord, pintasan global, geometri jendela).

- [x] 31. Aksesibilitas antarmuka
  - [x] 31.1 Beri nama aksesibilitas pada seluruh kontrol ikon-saja dan nilai-saja
    - **SELESAI**: sebelumnya `AutomationProperties` muncul **0 kali** di 26 file XAML — setiap tombol
      ikon-saja terbaca Narrator sebagai "button" tanpa nama. Ditambahkan helper
      `KasetWin.App/Accessibility/A11y.cs` (`Label` = nama + tooltip sekaligus, `Name` = nama saja
      untuk slider, `Decorative` = sembunyikan sampul/ikon dekoratif). Seluruh `SetToolTip` di jalur
      `ApplyLanguage` diganti `A11y.Label`, sehingga nama aksesibilitas ikut bahasa aplikasi tanpa
      jalur terpisah. Hasil akhir: **47 atribut `AutomationProperties` di XAML, 0 tombol ikon-saja
      tanpa nama** (diverifikasi dengan skrip pemindai, bukan perkiraan).
    - Cakupan: 13 tombol transport Player_Bar, slider seek + volume, tombol/daftar panel now-playing,
      tombol kembali title bar, toggle sumber (pill + compact, menyebut sumber yang sedang aktif),
      tombol playlist baru, hapus riwayat pencarian (di dalam `DataTemplate`, lewat handler `Loaded`,
      menyertakan query yang akan dihapus), 9 slider ekualiser, dan 14 tombol di halaman konten.
    - **Sengaja TIDAK diberi nama**: tautan pada `TrackInfo` — nama aksesibilitasnya sudah berasal
      dari judul/artis; menimpanya dengan "Buka album" justru menghapus judulnya. Alasan ini ditulis
      di komentar kode agar tidak "diperbaiki" belakangan.
    - _Requirements: 38.1, 38.2, 38.3, 38.4, 38.5, 38.6_

- [x] 32. Koherensi lokalisasi + cacat RTL
  - [x] 32.1 Persempit bahasa yang didukung agar cocok dengan string yang ada
    - **SELESAI**: `SupportedLanguages.All` menyebut 6 bahasa karena ada 6 folder `.resw`, padahal
      `x:Uid` di seluruh app = **0** dan semua teks berasal dari `UiStrings` (id/en saja). Cacat yang
      terlihat pengguna: di Windows berbahasa Arab, jendela dibalik ke **RTL** lalu diisi teks bahasa
      Indonesia. Diperbaiki ke `["en", "id"]`; stub `ar/fr-FR/ko-KR/tr-TR` dihapus; mesin RTL
      dipertahankan + tetap diuji. `Strings/README.md` ditulis ulang (urutan menambah bahasa:
      terjemahkan `UiStrings` dulu, baru tambah subtag). +2 test regresi.
    - _Requirements: 19.1, 19.3_
  - [x] 32.2 Perbaiki kebocoran bahasa yang tersisa
    - **TERVERIFIKASI MANUAL (2026-07-22)**: tombol lirik dalam mode English menampilkan "Lyrics"
      (langkah 32) — OK.
    - **SELESAI**: 4 kebocoran nyata (bukan 10 seperti dugaan awal — literal XAML lain ternyata hanya
      *fallback* yang ditimpa `ApplyLanguage` di konstruktor). (a) `PlayerBar.xaml.cs` menetapkan
      tooltip lirik hardcoded `"Subtitel (CC)" : "Lirik"` yang **menimpa** versi terlokalisasi →
      dipindah ke `ApplyLyricsButtonLabel()`; (b) tooltip tombol kembali title bar tak pernah
      direlabel; (c) judul + pesan banner offline hardcoded **Inggris** (kebalikannya); (d) tombol
      hapus riwayat di dalam `DataTemplate` tak terjangkau. Fallback XAML dinormalkan ke Inggris agar
      konvensinya konsisten.

- [x] 33. Integritas CI — kompilasi XAML
  - [x] 33.1 Tambah job CI yang membangun `KasetWin.App`
    - **SELESAI**: job `build-app` di `.github/workflows/ci.yml` menjalankan
      `dotnet build src/KasetWin.App -c Release -p:Platform=x64 -p:WindowsPackageType=None`.
      Sebelumnya CI **hanya** membangun test core, sehingga error XAML (jalur `x:Bind` salah, `x:Name`
      hilang, `StaticResource` tak dikenal) tidak pernah tertangkap di PR. Windows App SDK ter-restore
      dari NuGet di runner standar — tanpa install workload. Perintahnya diverifikasi jalan lokal
      sebelum ditulis ke workflow.
    - _Requirements: 40.1, 40.2_

- [x] 34. Mini player (CompactOverlay)
  - [x] 34.1 Implementasikan mode mini player pada jendela utama
    - **SELESAI**: `MainWindow.MiniPlayer.cs` + `Controls/MiniPlayerView.xaml`. Jendela yang sama
      dipakai ulang dan hanya chrome-nya ditukar — membuat jendela kedua akan me-re-parent WebView2
      pemutaran dan **mematikan audio**. `MainWindowLayout` dapat `SuspendMinimumSize()` /
      `RestoreMinimumSize()` karena floor 980×600 akan memveto ukuran compact. Panel now-playing
      ditutup saat masuk dan modenya dipulihkan saat keluar.
    - **BUG DITEMUKAN & DIPERBAIKI SAAT UJI MANUAL (2026-07-22)**: menutup jendela dari dalam mode
      mini player lalu membukanya lagi dari tray mengembalikan jendela 400×150 tanpa chrome, dan
      pengguna tidak punya jalan keluar yang jelas. Penyebabnya bukan persistensi geometri sama
      sekali: menutup jendela **tidak menghancurkannya** — jendela hanya di-`Hide()` ke tray agar
      audio tetap jalan, sehingga presenter `CompactOverlay` bertahan. Perbaikan: `OnAppWindowClosing`
      memanggil `ExitMiniPlayer()` sebelum menyembunyikan. Sekalian: frame sebelum masuk mini player
      kini disimpan eksplisit (`_frameBeforeMini`) dan dipulihkan saat keluar — peralihan presenter
      tidak dapat diandalkan mengembalikan ukuran yang dipilih pengguna.
    - **TERVERIFIKASI MANUAL (2026-07-22)**: kontinuitas audio saat masuk/keluar mini player (langkah 2)
      OK; buka-lagi-dari-tray kembali ke ukuran normal (58) OK; tombol restore mengembalikan ukuran &
      posisi persis seperti sebelumnya (58b) OK; batas ukuran minimum aktif lagi setelah keluar mini
      player (9) OK. Langkah 58: ukuran sudah benar, tetapi memunculkan cacat visual di bawah.
    - **CACAT VISUAL LANJUTAN, SUDAH DIPERBAIKI (2026-07-22)**: perbaikan pertama benar secara
      perilaku tetapi jelek dilihat — jendela tampak membesar kembali ke ukuran penuh sesaat sebelum
      hilang ke tray, karena `ExitMiniPlayer()` dijalankan **sebelum** `AppWindow.Hide()`. Urutan
      dibalik: `Hide()` dulu, pelepasan mini player menyusul saat jendela sudah tidak terlihat.
      Ukuran yang dipersistensi juga tidak lagi diambil dari frame aktif (400×150) melainkan dari
      `FrameBeforeMiniPlayer` lewat parameter baru `SaveGeometry(window, overrideFrame)`.
      **Perlu diuji ulang** (langkah 58 + 58c pada checklist).
    - **Jangan** membalik urutan ini lagi: melepas mini player sebelum menyembunyikan memunculkan
      animasi; tidak melepasnya sama sekali memunculkan bug aslinya (jendela 400×150 tanpa chrome
      saat dibuka lagi dari tray).
    - _Requirements: 39.1, 39.2, 39.3_

- [x] 35. Timer tidur
  - [x] 35.1 Implementasikan `SleepTimer` (Core) + UI Player_Bar
    - **SELESAI**: `KasetWin.Core/Services/Player/SleepTimer.cs` — state machine murni, waktu disuplai
      pemanggil sehingga deterministik dan bisa diuji headless. `Advance`/`NotifyTrackEnded`
      mengembalikan `true` **tepat sekali** lalu men-disarm dirinya. Singleton DI dipakai dua pihak:
      `PlayerBar` (arm + tick 1 detik, hanya berjalan saat aktif + hitung mundur di nama
      aksesibilitas) dan `PlayerService.HandleTrackEndedAsync` (menegakkan mode "akhir lagu ini").
    - **Keputusan penting**: hook diletakkan di `HandleTrackEndedAsync` **setelah** `WebQueueSync`
      mengklasifikasi event — bukan pada perubahan `CurrentTrack`, yang juga menyala saat pengguna
      menekan Next manual dan akan menjeda tepat setelah pengguna ganti lagu.
    - _Requirements: 39.4, 39.5, 39.6, 39.7_
  - **TERVERIFIKASI MANUAL (2026-07-22)**: mode "akhir lagu ini" + Next manual (langkah 16) — timer
    tetap aktif dan pemutaran tidak terjeda, sesuai desain.
  - [x] 35.2 Test `SleepTimer`
    - **SELESAI**: `SleepTimerTests.cs` — 9 test, termasuk properti 100 iterasi bahwa timer durasi
      menyala tepat sekali dan tidak pernah lebih cepat dari durasi penuh.
    - _Requirements: 39.4, 39.7_

- [x] 36. Polish tata letak shell
  - [x] 36.1 Banner offline jadi overlay (pemindahan toggle sumber: dibatalkan)
    - **SELESAI**: banner offline dulu menempati baris Grid tersendiri, sehingga putus koneksi
      menggeser seluruh aplikasi turun lalu menyentak balik saat online — reflow atas kejadian yang
      bukan ulah pengguna. Kini overlay di area konten (baris 1 sengaja dibiarkan kosong).
    - **Toggle sumber: DIKEMBALIKAN ke `PaneFooter` (2026-07-22, keputusan user).** Sempat dipindah
      ke `PaneHeader` dengan alasan ia mengganti seluruh isi sidebar sehingga layak di posisi yang
      pertama dilihat. Setelah dilihat langsung di aplikasi, pemilik repo menolak — itu penilaian
      tata letak yang subjektif, dan penilaian pengguna yang memakainya tiap hari yang menang.
      Jangan diulang tanpa diminta.
    - **BELUM DIVERIFIKASI VISUAL** (banner offline).

- [x] 36b. Fix crash halaman Pengaturan (ditemukan saat uji manual 2026-07-22)
  - **BUG**: membuka Pengaturan langsung membuat aplikasi crash (`0xc000027b` di
    `Microsoft.ui.xaml.dll`, tanda WER `80004003` = null pointer). Penyebab: `ApplyLabels()` dipanggil
    di ctor **sebelum** `ViewModel` dibuat, dan baris baru di dalamnya membaca
    `ViewModel.IsDiscordAvailable`. Perbaikan: baris yang membaca ViewModel dipindah ke setelah
    ViewModel dibuat; `ApplyLabels()` kembali hanya menyentuh elemen XAML.
  - **PELAJARAN**: di halaman-halaman ini `ApplyLabels()` berjalan sebelum ViewModel ada. Jangan
    pernah membaca ViewModel dari dalamnya.
  - **TERVERIFIKASI**: dibuka lewat `Ctrl+,`, tidak crash, tidak ada event WER baru.

- [x] 37. Discord Rich Presence
  - [x] 37.1 Implementasikan klien IPC Discord + service pengamat player
    - **SELESAI**: protokol IPC Discord ditulis langsung (named pipe `discord-ipc-0..9`, frame
      `[int32 opcode][int32 length][utf8 json]` little-endian) — **tanpa library pihak ketiga**
      sesuai AGENTS.md. `KasetWin.Core/Services/RichPresence/DiscordActivity.cs` = pemetaan murni
      dari state player → payload (bisa dites headless); `KasetWin.Platform/RichPresence/DiscordRpcClient.cs`
      = koneksi pipa; `KasetWin.App/Hosting/RichPresenceService.cs` = pengamat player.
    - **Detail yang gampang salah**: (a) timestamp mulai harus `now - progress`, kalau `now` saja
      penghitung Discord mengulang dari nol tiap seek; (b) `details`/`state` di luar 2–128 karakter
      **ditolak diam-diam** oleh Discord — gejalanya "presence-nya nggak muncul" tanpa error apa pun;
      (c) update tidak boleh mengikuti `Progress` (tik ~1 detik) karena Discord membatasi ~1 per 15 detik.
    - **KOREKSI DESAIN (2026-07-22).** Versi pertama mewajibkan tiap pengguna mendaftarkan aplikasi
      Discord sendiri lalu menempel Application ID-nya. Itu salah: Application ID **bukan rahasia**
      (ikut terkirim di tiap payload presence dan bisa dilihat siapa saja) — yang rahasia adalah
      *Client Secret*, dan jalur IPC lokal ini tidak memakainya sama sekali. Mewajibkan setiap
      pengguna mampir ke Developer Portal dulu sama saja dengan meniadakan fitur ini bagi hampir
      semua orang. Sekarang: **satu ID bersama** di `Hosting/DiscordRichPresenceOptions.cs`, pengguna
      cukup menyalakan satu toggle. Kolom Application ID tetap ada tetapi turun jadi override
      opsional di bagian "Lanjutan".
    - **TINDAKAN YANG MASIH DIBUTUHKAN**: `DiscordRichPresenceOptions.DefaultApplicationId` masih
      kosong. Pemilik repo perlu membuat satu aplikasi di Discord Developer Portal (beri nama
      **"Kaset"** — nama itu yang muncul sebagai "Listening to …"), lalu menempel Application ID-nya
      ke konstanta tersebut. Sampai itu dilakukan, toggle-nya tidak berfungsi dan kartu Pengaturan
      menjelaskan kenapa.
    - Mati secara default: menyiarkan apa yang didengar ke profil publik adalah pilihan privasi.
    - _Requirements: 41.1, 41.2, 41.3, 41.4, 41.5, 41.6, 41.7_
  - [x] 37.2 Test pemetaan aktivitas Discord
    - **SELESAI**: `DiscordActivityBuilderTests.cs` — 12 test, termasuk properti 100 iterasi bahwa
      hasil clamp selalu masuk rentang 2–128 karakter.
    - _Requirements: 41.3, 41.4, 41.5_

- [x] 38. Pintasan global + persistensi geometri jendela
  - [x] 38.1 Implementasikan `GlobalHotkeys` (RegisterHotKey + subclass WM_HOTKEY)
    - **SELESAI**: `Ctrl+Alt+↓/→/←/↑`. Kombinasi sengaja dipilih yang jarang dipakai — `RegisterHotKey`
      memberi satu kombinasi ke **satu proses saja** se-sistem, jadi kombinasi populer berisiko
      merebut pintasan editor/browser pengguna atau justru gagal diam-diam. Kegagalan per-kombinasi
      ditoleransi: tiga dari empat tetap jalan kalau satu sudah dipakai aplikasi lain. **Mati secara
      default** dan bisa dinyalakan/dimatikan tanpa restart.
    - **TERVERIFIKASI MANUAL (2026-07-22)**: `Ctrl+Alt+→` dari aplikasi lain (Kaset di latar)
      memajukan lagu (langkah 53) — OK.
    - _Requirements: 42.1, 42.2, 42.3, 42.4_
  - [x] 38.2 Simpan & pulihkan geometri jendela
    - **SELESAI**: disimpan saat `AppWindow.Closing`, dipulihkan di `MainWindowLayout.Configure`.
      Dua penjagaan yang penting: (a) **tidak** menyimpan saat mode mini player / maximized /
      minimized — kalau tidak, shell penuh akan dibuka lagi seukuran mini player 400×150;
      (b) geometri tersimpan divalidasi terhadap `DisplayArea.FindAll()`, jadi monitor yang dicabut
      tidak membuat jendela terbuka di luar layar (kondisi yang tak bisa dipulihkan pengguna biasa).
    - _Requirements: 42.5, 42.6, 42.7_

### PERBAIKAN DARI PUTARAN UJI MANUAL 2 (2026-07-22)

Semua berasal dari `docs/manual-test-checklist.md` putaran 2. Nomor dalam kurung = nomor langkah uji.

- [x] 39. Otoritas antrean saat berpindah track (#65b)
  - **GEJALA**: memutar album lalu menekan Next membuat antrean berubah jadi mix, Previous mati, dan
    track semula tidak bisa diputar ulang.
  - **SEBAB**: `_expectedVideoId` — penjaga yang seharusnya mengabaikan `STATE_UPDATE` untuk video lain
    selama perpindahan — dilepas di blok `finally` begitu `LoadVideoAsync` **kembali**. Padahal
    panggilan itu hanya *memulai* navigasi; YouTube Music masih melaporkan halaman lama beberapa detik
    setelahnya. Tiap laporan yang lolos ditambahkan ke antrean sebagai riwayat efemeral
    (`AppendDeduplicated`), lalu indeks aktif dipindah ke situ — itulah "antrean jadi mix".
  - **PERBAIKAN**: penjaga baru dilepas saat videoId yang ditunggu **benar-benar terlihat**, bukan saat
    panggilan navigasi selesai. Load yang gagal tetap melepas penjaga (lewat `catch`), dan ada batas
    `MaxIgnoredUpdatesDuringLoad` (30 laporan) supaya navigasi yang tidak pernah mendarat tidak
    mengunci player dari kenyataan selamanya. Dihitung, bukan diukur waktu, supaya deterministik.
  - **CATATAN TEST**: dua test lama (`StateUpdate_ForAutoplayTrack_…`, `StateUpdate_ForQueuedAutoAdvancedTrack_…`)
    ternyata mensimulasikan laporan asing yang datang **sebelum** track termuat pernah melapor —
    skenario yang justru sedang ditolak. Keduanya diperbaiki agar menyertakan laporan settle dulu.
  - _Requirements: 2.4, 2.5, 2.6_

- [x] 40. Timer tidur "akhir lagu ini" benar-benar menghentikan pemutaran (#15)
  - **GEJALA**: ikon timer padam (jadi timer terpakai) tapi musik lanjut ke lagu berikutnya.
  - **SEBAB GANDA**: (a) jalur timer memanggil `PlayerService.PauseAsync()`, yang **return lebih awal
    saat `IsPlaying` sudah false** — dan itu justru keadaan normal ketika event track-ended tiba, jadi
    tidak ada jeda yang benar-benar dikirim; (b) YouTube Music bereaksi pada event `ended` yang sama
    dan memulai lagu berikutnya sendiri, sehingga satu jeda pun bisa mendarat di video yang sudah
    ditinggalkan.
  - **PERBAIKAN**: jeda dikirim langsung ke controller (bukan lewat `PauseAsync` yang bersyarat), plus
    flag `_sleepStopEnforced` yang menekan balik setiap laporan "sedang memutar" ke posisi jeda dan
    menahan adopsi antrean — sampai pengguna sendiri menekan play/next/prev atau memuat lagu lain.
  - _Requirements: 39.4, 39.5_

- [x] 41. Media key tidak berfungsi (#64)
  - **SEBAB**: bukan di SMTC Kaset sama sekali. Chromium di dalam WebView2 menangani tombol media
    perangkat keras sendiri selama halamannya punya media session aktif — dan halaman pemutaran selalu
    punya. Tombolnya tidak pernah sampai ke registrasi SMTC milik Kaset. Rute mana yang dipakai Windows
    bergantung urutan aktivasi, itulah kenapa gejalanya kadang muncul kadang tidak, bukan mati total.
  - **PERBAIKAN**: `--disable-features=HardwareMediaKeyHandling` pada `AdditionalBrowserArguments` di
    `WebViewEnvironmentProvider`. Hanya penanganan tombolnya yang dimatikan; media session halaman
    tetap ada, jadi pemutaran dan DRM tidak tersentuh.
  - _Requirements: 10.2, 37.7_

- [x] 42. Baris artis kosong pada peluncuran `kaset://` (#73)
  - **SEBAB**: `PlayAsync(videoId)` membangun `Song` hanya dari videoId, dan `ResolveTrackFromMessage`
    selalu memenangkan entri antrean atas laporan halaman — termasuk daftar artis yang kosong.
  - **PERBAIKAN**: artis dari halaman dipakai **hanya untuk mengisi kekosongan**, tidak pernah menimpa
    artis milik entri antrean yang sungguhan (yang punya `Id` dan bisa dinavigasi).
  - _Requirements: 33.1, 2.6_

- [x] 43. Ikon glyph dibacakan Narrator sebagai kode (#20, #22, #28)
  - **SEBAB**: `FontIcon` di dalam tombol tidak pernah disembunyikan dari pohon aksesibilitas, jadi
    Narrator masuk ke dalam tombol dan membacakan glyph Segoe Fluent — yang merupakan codepoint
    private-use, sehingga terdengar sebagai kode acak setelah nama tombolnya.
  - **PERBAIKAN**: satu implicit style `FontIcon` di `App.xaml` menyetel `AccessibilityView="Raw"`
    se-aplikasi. Dipilih daripada menambah atribut di puluhan tempat justru supaya ikon baru di masa
    depan tidak bisa memunculkan lagi masalahnya karena lupa satu atribut.
  - _Requirements: 38.1, 38.2_

- [x] 44. Sisa waktu timer tidur terlihat tanpa hover (#11, #12)
  - **PERBAIKAN**: badge angka menit di atas ikon bulan (detik pada menit terakhir, "♪" untuk mode
    akhir-lagu). Tooltip saja tidak cukup: ia menuntut hover, dan pada putaran 2 tooltipnya sendiri
    tidak menampilkan sisa waktu.
  - _Requirements: 39.2_

### PERBAIKAN PUTARAN UJI 3 (2026-07-23) — ditemukan dengan MENJALANKAN aplikasi

Seluruh isi seksi ini lolos dari build hijau dan 469 test. Tidak satu pun bisa ditangkap CI; semuanya
muncul begitu aplikasinya benar-benar dibuka dan dipakai.

- [x] 45. **Aplikasi crash beberapa detik setelah dibuka** (cacat paling parah di sesi ini)
  - **SEBAB**: `MainWindowLayout.IsFrameVisibleOnAnyDisplay` meng-iterasi `DisplayArea.FindAll()`, dan
    proyeksi CsWinRT-nya melempar `InvalidCastException` (`IReadOnlyListImpl<T>.GetEnumerator`).
    Jalannya di dalam konstruktor `MainWindow`, jadi aplikasi tidak pernah selesai dibuka.
  - **KENAPA SULIT DILACAK**: pemicunya **data, bukan kode** — hanya menyala kalau sudah ada geometri
    jendela tersimpan. Karena itu commit-commit lama pun ikut crash saat di-bisect, sehingga sempat
    terlihat seolah bukan ulah perubahan terbaru.
  - **PERBAIKAN**: `DisplayArea.GetFromRect(..., Nearest)` — satu panggilan, tanpa proyeksi koleksi;
    `catch` diperlebar karena tidak ada kegagalan di pemeriksaan kenyamanan ini yang lebih berharga
    daripada jendela yang mau terbuka.
  - **PELAJARAN YANG DIKODEKAN**: aplikasi **tidak punya** `Application.UnhandledException` sama
    sekali, jadi tiap crash hanya menyisakan `0xc000027b` di Event Log — kode yang sama persis untuk
    semua penyebab. Sekarang exception lengkap ditulis ke `crash.log` di LocalState. Setelah dipasang,
    satu kali jalan langsung memberi stack persisnya.
  - _Requirements: 42.5, 42.6_

- [x] 46. **Lirik tersinkron tidak pernah jalan di dalam aplikasi**
  - **SEBAB**: InnerTube menolak konteks klien seluler yang membawa `SAPISIDHASH` beroriginkan web.
    Eksplorasi lewat `ApiExplorer` (tanpa cookie) berhasil, aplikasi yang sudah login gagal 100%.
  - **PERBAIKAN**: permintaan Android dikirim anonim. Terverifikasi hidup: `timed lines=63
    credit=LyricFind`. Lihat ADR 0005 keputusan 6 — **jangan dihapus**.
  - **EFEK SAMPING YANG TERJELASKAN**: label "LRCLib" yang dikeluhkan ternyata jujur — YouTube Music
    hanya pernah bisa mengembalikan teks polos, jadi hasil tersinkron LRCLib memenangkan tier.
  - _Requirements: 17.1, 17.4_

- [x] 47. **Kredit lisensor pindah ke label sumber** (ADR 0005 keputusan 7)
  - Dulu disisipkan sebagai baris lirik palsu; dengan gulir gaya Apple Music baris itu ikut disorot
    dan meluncur ke puncak panel. Sekarang `Sumber: YouTube Music — LyricFind`, selalu terlihat.

- [x] 48. **Regresi player bar** (disebabkan oleh perbaikan teks terpotong #42)
  - Kolom judul dipatok 520px, sehingga tombol suka menempel di tepi kolom — terdampar di tengah bar.
    Barisnya kini memeluk kontennya dan tetap di tengah, kolomnya tetap star agar masih bisa menyusut
    di lebar minimum 980px.

- [x] 49. **Gulir lirik gaya Apple Music** — baris aktif naik ke atas, baris berikutnya menyusul.
  - Target gulir diukur terhadap **isi** yang digulir, bukan viewport: mengukur ke viewport lalu
    menjumlahkannya dengan `VerticalOffset` menggabungkan dua angka dari dua momen berbeda, dan saat
    animasi sebelumnya masih jalan hasilnya kelewat naik sehingga baris aktif terpotong header.
  - **SISA**: baris sebelumnya masih tampak separuh — tinggi baris dihitung sebelum teks yang melipat
    selesai di-layout. Diterima apa adanya untuk sekarang, belum diperbaiki.

- [x] 50. **Kata sambung "dan" terbaca sebagai nama artis**
  - `Tenxi, Anangga dan Suisei` tampil sebagai tiga artis dengan artis ketiga bernama "dan Suisei".
    Pembedanya: YouTube menulis nama artis dengan kapital, tetapi kata sambung yang ia bangkitkan
    sendiri selalu huruf kecil — sehingga "Dan Auerbach" dan "Simon and Garfunkel" terbukti aman.

- [x] 51. **Sumber lirik di Settings** — YouTube Music masuk daftar, plus keterangan bahwa isinya
  dari LyricFind/Musixmatch (YouTube yang menentukan per lagu) dan **tidak semua lagu tersinkron**.
  "Otomatis" tetap default: memaksa ke YouTube Music akan menghapus cadangan LRCLib/NetEase diam-diam.

### PERBAIKAN PUTARAN UJI 5 (2026-07-23)

Dari seksi F `docs/manual-test-checklist.md`. Dikerjakan paralel oleh tiga subagent + integrasi.

- [x] 52. **Narrator membacakan dump properti** (#20/23/28/91/92)
  - **SEBAB**: `Song` dan `SearchSuggestion` adalah `record` tanpa `ToString()` sendiri. `ToString()`
    bawaan record mencetak SEMUA properti — dan itulah yang dibacakan Narrator setiap kali sebuah
    objek sampai ke nama aksesibilitas tanpa nama miliknya sendiri. Terdengar sebagai "track id,
    song id, dsb".
  - **PERBAIKAN**: `ToString()` manusiawi ("Judul — Artis"). Satu perubahan menutup seluruh kelas
    masalah, alih-alih menambal tiap list dan template satu per satu.
  - **CATATAN**: menandai `FontIcon` dekoratif (putaran 3) hanya menghilangkan bunyi kode glyph;
    ia justru membuka fallback ini. Dua gejala, satu rantai.
  - _Requirements: 38.1_

- [x] 53. **Tombol sumber ringkas tidak menyebut sumber aktif** (#29)
  - Tombol bundar itu `Button`, bukan `ToggleButton`, jadi tidak ada on/off untuk dibacakan. Namanya
    kini menyebut sumber aktif + apa yang terjadi bila diklik, dan diperbarui lewat callback
    `IsChecked` sehingga ikut berubah, bukan hanya saat ganti bahasa. Dua tombol segmennya ternyata
    sudah benar sejak awal.

- [x] 54. **`kaset://` tidak melengkapi metadata** (#73/90) — dua sebab terpisah
  - Panel antrean merender `queue.Tracks[CurrentIndex]`, bukan `CurrentTrack`; pengayaan hanya
    mengisi album/artis/thumbnail dan **tidak pernah judul**, dan hasil resolusi halaman hanya masuk
    ke `CurrentTrack`. Kini `TryEnrichTrack` juga mengisi judul & durasi **yang kosong** (tidak
    pernah menimpa yang sudah ada) dan `HandleStateUpdate` menulis balik ke entri antrean.
  - Baris album hilang karena `AlbumFromRenderer` hanya mendapat browse id tanpa judul. Ditambal
    lewat `TrackMetadataEnricher` headless yang menyusulkan judul album.
  - _Requirements: 33.1, 2.6_

- [x] 55. **Next cepat menghentikan pemutaran** (#77b) — lihat ADR 0006
  - `LoadTrackAsync` tidak diserialisasi; pemuatan lama bisa menyelesaikan `finally`-nya setelah yang
    baru memasang penjaga, meninggalkan penjaga menunjuk track mati. Kini pakai tiket generasi, dan
    kegagalan pemuatan yang tersalip **tidak** melepas penjaga.

- [x] 56. **Lagu yang sama tidak mau diputar setelah internet kembali** (#111b) — lihat ADR 0006
  - `LoadVideoAsync` keluar lebih awal saat videoId sama. Idempotensi itu benar untuk event otomatis,
    salah untuk niat pengguna. Ditambah `forceReload`, hanya dipakai jalur yang berasal dari klik
    pengguna. **Konsekuensi disengaja**: mengklik lagu yang sedang diputar kini mengulanginya.
  - **TIDAK diselesaikan**: pemutaran tidak melanjut *otomatis* saat Wi-Fi kembali.

- [x] 57. **Ikon tombol thumbnail taskbar kosong** (#88c)
  - **SEBAB**: konstanta `THUMBBUTTONMASK` salah nilai — `THB_ICON` ditulis `0x8` (itu `THB_FLAGS`),
    `THB_TOOLTIP` `0x20` (bukan bit yang terdefinisi). `dwMask` jadi `0x2C`, yang tidak pernah
    menyalakan `THB_ICON`: shell diberi tahu field ikonnya tidak valid. Diperbaiki ke nilai
    `shobjidl_core.h`. Berkas `kaset.ico` terbukti sudah ter-deploy — tidak ada kode yang ditambahkan
    untuk itu.
  - Sekalian: tooltip tombolnya tadinya literal bahasa Indonesia, kini lewat `UiStrings`.

- [x] 58. **Like/love tidak sinkron** (parsial, jujur dicatat)
  - Player bar menulis ke `ILikeStateStore` **setelah** round-trip server, sedangkan permukaan lain
    menulis optimistis lebih dulu — jendela nyata di mana kedua tampilan berbeda. Kini optimistis
    juga, dan mengembalikan nilai lama ke store bila gagal. Langganan `Changed` juga dipasang ulang
    di `OnLoaded`. **Belum terbukti** ini penyebab yang dialami pemilik repo.

- [x] 59. **Antrean menampilkan "Sudah diputar"** (#78)
  - Core tidak diubah sama sekali: `QueueService` tidak pernah membuang track yang sudah diputar dan
    `QueueViewModel` sudah punya `History`/`NowPlaying`/`UpNext`. Yang hilang murni render.

- [x] 60. **Bunyi + toast timer tidur untuk KEDUA mode**
  - `SleepTimer` dapat event `Expired` yang hanya menyala saat benar-benar habis (tidak saat
    dibatalkan). Sebelumnya mode "akhir lagu ini" dieksekusi di `PlayerService`, jauh dari UI, jadi
    mode itu berhenti tanpa toast maupun bunyi. Pengumumannya kini hidup di satu tempat.

- [x] 61. **Keterangan sumber lirik dipangkas** (#121) — lisensornya sudah tampil di tiap lirik.

### PERBAIKAN PUTARAN UJI 6 & 7 (2026-07-23) — pelajarannya: klaim putaran 5 sebagian besar salah

Seksi G disusun untuk menguji perbaikan putaran 5. Saat akhirnya dijalankan dengan tangan, **enam dari
klaim itu terbukti tidak benar** — dan tiga di antaranya adalah fitur yang tidak pernah berjalan
sekali pun. Semua sebab di bawah dibuktikan (log runtime atau jalur kode yang ditunjuk), bukan
ditebak.

- [x] 62. **Antrean melompat belasan–puluhan lagu setelah timer tidur** (#81)
  - **SEBAB**: penjaga sleep-stop di `HandleStateUpdate` hanya mencegat laporan ber-`IsPlaying=true`.
    Tiap pause yang Kaset kirim membuat halaman melapor dirinya *paused* pada video yang sudah dia
    pindahi sendiri, dan laporan paused itu jatuh lurus ke adopsi antrean. YouTube menyusuri rantai
    autoplay-nya, kita mem-pause tiap satu, dan tiap pause menambah track + memajukan indeks.
  - **CATATAN**: komentar di kodenya sudah mengklaim "ia juga menekan adopsi antrean" — perilaku yang
    tidak pernah ditulis. Dokumentasi yang mendahului kode, dan tidak ada yang mengecek.
  - **PERBAIKAN**: penyaringan berdasarkan videoId, bukan status main. 3 test.

- [x] 63. **Setiap panggilan API dari thread non-UI gagal untuk pengguna login**
  - **SEBAB**: `CoreWebView2.CookieManager` objek COM dengan afinitas thread; tiap request InnerTube
    menandatangani diri dengan cookie. `COMException` di 100% panggilan latar — terbukti dari
    `diag.log` setelah kedua fetcher diinstrumentasi.
  - **DAMPAK**: album & sampul hilang di `kaset://` (#73/#115/#129) — bukan cacat parser seperti
    dikira putaran 5. Enrichment menelan semua kegagalan by design, jadi gagalnya senyap.
  - **PERBAIKAN**: `UiThreadCookieSource` (App layer, bukan Platform — batas WinUI-free dijaga).
  - _Lihat ADR 0007._

- [x] 64. **Timer tidur tidak pernah berbunyi maupun memunculkan toast** (#81/#137)
  - **SEBAB 1**: `PlayerBar` berlangganan `Expired` di konstruktor; `OnLoaded` melepas `StateChanged`
    **dan** `Expired` tapi memasang ulang hanya `StateChanged`. `Loaded` datang beberapa detik
    setelah konstruktor, jadi handler copot sebelum dipakai sekali pun. Tugas 60 tidak pernah hidup.
  - **SEBAB 2**: chime dijaga `IsPlaying: true`, padahal di akhir lagu `IsPlaying` sudah false —
    fakta yang didokumentasikan sendiri di tugas 60 soal `PauseAsync`.
  - **PERBAIKAN**: kedua handler dipasang & dilepas berpasangan; penjaga `IsPlaying` dihapus.

- [x] 65. **Klik sumber yang sedang aktif memunculkan placeholder "Coming soon"**
  - **SEBAB**: `NavigateToTag` menggabung "tag punya halaman nyata" dan "belum di halaman itu" dalam
    satu `if`; saat sudah di halaman itu seluruh blok dilewati dan jatuh ke `PlaceholderPage`.
  - **CATATAN**: mengenai **Ctrl+,** di halaman Settings juga. Satu perbaikan menutup keduanya.

- [x] 66. **Contekan pintasan memenuhi layar** (#74/#100, gagal 3 putaran)
  - **SEBAB**: anggaran `tinggiJendela − 220` selalu habis karena isinya butuh ±760 px → dialog tetap
    setinggi jendela − 60. `ContentDialog.MaxHeight` tidak berpengaruh: template-nya mengikat border
    ke ThemeResource, bukan ke properti dialog (diverifikasi dari XBF WindowsAppRuntime).
  - **BELUM PASTI**: "tidak bisa digulir" tidak terbukti dari kode; scrollbar kini dibuat terlihat.

- [x] 67. **Ikon thumbnail taskbar nyaris tak terlihat** (#133)
  - **SEBAB**: glyph *outline* dirender 32 px lalu diperkecil shell ke 16 → stroke sub-piksel, tanpa
    kontras. `GetHicon` alpha diuji dan **bukan** penyebabnya.
  - **PERBAIKAN**: bentuk vektor padat + halo gelap, ukuran dari `SM_CXSMICON` per-DPI,
    `CreateDIBSection` + premultiplied alpha; `THB_ICON` hanya diset bila ikonnya benar-benar ada.

- [x] 68. **Lirik & pemutaran tidak pulih setelah internet kembali** (#131)
  - **SEBAB (a)**: hasil kosong dicatat sebagai "sudah dimuat" di `LyricsViewModel`, jadi percobaan
    berikutnya dilewati sebagai redundan. `LyricsService` sendiri **tidak** menyimpan kegagalan ke
    cache — hipotesis itu diuji dan gugur; `YouTubeMusicLyricsProvider` tetap dikeraskan.
  - **SEBAB (b)**: terbukti dari jejak Serilog — klik terjadi saat jaringan **masih** mati, jadi
    pemuatan paksa (ADR 0006) menghancurkan halaman yang masih memutar dari buffer dan bernavigasi
    ke jaringan mati. Kegagalannya diingat, tapi hanya untuk menolong klik berikutnya.
  - **PERBAIKAN**: hasil kosong tidak lagi mengunci; controller memuat ulang saat konektivitas
    kembali, **tanpa** melanjutkan pemutaran sendiri.

- [ ] 69. **Race laten di `LoadTrackAsync`** — `_expectedVideoId`/`CurrentTrack` diset di luar
  `_loadGate` dan tidak atomik dengan `Interlocked.Increment(_loadGeneration)`. Kelas bug yang sama
  dengan ADR 0006, di baris berbeda. **Tidak terbukti** menyebabkan gejala; jendelanya nanodetik
  sehingga tidak bisa dibuatkan test deterministik. Dicatat, sengaja belum diubah.

## Notes

- Sub-tugas bertanda `*` bersifat opsional (test atau fitur fase lanjutan) dan **tidak** diimplementasikan otomatis; dapat dilewati untuk MVP yang lebih cepat.
- Setiap tugas merujuk requirement spesifik untuk keterlacakan; property test merujuk properti korektnes pada `design.md`.
- Property test memakai **CsCheck/FsCheck** (library), **minimum 100 iterasi**, **satu properti = satu test**, dengan komentar `// Feature: kaset-winui3, Property N: {judul}`.
- Fixtures parser (Tugas 5.1) **disanitasi**: tidak ada cookie/token/SAPISID/PII nyata — hanya placeholder seperti `"REDACTED"`/`"mock-token"`.
- Checkpoint memastikan validasi incremental sebelum melanjutkan.
- `KasetWin.Core` tidak bergantung pada WinUI sehingga parser & logika queue/auth/player dapat diuji headless dan dipakai ulang oleh API Explorer.

### Out of Scope (Ditunda — Req 36, tidak dibuat task)

Fitur berikut **sengaja tidak dibuat task** dan dicatat sebagai future work; seam-nya dipertahankan agar penambahan kemudian tidak memecah kontrak publik.

> **Diperbarui 2026-07-22.** Tiga item di bawah ternyata sudah dikerjakan di luar jalur task dan
> tidak lagi "ditunda" — ditandai ✅ agar daftar ini tidak menyesatkan pembaca berikutnya.

- ✅ **Equalizer** (Req 36.1) — **SUDAH ADA**: ekualiser 9-band + preset, diterapkan ke keluaran WebView2 lewat Web Audio; UI di SettingsPage.
- **Haptic feedback** (Req 36.2) — tidak ada padanan native Windows.
- ✅ **Web Extensions** (Req 36.3) — **SUDAH ADA**: `ExtensionsService` memuat ekstensi unpacked ke profil WebView2 pemutaran; uBlock Origin diunduh & diperbarui otomatis.
- **AppleScript penuh** (Req 36.4) — digantikan Protocol activation `kaset://` + CLI args (Tugas 26).
- ✅ **Auto-update** MSIX/Velopack (Req 36.5) — **SUDAH ADA**: `Updates/AppUpdateService.cs` (Velopack + GithubSource), cek/unduh di startup, notifikasi in-app "Mulai ulang". Hanya aktif pada build terinstal.
- **Seluruh fitur AI/Apple Intelligence** (Req 36.6) — Command Bar AI, penjelasan lirik AI, analisis antrian AI, refine playlist AI.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1", "3.1", "4.1", "4.2", "5.1"] },
    { "id": 2, "tasks": ["2.2", "5.2", "6.1", "7.1", "8.1", "9.1", "10.1", "13.2"] },
    { "id": 3, "tasks": ["1.3", "5.3", "5.4", "5.5", "5.6", "5.7", "5.8", "5.9", "6.2", "8.2", "9.2", "11.1", "13.1", "13.3", "13.4", "2.3", "2.4", "2.5", "4.3", "4.4", "3.2", "13.6"] },
    { "id": 4, "tasks": ["7.2", "7.3", "7.4", "8.3", "9.3", "12.1", "14.1", "14.2", "15.1", "5.10", "5.11", "5.12", "5.13", "5.14", "5.15", "5.16", "6.3", "6.4", "10.2", "10.3", "10.4", "10.5", "10.6", "10.7", "11.2", "11.3", "11.4", "11.5", "11.6", "11.7", "11.8", "8.4", "8.5", "9.4", "9.5", "13.5"] },
    { "id": 5, "tasks": ["7.5", "7.6", "7.7", "16.1", "14.3", "14.4", "14.5", "14.6", "14.7", "14.8", "14.9", "14.10", "15.2", "12.2"] },
    { "id": 6, "tasks": ["14.11", "14.12", "16.2"] },
    { "id": 7, "tasks": ["18.1", "19.1", "20.1", "22.1", "23.1", "24.1", "25.1", "25.2", "25.3", "26.1", "27.1", "28.1"] },
    { "id": 8, "tasks": ["18.2", "19.2", "20.2", "22.2", "26.2"] }
  ]
}
```
