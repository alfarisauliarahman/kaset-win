namespace KasetWin.App.Localization;

/// <summary>
/// Labels for strings the app hardcodes itself (page titles, section headers, buttons), following
/// the same content-language setting that pins <c>hl</c> on API requests — so the shell never
/// shows English chrome in Indonesian mode (or vice versa). Pages are recreated on navigation, so
/// reading these in the page constructor is enough to stay current after a language switch.
/// </summary>
internal static class UiStrings
{
    /// <summary>Whether the app language is Indonesian (the default).</summary>
    internal static bool IsIndonesian => ViewModels.SettingsViewModel.LoadLanguageSetting() != "en";

    // ── Page titles ────────────────────────────────────────────────────────────────────────────

    internal static string HomeTitle => IsIndonesian ? "Beranda" : "Home";
    internal static string ExploreTitle => IsIndonesian ? "Jelajahi" : "Explore";
    internal static string LibraryTitle => IsIndonesian ? "Koleksi" : "Library";
    internal static string LikedSongsTitle => IsIndonesian ? "Lagu yang disukai" : "Liked Songs";
    internal static string HistoryTitle => IsIndonesian ? "Riwayat" : "History";
    internal static string PodcastsTitle => IsIndonesian ? "Podcast" : "Podcasts";
    internal static string SettingsTitle => IsIndonesian ? "Pengaturan" : "Settings";

    // ── Search ─────────────────────────────────────────────────────────────────────────────────

    internal static string SearchTopResults => IsIndonesian ? "Hasil teratas" : "Top Results";
    internal static string SearchArtists => IsIndonesian ? "Artis" : "Artists";
    internal static string SearchAlbums => IsIndonesian ? "Album" : "Albums";
    internal static string SearchSongs => IsIndonesian ? "Lagu" : "Songs";
    internal static string SearchMusicVideos => IsIndonesian ? "Video musik" : "Music Videos";
    internal static string SearchPlaylists => IsIndonesian ? "Playlist" : "Playlists";
    internal static string SearchPodcasts => IsIndonesian ? "Podcast" : "Podcasts";
    internal static string SearchEpisodes => IsIndonesian ? "Episode" : "Episodes";
    internal static string SearchPlaceholder => IsIndonesian ? "Cari lagu, album, artis, playlist" : "Search songs, albums, artists, playlists";
    internal static string SearchNoResults => IsIndonesian ? "Tidak ada hasil" : "No results found";

    // ── Shared chrome ──────────────────────────────────────────────────────────────────────────

    internal static string LoadMore => IsIndonesian ? "Muat lagi" : "Load more";
    internal static string PlayLabel => IsIndonesian ? "Putar" : "Play";
    internal static string DeleteLabel => IsIndonesian ? "Hapus" : "Delete";
    internal static string ComingSoon => IsIndonesian ? "Segera hadir" : "Coming soon";
    internal static string SignIn => IsIndonesian ? "Masuk" : "Sign in";
    internal static string SignOut => IsIndonesian ? "Keluar" : "Sign out";
    internal static string LoginSuccessTitle => IsIndonesian ? "Berhasil masuk" : "Signed in";
    internal static string LoginFailedTitle => IsIndonesian ? "Gagal masuk" : "Sign-in failed";
    internal static string LoginSuccessGeneric => IsIndonesian ? "Kamu sudah masuk ke YouTube Music." : "You're signed in to YouTube Music.";
    internal static string LoginSuccessNamed(string name) => IsIndonesian ? $"Masuk sebagai {name}." : $"Signed in as {name}.";
    internal static string LoginFailedSubtitle => IsIndonesian ? "Tidak bisa memasukkanmu. Silakan coba lagi." : "Couldn't sign you in. Please try again.";

    // ── Home ───────────────────────────────────────────────────────────────────────────────────

    internal static string HomeFavorites => IsIndonesian ? "Favorit" : "Favorites";
    internal static string MenuShare => IsIndonesian ? "Bagikan" : "Share";
    internal static string MenuAddToFavorites => IsIndonesian ? "Tambahkan ke Favorit" : "Add to Favorites";
    internal static string MenuRemoveFromFavorites => IsIndonesian ? "Hapus dari Favorit" : "Remove from Favorites";

    // ── Explore ────────────────────────────────────────────────────────────────────────────────

    internal static string ExploreNewReleases => IsIndonesian ? "Rilis baru" : "New Releases";
    internal static string ExploreCharts => IsIndonesian ? "Tangga lagu" : "Charts";
    internal static string ExploreMoodsGenres => IsIndonesian ? "Suasana & genre" : "Moods & Genres";

    // ── Library section headers ────────────────────────────────────────────────────────────────

