using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace KasetWin.App.Views;

/// <summary>
/// Shared zebra-striping for virtualized track lists (Album/Playlist pages).
/// </summary>
/// <remarks>
/// The stripe is painted on the row's TEMPLATE ROOT, not on the <see cref="SelectorItem"/> itself:
/// the ListViewItem presenter caches its background and only re-rendered a late change on a
/// visual-state transition — rows looked unstriped until the pointer hovered them.
/// </remarks>
internal static class ZebraStriping
{
    /// <summary>Applies the zebra background to a track row: odd rows get a subtle fill.</summary>
    public static void StripeRow(SelectorItem container, int index)
    {
        var brush = index % 2 == 1
            ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (container.ContentTemplateRoot is Panel root)
        {
            root.Background = brush;
        }
        else
        {
            // Non-panel template root (unexpected): keep the old container paint as a fallback.
            container.Background = brush;
        }
    }
}
