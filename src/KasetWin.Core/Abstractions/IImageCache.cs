namespace KasetWin.Core.Abstractions;

/// <summary>
/// Two-tier (memory + disk) artwork cache with downsampling and lifecycle-aware prefetch
/// (Req 16.2, Design "ImageCache" / "Caching &amp; Performa"). Faithful Windows counterpart of the
/// macOS <c>NSCache</c>+disk image cache.
/// </summary>
/// <remarks>
/// <para>
/// Images are keyed by source <see cref="Uri"/> <em>and</em> requested display size, so different
/// render sizes are cached independently. Lookups consult the bounded in-memory LRU first
/// (~50&#160;MB / ~200 items), then the disk LRU (~200&#160;MB), and finally download via an
/// injected <c>HttpClient</c>; freshly fetched bytes are downsampled to the display size and
/// promoted back into both tiers.
/// </para>
/// <para>
/// The pure cache-keying and LRU-eviction logic lives in <c>KasetWin.Core</c> (see
/// <c>ImageCacheKey</c>, <c>MemoryImageLru</c>, <c>DiskImageLru</c>) and is headless-testable;
/// the imaging I/O (decode/downsample/averaging) is delegated to the platform via
/// <see cref="IImageDecoder"/>.
/// </para>
/// </remarks>
public interface IImageCache
{
    /// <summary>
    /// Returns the (possibly downsampled) encoded image bytes for <paramref name="url"/> at the
    /// requested <paramref name="targetSize"/>, fetching and caching on miss.
    /// </summary>
    /// <param name="url">Absolute source URL of the artwork.</param>
    /// <param name="targetSize">
    /// Target display size in pixels for the longer edge. A value of <c>0</c> (or negative) means
    /// "do not downsample" and the original bytes are cached/returned.
    /// </param>
    /// <param name="ct">Cancellation token following the consumer's lifecycle.</param>
    /// <returns>The encoded image bytes, or <see langword="null"/> when the image could not be obtained.</returns>
    Task<byte[]?> GetAsync(Uri url, int targetSize, CancellationToken ct = default);

    /// <summary>
    /// Warms the cache for <paramref name="url"/> at <paramref name="targetSize"/> without
    /// returning the bytes. Honors <paramref name="ct"/> so windowed prefetch can be cancelled as
    /// items scroll out of view (Req 16.2). Failures are swallowed — prefetch is best-effort.
    /// </summary>
    Task PrefetchAsync(Uri url, int targetSize, CancellationToken ct = default);

    /// <summary>Clears both the in-memory and on-disk cache tiers.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
