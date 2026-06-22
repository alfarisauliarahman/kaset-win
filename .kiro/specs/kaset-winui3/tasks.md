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

- [ ] 21. (Fase Lanjutan) Scrobbling Last.fm
  - [ ]* 21.1 Implementasikan scrobble threshold + proxy + antrian persisten
    - Antrikan scrobble pada ≥50% atau ≥240s; komunikasi via proxy (tanpa secret di binary); antrian FIFO persisten saat offline; kredensial di Credential_Store
    - _Requirements: 28.1, 28.2, 28.3, 28.4_
  - [ ]* 21.2 Property test ambang scrobble
    - **Property 38: Ambang scrobble**
    - **Validates: Requirements 28.1**
    - _Properties: 38_
  - [ ]* 21.3 Property test round-trip antrian scrobble persisten
    - **Property 39: Round-trip antrian scrobble persisten**
    - **Validates: Requirements 28.3**
    - _Properties: 39_

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

## Notes

- Sub-tugas bertanda `*` bersifat opsional (test atau fitur fase lanjutan) dan **tidak** diimplementasikan otomatis; dapat dilewati untuk MVP yang lebih cepat.
- Setiap tugas merujuk requirement spesifik untuk keterlacakan; property test merujuk properti korektnes pada `design.md`.
- Property test memakai **CsCheck/FsCheck** (library), **minimum 100 iterasi**, **satu properti = satu test**, dengan komentar `// Feature: kaset-winui3, Property N: {judul}`.
- Fixtures parser (Tugas 5.1) **disanitasi**: tidak ada cookie/token/SAPISID/PII nyata — hanya placeholder seperti `"REDACTED"`/`"mock-token"`.
- Checkpoint memastikan validasi incremental sebelum melanjutkan.
- `KasetWin.Core` tidak bergantung pada WinUI sehingga parser & logika queue/auth/player dapat diuji headless dan dipakai ulang oleh API Explorer.

### Out of Scope (Ditunda — Req 36, tidak dibuat task)

Fitur berikut **sengaja tidak dibuat task** dan dicatat sebagai future work; seam-nya dipertahankan agar penambahan kemudian tidak memecah kontrak publik:

- **Equalizer** (Req 36.1) — tidak ada padanan langsung; preferensi audio seam tetap ada di SettingsService.
- **Haptic feedback** (Req 36.2) — tidak ada padanan native Windows.
- **Web Extensions** (Req 36.3) — menunggu evaluasi padanan Windows.
- **AppleScript penuh** (Req 36.4) — digantikan Protocol activation `kaset://` + CLI args (Tugas 26).
- **Auto-update** MSIX/Velopack (Req 36.5) — future work.
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
    { "id": 7, "tasks": ["18.1", "19.1", "20.1", "21.1", "22.1", "23.1", "24.1", "25.1", "25.2", "25.3", "26.1", "27.1", "28.1"] },
    { "id": 8, "tasks": ["18.2", "19.2", "20.2", "21.2", "21.3", "22.2", "26.2"] }
  ]
}
```
