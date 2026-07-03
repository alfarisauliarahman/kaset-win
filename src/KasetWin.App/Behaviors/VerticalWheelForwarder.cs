using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace KasetWin.App.Behaviors;

/// <summary>
/// Reusable attached behavior that fixes the "mouse wheel scrolls a horizontal shelf sideways"
/// bug on pages that nest a horizontal card shelf (an inner <see cref="ScrollViewer"/> with
/// <c>HorizontalScrollMode=Enabled</c> / <c>VerticalScrollMode=Disabled</c>) inside an outer
/// vertical page <see cref="ScrollViewer"/>.
/// </summary>
/// <remarks>
/// <para>
/// By default a horizontal <see cref="ScrollViewer"/> translates the standard (vertical) mouse-wheel
/// delta into a sideways scroll and marks the event handled, so the outer vertical scroller never
/// sees it and the page won't scroll while the cursor sits over a shelf. Touchpad panning is
/// unaffected, but mouse-wheel users (and Shift+wheel) get the wrong behavior.
/// </para>
/// <para>
/// When <see cref="IsEnabledProperty"/> is set on an inner horizontal shelf <see cref="ScrollViewer"/>,
/// this behavior intercepts <see cref="UIElement.PointerWheelChanged"/>:
/// <list type="bullet">
/// <item>A <b>vertical</b> wheel (standard wheel, no horizontal intent / no Shift) is <i>not</i>
/// consumed by the inner shelf — the delta is forwarded to the nearest ancestor vertical
/// <see cref="ScrollViewer"/> via <see cref="ScrollViewer.ChangeView(double?, double?, float?)"/>
/// and the event is marked handled so the shelf does not also scroll sideways.</item>
/// <item>A <b>horizontal</b> wheel (tilt wheel / <c>IsHorizontalMouseWheel</c>) or <b>Shift+wheel</b>
/// is left alone so the shelf scrolls sideways exactly as it does today.</item>
/// </list>
/// </para>
/// </remarks>
public static class VerticalWheelForwarder
{
    /// <summary>
    /// Attached property; set to <see langword="true"/> on an inner horizontal shelf
    /// <see cref="ScrollViewer"/> to enable vertical-wheel forwarding to the outer page scroller.
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(VerticalWheelForwarder),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            return;
        }

        // Attach the wheel handler on the shelf's CONTENT (the ItemsRepeater), not the ScrollViewer.
        // The wheel event bubbles up from a card THROUGH the content BEFORE the ScrollViewer's
        // internal ScrollContentPresenter scrolls it sideways. Handling it there (and marking it
        // handled) pre-empts the sideways scroll entirely — the previous approach used
        // handledEventsToo on the ScrollViewer, which ran only AFTER the shelf had already scrolled
        // horizontally, so the page and the shelf both moved (the "scroll goes both ways" bug).
        void Attach()
        {
            if (scrollViewer.Content is not UIElement content)
            {
                return;
            }

            content.RemoveHandler(UIElement.PointerWheelChangedEvent, (PointerEventHandler)OnPointerWheelChanged);
            if (GetIsEnabled(scrollViewer))
            {
                content.AddHandler(
                    UIElement.PointerWheelChangedEvent,
                    new PointerEventHandler(OnPointerWheelChanged),
                    handledEventsToo: false);
            }
        }

        if (scrollViewer.Content is not null)
        {
            Attach();
        }
        else
        {
            scrollViewer.Loaded += (_, _) => Attach();
        }
    }

    private static void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement content)
        {
            return;
        }

        var properties = e.GetCurrentPoint(content).Properties;

        // Horizontal (tilt) wheel → let it bubble to the shelf and scroll sideways as usual.
        if (properties.IsHorizontalMouseWheel)
        {
            return;
        }

        // Shift+wheel is an explicit horizontal-scroll intent → leave it to the shelf.
        if ((e.KeyModifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift)
        {
            return;
        }

        // Standard vertical wheel: scroll the outer page vertically and consume the event so the
        // inner horizontal shelf never turns it into a sideways scroll.
        var outer = FindAncestorVerticalScrollViewer(content);
        if (outer is null)
        {
            return;
        }

        outer.ChangeView(null, outer.VerticalOffset - properties.MouseWheelDelta, null);
        e.Handled = true;
    }

    /// <summary>
    /// Walks the visual tree up from <paramref name="start"/> and returns the first ancestor
    /// <see cref="ScrollViewer"/> that can scroll vertically (the outer page scroller), or
    /// <see langword="null"/> when none exists.
    /// </summary>
    private static ScrollViewer? FindAncestorVerticalScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer && scrollViewer.VerticalScrollMode != ScrollMode.Disabled)
            {
                return scrollViewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
