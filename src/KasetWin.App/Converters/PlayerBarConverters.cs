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
