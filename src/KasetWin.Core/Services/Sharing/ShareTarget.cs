namespace KasetWin.Core.Services.Sharing;

/// <summary>
/// The kind of content a share request addresses (Req 34.1). Determines which
/// <c>music.youtube.com</c> URL shape <see cref="ShareUrlBuilder"/> produces and which id-prefix
/// validation applies.
/// </summary>
public enum ShareContentKind
{
    /// <summary>A song / video, shared as <c>watch?v={videoId}</c>.</summary>
    Song,

    /// <summary>A playlist, shared as <c>playlist?list={id}</c>.</summary>
    Playlist,

    /// <summary>An album or single/EP, shared as <c>browse/{id}</c> (navigable <c>MPRE/OLAK</c> ids only).</summary>
    Album,

    /// <summary>An artist / channel, shared as <c>channel/{id}</c> (public <c>UC</c> channel ids only).</summary>
    Artist,

    /// <summary>A podcast show, shared as <c>browse/{id}</c> (navigable <c>MPSPP</c> ids only).</summary>
    Podcast,
}

/// <summary>
/// A fully-resolved, shareable piece of content: a display <see cref="Title"/>, optional
/// <see cref="Subtitle"/> (artist / author), and the canonical <see cref="Url"/> to share
/// (Req 34.1). Produced by <see cref="ShareUrlBuilder"/>; a <see langword="null"/> result there
/// means the content has no shareable URL and the share affordance must be disabled (Req 34.2).
/// </summary>
/// <param name="Title">The content title shown in the share UI.</param>
/// <param name="Subtitle">Optional artist / author line; <see langword="null"/> for artists.</param>
/// <param name="Url">The canonical <c>music.youtube.com</c> URL for the content.</param>
public sealed record ShareTarget(string Title, string? Subtitle, Uri Url)
{
    /// <summary>
    /// Formatted share text: <c>"{Title} by {Subtitle}"</c> when a non-empty subtitle is present,
    /// otherwise just <c>"{Title}"</c> (mirrors the macOS <c>Shareable.shareText</c>).
    /// </summary>
    public string ShareText =>
        string.IsNullOrEmpty(Subtitle) ? Title : $"{Title} by {Subtitle}";
}
