using System.Text.Json.Serialization;

namespace KasetWin.Core.Models;

/// <summary>
/// A section of the Podcasts discovery surface (<c>FEmusic_podcasts</c>), holding a typed list of
/// shows and/or episodes (advanced phase, Req 27.1). Identity is content-derived and stable so the
/// UI does not churn list virtualization across refreshes (Req 16.1).
/// </summary>
public sealed record PodcastSection
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<PodcastSectionItem> Items { get; init; } = [];
}

/// <summary>
/// Discriminated union of items that can appear in a podcast section: a <see cref="PodcastShow"/>
/// card or a playable <see cref="PodcastEpisode"/>. Modelled as an abstract record with sealed
/// subtypes (mirrors <see cref="HomeSectionItem"/>) so the parser and the UI can pattern-match on
/// the concrete kind, with a <c>$type</c> discriminator for round-trip-safe serialization.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ShowItem), "show")]
[JsonDerivedType(typeof(EpisodeItem), "episode")]
public abstract record PodcastSectionItem
{
    /// <summary>Stable, kind-prefixed identity for UI virtualization.</summary>
    public abstract string Id { get; }

    /// <summary>A podcast show card (browseId <c>MPSPP...</c>).</summary>
    public sealed record ShowItem(PodcastShow Show) : PodcastSectionItem
    {
        public override string Id => $"show-{Show.Id}";
    }

    /// <summary>A playable podcast episode (videoId).</summary>
    public sealed record EpisodeItem(PodcastEpisode Episode) : PodcastSectionItem
    {
        public override string Id => $"episode-{Episode.Id}";
    }
}

/// <summary>
/// Result of probing the region-aware Podcasts discovery surface (Req 27.1/27.2).
/// </summary>
/// <remarks>
/// <see cref="IsAvailable"/> distinguishes a region where the <c>FEmusic_podcasts</c> endpoint
/// responded (tab should be shown, Req 27.1) from one where it returned HTTP 404 (tab hidden,
/// Req 27.2). A 404 yields <c>IsAvailable == false</c> with empty <see cref="Sections"/> — it is a
/// normal, non-fatal outcome rather than an error (see ADR-0019).
/// </remarks>
public sealed record PodcastsResult(bool IsAvailable, IReadOnlyList<PodcastSection> Sections)
{
    /// <summary>An unavailable result (region returned 404): hide the tab, no sections.</summary>
    public static readonly PodcastsResult Unavailable = new(false, []);
}

/// <summary>
/// A podcast creator's channel page (browseId <c>UC…</c>): round-avatar header with the
/// subscribe affordance plus the channel's shelves ("Episode terbaru", shows, …) as
/// <see cref="PodcastSection"/>s.
/// </summary>
public sealed record PodcastChannel
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Channel description, when the header carries one.</summary>
    public string? Description { get; init; }

    /// <summary>Round avatar image.</summary>
    public Uri? AvatarUrl { get; init; }

    /// <summary>Subscriber count exactly as YT displays it ("11,7 jt subscriber").</summary>
    public string? SubscriberText { get; init; }

    /// <summary>Whether the signed-in account subscribes to the channel.</summary>
    public bool IsSubscribed { get; init; }

    /// <summary>The channel's shelves in page order (episodes, shows, …).</summary>
    public IReadOnlyList<PodcastSection> Sections { get; init; } = [];
}

/// <summary>
/// Persisted playback progress for a single podcast episode (Req 27.3).
/// </summary>
/// <remarks>
/// Identity is the episode <c>videoId</c> (<see cref="EpisodeId"/>). <see cref="PositionSeconds"/>
/// is the last-known playback position in seconds (never negative — clamped on save), and
/// <see cref="Played"/> records whether the episode has been marked fully played. The shape is a
/// plain record so it round-trips exactly through <c>EpisodeProgressStore</c> (Property 41).
/// </remarks>
public sealed record EpisodeProgress(string EpisodeId, double PositionSeconds, bool Played);
