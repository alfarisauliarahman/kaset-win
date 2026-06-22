using KasetWin.Core.Models;
using KasetWin.Core.Services.Sharing;
using Microsoft.UI.Xaml;

namespace KasetWin.App.ViewModels;

/// <summary>
/// View-friendly projection of a single <see cref="FavoriteItem"/> for the Home Favorites shelf
/// (Task 22.1, Req 29.4). Mirrors <see cref="HomeCardItem"/> so the existing card template renders
/// it, while keeping the original <see cref="Model"/> around for activation and unpin routing.
/// </summary>
public sealed class FavoriteCardItem
{
    private const double SquareRadius = 6;
    private const double RoundRadius = 80; // artist avatars read as circular

    public FavoriteCardItem(FavoriteItem model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        Title = model.Title;
        Subtitle = model.Subtitle ?? DefaultSubtitle(model.Type);
        ThumbnailUrl = model.ThumbnailUrl;
        ImageCornerRadius = new CornerRadius(model.Type == FavoriteItemType.Artist ? RoundRadius : SquareRadius);
        CanShare = ShareUrlBuilder.TryCreate(model) is not null;
    }

    /// <summary>The original favorite, used by the page to route activation / unpin.</summary>
    public FavoriteItem Model { get; }

    /// <summary>Whether this favorite has a shareable URL; gates the Share affordance (Req 34.2).</summary>
    public bool CanShare { get; }

    /// <summary>Stable identity for list virtualization (Req 16.1).</summary>
    public string Key => Model.ContentId;

    public string Title { get; }

    public string Subtitle { get; }

    public Uri? ThumbnailUrl { get; }

    /// <summary>Circular for artists, lightly rounded otherwise.</summary>
    public CornerRadius ImageCornerRadius { get; }

    private static string DefaultSubtitle(FavoriteItemType type) => type switch
    {
        FavoriteItemType.Song => "Song",
        FavoriteItemType.Album => "Album",
        FavoriteItemType.Playlist => "Playlist",
        FavoriteItemType.Artist => "Artist",
        _ => string.Empty,
    };
}
