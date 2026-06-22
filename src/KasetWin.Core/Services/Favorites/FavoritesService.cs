using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Favorites;

/// <summary>
/// Default <see cref="IFavoritesService"/> implementation that persists the ordered favorites list
/// through an <see cref="ISettingsStore"/> (Task 22.1, Req 29.1–29.4).
/// </summary>
/// <remarks>
/// <para>
/// The whole list is serialized to/from a single store key with <see cref="KasetJson"/>
/// (<see cref="System.Text.Json"/>): each <see cref="FavoriteItem"/> persists its content id, kind
/// (enum by stable name), title, optional subtitle and thumbnail. This makes the persisted list
/// round-trip exactly — for any sequence of operations, a fresh service over the same store
/// reproduces an equal ordered list (Property 40), resilient to enum reordering.
/// </para>
/// <para>
/// Membership is keyed by <see cref="FavoriteItem.ContentId"/> (ordinal), so <see cref="Add"/> never
/// produces duplicates (Req 29.1). The backing list is hydrated from the store at construction and
/// every mutation writes through immediately (Req 29.2/29.3) and raises <see cref="Changed"/>.
/// </para>
/// </remarks>
public sealed class FavoritesService : IFavoritesService
{
    private const string StorageKey = "favorites.items";

    private readonly ISettingsStore _store;
    private readonly ILogger<FavoritesService> _logger;
    private readonly List<FavoriteItem> _items = [];

    /// <summary>
    /// Creates the service over <paramref name="store"/> and immediately hydrates the favorites list
    /// from it (empty when nothing has been persisted).
    /// </summary>
    /// <param name="store">Backing persistence store (in-memory for tests, LocalSettings in the app).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public FavoritesService(ISettingsStore store, ILogger<FavoritesService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _logger = logger ?? NullLogger<FavoritesService>.Instance;
        Reload();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<FavoriteItem> Items => _items;

    /// <inheritdoc />
    public bool IsVisible => _items.Count > 0;

    /// <inheritdoc />
    public bool Contains(string contentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentId);
        return IndexOf(contentId) >= 0;
    }

    /// <inheritdoc />
    public bool Add(FavoriteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (IndexOf(item.ContentId) >= 0)
        {
            // Already pinned: de-duplicate by content id, leaving order/state untouched (Req 29.1).
            return false;
        }

        _items.Insert(0, item); // newest pins surface first
        Persist();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public bool Remove(string contentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentId);

        var index = IndexOf(contentId);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        Persist();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public bool Toggle(FavoriteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (IndexOf(item.ContentId) >= 0)
        {
            Remove(item.ContentId);
            return false;
        }

        Add(item);
        return true;
    }

    /// <inheritdoc />
    public bool Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _items.Count ||
            toIndex < 0 || toIndex >= _items.Count)
        {
            return false;
        }

        if (fromIndex == toIndex)
        {
            return true; // in-range no-op
        }

        var item = _items[fromIndex];
        _items.RemoveAt(fromIndex);
        _items.Insert(toIndex, item);
        Persist();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public void Reload()
    {
        _items.Clear();

        var raw = _store.Get(StorageKey);
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        try
        {
            var decoded = KasetJson.Deserialize<List<FavoriteItem>>(raw);
            if (decoded is null)
            {
                return;
            }

            // De-duplicate defensively in case a previously persisted blob contained duplicates;
            // first occurrence wins so order is preserved.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in decoded)
            {
                if (item is not null && !string.IsNullOrEmpty(item.ContentId) && seen.Add(item.ContentId))
                {
                    _items.Add(item);
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt blob: start empty rather than throwing so a single bad value cannot prevent
            // the app from starting.
            _logger.LogWarning("Stored favorites are unreadable; starting with an empty list.");
        }
    }

    private int IndexOf(string contentId)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].ContentId, contentId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void Persist() => _store.Set(StorageKey, KasetJson.Serialize(_items));

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
