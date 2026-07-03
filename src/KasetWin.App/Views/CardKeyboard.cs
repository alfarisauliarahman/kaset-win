using Windows.System;

namespace KasetWin.App.Views;

/// <summary>
/// Keyboard helpers shared by the Grid-based card roots on Home/Explore/ExploreDetail/Artist shelves.
/// Because the card root is a <c>Grid</c> (not a <c>Button</c> — a nested Play Button would break a
/// Button root's hit-testing), it does not raise <c>Click</c> for Enter/Space on its own; each page
/// wires a <c>KeyDown</c> handler that uses this helper to activate the card from the keyboard.
/// </summary>
internal static class CardKeyboard
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/> is the standard activation key
    /// (Enter or Space) that should invoke a focused card.
    /// </summary>
    public static bool IsActivationKey(VirtualKey key) =>
        key is VirtualKey.Enter or VirtualKey.Space;
}
