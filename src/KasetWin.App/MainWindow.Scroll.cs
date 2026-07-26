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
/// The shell's mouse-wheel policy: a plain vertical wheel always scrolls the PAGE (smoothly),
/// never a horizontal card rail sideways; Shift+wheel and a tilt wheel still pan the rail.
/// </summary>
/// <remarks>
/// <para>
/// Two generations of this file were wrong in two different ways, and the shape below exists
/// because of both. Attaching a swallow handler to every rail found by visual-tree enumeration
/// (generation one) lost to virtualization — rails realized after the sweep were never covered.
/// One central handler on the content frame (generation two) lost to ROUTING: the rail's own
/// ScrollViewer sits closer to the pointer than the frame, processes the wheel first, and has
/// already scrolled sideways by the time the central handler runs — which then scrolled the page
/// as well, one spin doing two things at once, "kacau".
/// </para>
/// <para>
/// The block therefore has to live BELOW the rail's ScrollViewer (on its content, so it marks the
/// event handled before the ScrollViewer sees it), and the attachment has to happen before the
/// first wheel tick. The trick: <c>PointerMoved</c> bubbles to the frame while the mouse travels
/// across a rail — always before that rail is wheeled — so the frame lazily attaches the swallow
/// to any rail the pointer passes over. No enumeration, no realization race, no first-notch loss.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private sealed class ScrollTarget
    {
        public double Value;
        public bool HasValue;
    }

    private readonly ConditionalWeakTable<ScrollViewer, ScrollTarget> _scrollTargets = new();

    /// <summary>Rail contents that already carry the swallow handler (weak: pages come and go).</summary>
    private readonly ConditionalWeakTable<UIElement, object> _attachedRails = new();

    private bool _wheelPolicyAttached;

    /// <summary>Attaches the wheel policy. Idempotent; called on navigation.</summary>
    private void HookPageScrolling()
    {
        if (_wheelPolicyAttached)
        {
            return;
        }

        _wheelPolicyAttached = true;

        // Lazy rail discovery: runs as the pointer travels, i.e. strictly before that rail can be
        // wheeled. handledEventsToo because item containers routinely mark moves handled.
        ContentFrame.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnFramePointerMoved),
            handledEventsToo: true);

        // Smooth vertical scrolling for whatever scroller the wheel was really aimed at.
        ContentFrame.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnFrameWheel),
            handledEventsToo: true);
    }

    private void OnFramePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin)
        {
            return;
        }

        for (DependencyObject? current = origin; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer sv && IsHorizontalRail(sv) && sv.Content is UIElement content
                && !_attachedRails.TryGetValue(content, out _))
            {
                _attachedRails.Add(content, new object());
                // On the CONTENT, below the ScrollViewer in the route: Handled is set before the
                // rail's own wheel processing runs, which is the entire point.
                content.AddHandler(
                    UIElement.PointerWheelChangedEvent,
                    new PointerEventHandler(OnRailWheel),
                    handledEventsToo: false);
            }
        }
    }

    private void OnRailWheel(object sender, PointerRoutedEventArgs e)
    {
        // A plain vertical wheel must not pan the rail sideways. Swallow it here; the frame's
        // OnFrameWheel (handledEventsToo) still scrolls the page vertically, so the spin does
        // exactly one thing. Shift+wheel / tilt wheel fall through and pan the rail.
        if (sender is UIElement element && IsPlainVerticalWheel(e, element, out _))
        {
            e.Handled = true;
        }
    }

    private void OnFrameWheel(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin
            || !IsPlainVerticalWheel(e, ContentFrame, out var delta))
        {
            return;
        }

        // First vertically-scrollable ancestor of what the wheel hit — skipping rails, which are
        // never a vertical wheel's target.
        ScrollViewer? vertical = null;
        for (DependencyObject? current = origin; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer sv && IsVerticallyScrollable(sv))
            {
                vertical = sv;
                break;
            }
        }

        if (vertical is null)
        {
            return;
        }

        SmoothScrollBy(vertical, -delta);
        e.Handled = true;
    }

    /// <summary>A horizontal card rail: pans sideways, nothing to scroll vertically.</summary>
    private static bool IsHorizontalRail(ScrollViewer sv) =>
        sv.HorizontalScrollMode != ScrollMode.Disabled
        && (sv.VerticalScrollMode == ScrollMode.Disabled || sv.ScrollableHeight <= 0)
        && sv.ScrollableWidth > 0;

    /// <summary>
    /// Whether <paramref name="sv"/> is a scroller a vertical wheel should drive. Mode alone is not
    /// enough: a rail's inner scroller can report <c>Auto</c> while having nothing to scroll
    /// vertically, so the actual scrollable extent is consulted too.
    /// </summary>
    private static bool IsVerticallyScrollable(ScrollViewer sv) =>
        sv.VerticalScrollMode != ScrollMode.Disabled && sv.ScrollableHeight > 0;

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
}
