namespace KasetWin.Core.Abstractions;

/// <summary>
/// Extracts a representative accent color from artwork (Req 16.2, Design "ColorExtractor").
/// Windows counterpart of the macOS <c>CIAreaAverage</c>-based extractor: the platform layer
/// averages decoded pixels via <c>Windows.Graphics.Imaging.BitmapDecoder</c>.
/// </summary>
public interface IColorExtractor
{
    /// <summary>
    /// Returns the dominant (average) color for the artwork at <paramref name="url"/>, or
    /// <see langword="null"/> when the image cannot be fetched or decoded.
    /// </summary>
    Task<RgbColor?> DominantColorAsync(Uri url, CancellationToken ct = default);

    /// <summary>
    /// Returns the dominant (average) color for already-loaded encoded image
    /// <paramref name="imageBytes"/>, or <see langword="null"/> when decoding fails.
    /// </summary>
    Task<RgbColor?> DominantColorAsync(byte[] imageBytes, CancellationToken ct = default);
}
