using Microsoft.UI.Xaml;

namespace KasetWin.App.Behaviors;

/// <summary>
/// SUPERSEDED — kept only so existing <c>behaviors:VerticalWheelForwarder.IsEnabled="True"</c> markup
/// still compiles. Mouse-wheel handling (smooth vertical scrolling + swallowing a vertical wheel over
/// a horizontal card shelf so it doesn't scroll sideways) now lives in a single shell-wide handler,
/// <c>MainWindow.Scroll.cs</c>, which covers every page and every shelf — including shelves that are
/// virtualized into view later — without per-page wiring. This attached property is now a no-op.
/// </summary>
public static class VerticalWheelForwarder
{
    /// <summary>No-op attached property retained for markup compatibility (see the type remarks).</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(VerticalWheelForwarder),
            new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
}
