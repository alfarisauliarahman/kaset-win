using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Sharing;

/// <summary>
/// Pure, dependency-free helper that builds canonical <c>music.youtube.com</c> share URLs and
/// <see cref="ShareTarget"/>s from domain models (Req 34.1/34.2). This is the Windows port of the
/// macOS <c>ShareService</c> / <c>Shareable</c> conformances and mirrors their URL shapes exactly:
/// <list type="bullet">
///   <item><description>Song → <c>https://music.youtube.com/watch?v={videoId}</c></description></item>
///   <item><description>Playlist → <c>https://music.youtube.com/playlist?list={id}</c></description></item>
///   <item><description>Album → <c>https://music.youtube.com/browse/{id}</c> (navigable <c>MPRE/OLAK</c> ids only)</description></item>
///   <item><description>Artist → <c>https://music.youtube.com/channel/{id}</c> (public <c>UC</c> channel ids only)</description></item>
///   <item><description>Podcast → <c>https://music.youtube.com/browse/{id}</c> (navigable <c>MPSPP</c> ids only)</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Every method is <c>static</c>, deterministic and side-effect free (no I/O, clocks, randomness),
/// and <b>total</b>: it never throws on malformed input. A missing/empty id, or an id whose prefix
/// is not navigable for its kind (albums, artists, podcasts), yields <see langword="null"/> so the
/// caller can disable the share affordance for that content (Req 34.2). The emitted URLs are
/// round-trip compatible with the app's own <c>kaset://</c> / web URL handling and with the macOS
/// client, so shared links open correctly in other YouTube Music clients.
/// </para>
/// </remarks>
public static class ShareUrlBuilder
{
    /// <summary>The YouTube Music origin every share URL is rooted at.</summary>
    public const string MusicBaseUrl = "https://music.youtube.com";

    /// <summary>Prefix identifying a public artist channel id (the only shareable artist shape).</summary>
    private const string ChannelPrefix = "UC";

    /// <summary>Prefix identifying a podcast show browse id.</summary>
    private const string PodcastShowPrefix = "MPSPP";

    /// <summary>
    /// Builds the canonical share <see cref="Uri"/> for a content <paramref name="id"/> of the given
    /// <paramref name="kind"/>, or <see langword="null"/> when the content is not shareable
    /// (empty id, or a non-navigable album/artist/podcast id) (Req 34.2). Never throws.
    /// </summary>
    /// <param name="kind">The content kind being shared.</param>
    /// <param name="id">The content id (videoId / playlistId / browseId / channelId).</param>
    public static Uri? BuildUrl(ShareContentKind kind, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var encoded = Uri.EscapeDataString(id);

        return kind switch
        {
            ShareContentKind.Song => Absolute($"{MusicBaseUrl}/watch?v={encoded}"),
            ShareContentKind.Playlist => Absolute($"{MusicBaseUrl}/playlist?list={encoded}"),

            // Only album / single browse ids (MPRE…/OLAK…) are navigable and therefore shareable;
            // mirrors the macOS Album.hasNavigableId guard.
            ShareContentKind.Album => BrowseIdClassifier.Classify(id) == BrowseIdKind.Album
                ? Absolute($"{MusicBaseUrl}/browse/{encoded}")
                : null,

            // Only public channel ids (UC…) map to a shareable /channel/ URL; mirrors the macOS
            // Artist.publicChannelId guard (library-artist MPLAUC ids are not shareable).
            ShareContentKind.Artist => id.StartsWith(ChannelPrefix, StringComparison.Ordinal)
                ? Absolute($"{MusicBaseUrl}/channel/{encoded}")
                : null,

            // Only podcast show ids (MPSPP…) are navigable; mirrors PodcastShow.hasNavigableId.
            ShareContentKind.Podcast => id.StartsWith(PodcastShowPrefix, StringComparison.Ordinal)
                ? Absolute($"{MusicBaseUrl}/browse/{encoded}")
                : null,

            _ => null,
        };
    }

