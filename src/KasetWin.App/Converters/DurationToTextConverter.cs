using Microsoft.UI.Xaml.Data;

namespace KasetWin.App.Converters;

/// <summary>
/// Formats a nullable <see cref="TimeSpan"/> (e.g. <c>Song.Duration</c>) as <c>m:ss</c>
/// (or <c>h:mm:ss</c> for hour-long tracks) for the playlist/album track rows (Task 14.6).
/// Returns an empty string when the duration is missing so the row simply omits the label.
/// </summary>
public sealed partial class DurationToTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not TimeSpan time || time <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
