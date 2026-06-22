using KasetWin.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Platform.Imaging;

/// <summary>
/// <see cref="IColorExtractor"/> that derives an accent color by averaging decoded artwork pixels
/// (Req 16.2, Design "ColorExtractor" — Windows counterpart of the macOS <c>CIAreaAverage</c>
/// extractor). Fetching reuses <see cref="IImageCache"/> so accent extraction shares the artwork
/// cache, and pixel averaging is delegated to the platform <see cref="IImageDecoder"/>.
/// </summary>
public sealed class ColorExtractor : IColorExtractor
{
    // Fetch the full-resolution image for averaging; the decoder downscales internally for speed.
    private const int OriginalSize = 0;

    private readonly IImageCache _imageCache;
    private readonly IImageDecoder _decoder;
    private readonly ILogger<ColorExtractor> _logger;

    /// <summary>Creates a color extractor over the shared image cache and platform decoder.</summary>
    public ColorExtractor(IImageCache imageCache, IImageDecoder decoder, ILogger<ColorExtractor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(imageCache);
        ArgumentNullException.ThrowIfNull(decoder);
        _imageCache = imageCache;
        _decoder = decoder;
        _logger = logger ?? NullLogger<ColorExtractor>.Instance;
    }

    /// <inheritdoc />
    public async Task<RgbColor?> DominantColorAsync(Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var bytes = await _imageCache.GetAsync(url, OriginalSize, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        return await DominantColorAsync(bytes, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RgbColor?> DominantColorAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            return null;
        }

        var color = await _decoder.AverageColorAsync(imageBytes, ct).ConfigureAwait(false);
        if (color is null)
        {
            _logger.LogDebug("Dominant color extraction yielded no opaque pixels.");
        }

        return color;
    }
}
