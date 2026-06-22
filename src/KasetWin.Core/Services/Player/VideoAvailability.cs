using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Pure, headless-testable policy for deciding whether a track exposes genuine standalone video
/// content worth surfacing in the floating video window (Req 26.1). The authoritative signal is the
/// InnerTube <see cref="MusicVideoType"/>: only an Official Music Video (<see cref="MusicVideoType.Omv"/>)
/// has real video; ATV (art-track video), UGC, podcast episodes and unknown types are treated as
/// audio-only. Mirrors the macOS reference rule (<c>MusicVideoType.hasVideoContent</c>).
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency so the App layer can gate the
/// "pop out video" affordance (Req 26.1/26.4) against the same rule that the parser uses when it
/// populates <see cref="Song.HasVideo"/>. The single source of truth for the OMV rule is
/// <see cref="MusicVideoTypeExtensions.HasVideoContent"/>; this type only adds convenient overloads
/// for nullable values and for a whole <see cref="Song"/>.
/// </remarks>
public static class VideoAvailability
{
    /// <summary>
    /// Whether <paramref name="type"/> carries genuine standalone video content. Total over the
    /// <see cref="MusicVideoType"/> domain: <see langword="true"/> if and only if the type is
    /// <see cref="MusicVideoType.Omv"/> (Property 37, Req 26.1). Never throws.
    /// </summary>
    public static bool IsVideoAvailable(MusicVideoType type) => type.HasVideoContent();

    /// <summary>
    /// Nullable overload: an absent type (no metadata yet) means no video is available.
    /// </summary>
    public static bool IsVideoAvailable(MusicVideoType? type) => type is { } t && t.HasVideoContent();

    /// <summary>
    /// Whether the floating video window may be offered for <paramref name="song"/> (Req 26.1).
    /// Prefers the authoritative <see cref="Song.VideoType"/>; when that is absent it falls back to
    /// the boolean <see cref="Song.HasVideo"/> hint (which the parser also derives from OMV). A
    /// <see langword="null"/> song (nothing playing) is never video-available.
    /// </summary>
    public static bool IsVideoAvailable(Song? song)
    {
        if (song is null)
        {
            return false;
        }

        if (song.VideoType is { } type)
        {
            return type.HasVideoContent();
        }

        return song.HasVideo ?? false;
    }
}