    internal static string LibraryPlaylists => IsIndonesian ? "Playlist" : "Playlists";
    internal static string LibrarySongs => IsIndonesian ? "Lagu" : "Songs";
    internal static string LibraryUploads => IsIndonesian ? "Upload" : "Uploads";
    internal static string LibraryArtists => IsIndonesian ? "Artis" : "Artists";
    internal static string LibraryAlbums => IsIndonesian ? "Album" : "Albums";

    // ── Podcasts ───────────────────────────────────────────────────────────────────────────────

    internal static string MenuSubscribe => "Subscribe";
    internal static string MenuUnsubscribe => IsIndonesian ? "Berhenti subscribe" : "Unsubscribe";

    // ── YouTube source pages ───────────────────────────────────────────────────────────────────

    internal static string YtRecommended => IsIndonesian ? "Direkomendasikan" : "Recommended";
    internal static string YtLike => IsIndonesian ? "Suka" : "Like";
    internal static string YtDislike => IsIndonesian ? "Tidak suka" : "Dislike";
    internal static string YtWatchLater => IsIndonesian ? "Tonton nanti" : "Watch Later";
    internal static string YtComments => IsIndonesian ? "Komentar" : "Comments";
    internal static string YtLoadMoreComments => IsIndonesian ? "Muat komentar lainnya" : "Load more comments";

    // ── Settings ───────────────────────────────────────────────────────────────────────────────

    internal static string SettingsGeneral => IsIndonesian ? "Umum" : "General";
    internal static string SettingsLaunchPageLabel => IsIndonesian ? "Halaman awal" : "Default launch page";
    internal static string SettingsLaunchPageCaption => IsIndonesian ? "Halaman yang dibuka Kaset saat dijalankan." : "The page Kaset opens to on launch.";
    internal static string SettingsCloseBehaviorLabel => IsIndonesian ? "Saat menutup jendela" : "When closing the window";
    internal static string SettingsCloseBehaviorCaption => IsIndonesian
        ? "Kecilkan ke tray membuat musik tetap berjalan di latar; Keluar menutup Kaset sepenuhnya."
        : "Minimize to tray keeps music playing in the background; Quit closes Kaset entirely.";
    internal static string[] CloseBehaviorOptions => IsIndonesian
        ? ["Kecilkan ke tray", "Keluar"]
        : ["Minimize to tray", "Quit"];
    internal static string SettingsThemeLabel => IsIndonesian ? "Tema" : "Theme";
    internal static string SettingsThemeCaption => IsIndonesian ? "Terang, gelap, atau ikuti tema Windows." : "Light, dark, or follow the Windows theme.";
    internal static string SettingsLanguageLabel => IsIndonesian ? "Bahasa" : "Language";
    internal static string SettingsLanguageCaption => IsIndonesian
        ? "Bahasa aplikasi dan konten YT Music (judul section, tanggal, deskripsi)."
        : "App and YT Music content language (section titles, dates, descriptions).";
    internal static string SettingsPlayback => IsIndonesian ? "Pemutaran" : "Playback";
    internal static string SettingsAudioQualityLabel => IsIndonesian ? "Kualitas audio" : "Preferred audio quality";
    internal static string SettingsAudioQualityCaption => IsIndonesian ? "Kualitas streaming yang diminta Kaset bila tersedia." : "Streaming quality Kaset requests when available.";
    internal static string SettingsRememberLabel => IsIndonesian ? "Ingat pengaturan pemutaran" : "Remember playback settings";
    internal static string SettingsRememberCaption => IsIndonesian ? "Pulihkan acak dan ulangi saat diluncurkan lagi." : "Restore shuffle and repeat on next launch.";
    internal static string SettingsLyricsHeader => IsIndonesian ? "Lirik" : "Lyrics";
    internal static string SettingsLyricsSourceLabel => IsIndonesian ? "Sumber lirik" : "Lyrics source";
    internal static string SettingsLyricsSourceCaption => IsIndonesian
        ? "Pilih penyedia lirik yang diutamakan. NetEase bagus untuk lagu Asia/K-pop."
        : "Pick the preferred lyrics provider. NetEase covers Asian/K-pop songs well.";
    internal static string SettingsSyncedLabel => IsIndonesian ? "Lirik sinkron" : "Synced lyrics";
    internal static string SettingsSyncedCaption => IsIndonesian ? "Utamakan lirik sinkron-waktu, fallback ke lirik biasa." : "Prefer time-synced lyrics, falling back to plain lyrics.";
    internal static string SettingsEqualizerHeader => IsIndonesian ? "Ekualiser" : "Equalizer";
    internal static string SettingsEqLink => IsIndonesian ? "Geser slider tetangga ikut bersama" : "Move nearby sliders together";
    internal static string SettingsExtensionsHeader => IsIndonesian ? "Ekstensi (Adblock)" : "Extensions (Adblock)";
    internal static string SettingsExtensionsCaption => IsIndonesian
        ? "Kaset otomatis mengunduh dan memperbarui uBlock Origin untuk profil WebView pemutaran bersama. Folder di bawah tetap tersedia untuk ekstensi unpacked tambahan; mulai ulang Kaset setelah mengubah ekstensi kustom."
        : "Kaset automatically downloads and updates uBlock Origin for the shared playback WebView profile. The folder below is still available for additional unpacked extensions; restart Kaset after changing custom extensions.";
    internal static string SettingsOpenExtensionsFolder => IsIndonesian ? "Buka folder ekstensi" : "Open extensions folder";
    internal static string SettingsRestartKaset => IsIndonesian ? "Mulai ulang Kaset" : "Restart Kaset";

