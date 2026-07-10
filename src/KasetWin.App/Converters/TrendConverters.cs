using KasetWin.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace KasetWin.App.Converters;

/// <summary>
/// Maps a <see cref="TrendDirection"/> to the Segoe Fluent glyph for its chart movement arrow
/// (solid up caret / down caret / dash), used by the chart playlist track rows. Returns an empty
/// string for <see cref="TrendDirection.None"/> so no icon shows.
/// </summary>
public sealed partial class TrendToGlyphConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is TrendDirection trend
            ? trend switch
            {
                TrendDirection.Up => "",      // CaretSolidUp
                TrendDirection.Down => "",    // CaretSolidDown
                TrendDirection.Neutral => "", // Remove (dash) — steady
                _ => string.Empty,
            }
            : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="TrendDirection"/> to the arrow colour: green rising, red falling, grey steady.
/// </summary>
public sealed partial class TrendToBrushConverter : IValueConverter
{
    private static readonly Brush Up = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B));
    private static readonly Brush Down = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x3E, 0x52));
    private static readonly Brush Neutral = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A));

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is TrendDirection.Up ? Up : value is TrendDirection.Down ? Down : Neutral;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Collapses when a chart rank is absent (<c>0</c>) and shows otherwise — gates the rank/arrow
/// column so ordinary (non-chart) playlist rows keep their normal layout.
/// </summary>
public sealed partial class RankToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int rank && rank > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
