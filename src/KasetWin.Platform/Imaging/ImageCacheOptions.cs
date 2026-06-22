namespace KasetWin.Platform.Imaging;

/// <summary>
/// Tunable limits and storage location for <see cref="ImageCache"/> (Req 16.2). Defaults follow the
/// design budget: ~200 items / ~50&#160;MB in memory and ~200&#160;MB on disk.
/// </summary>
public sealed class ImageCacheOptions
{
    /// <summary>
    /// Directory for the on-disk cache tier. When <see langword="null"/>, <see cref="ImageCache"/>
    /// resolves the app's temporary folder (<c>ApplicationData.Current.TemporaryFolder</c>) with a
    /// process-temp fallback for unpackaged/test hosts.
    /// </summary>
    public string? CacheDirectory { get; init; }

    /// <summary>Maximum number of entries held in the in-memory tier.</summary>
    public int MaxMemoryItems { get; init; } = 200;

    /// <summary>Maximum total payload bytes held in the in-memory tier (~50&#160;MB).</summary>
    public long MaxMemoryBytes { get; init; } = 50L * 1024 * 1024;

    /// <summary>Maximum total bytes held in the on-disk tier (~200&#160;MB).</summary>
    public long MaxDiskBytes { get; init; } = 200L * 1024 * 1024;
}