    // About / version.
    internal static string SettingsAboutHeader => IsIndonesian ? "Tentang" : "About";
    internal static string SettingsVersionLabel => IsIndonesian ? "Versi aplikasi" : "App version";
    internal static string SettingsVersionCaption => IsIndonesian ? "Versi Kaset yang sedang berjalan." : "The running version of Kaset.";

    // Auto-update (Velopack) — shown when a newer release has been downloaded in the background.
    internal static string UpdateAvailableTitle => IsIndonesian ? "Pembaruan tersedia" : "Update available";
    internal static string UpdateRestartAction => IsIndonesian ? "Mulai ulang" : "Restart";
    internal static string UpdateReadyMessage(string version) => IsIndonesian
        ? $"Kaset {version} siap dipasang."
        : $"Kaset {version} is ready to install.";

    // Repeat button tooltip — reflects the current repeat mode (Off / All / One).
    internal static string RepeatTooltipOff => IsIndonesian ? "Ulangi: nonaktif" : "Repeat: off";
    internal static string RepeatTooltipAll => IsIndonesian ? "Ulangi semua" : "Repeat all";
    internal static string RepeatTooltipOne => IsIndonesian ? "Ulangi satu" : "Repeat one";

    internal static string SettingsPresetLabel => "Preset";
    internal static string SettingsExtensionsCaption2 => IsIndonesian
        ? "Kaset memuat ekstensi browser unpacked (seperti uBlock Origin) ke WebView pemutarannya — padanan Windows dari ekstensi kelolaan-pengguna di macOS. Tidak ada yang dibundel: taruh tiap ekstensi unpacked (folder berisi manifest.json) ke folder Ekstensi di bawah, lalu mulai ulang Kaset. Ekstensi dimuat ke profil browser bersama, jadi aturan iklan/jaringannya berlaku untuk pemutaran YouTube Music dan YouTube."
        : "Kaset loads unpacked browser extensions (like uBlock Origin) into its playback WebView — the Windows equivalent of the macOS user-managed extensions. Nothing is bundled: put each unpacked extension (a folder containing manifest.json) into the Extensions folder below, then restart Kaset. Extensions are loaded onto the shared browser profile, so their ad/network rules apply to YouTube Music and YouTube playback.";

    // ── Context menus & tooltips (player bar, track rows, headers) ─────────────────────────────

    internal static string MenuPlayNext => IsIndonesian ? "Putar setelah ini" : "Play next";
    internal static string MenuAddToQueue => IsIndonesian ? "Tambahkan ke antrean" : "Add to queue";
    internal static string MenuLikeToggle => IsIndonesian ? "Suka / batal suka" : "Like / unlike";
    internal static string MenuGoToArtist => IsIndonesian ? "Buka halaman artis" : "Go to artist";
    internal static string MenuGoToAlbum => IsIndonesian ? "Buka album" : "Go to album";
    internal static string MenuShufflePlay => IsIndonesian ? "Putar acak" : "Shuffle play";
    internal static string MenuStartMix => IsIndonesian ? "Mulai mix" : "Start mix";
    internal static string MenuSaveToPlaylist => IsIndonesian ? "Simpan ke playlist" : "Save to playlist";
    internal static string MenuPinListenAgain => IsIndonesian ? "Sematkan ke Dengarkan lagi" : "Pin to Listen again";
    internal static string MenuEditPlaylist => "Edit playlist";
    internal static string MenuDeletePlaylist => IsIndonesian ? "Hapus playlist" : "Delete playlist";
    internal static string MenuMarkPlayedToggle => IsIndonesian ? "Tandai telah/belum diputar" : "Mark as played/unplayed";
    internal static string MenuQueueForLater => IsIndonesian ? "Antrean ke Episode untuk Nanti" : "Queue to Episodes for Later";
    internal static string TipMore => IsIndonesian ? "Lainnya" : "More";
    internal static string TipShuffle => IsIndonesian ? "Acak" : "Shuffle";
    internal static string TipPrevious => IsIndonesian ? "Sebelumnya" : "Previous";
    internal static string TipBack10 => IsIndonesian ? "Mundur 10 detik" : "Back 10 seconds";
    internal static string TipPlayPause => IsIndonesian ? "Putar/Jeda" : "Play/Pause";
    internal static string TipForward30 => IsIndonesian ? "Maju 30 detik" : "Forward 30 seconds";
    internal static string TipNext => IsIndonesian ? "Berikutnya" : "Next";
    internal static string TipRepeat => IsIndonesian ? "Ulangi" : "Repeat";
    internal static string TipLike => IsIndonesian ? "Suka" : "Like";
    internal static string TipUnlike => IsIndonesian ? "Batal suka" : "Unlike";
    internal static string TipDislike => IsIndonesian ? "Tidak suka" : "Dislike";
    internal static string TipSpeed => IsIndonesian ? "Kecepatan pemutaran" : "Playback speed";
    internal static string TipLyrics => IsIndonesian ? "Lirik" : "Lyrics";
    internal static string TipQueue => IsIndonesian ? "Antrean" : "Queue";
    internal static string TipMute => IsIndonesian ? "Bisukan" : "Mute";
    internal static string TipClose => IsIndonesian ? "Tutup" : "Close";
    internal static string TipSubtitles => IsIndonesian ? "Pilih subtitel" : "Choose subtitles";
    internal static string TipSharePlaylist => IsIndonesian ? "Bagikan playlist" : "Share playlist";
    internal static string TipDeleteFromHistory => IsIndonesian ? "Hapus dari riwayat" : "Remove from history";
    internal static string TipNewPlaylist => IsIndonesian ? "Playlist baru" : "New playlist";
    internal static string CollectionAdd => IsIndonesian ? "Tambahkan ke koleksi" : "Add to library";
    internal static string CollectionSave => IsIndonesian ? "Simpan ke koleksi" : "Save to library";
    internal static string CollectionRemove => IsIndonesian ? "Hapus dari koleksi" : "Remove from library";

