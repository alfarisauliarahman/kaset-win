using CsCheck;
using KasetWin.Core.Services.Podcasts;
using KasetWin.Core.Services.Settings;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test for podcast episode progress persistence (Task 20.2, Req 27.3). A single
/// CsCheck property (minimum 100 iterations) verifies the round-trip law against the headless
/// <see cref="EpisodeProgressStore"/> backed by the in-memory settings store.
/// </summary>
public class PodcastProperties
{
    /// <summary>A single (episodeId, positionSeconds, played) entry. Position may be negative to exercise clamping.</summary>
    private static readonly Gen<(string Id, double Position, bool Played)> EntryGen =
        from id in PbtGenerators.ShortToken
        from position in Gen.Double[-600.0, 36_000.0]
        from played in Gen.Bool
        select (id, position, played);

    // Feature: kaset-winui3, Property 41: Round-trip progres episode podcast
    // Validates: Requirements 27.3
    [Fact]
    public void Property41_Episode_progress_round_trips_through_the_store()
    {
        // For any sequence of (episodeId, positionSeconds, played) saves: saving then reloading
        // through a FRESH store-backed instance over the same in-memory store reproduces the exact
        // progress/played state; saving the same episode again overwrites the prior value; and the
        // non-negative position clamp is applied consistently (so a reload never resurrects a
        // negative position).
        var scenario =
            from entries in EntryGen.Array[0, 12]
            from update in EntryGen
            select (entries, update);

        scenario.Sample(
            s =>
            {
                var (entries, update) = s;

                var store = new InMemorySettingsStore();
                var svc = new EpisodeProgressStore(store);

                // Expected map mirrors the store contract: last write wins per episode id, and the
                // position is clamped to be non-negative.
                var expected = new Dictionary<string, (double Position, bool Played)>(StringComparer.Ordinal);

                foreach (var (id, position, played) in entries)
                {
                    var saved = svc.Save(id, position, played);

                    // --- Save clamps the position consistently (never negative) ---
                    var clamped = position < 0 ? 0 : position;
                    Assert.Equal(clamped, saved.PositionSeconds);
                    Assert.Equal(id, saved.EpisodeId);
                    Assert.Equal(played, saved.Played);

                    expected[id] = (clamped, played);

                    // The live instance reflects the most recent write for this id (overwrite).
                    var current = svc.Get(id);
                    Assert.NotNull(current);
                    Assert.Equal(clamped, current!.PositionSeconds);
                    Assert.Equal(played, current.Played);
                }

                // --- Updating an episode's progress overwrites the prior value ---
                var updated = svc.Save(update.Id, update.Position, update.Played);
                var updateClamped = update.Position < 0 ? 0 : update.Position;
                Assert.Equal(updateClamped, updated.PositionSeconds);
                Assert.Equal(update.Played, updated.Played);
                expected[update.Id] = (updateClamped, update.Played);

                // No duplicate entries: one entry per distinct episode id.
                Assert.Equal(expected.Count, svc.Entries.Count);

                // --- Save -> reload round-trips exactly through the store (Property 41) ---
                var reloaded = new EpisodeProgressStore(store);
                Assert.Equal(expected.Count, reloaded.Entries.Count);

                foreach (var (id, value) in expected)
                {
                    var entry = reloaded.Get(id);
                    Assert.NotNull(entry);
                    Assert.Equal(value.Position, entry!.PositionSeconds);
                    Assert.Equal(value.Played, entry.Played);
                }
            },
            iter: 100);
    }
}
