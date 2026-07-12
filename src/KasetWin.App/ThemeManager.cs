using KasetWin.Platform.Storage;
using Microsoft.UI.Xaml;

namespace KasetWin.App;

/// <summary>
/// Applies and persists the app-wide light/dark theme. The XAML already uses theme-aware
/// <c>ThemeResource</c> brushes + a Mica backdrop, so switching is just a matter of stamping
/// <see cref="FrameworkElement.RequestedTheme"/> on the window root. Persisted via
/// <see cref="AppData.Settings"/> (same lightweight store the equalizer uses) so it survives
/// relaunches — in both packaged and standalone (.exe) modes.
/// </summary>
public static class ThemeManager
{
    private const string ThemeKey = "appearance.theme";

    /// <summary>The persisted theme, defaulting to <see cref="ElementTheme.Default"/> (follow the OS).</summary>
    public static ElementTheme Current
    {
        get => AppData.Settings[ThemeKey] switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        private set => AppData.Settings[ThemeKey] =
            value switch { ElementTheme.Light => "Light", ElementTheme.Dark => "Dark", _ => "Default" };
    }

    /// <summary>Persists <paramref name="theme"/> and applies it to the live window immediately.</summary>
    public static void Set(ElementTheme theme)
    {
        Current = theme;
        Apply();
    }

    /// <summary>Stamps the current theme onto the window root; safe to call once the shell exists.</summary>
    public static void Apply()
    {
        if (App.Current.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = Current;
        }
    }
}