    // ── Now-playing side panel ─────────────────────────────────────────────────────────────────

    internal static string QueueTabUpNext => IsIndonesian ? "Berikutnya" : "Up next";
    internal static string QueueTabHistory => IsIndonesian ? "Riwayat" : "History";
    internal static string QueueTabRelated => IsIndonesian ? "Terkait" : "Related";
    internal static string QueueNowPlaying => IsIndonesian ? "Sedang diputar" : "Now playing";
    internal static string QueueUpNextHeader => IsIndonesian ? "Selanjutnya" : "Up next";
    internal static string QueueEmpty => IsIndonesian ? "Tidak ada lagu berikutnya." : "No upcoming songs.";
    internal static string RelatedEmpty => IsIndonesian ? "Tidak ada konten terkait." : "No related content.";
    internal static string LyricsEmpty => IsIndonesian ? "Putar lagu untuk melihat lirik di sini." : "Play a song to see its lyrics here.";
    internal static string SubtitlesCcLabel => IsIndonesian ? "Subtitel (CC)" : "Subtitles (CC)";
    internal static string CcOff => IsIndonesian ? "Nonaktif" : "Off";

    // ── Artist / album pages ───────────────────────────────────────────────────────────────────

    internal static string SeeAll => IsIndonesian ? "Lihat semua" : "See all";
    internal static string MoreLabel => IsIndonesian ? "Selengkapnya" : "More";
    internal static string ShuffleLabel => IsIndonesian ? "Acak" : "Shuffle";
    internal static string ArtistTopSongs => IsIndonesian ? "Lagu teratas" : "Top songs";
    internal static string ArtistSinglesEps => IsIndonesian ? "Single & EP" : "Singles & EPs";
    internal static string ArtistVideos => IsIndonesian ? "Video" : "Videos";
    internal static string ArtistLive => IsIndonesian ? "Pertunjukan langsung" : "Live performances";
    internal static string ArtistFeaturedOn => IsIndonesian ? "Tampil di" : "Featured on";
    internal static string ArtistFansMayLike => IsIndonesian ? "Penggemar mungkin juga suka" : "Fans might also like";
    internal static string NoData => IsIndonesian ? "Tidak ada data." : "No data.";
    internal static string TrackCountText(int count) => IsIndonesian
        ? $"{count} lagu"
        : (count == 1 ? "1 song" : $"{count} songs");
    internal static string FollowLabel => IsIndonesian ? "Ikuti" : "Follow";
    internal static string FollowingLabel => IsIndonesian ? "Diikuti" : "Following";
    internal static string SubscribedLabel => IsIndonesian ? "Disubscribe" : "Subscribed";
    internal static string UnsubscribedToast => IsIndonesian ? "Berhenti subscribe" : "Unsubscribed";

    // ── Library ────────────────────────────────────────────────────────────────────────────────

    internal static string LibraryNewPlaylistPlaceholder => IsIndonesian ? "Judul playlist baru" : "New playlist title";
    internal static string LibraryCreatePlaylist => IsIndonesian ? "Buat playlist" : "Create playlist";
    internal static string FilterAll => IsIndonesian ? "Semua" : "All";

    // ── Podcast pages ──────────────────────────────────────────────────────────────────────────