    /// <summary>Builds a <see cref="ShareTarget"/> for a <see cref="Song"/>, or <see langword="null"/> when unshareable.</summary>
    public static ShareTarget? TryCreate(Song? song)
    {
        if (song is null)
        {
            return null;
        }

        var url = BuildUrl(ShareContentKind.Song, song.VideoId);
        if (url is null)
        {
            return null;
        }

        var subtitle = string.IsNullOrEmpty(song.ArtistsDisplay) ? null : song.ArtistsDisplay;
        return new ShareTarget(song.Title, subtitle, url);
    }

    /// <summary>Builds a <see cref="ShareTarget"/> for a <see cref="Playlist"/>, or <see langword="null"/> when unshareable.</summary>
    public static ShareTarget? TryCreate(Playlist? playlist)
    {
        if (playlist is null)
        {
            return null;
        }

        var url = BuildUrl(ShareContentKind.Playlist, playlist.Id);
        return url is null ? null : new ShareTarget(playlist.Title, playlist.Author?.Name, url);
    }

    /// <summary>Builds a <see cref="ShareTarget"/> for an <see cref="Album"/>, or <see langword="null"/> when unshareable.</summary>
    public static ShareTarget? TryCreate(Album? album)
    {
        if (album is null)
        {
            return null;
        }

        var url = BuildUrl(ShareContentKind.Album, album.Id);
        if (url is null)
        {
            return null;
        }

        var subtitle = string.IsNullOrEmpty(album.ArtistsDisplay) ? null : album.ArtistsDisplay;
        return new ShareTarget(album.Title, subtitle, url);
    }

    /// <summary>Builds a <see cref="ShareTarget"/> for an <see cref="Artist"/>, or <see langword="null"/> when unshareable.</summary>
    public static ShareTarget? TryCreate(Artist? artist)
    {
        if (artist is null)
        {
            return null;
        }

        // Artists carry no subtitle (mirrors the macOS Artist.shareSubtitle == nil).
        var url = BuildUrl(ShareContentKind.Artist, artist.Id);
        return url is null ? null : new ShareTarget(artist.Name, Subtitle: null, url);
    }

    /// <summary>Builds a <see cref="ShareTarget"/> for a <see cref="PodcastShow"/>, or <see langword="null"/> when unshareable.</summary>
    public static ShareTarget? TryCreate(PodcastShow? show)
    {
        if (show is null)
        {
            return null;
        }

        var url = BuildUrl(ShareContentKind.Podcast, show.Id);
        return url is null ? null : new ShareTarget(show.Title, show.Author, url);
    }

    /// <summary>
    /// Builds a <see cref="ShareTarget"/> for a Home / Search union item (<see cref="HomeSectionItem"/>),
    /// dispatching to the kind-specific overload. Returns <see langword="null"/> when unshareable.
    /// </summary>
    public static ShareTarget? TryCreate(HomeSectionItem? item) => item switch
    {
        HomeSectionItem.SongItem s => TryCreate(s.Song),
        HomeSectionItem.AlbumItem a => TryCreate(a.Album),
        HomeSectionItem.PlaylistItem p => TryCreate(p.Pl),
        HomeSectionItem.ArtistItem ar => TryCreate(ar.Artist),
        _ => null,
    };

    /// <summary>
    /// Builds a <see cref="ShareTarget"/> for a pinned <see cref="FavoriteItem"/> (Home Favorites
    /// shelf), mapping its <see cref="FavoriteItemType"/> to the matching content kind. Returns
    /// <see langword="null"/> when unshareable (Req 34.2).
    /// </summary>
    public static ShareTarget? TryCreate(FavoriteItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var kind = item.Type switch
        {
            FavoriteItemType.Song => ShareContentKind.Song,
            FavoriteItemType.Album => ShareContentKind.Album,
            FavoriteItemType.Playlist => ShareContentKind.Playlist,
            FavoriteItemType.Artist => ShareContentKind.Artist,
            _ => (ShareContentKind?)null,
        };

        if (kind is null)
        {
            return null;
        }

        var url = BuildUrl(kind.Value, item.ContentId);
        return url is null ? null : new ShareTarget(item.Title, item.Subtitle, url);
    }

    private static Uri? Absolute(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
