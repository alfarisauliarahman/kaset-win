using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Accessibility;

/// <summary>
/// Accessibility helpers for the shell's icon-only controls.
///
/// A <c>ToolTipService.ToolTip</c> is <b>not</b> read by Narrator — an icon-only button with only a
/// tooltip is announced as an unnamed "button". Every such control therefore needs an
/// <c>AutomationProperties.Name</c> as well, and the two must say the same thing in the same
/// language. <see cref="Label"/> sets both from one call so they cannot drift apart, and so the
/// language-refresh paths (<c>ApplyLanguage</c>) keep the accessible name current for free.
/// </summary>
internal static class A11y
{
    /// <summary>
    /// Gives <paramref name="element"/> a localized accessible name and the matching tooltip.
    /// Null-safe on the element so a control hidden behind a feature flag never crashes a relabel.
    /// </summary>
    internal static void Label(DependencyObject? element, string text)
    {
        if (element is null)
        {
            return;
        }

        AutomationProperties.SetName(element, text);
        if (element is UIElement ui)
        {
            ToolTipService.SetToolTip(ui, text);
        }
    }

    /// <summary>
    /// Names a control for screen readers <b>without</b> attaching a tooltip. For controls whose
    /// purpose is not obvious from their value (sliders, list panes) but where a hover tooltip
    /// would be noise.
    /// </summary>
    internal static void Name(DependencyObject? element, string text)
    {
        if (element is not null)
        {
            AutomationProperties.SetName(element, text);
        }
    }

    /// <summary>
    /// Hides purely decorative content (album art, glyph-only adornments) from the accessibility
    /// tree, so Narrator does not stop on an image that repeats the adjacent text.
    /// </summary>
    internal static void Decorative(DependencyObject? element)
    {
        if (element is not null)
        {
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        }
    }
}
