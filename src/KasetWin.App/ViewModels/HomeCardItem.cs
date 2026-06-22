using KasetWin.Core.Models;
using KasetWin.Core.Services.Sharing;
using Microsoft.UI.Xaml;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Flattened, view-friendly projection of a single <see cref="HomeSectionItem"/> for the Home /
/// Explore shelves (Task 14.3, Req 11.1/31.1).
/// </summary>
/// <remarks>
/// <para>
/// The Core <see cref="HomeSectionItem"/> is a polymorphic record union (song / album / playlist /
/// artist), each wrapping a different model type. Binding a uniform card template directly to that
/// union would require a <c>DataTemplateSelector</c> and per-type bindings; instead this wrapper
/// exposes the common display fields (<see cref="Title"/>, <see cref="Subtitle"/>,
/// <see cref="ThumbnailUrl"/>) plus the original <see cref="Model"/> so the page code-behind can
/// route activation by concrete kind.
/// </para>
/// <para>
/// Identity is the union's kind-prefixed <see cref="HomeSectionItem.Id"/> (e.g. <c>song-…</c>,
/// <c>artist-…</c>), giving virtualized lists a stable key (Req 16.1).
/// </para>
/// </remarks>
public sealed class HomeCardItem
{
    private const double SquareRadius = 6;
    private const double RoundRadius = 80; // artist avatars read as circular

    private HomeCardItem(HomeSectionItem model, string title, string subtitle, Uri? thumbnailUrl, bool isArtist)
    {
        Model = model;
        Title = title;
        Subtitle = subtitle;
        ThumbnailUrl = thumbnailUrl;
        ImageCornerRadius = new CornerRadius(isArtist ? RoundRadius : SquareRadius);
        CanShare = ShareUrlBuilder.TryCreate(model) is not null;
    }

    /// <summary>The original Core union item, used by the page to route activation/navigation.</summary>
    public HomeSectionItem Model { get; }

    /// <summary>Whether this item has a shareable URL; gates the Share affordance (Req 34.2).</summary>
    public bool CanShare { get; }

    /// <summary>Stable, kind-prefixed identity for list virtualization (Req 16.1).</summary>
    public string Key => Model.Id;

    public string Title { get; }

    public string Subtitle { get; }

    public Uri? ThumbnailUrl { get; }

    /// <summary>Circular for artists, lightly rounded otherwise.</summary>
    public CornerRadius ImageCornerRadius { get; }

    /// <summary>Projects a Core <see cref="HomeSectionItem"/> into a uniform display card.</summary>
    public static HomeCardItem FromModel(HomeSectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            HomeSectionItem.SongItem s => new HomeCardItem(
                item,
                s.Song.Title,
                s.Song.ArtistsDisplay,
                s.Song.ThumbnailUrl ?? s.Song.FallbackThumbnailUrl,
                isArtist: false),

            HomeSectionItem.AlbumItem a => new HomeCardItem(
                item,
                a.Album.Title,
                AlbumSubtitle(a.Album),
                a.Album.ThumbnailUrl,
                isArtist: false),

            HomeSectionItem.PlaylistItem p => new HomeCardItem(
                item,
                p.Pl.Title,
                p.Pl.Author?.Name ?? "Playlist",
                p.Pl.ThumbnailUrl,
                isArtist: false),

            HomeSectionItem.ArtistItem ar => new HomeCardItem(
                item,
                ar.Artist.Name,
                "Artist",
                ar.Artist.ThumbnailUrl,
                isArtist: true),

            _ => new HomeCardItem(item, "Unknown", string.Empty, null, isArtist: false),
        };
    }

    private static string AlbumSubtitle(Album album)
    {
        var artists = album.ArtistsDisplay;
        if (string.IsNullOrEmpty(album.Year))
        {
            return string.IsNullOrEmpty(artists) ? "Album" : artists;
        }

        return string.IsNullOrEmpty(artists) ? album.Year : $"{artists} • {album.Year}";
    }
}

/// <summary>
/// View projection of a <see cref="HomeSection"/>: a title plus its shelf of
/// <see cref="HomeCardItem"/> cards (Task 14.3).
/// </summary>
public sealed class HomeSectionView
{
    public HomeSectionView(string title, IReadOnlyList<HomeCardItem> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }

    public IReadOnlyList<HomeCardItem> Items { get; }

    /// <summary>Projects a Core <see cref="HomeSection"/> (skipping empty shelves).</summary>
    public static HomeSectionView FromModel(HomeSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var cards = section.Items.Select(HomeCardItem.FromModel).ToList();
        return new HomeSectionView(section.Title, cards);
    }
}
