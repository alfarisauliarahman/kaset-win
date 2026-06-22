namespace KasetWin.Core.Services.Imaging;

/// <summary>
/// Pure, thread-safe in-memory LRU byte-blob cache bounded by both an item count and a total byte
/// budget (Req 16.2, Design "ImageCache": ~50&#160;MB / ~200 items). The macOS app relies on
/// <c>NSCache</c>; this is the headless-testable Windows counterpart of the memory tier.
/// </summary>
/// <remarks>
/// <para>
/// Backed by a <see cref="LinkedList{T}"/> (recency order, most-recently-used at the front) plus a
/// <see cref="Dictionary{TKey,TValue}"/> for O(1) lookup. Every read/write moves the touched entry
/// to the front; eviction removes from the back until both caps are satisfied.
/// </para>
/// <para>
/// All public members lock a single gate, so the cache is safe to share across concurrent callers.
/// No WinRT/imaging dependency — eviction is exercised directly by unit/property tests.
/// </para>
/// </remarks>
public sealed class MemoryImageLru
{
    private readonly int _maxItems;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _recency = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);
    private long _totalBytes;

    /// <summary>
    /// Creates a memory LRU bounded by <paramref name="maxItems"/> entries and
    /// <paramref name="maxBytes"/> total payload bytes.
    /// </summary>
    /// <param name="maxItems">Maximum number of cached entries (default 200).</param>
    /// <param name="maxBytes">Maximum total payload bytes (default ~50&#160;MB).</param>
    public MemoryImageLru(int maxItems = 200, long maxBytes = 50L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _maxItems = maxItems;
        _maxBytes = maxBytes;
    }

    /// <summary>Current number of cached entries.</summary>
    public int Count
    {
        get { lock (_gate) { return _index.Count; } }
    }

    /// <summary>Current total payload bytes held by the cache.</summary>
    public long TotalBytes
    {
        get { lock (_gate) { return _totalBytes; } }
    }

    /// <summary>
    /// Attempts to read the bytes for <paramref name="key"/>. On a hit the entry is promoted to
    /// most-recently-used and a defensive copy of the bytes is returned.
    /// </summary>
    public bool TryGet(string key, out byte[]? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                value = (byte[])node.Value.Payload.Clone();
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Inserts or replaces the entry for <paramref name="key"/> with a defensive copy of
    /// <paramref name="value"/>, promotes it to most-recently-used, then evicts least-recently-used
    /// entries until both the item and byte caps are satisfied.
    /// </summary>
    public void Set(string key, byte[] value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var copy = (byte[])value.Clone();
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var existing))
            {
                _totalBytes -= existing.Value.Payload.LongLength;
                _recency.Remove(existing);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, copy));
            _recency.AddFirst(node);
            _index[key] = node;
            _totalBytes += copy.LongLength;

            EvictWhileOverBudget();
        }
    }

    /// <summary>Removes the entry for <paramref name="key"/> if present.</summary>
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _totalBytes -= node.Value.Payload.LongLength;
                _recency.Remove(node);
                _index.Remove(key);
                return true;
            }
        }

        return false;
    }

    /// <summary>Empties the cache.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _recency.Clear();
            _totalBytes = 0;
        }
    }

    // Caller must hold _gate. Evict from the LRU end until within both caps, always keeping at
    // least one entry (the just-inserted MRU) so an oversized single blob does not self-evict.
    private void EvictWhileOverBudget()
    {
        while ((_index.Count > _maxItems || _totalBytes > _maxBytes) && _index.Count > 1)
        {
            var lru = _recency.Last;
            if (lru is null)
            {
                break;
            }

            _recency.RemoveLast();
            _index.Remove(lru.Value.Key);
            _totalBytes -= lru.Value.Payload.LongLength;
        }
    }

    private readonly record struct Entry(string Key, byte[] Payload);
}
