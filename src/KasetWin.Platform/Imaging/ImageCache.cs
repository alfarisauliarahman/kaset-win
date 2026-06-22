using KasetWin.Core.Abstractions;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Platform.Imaging;

/// <summary>
/// Two-tier artwork cache (Req 16.2, Design "ImageCache"). Composes the pure, headless-testable
/// <c>Core</c> LRU primitives (<see cref="MemoryImageLru"/>, <see cref="DiskImageLru"/>,
/// <see cref="ImageCacheKey"/>) with an injectable <see cref="HttpClient"/> for download and an
/// <see cref="IImageDecoder"/> for downsampling. Concurrent identical fetches are coalesced via
/// single-flight (Req 16.3).
/// </summary>
/// <remarks>
/// Lookup order on <see cref="GetAsync"/>: in-memory LRU → disk LRU → network (download +
/// downsample). Disk and memory tiers are populated on a network hit, and a disk hit is promoted
/// back into memory. The disk tier lives under the app temporary folder by default.
/// </remarks>
public sealed class ImageCache : IImageCache, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IImageDecoder _decoder;
    private readonly ISingleFlight _singleFlight;
    private readonly ILogger<ImageCache> _logger;
    private readonly MemoryImageLru _memory;
    private readonly DiskImageLru _disk;

    /// <summary>
    /// Creates an image cache.
    /// </summary>
    /// <param name="httpClient">Client used to download artwork on a cache miss (injectable for tests).</param>
    /// <param name="decoder">Platform imaging seam used to downsample fetched images.</param>
    /// <param name="options">Optional cache limits / directory; defaults follow the design budget.</param>
    /// <param name="singleFlight">Optional request coalescer; a private instance is used when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    public ImageCache(
        HttpClient httpClient,
        IImageDecoder decoder,
        ImageCacheOptions? options = null,
        ISingleFlight? singleFlight = null,
        ILogger<ImageCache>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(decoder);

        _httpClient = httpClient;
        _decoder = decoder;
        _singleFlight = singleFlight ?? new SingleFlight();
        _logger = logger ?? NullLogger<ImageCache>.Instance;

        options ??= new ImageCacheOptions();
        _memory = new MemoryImageLru(options.MaxMemoryItems, options.MaxMemoryBytes);
        _disk = new DiskImageLru(options.CacheDirectory ?? ResolveCacheDirectory(), options.MaxDiskBytes);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(Uri url, int targetSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ct.ThrowIfCancellationRequested();

        var key = ImageCacheKey.For(url, targetSize);

        // Tier 1: in-memory.
        if (_memory.TryGet(key, out var cached) && cached is not null)
        {
            return cached;
        }

        // Tier 2: disk (promote into memory on hit).
        var fromDisk = await _disk.TryReadAsync(key, ct).ConfigureAwait(false);
        if (fromDisk is not null)
        {
            _memory.Set(key, fromDisk);
            return fromDisk;
        }

        // Tier 3: network. Coalesce concurrent identical fetches so only one download happens.
        try
        {
            return await _singleFlight.RunAsync(
                key,
                () => DownloadAndStoreAsync(key, url, targetSize, ct),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image fetch failed for a cache miss; returning null.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task PrefetchAsync(Uri url, int targetSize, CancellationToken ct = default)
    {
        try
        {
            await GetAsync(url, targetSize, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the item scrolls out of view; prefetch is best-effort.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Prefetch failed (best-effort).");
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
    {
        _memory.Clear();
        await _disk.ClearAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Disposes the disk tier's synchronization primitive.</summary>
    public void Dispose() => _disk.Dispose();

    private async Task<byte[]?> DownloadAndStoreAsync(string key, Uri url, int targetSize, CancellationToken ct)
    {
        var raw = await _httpClient.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        if (raw.Length == 0)
        {
            return null;
        }

        var bytes = await _decoder.DownsampleAsync(raw, targetSize, ct).ConfigureAwait(false);

        // Populate both tiers. Disk write must not fail the read path.
        _memory.Set(key, bytes);
        try
        {
            await _disk.WriteAsync(key, bytes, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Writing image to disk cache failed; memory cache still populated.");
        }

        return bytes;
    }

    // Prefer the packaged app's temporary folder; fall back to a process-temp path for unpackaged
    // / headless hosts where ApplicationData.Current is unavailable.
    private static string ResolveCacheDirectory()
    {
        try
        {
            var temp = Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
            return Path.Combine(temp, "ImageCache");
        }
        catch (InvalidOperationException)
        {
            return Path.Combine(Path.GetTempPath(), "KasetWin", "ImageCache");
        }
    }
}
