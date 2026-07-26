using KasetWin.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KasetWin.App.Converters;

/// <summary>
/// Converts a <see cref="System.Uri"/> (e.g. <c>Song.ThumbnailUrl</c>) into an
/// <see cref="ImageSource"/> for an <see cref="Microsoft.UI.Xaml.Controls.Image"/> in the
/// <see cref="Controls.PlayerBar"/>. Returns <see langword="null"/> for a missing/invalid value so
/// the bound image simply shows nothing rather than throwing.
/// </summary>
public sealed partial class UriToImageSourceConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            Uri uri => new BitmapImage(uri),
            string s when Uri.TryCreate(s, UriKind.Absolute, out var uri) => new BitmapImage(uri),
            _ => null,
        };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts the now-playing <see cref="Song"/> into cover-art <see cref="ImageSource"/> for the
/// player bar and the queue panel's "Sedang diputar" card (#150). Differs from
/// <see cref="UriToImageSourceConverter"/> in three deliberate ways, each one closing a way the
/// cover could stay blank with no evidence left behind:
/// <list type="number">
/// <item><b>Never null for a playable track.</b> Binds the whole <see cref="Song"/> and resolves
/// <see cref="Song.ThumbnailUrl"/> ?? <see cref="Song.FallbackThumbnailUrl"/>, so the
/// <c>ThumbnailUrl == null</c> window of a bare <c>kaset://play?v=…</c> Song still renders the
/// deterministic <c>i.ytimg.com/vi/{id}/hqdefault.jpg</c> art instead of nothing.</item>
/// <item><b>Failures leave a trace and recover.</b> A <see cref="BitmapImage"/> that cannot load
/// its URL fails silently by default — the exact "swallowed failure with no trace" that cost two
/// rounds on the album line (ADR 0007). <c>ImageFailed</c> now writes the failing URL to
/// <c>diag.log</c> and swaps the source to the fallback in place, so a queue entry pinned to an
/// unloadable URL (the gaps-only merges in PlayerService/QueueService keep whichever URL arrived
/// first) degrades to the stock thumbnail instead of a permanently empty cover.</item>
/// <item><b>One download per URL.</b> Conversions re-run on every <c>Bindings.Update()</c> pass;
/// recreating the <see cref="BitmapImage"/> each time restarts the download. A small cache keyed
/// by URL returns the same instance instead.</item>
/// </list>
/// Also accepts a bare <see cref="Uri"/>/string (no fallback available then).
/// </summary>
public sealed partial class CoverArtConverter : IValueConverter
{
    // Reuse per URL so repeated binding passes do not restart the download. UI-thread only
    // (converters run during binding evaluation). Bounded: reset once it outgrows a shell's worth
    // of concurrently visible covers.
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        Uri? fallback = value is Song { VideoId.Length: > 0 } withId ? withId.FallbackThumbnailUrl : null;
        Uri? primary = value switch
        {
            Song song => song.ThumbnailUrl ?? fallback,
            Uri uri => uri,
            string s when Uri.TryCreate(s, UriKind.Absolute, out var uri) => uri,
            _ => null,
        };

        return primary is null ? null : GetOrCreate(primary, fallback);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static BitmapImage GetOrCreate(Uri primary, Uri? fallback)
    {
        string key = primary.AbsoluteUri;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (Cache.Count > 16)
        {
            Cache.Clear();
        }

        var image = new BitmapImage();
        image.ImageOpened += (_, _) => KasetWin.Core.Diag.Write($"cover art OK url={key}");
        image.ImageFailed += (_, e) =>
        {
            KasetWin.Core.Diag.Write(
                $"cover art FAILED url={image.UriSource?.AbsoluteUri ?? key} err={e.ErrorMessage}");
            // Drop the failed entry so a later conversion retries from scratch, then degrade this
            // instance (and every Image showing it) to the deterministic fallback.
            Cache.Remove(key);
            if (fallback is not null
                && !string.Equals(image.UriSource?.AbsoluteUri, fallback.AbsoluteUri, StringComparison.Ordinal))
            {
                image.UriSource = fallback;
            }
        };
        image.UriSource = primary;
        Cache[key] = image;
        return image;
    }
}

