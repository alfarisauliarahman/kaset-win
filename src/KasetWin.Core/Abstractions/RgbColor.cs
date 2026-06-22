namespace KasetWin.Core.Abstractions;

/// <summary>
/// A WinRT-free 24-bit RGB color value used to surface an extracted accent color across the
/// layer boundary (Req 16.2). The platform layer maps this onto <c>Windows.UI.Color</c> /
/// <c>Microsoft.UI.Colors</c> when applying it to UI; <c>Core</c> stays headless-testable.
/// </summary>
/// <param name="R">Red channel (0–255).</param>
/// <param name="G">Green channel (0–255).</param>
/// <param name="B">Blue channel (0–255).</param>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Returns the color as a CSS-style <c>#RRGGBB</c> hex string (handy for logging/UI binding).</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}
