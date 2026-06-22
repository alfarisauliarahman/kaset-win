using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Favorites;
using KasetWin.Core.Services.Settings;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test for the Favorites service (Task 22.2, Req 29.1–29.4). A single CsCheck
/// property (minimum 100 iterations) verifies the universal Favorites laws against the headless
/// <see cref="FavoritesService"/> backed by the in-memory settings store.
/// </summary>
public class FavoritesProperties
{
    /// <summary>A favorited item with a non-empty content id, kind, and title.</summary>
    private static readonly Gen<FavoriteItem> FavoriteItemGen =
        from id in PbtGenerators.ShortToken
        from type in Gen.OneOfConst(Enum.GetValues<FavoriteItemType>())
        from title in PbtGenerators.ShortToken
        select new FavoriteItem(id, type, title, null, null);

    // Feature: kaset-winui3, Property 40: Operasi Favorites menjaga keunikan dan reversibilitas
    // Validates: Requirements 29.1, 29.2, 29.3, 29.4
    [Fact]
    public void Property40_Favorites_operations_preserve_uniqueness_and_reversibility()
    {
        // For any favorites list and item: Add is idempotent on ContentId (never duplicates,
        // Req 29.1); Add(x) then Remove(x) restores the prior list (reversibility, Req 29.2);
        // Move preserves the multiset and the new order persists across reload (Req 29.3); and
        // IsVisible is true iff the list is non-empty (Req 29.4). Every applied state round-trips
        // exactly through the in-memory store (a fresh service reproduces the ordered list).
        var scenario =
            from rawItems in FavoriteItemGen.Array[0, 8]
            from fresh in FavoriteItemGen
            from moveFrom in Gen.Int[0, 7]
            from moveTo in Gen.Int[0, 7]
            select (rawItems, fresh, moveFrom, moveTo);

        scenario.Sample(
            s =>
            {
                var (rawItems, fresh, moveFrom, moveTo) = s;

                // A valid favorites state never contains duplicate content ids: keep first occurrence.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var initialItems = new List<FavoriteItem>();
                foreach (var item in rawItems)
                {
                    if (seen.Add(item.ContentId))
                    {
                        initialItems.Add(item);
                    }
                }

                var store = new InMemorySettingsStore();
                var svc = new FavoritesService(store);

                // --- Add is idempotent on ContentId — no duplicates (Req 29.1) ---
                foreach (var item in initialItems)
                {
                    Assert.True(svc.Add(item), "First add of a new content id should succeed.");
                }

                foreach (var item in initialItems)
                {
                    Assert.False(svc.Add(item), "Re-adding an existing content id must be a no-op.");
                }

                Assert.Equal(initialItems.Count, svc.Items.Count);
                Assert.Equal(
                    svc.Items.Count,
                    svc.Items.Select(i => i.ContentId).Distinct(StringComparer.Ordinal).Count());

                // --- IsVisible iff the list is non-empty (Req 29.4) ---
                Assert.Equal(svc.Items.Count > 0, svc.IsVisible);

                // --- Add then Remove is reversible for an item not already present (Req 29.1/29.2) ---
                var freshId = fresh.ContentId;
                while (seen.Contains(freshId))
                {
                    freshId += "x";
                }

                var freshItem = fresh with { ContentId = freshId };
                var before = svc.Items.ToList();
                Assert.True(svc.Add(freshItem));
                Assert.True(svc.Contains(freshId));
                Assert.True(svc.Remove(freshId));
                Assert.Equal(before, svc.Items);

                // --- Move preserves the multiset and the order persists across reload (Req 29.3) ---
                if (svc.Items.Count > 0)
                {
                    var n = svc.Items.Count;
                    var from = moveFrom % n;
                    var to = moveTo % n;

                    var multisetBefore = svc.Items
                        .Select(i => i.ContentId)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToList();

                    Assert.True(svc.Move(from, to));

                    var multisetAfter = svc.Items
                        .Select(i => i.ContentId)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToList();

                    Assert.Equal(multisetBefore, multisetAfter);
                }

                // --- Apply -> reload round-trips equal ordered state through the store (Property 40) ---
                var finalState = svc.Items.ToList();
                var reloaded = new FavoritesService(store);
                Assert.Equal(finalState, reloaded.Items);
            },
            iter: 100);
    }
}
