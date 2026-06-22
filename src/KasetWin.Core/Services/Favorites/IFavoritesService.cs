using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Favorites;

/// <summary>
/// Manages the ordered list of favorited ("pinned") items (Req 29). Mutations are de-duplicated by
/// <see cref="FavoriteItem.ContentId"/> (Req 29.1), persisted immediately through the backing
/// <see cref="KasetWin.Core.Abstractions.ISettingsStore"/> (Req 29.2/29.3), and reload exactly on a
/// fresh service over the same store (Property 40). The Home surface shows the Favorites section
/// only when <see cref="IsVisible"/> is <see langword="true"/> (Req 29.4).
/// </summary>
/// <remarks>
/// The service is intentionally WinUI/WinRT-free so it can be exercised headless; the platform store
/// (<c>LocalSettingsStore</c>) supplies real on-disk persistence in the app, while
/// <see cref="KasetWin.Core.Services.Settings.InMemorySettingsStore"/> backs tests.
/// </remarks>
public interface IFavoritesService
{
    /// <summary>The favorited items in display order (most-recently pinned first).</summary>
    IReadOnlyList<FavoriteItem> Items { get; }

    /// <summary>Whether the Favorites section should be shown — true iff <see cref="Items"/> is non-empty (Req 29.4).</summary>
    bool IsVisible { get; }

    /// <summary>Whether an item with <paramref name="contentId"/> is currently favorited.</summary>
    /// <param name="contentId">Content identity (videoId/browseId).</param>
    bool Contains(string contentId);

    /// <summary>
    /// Adds <paramref name="item"/> to the front of the list unless an item with the same
    /// <see cref="FavoriteItem.ContentId"/> already exists (Req 29.1). Persists on success.
    /// </summary>
    /// <returns><see langword="true"/> when added; <see langword="false"/> when it was already present.</returns>
    bool Add(FavoriteItem item);

    /// <summary>Removes the item identified by <paramref name="contentId"/> if present (Req 29.2). Persists on success.</summary>
    /// <returns><see langword="true"/> when an item was removed; otherwise <see langword="false"/>.</returns>
    bool Remove(string contentId);

    /// <summary>
    /// Adds <paramref name="item"/> when absent, or removes it when present (toggle pin state).
    /// </summary>
    /// <returns>The resulting favorited state: <see langword="true"/> when now favorited.</returns>
    bool Toggle(FavoriteItem item);

    /// <summary>
    /// Reorders the list by moving the item at <paramref name="fromIndex"/> to
    /// <paramref name="toIndex"/>, preserving the multiset of items, and persists the new order
    /// (Req 29.3). A no-op (returns <see langword="true"/>) when the indices are equal.
    /// </summary>
    /// <returns><see langword="true"/> when both indices are in range; otherwise <see langword="false"/>.</returns>
    bool Move(int fromIndex, int toIndex);

    /// <summary>(Re)hydrates <see cref="Items"/> from the backing store. Does not raise <see cref="Changed"/>.</summary>
    void Reload();

    /// <summary>Raised after the favorites list changes (add/remove/move).</summary>
    event EventHandler? Changed;
}
