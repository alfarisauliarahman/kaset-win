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
/// One central mouse-wheel policy for everything the content frame hosts: a plain vertical wheel
/// always scrolls the PAGE (smoothly), never a horizontal card rail sideways; Shift+wheel and a
/// tilt wheel still pan the rail.
/// </summary>
/// <remarks>
/// <para>
/// The previous design attached a swallow handler to each horizontal shelf it could find in the
/// visual tree at navigation time, plus <c>ItemsRepeater.ElementPrepared</c> for late arrivals.
/// That enumeration was structurally incomplete: shelves inside virtualized <c>GridView</c>/
/// <c>ListView</c> items realize long after <c>Loaded</c>, and a shelf that was never attached fell
/// back to default WinUI routing — the wheel scrolled the rail sideways AND the page handler
/// (registered <c>handledEventsToo</c>) scrolled vertically, one spin doing two things at once.
/// Three rounds of testing reported it "still mixed" because every round left different shelves
/// unenumerated.
/// </para>
/// <para>
/// So the policy now lives at exactly one place, the content frame, attached once and inspecting
/// the ancestor chain of whatever the wheel actually hit. There is nothing to enumerate and no
/// realization timing to lose a race to.
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

    private bool _wheelPolicyAttached;

    /// <summary>Attaches the single wheel policy handler. Idempotent; called on navigation.</summary>
    private void HookPageScrolling()
    {
        if (_wheelPolicyAttached)
        {
            return;
        }

        _wheelPolicyAttached = true;

        // handledEventsToo: a shelf's own ScrollViewer marks wheel events handled while it scrolls
        // them; the policy must see the event regardless, or the rail keeps winning.
        ContentFrame.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnFrameWheel),
            handledEventsToo: true);
    }

    private void OnFrameWheel(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin
            || !IsPlainVerticalWheel(e, ContentFrame, out var delta))
        {
            return; // Shift+wheel / tilt wheel: let the rail pan.
        }

        // Walk up from what the wheel hit. The first scroller decides:
        //  - vertical-capable → that is the scroller the user means; glide it.
        //  - horizontal-only (a card rail) → skip it and keep walking to the page scroller, so the
        //    rail never moves sideways on a vertical wheel.
        ScrollViewer? vertical = null;
        for (DependencyObject? current = origin; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer sv)
            {
                if (IsVerticallyScrollable(sv))
                {
                    vertical = sv;
                    break;
                }

                // Horizontal-only rail: deliberately NOT the target; continue upward.
            }
        }

        if (vertical is null)
        {
            return;
        }

        SmoothScrollBy(vertical, -delta);
        e.Handled = true;
    }

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
