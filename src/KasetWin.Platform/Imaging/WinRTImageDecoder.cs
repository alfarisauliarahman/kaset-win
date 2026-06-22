using KasetWin.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KasetWin.Platform.Imaging;

/// <summary>
/// <see cref="IImageDecoder"/> implemented with <c>Windows.Graphics.Imaging</c>
/// (<see cref="BitmapDecoder"/>/<see cref="BitmapEncoder"/>). Provides the platform-specific
/// imaging I/O — downsampling and pixel averaging — behind the WinRT-free Core seam so the cache
/// keying/LRU logic stays headless-testable (Req 16.2, Design: "ColorExtractor" /
/// "BitmapDecoder averaging").
/// </summary>
/// <remarks>
/// No third-party imaging dependency (no Win2D) is required: the WinRT codecs ship with the OS.
/// Both operations are best-effort — on decode failure <see cref="DownsampleAsync"/> returns the
/// original bytes and <see cref="AverageColorAsync"/> returns <see langword="null"/>.
/// </remarks>
public sealed class WinRTImageDecoder : IImageDecoder
{
    // Average color is computed from a small thumbnail for speed; full resolution is unnecessary.
    private const uint ColorSampleEdge = 16;

    private readonly ILogger<WinRTImageDecoder> _logger;

    /// <summary>Creates a decoder with an optional logger (defaults to a no-op logger).</summary>
    public WinRTImageDecoder(ILogger<WinRTImageDecoder>? logger = null)
        => _logger = logger ?? NullLogger<WinRTImageDecoder>.Instance;

    /// <inheritdoc />
    public async Task<byte[]> DownsampleAsync(byte[] source, int targetSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (targetSize <= 0 || source.Length == 0)
        {
            return source;
        }

        try
        {
            using var inStream = new InMemoryRandomAccessStream();
            await WriteBytesAsync(inStream, source, ct).ConfigureAwait(false);
            inStream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(inStream).AsTask(ct).ConfigureAwait(false);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            var longest = Math.Max(width, height);

            // Already within the requested size: keep the original encoded bytes.
            if (longest <= (uint)targetSize)
            {
                return source;
            }

            var scale = (double)targetSize / longest;
            var scaledWidth = Math.Max(1u, (uint)Math.Round(width * scale));
            var scaledHeight = Math.Max(1u, (uint)Math.Round(height * scale));

            var pixelProvider = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform
                {
                    ScaledWidth = scaledWidth,
                    ScaledHeight = scaledHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                },
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

            var pixels = pixelProvider.DetachPixelData();

            using var outStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream)
                .AsTask(ct).ConfigureAwait(false);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                scaledWidth,
                scaledHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixels);
            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);

            outStream.Seek(0);
            return await ReadBytesAsync(outStream, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort: fall back to the original bytes on any codec failure.
            _logger.LogWarning(ex, "Downsample failed; returning original image bytes.");
            return source;
        }
    }

    /// <inheritdoc />
    public async Task<RgbColor?> AverageColorAsync(byte[] source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0)
        {
            return null;
        }

        try
        {
            using var inStream = new InMemoryRandomAccessStream();
            await WriteBytesAsync(inStream, source, ct).ConfigureAwait(false);
            inStream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(inStream).AsTask(ct).ConfigureAwait(false);
            var width = Math.Min(ColorSampleEdge, decoder.PixelWidth);
            var height = Math.Min(ColorSampleEdge, decoder.PixelHeight);
            if (width == 0 || height == 0)
            {
                return null;
            }

            // Use straight (non-premultiplied) alpha so transparent pixels can be excluded cleanly.
            var pixelProvider = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform
                {
                    ScaledWidth = width,
                    ScaledHeight = height,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

            var pixels = pixelProvider.DetachPixelData();
            return AveragePixels(pixels);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Average color extraction failed.");
            return null;
        }
    }

    // Writes raw bytes into a WinRT random-access stream via DataWriter (no buffer-interop extensions).
    private static async Task WriteBytesAsync(IRandomAccessStream stream, byte[] bytes, CancellationToken ct)
    {
        var writer = new DataWriter(stream);
        try
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(ct).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }
        finally
        {
            writer.DetachStream();
            writer.Dispose();
        }
    }

    // Reads the full contents of a WinRT random-access stream via DataReader.
    private static async Task<byte[]> ReadBytesAsync(IRandomAccessStream stream, CancellationToken ct)
    {
        var size = (uint)stream.Size;
        var reader = new DataReader(stream);
        try
        {
            await reader.LoadAsync(size).AsTask(ct).ConfigureAwait(false);
            var result = new byte[size];
            reader.ReadBytes(result);
            return result;
        }
        finally
        {
            reader.DetachStream();
            reader.Dispose();
        }
    }

    // Averages BGRA8 pixel data, weighting by alpha and skipping fully transparent pixels.
    private static RgbColor? AveragePixels(byte[] bgra)
    {
        if (bgra.Length < 4)
        {
            return null;
        }

        ulong sumR = 0, sumG = 0, sumB = 0, weight = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var b = bgra[i];
            var g = bgra[i + 1];
            var r = bgra[i + 2];
            var a = bgra[i + 3];
            if (a == 0)
            {
                continue;
            }

            sumR += (ulong)r * a;
            sumG += (ulong)g * a;
            sumB += (ulong)b * a;
            weight += a;
        }

        if (weight == 0)
        {
            return null;
        }

        return new RgbColor(
            (byte)(sumR / weight),
            (byte)(sumG / weight),
            (byte)(sumB / weight));
    }
}
