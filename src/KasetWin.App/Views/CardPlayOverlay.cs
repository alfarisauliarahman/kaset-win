using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Shared hover/focus behaviour for the circular Play overlay on Home/Explore/ExploreDetail/Artist
/// card shelves (Feature A). The overlay lives inside each card template named <c>PlayOverlay</c>;
/// this helper reveals it on pointer-over and keyboard focus and hides it otherwise, keeping the
/// behaviour DRY across every page that hosts card shelves.
/// </summary>
/// <remarks>
/// While hidden the overlay is also made non-hit-testable so a tap/click in the centre of the card
/// falls through to the card body (which navigates) rather than the invisible Play button. Keyboard
/// users can still Tab to the button (focus is independent of hit-testing); focusing reveals it and
/// Space/Enter activates it.
/// </remarks>
internal static class CardPlayOverlay
{
    /// <summary>Reveals the overlay for the card whose root element raised PointerEntered.</summary>
    public static void OnPointerEntered(object sender)
    {
        SetHover(sender, true);
        if (Find(sender) is { } overlay)
        {
            SetVisible(overlay, true);
        }
    }

    /// <summary>Hides the overlay on pointer-exit unless it currently holds keyboard focus.</summary>
    public static void OnPointerExited(object sender)
    {
        SetHover(sender, false);
        if (Find(sender) is { } overlay && overlay.FocusState == FocusState.Unfocused)
        {
            SetVisible(overlay, false);
        }
    }

    /// <summary>Reveals the overlay when it gains keyboard focus.</summary>
    public static void OnOverlayGotFocus(object sender)
    {
        if (sender is Button overlay)
        {
            SetVisible(overlay, true);
        }
    }

    /// <summary>Hides the overlay when it loses keyboard focus.</summary>
    public static void OnOverlayLostFocus(object sender)
    {
        if (sender is Button overlay)
        {
            SetVisible(overlay, false);
        }
    }

    private static Button? Find(object sender) =>
        (sender as FrameworkElement)?.FindName("PlayOverlay") as Button;

    /// <summary>
    /// Toggles the subtle hover background ("CardHover") that gives the Grid-based card root a
    /// ListViewItem-like pointer-over affordance now that the root is no longer a Button. Cards
    /// without a hover element (e.g. circular artist cards) simply no-op.
    /// </summary>
    private static void SetHover(object sender, bool visible)
    {
        if ((sender as FrameworkElement)?.FindName("CardHover") is UIElement hover)
        {
            hover.Opacity = visible ? 1 : 0;
        }
    }

    private static void SetVisible(Button overlay, bool visible)
    {
        overlay.Opacity = visible ? 1 : 0;
        overlay.IsHitTestVisible = visible;
    }
}