    internal static string PodcastFindInShow => IsIndonesian ? "Temukan di acara" : "Find in this show";
    internal static string PodcastSortNewest => IsIndonesian ? "Terbaru" : "Newest";
    internal static string PodcastSortOldest => IsIndonesian ? "Terlama" : "Oldest";
    internal static string PodcastFilterUnplayed => IsIndonesian ? "Belum diputar" : "Unplayed";
    internal static string PodcastFilterPlayed => IsIndonesian ? "Telah diputar" : "Played";
    internal static string PodcastFilterUnfinished => IsIndonesian ? "Belum selesai" : "Unfinished";
    internal static string PodcastSave => IsIndonesian ? "Simpan" : "Save";
    internal static string PodcastSaved => IsIndonesian ? "Tersimpan" : "Saved";
    internal static string PodcastsUnavailableTitle => IsIndonesian
        ? "Halaman Podcast belum tersedia di wilayahmu."
        : "The Podcasts page isn't available in your region.";
    internal static string PodcastsUnavailableCaption => IsIndonesian
        ? "YouTube Music membatasi jelajah podcast per negara. Kamu tetap bisa membuka acara/episode lewat pencarian atau dari Home."
        : "YouTube Music limits podcast browsing per country. You can still open shows and episodes from search or Home.";

    // ── Dialogs ────────────────────────────────────────────────────────────────────────────────

    internal static string DialogCancel => IsIndonesian ? "Batal" : "Cancel";
    internal static string DialogClose => IsIndonesian ? "Tutup" : "Close";
    internal static string DialogSave => IsIndonesian ? "Simpan" : "Save";
    internal static string DialogCreate => IsIndonesian ? "Buat" : "Create";
    internal static string DialogDelete => IsIndonesian ? "Hapus" : "Delete";
    internal static string DialogNewPlaylistTitle => IsIndonesian ? "Playlist baru" : "New playlist";
    internal static string DialogTitleHeader => IsIndonesian ? "Judul" : "Title";
    internal static string DialogPlaylistTitlePlaceholder => IsIndonesian ? "Judul playlist" : "Playlist title";
    internal static string DialogPlaylistNamePlaceholder => IsIndonesian ? "Nama playlist" : "Playlist name";
    internal static string DialogNewPlaylistButton => IsIndonesian ? "+ Playlist Baru" : "+ New Playlist";
    internal static string DialogKeepAdd => IsIndonesian ? "Tetap Tambahkan" : "Add Anyway";
    internal static string DialogAlreadyInPlaylistTitle => IsIndonesian ? "Sudah ada di playlist" : "Already in playlist";
    internal static string DialogCopyInviteLink => IsIndonesian ? "Salin link undangan" : "Copy invite link";
    internal static string DialogCollabNote => IsIndonesian
        ? "Catatan: sinkronisasi setelan kolaborasi ke server belum tersedia; link undangan sudah bisa dibagikan."
        : "Note: syncing collaboration settings to the server isn't available yet; the invite link can already be shared.";
    internal static string DeletePlaylistConfirm(string title) => IsIndonesian
        ? $"Hapus \"{title}\" secara permanen?"
        : $"Delete \"{title}\" permanently?";
    internal static string DialogEditPlaylistTitle => IsIndonesian ? "Edit playlist" : "Edit playlist";
    internal static string DialogDescriptionHeader => IsIndonesian ? "Deskripsi" : "Description";
    internal static string DialogPrivacyHeader => IsIndonesian ? "Privasi" : "Privacy";
    internal static string PrivacyPublic => IsIndonesian ? "Publik" : "Public";
    internal static string PrivacyPublicDesc => IsIndonesian ? "Dapat ditemukan dan dilihat siapa pun" : "Anyone can find and view it";
    internal static string PrivacyUnlisted => IsIndonesian ? "Tidak publik" : "Unlisted";
    internal static string PrivacyUnlistedDesc => IsIndonesian ? "Hanya dapat dilihat orang yang tahu linknya" : "Only people with the link can view it";
    internal static string PrivacyPrivate => IsIndonesian ? "Pribadi" : "Private";
    internal static string PrivacyPrivateDesc => IsIndonesian ? "Hanya dapat dilihat oleh Anda" : "Only you can view it";
    internal static string VotingHeader => IsIndonesian ? "Pemungutan suara" : "Voting";
    internal static string VotingEveryone => IsIndonesian ? "Semua orang" : "Everyone";
    internal static string VotingEveryoneDesc => IsIndonesian ? "Semua orang dapat memberikan suara" : "Everyone can vote";
    internal static string VotingCollaborators => IsIndonesian ? "Khusus kolaborator" : "Collaborators only";
    internal static string VotingCollaboratorsDesc => IsIndonesian ? "Hanya kolaborator yang dapat memberikan suara" : "Only collaborators can vote";
    internal static string VotingOff => IsIndonesian ? "Pemungutan suara nonaktif" : "Voting off";
    internal static string VotingOffDesc => IsIndonesian ? "Tidak ada yang dapat memberikan suara" : "No one can vote";
    internal static string CollaborationHeader => IsIndonesian ? "Kolaborasi" : "Collaboration";
    internal static string CollaborationOff => IsIndonesian ? "Nonaktif" : "Off";
    internal static string CollaborationOn => IsIndonesian ? "Kolaborator dapat menambahkan video" : "Collaborators can add videos";
    internal static string ToastInviteLinkCopied => IsIndonesian ? "Link undangan disalin." : "Invite link copied.";
    internal static string DialogTabGeneral => IsIndonesian ? "UMUM" : "GENERAL";
    internal static string DialogTabCollaboration => IsIndonesian ? "KOLABORASI" : "COLLABORATION";
    internal static string DialogOwnerLabel => IsIndonesian ? "Pemilik" : "Owner";
    internal static string DialogDescriptionPlaceholder => IsIndonesian ? "Deskripsi (opsional)" : "Description (optional)";
    internal static string CollaborationOnShort => IsIndonesian ? "Aktif" : "On";
    internal static string CollaborationAllowNew => IsIndonesian ? "Izinkan kolaborator baru" : "Allow new collaborators";
    internal static string AccountFallback => IsIndonesian ? "Akun" : "Account";
    internal static string AlreadyInPlaylistBody(string song, string playlist) => IsIndonesian
        ? $"\"{song}\" sudah ada di \"{playlist}\". Tetap tambahkan?"
        : $"\"{song}\" is already in \"{playlist}\". Add anyway?";

