namespace KasetWin.Core.Services.Imaging;

/// <summary>
/// Disk-backed LRU blob cache bounded by a total byte budget (Req 16.2, Design "ImageCache":
/// ~200&#160;MB disk LRU). Entries are stored as individual files named by their (already hashed)
/// key under a caller-supplied base directory.
/// </summary>
/// <remarks>
/// <para>
/// Kept in <c>Core</c> using only <see cref="System.IO"/> so it is headless-testable against a
/// temp directory; the platform layer simply supplies the cache directory
/// (<c>ApplicationData.Current.TemporaryFolder</c>). The WinRT-free design means the eviction
/// ordering can be exercised directly by tests.
/// </para>
/// <para>
/// Recency is tracked with a monotonic in-memory counter (seeded from each file's last-write time
/// on first use) rather than filesystem timestamps, which have coarse resolution. A single
/// <see cref="SemaphoreSlim"/> serializes index mutation and eviction; the actual byte copy in/out
/// happens off the lock.
/// </para>
/// </remarks>
public sealed class DiskImageLru : IDisposable
{
    private const string FileExtension = ".img";

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Dictionary<string, Meta> _index = new(StringComparer.Ordinal);

    private bool _initialized;
    private long _totalBytes;
    private long _accessTick;

    /// <summary>
    /// Creates a disk LRU rooted at <paramref name="directory"/> bounded by
    /// <paramref name="maxBytes"/> total bytes. The directory is created on first write.
    /// </summary>
    /// <param name="directory">Base directory for cache files (e.g. an app temp/cache folder).</param>
    /// <param name="maxBytes">Maximum total bytes across cache files (default ~200&#160;MB).</param>
    public DiskImageLru(string directory, long maxBytes = 200L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _directory = directory;
        _maxBytes = maxBytes;
    }

    /// <summary>Current total bytes tracked across cache files.</summary>
    public long TotalBytes
    {
        get
        {
            _mutex.Wait();
            try
            {
                return _totalBytes;
            }
            finally
            {
                _mutex.Release();
            }
        }
    }

    /// <summary>
    /// Reads the cached bytes for <paramref name="key"/>, returning <see langword="null"/> on miss.
    /// A hit refreshes the entry's recency.
    /// </summary>
    public async Task<byte[]?> TryReadAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        string path;
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (!_index.TryGetValue(key, out var meta))
            {
                return null;
            }

            path = PathFor(key);
            meta.AccessTick = NextTick();
            _index[key] = meta;
        }
        finally
        {
            _mutex.Release();
        }

        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // File vanished out from under us (external cleanup); forget it.
            await RemoveAsync(key, ct).ConfigureAwait(false);
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            await RemoveAsync(key, ct).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="key"/> and evicts least-recently-used
    /// entries until the total byte budget is satisfied.
    /// </summary>
    public async Task WriteAsync(string key, byte[] value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        Directory.CreateDirectory(_directory);
        var path = PathFor(key);
        await File.WriteAllBytesAsync(path, value, ct).ConfigureAwait(false);

        List<string> toDelete = [];
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (_index.TryGetValue(key, out var existing))
            {
                _totalBytes -= existing.Size;
            }

            _index[key] = new Meta(value.LongLength, NextTick());
            _totalBytes += value.LongLength;

            // Collect LRU victims while over budget (keep at least the entry we just wrote).
            while (_totalBytes > _maxBytes && _index.Count > 1)
            {
                var victim = LeastRecentlyUsedExcept(key);
                if (victim is null)
                {
                    break;
                }

                _totalBytes -= _index[victim].Size;
                _index.Remove(victim);
                toDelete.Add(victim);
            }
        }
        finally
        {
            _mutex.Release();
        }

        foreach (var victim in toDelete)
        {
            TryDeleteFile(PathFor(victim));
        }
    }

    /// <summary>Removes the entry (and its file) for <paramref name="key"/> if present.</summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (_index.Remove(key, out var meta))
            {
                _totalBytes -= meta.Size;
            }
        }
        finally
        {
            _mutex.Release();
        }

        TryDeleteFile(PathFor(key));
    }

    /// <summary>Deletes every cache file and resets the index.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _index.Clear();
            _totalBytes = 0;
            _initialized = true;

            if (Directory.Exists(_directory))
            {
                foreach (var file in Directory.EnumerateFiles(_directory, "*" + FileExtension))
                {
                    TryDeleteFile(file);
                }
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    // Caller must hold _mutex. Lazily scan the directory once, seeding recency from last-write time.
    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_directory, "*" + FileExtension))
        {
            try
            {
                var info = new FileInfo(file);
                var key = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                _index[key] = new Meta(info.Length, info.LastWriteTimeUtc.Ticks);
                _totalBytes += info.Length;
                _accessTick = Math.Max(_accessTick, info.LastWriteTimeUtc.Ticks);
            }
            catch (IOException)
            {
                // Ignore unreadable entries during seeding.
            }
        }
    }

    // Caller must hold _mutex.
    private string? LeastRecentlyUsedExcept(string protectedKey)
    {
        string? lruKey = null;
        var lruTick = long.MaxValue;
        foreach (var (k, meta) in _index)
        {
            if (string.Equals(k, protectedKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (meta.AccessTick < lruTick)
            {
                lruTick = meta.AccessTick;
                lruKey = k;
            }
        }

        return lruKey;
    }

    private long NextTick() => ++_accessTick;

    private string PathFor(string key) => Path.Combine(_directory, key + FileExtension);

    /// <summary>Disposes the internal synchronization primitive.</summary>
    public void Dispose() => _mutex.Dispose();

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private struct Meta(long size, long accessTick)
    {
        public long Size { get; set; } = size;

        public long AccessTick { get; set; } = accessTick;
    }
}
