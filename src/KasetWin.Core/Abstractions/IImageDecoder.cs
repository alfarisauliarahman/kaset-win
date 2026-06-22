namespace KasetWin.Core.Abstractions;

/// <summary>
/// Imaging I/O seam (Req 16.2). Isolates the only genuinely platform-specific part of artwork
/// handling — decoding, downsampling and pixel averaging — behind a WinRT-free contract so the
/// caching/keying/LRU logic can stay in <c>KasetWin.Core</c> and be tested headless.
/// </summary>
/// <remarks>
/// The platform layer implements this with <c>Windows.Graphics.Imaging.BitmapDecoder</c> /
/// <c>BitmapEncoder</c>. Tests provide an in-memory fake (e.g. identity downsample + fixed color).
/// </remarks>
public interface IImageDecoder
{
    /// <summary>
    /// Decodes <paramref name="source"/> and re-encodes it scaled so its longer edge is at most
    /// <paramref name="targetSize"/> pixels, preserving aspect ratio. When
    /// <paramref name="targetSize"/> is &lt;= 0, or the source is already smaller, the original
    /// bytes may be returned unchanged. Downsampling is best-effort: on decode failure the
    /// implementation returns <paramref name="source"/> unchanged rather than throwing.
    /// </summary>
    /// <param name="source">Encoded source image bytes (PNG/JPEG/etc.).</param>
    /// <param name="targetSize">Target longer-edge size in pixels; <c>0</c> or negative disables scaling.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Encoded (possibly downsampled) image bytes.</returns>
    Task<byte[]> DownsampleAsync(byte[] source, int targetSize, CancellationToken ct = default);

    /// <summary>
    /// Decodes <paramref name="source"/> and returns the average color of its (optionally
    /// downscaled) pixels, ignoring fully transparent pixels. Returns <see langword="null"/> when
    /// the image cannot be decoded or contains no opaque pixels.
    /// </summary>
    Task<RgbColor?> AverageColorAsync(byte[] source, CancellationToken ct = default);
}