    // ── Toasts / status messages ───────────────────────────────────────────────────────────────

    internal static string ThisSongFallback => IsIndonesian ? "lagu ini" : "this song";
    internal static string ToastLiked(string title) => IsIndonesian ? $"Disukai: {title}" : $"Liked: {title}";
    internal static string ToastUnliked(string title) => IsIndonesian ? $"Dihapus dari suka: {title}" : $"Removed from likes: {title}";
    internal static string ToastLikeFailed => IsIndonesian ? "Gagal menyimpan suka." : "Couldn't save your like.";
    internal static string ToastRateFailed => IsIndonesian ? "Gagal menyimpan penilaian." : "Couldn't save your rating.";
    internal static string ToastCollectionFailed => IsIndonesian ? "Gagal memperbarui koleksi." : "Couldn't update your library.";
    internal static string ToastSongAlreadyQueued => IsIndonesian ? "Lagu sudah ada di antrean." : "That song is already in the queue.";
    internal static string ToastAllAlreadyQueued => IsIndonesian ? "Semua lagu sudah ada di antrean." : "All songs are already in the queue.";
    internal static string ToastQueueUnavailable => IsIndonesian ? "Antrean belum tersedia." : "The queue isn't available yet.";
    internal static string ToastMixUnavailableAlbum => IsIndonesian ? "Mix belum tersedia untuk album ini." : "A mix isn't available for this album yet.";
    internal static string ToastMixUnavailablePlaylist => IsIndonesian ? "Mix belum tersedia untuk playlist ini." : "A mix isn't available for this playlist yet.";
    internal static string ToastPlaylistUpdated => IsIndonesian ? "Playlist diperbarui." : "Playlist updated.";
    internal static string ToastSavedToPlaylist => IsIndonesian ? "Disimpan ke playlist" : "Saved to playlist";
    internal static string ToastSaveToPlaylistFailed => IsIndonesian ? "Gagal menyimpan ke playlist" : "Couldn't save to the playlist";
    internal static string ToastLoadPlaylistsFailed => IsIndonesian ? "Gagal memuat daftar playlist" : "Couldn't load your playlists";
    internal static string ToastSubscriptionFailed => IsIndonesian ? "Gagal mengubah subscription" : "Couldn't update the subscription";
    internal static string ToastFeatureUnavailable => IsIndonesian ? "Fitur ini belum tersedia." : "This feature isn't available yet.";
    internal static string ToastNothingToShare => IsIndonesian ? "Tidak ada tautan untuk dibagikan." : "There's no link to share.";
    internal static string ToastAddedToQueue(string title) => IsIndonesian ? $"Ditambahkan ke antrean: {title}" : $"Added to queue: {title}";
    internal static string ToastPlayingNext(string title) => IsIndonesian ? $"Diputar setelah ini: {title}" : $"Playing next: {title}";
    internal static string ToastQueuedCount(int added, bool playNext) => IsIndonesian
        ? $"{added} lagu {(playNext ? "diputar setelah ini" : "ditambahkan ke antrean")}."
        : $"{added} song(s) {(playNext ? "playing next" : "added to the queue")}.";
    internal static string ToastPlayFailed(string message) => IsIndonesian ? $"Gagal memutar: {message}" : $"Couldn't play: {message}";
    internal static string ToastMixFailed(string message) => IsIndonesian ? $"Gagal memulai mix: {message}" : $"Couldn't start the mix: {message}";
    internal static string ToastGenericFailed(string message) => IsIndonesian ? $"Gagal: {message}" : $"Failed: {message}";
    internal static string ToastSaveFailed(string message) => IsIndonesian ? $"Gagal menyimpan: {message}" : $"Couldn't save: {message}";
    internal static string ToastDeleteFailed(string message) => IsIndonesian ? $"Gagal menghapus: {message}" : $"Couldn't delete: {message}";
    internal static string ToastPlaylistCreated(string title) => IsIndonesian ? $"Playlist \"{title}\" dibuat." : $"Playlist \"{title}\" created.";
    internal static string ToastCreatePlaylistFailed(string message) => IsIndonesian ? $"Gagal membuat playlist: {message}" : $"Couldn't create the playlist: {message}";
    internal static string ToastPlaylistDeleted(string title) => IsIndonesian ? $"\"{title}\" dihapus." : $"\"{title}\" deleted.";
    internal static string ToastAddedToPlaylist(string title) => IsIndonesian ? $"Ditambahkan ke playlist: {title}" : $"Added to playlist: {title}";
    internal static string ToastAddToPlaylistFailed(string message) => IsIndonesian ? $"Gagal menambah ke playlist: {message}" : $"Couldn't add to the playlist: {message}";
    internal static string ToastSavedCount(int added) => IsIndonesian ? $"{added} lagu disimpan ke playlist." : $"{added} song(s) saved to the playlist.";
    internal static string ToastSavedCountWithFailures(int added, int failed) => IsIndonesian ? $"{added} lagu disimpan, {failed} gagal." : $"{added} song(s) saved, {failed} failed.";
    internal static string ToastActionUnavailable(string action) => IsIndonesian ? $"{action} belum tersedia." : $"{action} isn't available yet.";
    internal static string ToastShowSaved => IsIndonesian ? "Disimpan ke koleksi" : "Saved to library";
    internal static string ToastShowRemoved => IsIndonesian ? "Dihapus dari koleksi" : "Removed from library";
    internal static string ToastShowSaveFailed => IsIndonesian ? "Gagal menyimpan ke koleksi" : "Couldn't save to your library";
    internal static string ToastShowRemoveFailed => IsIndonesian ? "Gagal menghapus dari koleksi" : "Couldn't remove from your library";
    internal static string ToastQueuedForLater => IsIndonesian ? "Ditambahkan ke Episode untuk Nanti" : "Added to Episodes for Later";
    internal static string ToastQueueForLaterFailed => IsIndonesian ? "Gagal menambahkan ke Episode untuk Nanti" : "Couldn't add to Episodes for Later";
    internal static string ToastMarkedPlayed => IsIndonesian ? "Ditandai telah diputar" : "Marked as played";
    internal static string ToastMarkedUnplayed => IsIndonesian ? "Ditandai belum diputar" : "Marked as unplayed";
    internal static string ToastPlayedSyncFailed => IsIndonesian ? "Gagal menyinkronkan status diputar" : "Couldn't sync the played status";
    internal static string ToastStartingMix => IsIndonesian ? "Memulai mix." : "Starting the mix.";
    internal static string ToastShufflingAlbum => IsIndonesian ? "Memutar acak album." : "Shuffling the album.";
    internal static string PodcastLatestEpisodes => IsIndonesian ? "Episode terbaru" : "Latest episodes";
    internal static string ToastDisliked(string title) => IsIndonesian ? $"Tidak disukai: {title}" : $"Disliked: {title}";
    internal static string ToastCountPlayedNext(int added) => IsIndonesian ? $"{added} lagu diputar setelah ini." : $"{added} song(s) playing next.";
    internal static string ToastCountAddedToQueue(int added) => IsIndonesian ? $"{added} lagu ditambahkan ke antrean." : $"{added} song(s) added to the queue.";
    internal static string ToastRemovedFromCollection(string title) => IsIndonesian ? $"Dihapus dari koleksi: {title}" : $"Removed from library: {title}";
    internal static string ToastSavedToCollection(string title) => IsIndonesian ? $"Disimpan ke koleksi: {title}" : $"Saved to library: {title}";
    internal static string ToastRemovedFromCollectionShort => IsIndonesian ? "Dihapus dari koleksi." : "Removed from your library.";
    internal static string ToastAddedToCollectionShort => IsIndonesian ? "Ditambahkan ke koleksi." : "Added to your library.";
    internal static string CollectionButtonLabelAdd => IsIndonesian ? "Tambahkan ke koleksi" : "Add to library";
    internal static string CollectionButtonLabelRemove => IsIndonesian ? "Hapus dari koleksi" : "Remove from library";

