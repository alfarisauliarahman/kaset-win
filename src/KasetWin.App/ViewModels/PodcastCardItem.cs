using KasetWin.Core.Models;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Flattened, view-friendly projection of a single <see cref="PodcastSectionItem"/> for the
/// Podcasts discovery shelves (Task 20.1, Req 27.1). Mirrors <see cref="HomeCardItem"/>: it exposes
/// the common display fields plus the original <see cref="Model"/> so the page can route activation
/// by concrete kind (show → navigate; episode → play).
/// </summary>
public sealed class PodcastCardItem : IKeyedItem
{
    private PodcastCardItem(PodcastSectionItem model, string title, string subtitle, Uri? thumbnailUrl, double progress, bool isEpisode)
    {
        Model = model;
        Title = title;
        Subtitle = subtitle;
        ThumbnailUrl = thumbnailUrl;
        Progress = progress;
        IsEpisode = isEpisode;
    }

    /// <summary>The original Core union item, used by the page to route activation/navigation.</summary>
    public PodcastSectionItem Model { get; }

    /// <summary>Stable, kind-prefixed identity for list virtualization (Req 16.1).</summary>
    public string Key => Model.Id;

    public string Title { get; }

    public string Subtitle { get; }

    public Uri? ThumbnailUrl { get; }

    /// <summary>Playback progress fraction (0–1) for episodes; 0 for shows.</summary>
    public double Progress { get; }

    /// <summary>Whether this card represents a playable episode (vs a show).</summary>
    public bool IsEpisode { get; }

    /// <summary>True when there is a non-zero progress bar to show on an episode card.</summary>
    public bool HasProgress => IsEpisode && Progress > 0;

    /// <summary>Projects a Core <see cref="PodcastSectionItem"/> into a uniform display card.</summary>
    public static PodcastCardItem FromModel(PodcastSectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            PodcastSectionItem.ShowItem s => new PodcastCardItem(
                item,
                s.Show.Title,
                string.IsNullOrEmpty(s.Show.Author) ? "Podcast" : s.Show.Author!,
                s.Show.ThumbnailUrl,
                progress: 0,
                isEpisode: false),

            PodcastSectionItem.EpisodeItem e => new PodcastCardItem(
                item,
                e.Episode.Title,
                e.Episode.ShowTitle ?? "Episode",
                e.Episode.ThumbnailUrl,
                progress: e.Episode.Progress,
                isEpisode: true),

            _ => new PodcastCardItem(item, "Unknown", string.Empty, null, 0, false),
        };
    }
}

/// <summary>
/// View projection of a <see cref="PodcastSection"/>: a title plus its shelf of
/// <see cref="PodcastCardItem"/> cards (Task 20.1).
/// </summary>
public sealed class PodcastSectionView
{
    public PodcastSectionView(string title, IReadOnlyList<PodcastCardItem> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }

    public IReadOnlyList<PodcastCardItem> Items { get; }

    /// <summary>Projects a Core <see cref="PodcastSection"/> into a view shelf.</summary>
    public static PodcastSectionView FromModel(PodcastSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var cards = section.Items.Select(PodcastCardItem.FromModel).ToList();
        return new PodcastSectionView(section.Title, cards);
    }
}
