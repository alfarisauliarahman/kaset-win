using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;

namespace KasetWin.App.Converters;

/// <summary>
/// Maps a synced lyric line's <c>IsActive</c> flag to a <see cref="FontWeight"/> so the current
/// line renders bold while upcoming/past lines stay normal (Req 17.2). Paired with
/// <see cref="BoolToOpacityConverter"/> for the dimming of inactive lines.
/// </summary>
public sealed partial class ActiveLineToFontWeightConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? FontWeights.SemiBold : FontWeights.Normal;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a synced lyric line's <c>IsActive</c> flag to a font size so the current line renders larger
/// (Apple-Music style), while past/upcoming lines stay at the base size.
/// </summary>
public sealed partial class ActiveLineToFontSizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 28.0 : 20.0;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a synced lyric line's <c>IsActive</c> flag to a <see cref="System.Numerics.Vector3"/> scale
/// so the current line grows slightly. Paired with a <c>ScaleTransition</c> in the template, this
/// animates the grow/shrink smoothly as the active line changes (Apple-Music style).
/// </summary>
public sealed partial class ActiveLineToScaleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? new System.Numerics.Vector3(1.06f, 1.06f, 1f) : System.Numerics.Vector3.One;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