    // ── Settings combo option lists (built once per page visit) ────────────────────────────────

    internal static string[] ThemeOptions => IsIndonesian
        ? ["Ikuti sistem", "Terang", "Gelap"]
        : ["Follow system", "Light", "Dark"];

    internal static string[] LyricsProviderOptions => IsIndonesian
        ? ["Otomatis (semua sumber)", "LRCLib", "NetEase (cakupan Asia)"]
        : ["Automatic (all sources)", "LRCLib", "NetEase (Asian coverage)"];

    /// <summary>Labels for <see cref="Core.Models.LaunchPage"/>, index-aligned with the enum.</summary>
    internal static string[] LaunchPageOptions => IsIndonesian
        ? ["Beranda", "Jelajahi", "Tangga lagu", "Suasana & genre", "Rilis baru", "Lagu yang disukai", "Playlist", "Terakhir dibuka"]
        : ["Home", "Explore", "Charts", "Moods & Genres", "New Releases", "Liked Music", "Playlists", "Last used"];

    /// <summary>Labels for <see cref="Core.Models.AudioQuality"/>, index-aligned with the enum.</summary>
    internal static string[] AudioQualityOptions => IsIndonesian
        ? ["Rendah", "Sedang", "Tinggi"]
        : ["Low", "Medium", "High"];

