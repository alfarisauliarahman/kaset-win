using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace KasetWin.App;

/// <summary>
/// Global mouse-wheel smoothing + horizontal-shelf disambiguation, applied to every page the shell
/// hosts (no per-page XAML wiring). Two problems are solved at once, everywhere:
/// <list type="bullet">
/// <item><b>Jumpy vertical scrolling.</b> The page's outer scroller is driven by an accumulated,
/// animated <see cref="ScrollViewer.ChangeView(double?, double?, float?)"/> target instead of the
/// framework's stepped wheel scroll, so a wheel spin glides instead of snapping line by line.</item>
/// <item><b>Vertical wheel scrolling a horizontal card rail sideways.</b> A plain vertical wheel over
/// a horizontal shelf is swallowed at the shelf (so it does not scroll sideways) while the outer page
/// still scrolls vertically — no more "one spin does two things". Shift+wheel / tilt wheel still pans
/// the shelf.</item>
/// </list>
/// Handlers are (re)attached on navigation and, for virtualized section lists, as each section is
/// realized (<see cref="ItemsRepeater.ElementPrepared"/>), so shelves scrolled into view later are
/// covered too.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class ScrollTarget
    {
        public double Value;
        public bool HasValue;
    }

    private readonly ConditionalWeakTable<ScrollViewer, ScrollTarget> _scrollTargets = new();

    /// <summary>Wires smooth-scroll + shelf handling for the page currently in the content frame.</summary>
    private void HookPageScrolling()
    {
        if (ContentFrame.Content is not FrameworkElement page)
        {
            return;
        }

        void Hook()
        {
            // 1) Smooth vertical wheel on the page's outer scroller (handledEventsToo so it still runs
            //    for wheel a shelf swallowed).
            if (FindPageScrollViewer(page) is { Content: UIElement outerContent })
            {
                outerContent.RemoveHandler(UIElement.PointerWheelChangedEvent, (PointerEventHandler)OnPageWheel);
                outerContent.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPageWheel), handledEventsToo: true);
            }

            // 2) Swallow vertical wheel on every realized horizontal shelf.
            AttachShelvesIn(page);

            // 3) Cover shelves that realize later (virtualized section lists).
            foreach (var repeater in FindDescendants<ItemsRepeater>(page))
            {
                repeater.ElementPrepared -= OnSectionRealized;
                repeater.ElementPrepared += OnSectionRealized;
            }
        }

        if (page.IsLoaded)
        {
            Hook();
        }
        else
        {
            page.Loaded += (_, _) => Hook();
        }
    }

    private void OnSectionRealized(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is DependencyObject element)
        {
            AttachShelvesIn(element);
        }
    }

    /// <summary>Attaches the vertical-wheel swallow to every horizontal-only shelf under <paramref name="root"/>.</summary>
    private void AttachShelvesIn(DependencyObject root)
    {
        foreach (var sv in FindDescendants<ScrollViewer>(root))
        {
            if (sv.HorizontalScrollMode == ScrollMode.Enabled
                && sv.VerticalScrollMode == ScrollMode.Disabled
                && sv.Content is UIElement content)
            {
                content.RemoveHandler(UIElement.PointerWheelChangedEvent, (PointerEventHandler)OnShelfWheel);
                content.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnShelfWheel), handledEventsToo: false);
            }
        }
    }

    private void OnPageWheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || !IsPlainVerticalWheel(e, element, out var delta))
        {
            return;
        }

        if (FindAncestorVerticalScrollViewer(element) is not { } scrollViewer)
        {
            return;
        }

        SmoothScrollBy(scrollViewer, -delta);
        e.Handled = true;
    }

    private void OnShelfWheel(object sender, PointerRoutedEventArgs e)
    {
        // A plain vertical wheel must not scroll the shelf sideways — swallow it here (the page's
        // OnPageWheel, registered handledEventsToo, still performs the vertical scroll). Shift+wheel
        // and tilt wheel fall through so they still pan the shelf.
        if (sender is UIElement element && IsPlainVerticalWheel(e, element, out _))
        {
            e.Handled = true;
        }
    }

    private static bool IsPlainVerticalWheel(PointerRoutedEventArgs e, UIElement relativeTo, out int delta)
    {
        var properties = e.GetCurrentPoint(relativeTo).Properties;
        delta = properties.MouseWheelDelta;
        if (properties.IsHorizontalMouseWheel)
        {
            return false;
        }

        return (e.KeyModifiers & VirtualKeyModifiers.Shift) != VirtualKeyModifiers.Shift;
    }

    /// <summary>Glides <paramref name="scrollViewer"/> toward an accumulated target so rapid notches feel smooth.</summary>
    private void SmoothScrollBy(ScrollViewer scrollViewer, double delta)
    {
        var target = _scrollTargets.GetValue(scrollViewer, static _ => new ScrollTarget());

        // Accumulate on the pending target while an animation is still in flight; if the offset has
        // diverged (the user dragged the scrollbar / a jump happened), re-base on the live offset.
        var settleWindow = Math.Max(scrollViewer.ViewportHeight, 240);
        var basis = target.HasValue && Math.Abs(scrollViewer.VerticalOffset - target.Value) <= settleWindow
            ? target.Value
            : scrollViewer.VerticalOffset;

        var next = Math.Clamp(basis + delta, 0, scrollViewer.ScrollableHeight);
        target.Value = next;
        target.HasValue = true;
        scrollViewer.ChangeView(null, next, null);
    }

    /// <summary>The page's main vertical scroller (skips horizontal shelves, which disable vertical scroll).</summary>
    private static ScrollViewer? FindPageScrollViewer(DependencyObject root)
    {
        foreach (var sv in FindDescendants<ScrollViewer>(root))
        {
            if (sv.VerticalScrollMode != ScrollMode.Disabled)
            {
                return sv;
            }
        }

        return null;
    }

    private static ScrollViewer? FindAncestorVerticalScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is ScrollViewer sv && sv.VerticalScrollMode != ScrollMode.Disabled)
            {
                return sv;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>Depth-first enumeration of visual descendants of type <typeparamref name="T"/>.</summary>
    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
