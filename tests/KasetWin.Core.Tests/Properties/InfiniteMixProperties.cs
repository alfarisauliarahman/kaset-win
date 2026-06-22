using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test for <see cref="InfiniteMixCoordinator"/> (Task 18.2, Design Property 18).
/// Runs headless against the real <see cref="QueueService"/> with a deterministic, in-memory
/// continuation source (no live network). The single property runs at least 100 CsCheck
/// iterations.
/// </summary>
public class InfiniteMixProperties
{
    /// <summary>A queue of <paramref name="count"/> songs with unique videoIds <c>v0..v(count-1)</c>.</summary>
    private static List<Song> DistinctSongs(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Song
        {
            Id = $"v{i}",
            VideoId = $"v{i}",
            Title = $"Song {i}",
        })];

    /// <summary>
    /// A deterministic continuation source: each call returns <paramref name="batchSize"/> brand
    /// new songs (videoIds <c>m{call}_{i}</c>, disjoint from the <c>v*</c> queue) plus a fresh
    /// token. <c>Calls</c> exposes how many times it was invoked so the property can assert that a
    /// fetch happened exactly when expected.
    /// </summary>
    private sealed class FakeContinuationSource(int batchSize)
    {
        public int Calls { get; private set; }

        public Task<RadioQueueResult> FetchAsync(string token, CancellationToken ct)
        {
            int call = ++Calls;
            var songs = Enumerable.Range(0, batchSize)
                .Select(i => new Song
                {
                    Id = $"m{call}_{i}",
                    VideoId = $"m{call}_{i}",
                    Title = $"Mix {call}.{i}",
                })
                .ToList();

            return Task.FromResult(new RadioQueueResult
            {
                Songs = songs,
                ContinuationToken = $"tok{call}",
            });
        }
    }

    // Feature: kaset-winui3, Property 18: Ambang pemuatan dan reset token mix
    // Validates: Requirements 25.2, 25.4
    [Fact]
    public void Property18_Load_threshold_and_mix_token_reset()
    {
        const int batchSize = 5;

        // Part (a) — Req 25.2: while a mix is active, a continuation load is triggered if and only
        // if the number of upcoming tracks is at or below the threshold (10).
        var thresholdScenario =
            from count in Gen.Int[1, 25]
            from startIndex in Gen.Int[0, count - 1]
            select (count, startIndex);

        thresholdScenario.Sample(
            s =>
            {
                var (count, startIndex) = s;

                var queue = new QueueService();
                var source = new FakeContinuationSource(batchSize);
                using var coordinator = new InfiniteMixCoordinator(queue, source.FetchAsync);

                coordinator.StartMix(
                    new RadioQueueResult { Songs = DistinctSongs(count), ContinuationToken = "tok0" },
                    startIndex);

                int remaining = count - startIndex - 1;
                bool expectLoad = remaining <= InfiniteMixCoordinator.LoadMoreThreshold;

                // The pure helpers agree with the queue arithmetic and the threshold definition.
                Assert.Equal(remaining, InfiniteMixCoordinator.RemainingUpcoming(count, startIndex));
                Assert.Equal(expectLoad, InfiniteMixCoordinator.ShouldLoadMore(remaining));

                int added = coordinator.MaybeLoadMoreAsync().GetAwaiter().GetResult();

                if (expectLoad)
                {
                    // A continuation page was fetched, its (all-new) songs appended, token advanced.
                    Assert.Equal(1, source.Calls);
                    Assert.Equal(batchSize, added);
                    Assert.Equal("tok1", coordinator.ContinuationToken);
                }
                else
                {
                    // Above the threshold nothing is fetched and the token is untouched.
                    Assert.Equal(0, source.Calls);
                    Assert.Equal(0, added);
                    Assert.Equal("tok0", coordinator.ContinuationToken);
                }
            },
            iter: 100);

        // Part (b) — Req 25.4: starting a regular queue, a song radio, or clearing the queue resets
        // the mix token, so no further mix append happens until a new mix starts.
        var resetScenario =
            from count in Gen.Int[1, 12]
            from startIndex in Gen.Int[0, count - 1]
            from trigger in Gen.Int[0, 2]
            select (count, startIndex, trigger);

        resetScenario.Sample(
            s =>
            {
                var (count, startIndex, trigger) = s;

                var queue = new QueueService();
                var source = new FakeContinuationSource(batchSize);
                using var coordinator = new InfiniteMixCoordinator(queue, source.FetchAsync);

                coordinator.StartMix(
                    new RadioQueueResult { Songs = DistinctSongs(count), ContinuationToken = "tok0" },
                    startIndex);
                Assert.True(coordinator.IsMixActive);

                switch (trigger)
                {
                    case 0: // regular queue started
                        coordinator.OnRegularQueueStarted();
                        break;
                    case 1: // song radio started
                        coordinator.OnSongRadioStarted();
                        break;
                    default: // queue cleared (from any caller)
                        queue.Clear();
                        break;
                }

                // The token is gone after every reset trigger.
                Assert.Null(coordinator.ContinuationToken);
                Assert.False(coordinator.IsMixActive);

                // No top-up happens while the token is null — even though a cleared queue is well
                // below the threshold.
                int addedAfterReset = coordinator.MaybeLoadMoreAsync().GetAwaiter().GetResult();
                Assert.Equal(0, addedAfterReset);
                Assert.Equal(0, source.Calls);

                // A new mix re-arms the flow: with the fresh queue below the threshold a load fires.
                coordinator.StartMix(
                    new RadioQueueResult { Songs = DistinctSongs(3), ContinuationToken = "fresh" },
                    0);
                int addedAfterRestart = coordinator.MaybeLoadMoreAsync().GetAwaiter().GetResult();
                Assert.Equal(batchSize, addedAfterRestart);
                Assert.Equal(1, source.Calls);
            },
            iter: 100);
    }
}
