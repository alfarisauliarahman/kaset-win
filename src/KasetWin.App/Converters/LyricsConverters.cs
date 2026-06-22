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