/// <summary>
/// Maps <c>IPlayerService.IsPlaying</c> to a Segoe Fluent Icons glyph: a pause glyph while
/// playing, a play glyph while paused (Req 5.1).
/// </summary>
public sealed partial class PlayPauseGlyphConverter : IValueConverter
{
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? PauseGlyph : PlayGlyph;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps <c>IPlayerService.RepeatMode</c> to a Segoe Fluent Icons glyph (Req 5.8). "Repeat one"
/// shows a distinct glyph; "Off" and "All" share the repeat-all glyph (the active state is
/// conveyed separately via <see cref="RepeatModeToOpacityConverter"/>).
/// </summary>
public sealed partial class RepeatGlyphConverter : IValueConverter
{
    private const string RepeatAllGlyph = "\uE8EE";
    private const string RepeatOneGlyph = "\uE8ED";

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is RepeatMode.One ? RepeatOneGlyph : RepeatAllGlyph;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps <c>IPlayerService.RepeatMode</c> to an opacity so an enabled repeat mode (All/One) reads
/// as "on" and <see cref="RepeatMode.Off"/> reads as "off".
/// </summary>
public sealed partial class RepeatModeToOpacityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is RepeatMode.Off or null ? 0.5 : 1.0;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a boolean "active" flag (e.g. <c>IsShuffled</c>) to an opacity so the toggle reads as
/// on/off without a custom control template (Req 5.7).
/// </summary>
public sealed partial class BoolToOpacityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 1.0 : 0.5;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps <c>IPlayerService.IsMuted</c> to a Segoe Fluent Icons volume glyph (Req 5.6).
/// </summary>
public sealed partial class MuteGlyphConverter : IValueConverter
{
    private const string VolumeGlyph = "\uE767";
    private const string MuteGlyph = "\uE74F";

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? MuteGlyph : VolumeGlyph;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Formats a number of seconds (a <see cref="double"/>) as <c>m:ss</c> (or <c>h:mm:ss</c>) for the
/// player bar's position/duration labels (Req 2.1).
/// </summary>
public sealed partial class SecondsToTimeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var seconds = value switch
        {
            double d when !double.IsNaN(d) && !double.IsInfinity(d) && d > 0 => d,
            _ => 0d,
        };

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps <c>IPlayerService.IsLive</c> to a <see cref="Visibility"/> for the LIVE indicator (Req 9.1).
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps the current <see cref="Song"/> to the player-bar cover dimension, adapting the aspect to
/// what is playing: a music video (OMV) gets a 16:9 cover, everything else a square one. The
/// converter parameter selects the axis — <c>"w"</c> (width) or <c>"h"</c> (height).
/// </summary>
public sealed partial class CoverDimensionConverter : IValueConverter
{
    private const double CoverHeight = 34;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // 16:9 when the track is a music video by metadata (Omv/Ugc) OR the player reported real
        // video frames for it (HasVideo) — the latter catches videos the queue metadata mislabels as
        // plain songs, so their cover no longer renders as a 1:1 square.
        var isVideo = value is Song song
            && (song.VideoType is MusicVideoType.Omv or MusicVideoType.Ugc || song.HasVideo == true);
        var wantWidth = string.Equals(parameter as string, "w", StringComparison.OrdinalIgnoreCase);
        if (!wantWidth)
        {
            return CoverHeight;
        }

        return isVideo ? CoverHeight * 16.0 / 9.0 : CoverHeight;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a track's <see cref="KasetWin.Core.Models.LikeStatus"/> to the like/dislike glyph, swapping
/// the outline thumb for the SOLID (filled) thumb when this button's rating is active, so a liked
/// track reads as a filled icon rather than just a brighter outline. The converter parameter selects
/// the button — <c>"like"</c> or <c>"dislike"</c>.
/// </summary>
public sealed partial class LikeStatusToGlyphConverter : IValueConverter
{
    // Segoe Fluent Icons: outline thumbs (E8E1/E8E0) vs solid thumbs (E19D/E19E).
    private const string LikeOutline = "";
    private const string LikeSolid = "";
    private const string DislikeOutline = "";
    private const string DislikeSolid = "";

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as KasetWin.Core.Models.LikeStatus?;
        var isLikeButton = !string.Equals(parameter as string, "dislike", StringComparison.OrdinalIgnoreCase);
        if (isLikeButton)
        {
            return status == KasetWin.Core.Models.LikeStatus.Like ? LikeSolid : LikeOutline;
        }

        return status == KasetWin.Core.Models.LikeStatus.Dislike ? DislikeSolid : DislikeOutline;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a track's in-library flag to the collection menu label: "Hapus dari koleksi" when it is
/// already saved, "Simpan ke koleksi" otherwise.
/// </summary>
public sealed partial class LibraryToggleTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Localization.UiStrings.CollectionRemove : Localization.UiStrings.CollectionSave;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a track's <see cref="KasetWin.Core.Models.LikeStatus"/> to <see cref="Visibility"/> for the
/// filled/outline like/dislike icon swap. Parameter selects the case: <c>"like-on"</c>,
/// <c>"like-off"</c>, <c>"dislike-on"</c>, <c>"dislike-off"</c> — the "on" icon (solid, white) shows
/// when that rating is active, the "off" icon (outline) shows otherwise.
/// </summary>
public sealed partial class LikeStatusToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as KasetWin.Core.Models.LikeStatus?;
        var key = (parameter as string ?? string.Empty).ToLowerInvariant();
        var isLike = key.StartsWith("like", StringComparison.Ordinal);
        var wantOn = key.EndsWith("-on", StringComparison.Ordinal);

        var active = isLike
            ? status == KasetWin.Core.Models.LikeStatus.Like
            : status == KasetWin.Core.Models.LikeStatus.Dislike;

        return active == wantOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps the active library filter to a button <see cref="Style"/>: the chip matching the converter
/// parameter (a filter name) gets the accent style, the rest the default style — so the active filter
/// is visibly highlighted.
/// </summary>
public sealed partial class LibraryFilterToStyleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var active = string.Equals(value?.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase);
        var key = active ? "AccentButtonStyle" : "DefaultButtonStyle";
        return Application.Current.Resources[key];
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Collapses when the bound bool is true, shows when false (the inverse of the above).</summary>
public sealed partial class InvertedBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a track's <see cref="KasetWin.Core.Models.LikeStatus"/> to a foreground brush for the
/// like/dislike glyphs: the active rating shows white (fully opaque), the rest stay muted. The
/// converter parameter selects which button this is — <c>"like"</c> or <c>"dislike"</c>.
/// </summary>
public sealed partial class LikeStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Active = new(Microsoft.UI.Colors.White);
    private static readonly SolidColorBrush Muted = new(Microsoft.UI.Colors.White) { Opacity = 0.55 };

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as KasetWin.Core.Models.LikeStatus?;
        var isLikeButton = !string.Equals(parameter as string, "dislike", StringComparison.OrdinalIgnoreCase);
        var active = isLikeButton
            ? status == KasetWin.Core.Models.LikeStatus.Like
            : status == KasetWin.Core.Models.LikeStatus.Dislike;
        return active ? Active : Muted;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
