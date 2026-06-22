namespace KasetWin.Core.Models;

/// <summary>
/// Repeat behaviour for the playback queue (Req 6.5 / 5.8).
/// </summary>
public enum RepeatMode
{
    /// <summary>No repeat; queue stops after the last track.</summary>
    Off,

    /// <summary>Repeat the whole queue.</summary>
    All,

    /// <summary>Repeat the current track.</summary>
    One,
}

/// <summary>
/// Preferred streaming audio quality (Req 7). The audio-quality seam stays here even
/// though the macOS equalizer models are intentionally not ported (Req 36.1, deferred).
/// </summary>
public enum AudioQuality
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Kind of music video backing a song, derived from InnerTube <c>musicVideoType</c>.
/// Only <see cref="Omv"/> exposes genuine standalone video content (see
/// <see cref="MusicVideoTypeExtensions.HasVideoContent"/>).
/// </summary>
public enum MusicVideoType
{
    /// <summary>Official Music Video — has real video content.</summary>
    Omv,

    /// <summary>Art Track Video — static art over audio.</summary>
    Atv,

    /// <summary>User Generated Content.</summary>
    Ugc,

    /// <summary>Podcast episode entry.</summary>
    PodcastEpisode,

    /// <summary>Unknown / unclassified.</summary>
    Unknown,
}

/// <summary>
/// Like / dislike state reported by InnerTube for a track.
/// </summary>
public enum LikeStatus
{
    Like,
    Dislike,
    Indifferent,
}

/// <summary>
/// Privacy level for a playlist.
/// </summary>
public enum PlaylistPrivacy
{
    Private,
    Unlisted,
    Public,
}

/// <summary>
/// Discriminates the type of a <see cref="FavoriteItem"/>.
/// </summary>
public enum FavoriteItemType
{
    Song,
    Album,
    Playlist,
    Artist,
}

/// <summary>
/// Page the app opens to on launch (Req 18.1). Faithful counterpart of the macOS
/// <c>SettingsManager.LaunchPage</c>. Persisted by <c>SettingsService</c> as a stable
/// enum name (never an ordinal) so reordering members never changes a saved preference.
/// </summary>
public enum LaunchPage
{
    /// <summary>Personalised home feed.</summary>
    Home,

    /// <summary>Explore surface.</summary>
    Explore,

    /// <summary>Charts surface.</summary>
    Charts,

    /// <summary>Moods &amp; Genres surface.</summary>
    MoodsAndGenres,

    /// <summary>New Releases surface.</summary>
    NewReleases,

    /// <summary>Liked Music library view.</summary>
    LikedMusic,

    /// <summary>User playlists library view.</summary>
    Playlists,

    /// <summary>Resume on whichever page the user last used.</summary>
    LastUsed,
}

/// <summary>
/// Helpers for <see cref="MusicVideoType"/>.
/// </summary>
public static class MusicVideoTypeExtensions
{
    /// <summary>
    /// Whether the music video type carries genuine standalone video content.
    /// Only <see cref="MusicVideoType.Omv"/> (Official Music Video) qualifies;
    /// ATV/UGC/PodcastEpisode/Unknown are treated as audio-only for the video seam
    /// (Req 26.1). Placed as an extension because enums cannot carry computed members.
    /// </summary>
    public static bool HasVideoContent(this MusicVideoType type) => type == MusicVideoType.Omv;
}