    // ── Keyboard shortcuts help (Shift+/) ──────────────────────────────────────────────────────

    internal static string ShortcutsTitle => IsIndonesian ? "Pintasan keyboard" : "Keyboard shortcuts";
    internal static string ShortcutsPlayback => IsIndonesian ? "Pemutaran" : "Playback";
    internal static string ShortcutsGeneral => IsIndonesian ? "Umum" : "General";
    internal static string ShortcutsNavigation => IsIndonesian ? "Navigasi" : "Navigation";
    internal static string ScOr => IsIndonesian ? "atau" : "or";
    internal static string ScPlayPause => IsIndonesian ? "Putar/Jeda" : "Play/pause";
    internal static string ScNextSong => IsIndonesian ? "Lagu berikutnya" : "Next song";
    internal static string ScPrevSong => IsIndonesian ? "Lagu sebelumnya" : "Previous song";
    internal static string ScFwd10 => IsIndonesian ? "Maju 10 detik" : "Seek forward 10s";
    internal static string ScBack10 => IsIndonesian ? "Mundur 10 detik" : "Seek back 10s";
    internal static string ScFwd1 => IsIndonesian ? "Maju 1 detik" : "Seek forward 1s";
    internal static string ScBack1 => IsIndonesian ? "Mundur 1 detik" : "Seek back 1s";
    internal static string ScShuffle => IsIndonesian ? "Acak antrean" : "Shuffle queue";
    internal static string ScRepeat => IsIndonesian ? "Aktifkan/nonaktifkan pengulangan" : "Toggle repeat";
    internal static string ScVolUp => IsIndonesian ? "Naikkan volume" : "Volume up";
    internal static string ScVolDown => IsIndonesian ? "Turunkan volume" : "Volume down";
    internal static string ScMute => IsIndonesian ? "Aktifkan/nonaktifkan suara" : "Toggle mute";
    internal static string ScToggleQueue => IsIndonesian ? "Buka/tutup antrean" : "Toggle queue";
    internal static string ScFullscreen => IsIndonesian ? "Beralih ke layar penuh" : "Toggle fullscreen";
    internal static string ScLike => IsIndonesian ? "Sukai lagu ini" : "Like this song";
    internal static string ScDislike => IsIndonesian ? "Tidak suka lagu ini" : "Dislike this song";
    internal static string ScShowHelp => IsIndonesian ? "Tampilkan pintasan ini" : "Show these shortcuts";
    internal static string ScLyrics => IsIndonesian ? "Buka/tutup lirik" : "Toggle lyrics";
    internal static string ScSpeedUp => IsIndonesian ? "Percepat pemutaran" : "Speed up playback";
    internal static string ScSpeedDown => IsIndonesian ? "Perlambat pemutaran" : "Slow down playback";
    internal static string ScOpenHome => IsIndonesian ? "Buka Beranda" : "Open Home";
    internal static string ScOpenExplore => IsIndonesian ? "Buka Jelajahi" : "Open Explore";
    internal static string ScOpenLibrary => IsIndonesian ? "Buka Koleksi" : "Open Library";
    internal static string ScOpenLiked => IsIndonesian ? "Buka Lagu yang disukai" : "Open Liked Songs";
    internal static string ScOpenHistory => IsIndonesian ? "Buka Riwayat" : "Open History";
    internal static string ScOpenPodcasts => IsIndonesian ? "Buka Podcast" : "Open Podcasts";
    internal static string ScOpenSettings => IsIndonesian ? "Buka Pengaturan" : "Open Settings";
    internal static string ScSearch => IsIndonesian ? "Telusuri" : "Search";
}
