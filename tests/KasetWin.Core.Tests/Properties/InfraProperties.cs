using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Settings;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests for the headless infrastructure seams of the kaset-winui3 feature:
/// single-flight request coalescing (Property 31) and settings/credential persistence round-trips
/// (Property 32). Each property is a single <see cref="FactAttribute"/> running a minimum of 100
/// CsCheck iterations.
///
/// SECURITY: every credential value is a randomly generated placeholder — never a real
/// cookie/token/SAPISID value.
/// </summary>
public class InfraProperties
{
    // Feature: kaset-winui3, Property 31: Single-flight menggabungkan request identik bersamaan
    // Validates: Requirements 16.3
    [Fact]
    public void Property31_SingleFlight_coalesces_concurrent_identical_requests()
    {
        // For any number of distinct keys, each with several concurrent callers, the underlying
        // factory runs EXACTLY ONCE per key (regardless of how many callers join), every caller for
        // a key observes that key's single shared result, and distinct keys execute independently.
        var scenario =
            from keyCount in Gen.Int[1, 4]
            from callersPerKey in Gen.Int[2, 6]
            select (keyCount, callersPerKey);

        scenario.Sample(
            s =>
            {
                var (keyCount, callersPerKey) = s;

                var sf = new SingleFlight();

                // Per-key invocation counters; the factory bumps its key's counter via Interlocked.
                var perKeyExecutions = new int[keyCount];

                // Held until every caller has joined its in-flight entry, so the factory stays
                // in flight long enough to prove coalescing rather than racing to completion.
                var release = new TaskCompletionSource();

                // Released once to start all callers together.
                var start = new TaskCompletionSource();

                var totalCallers = keyCount * callersPerKey;
                var joined = 0;

                var tasks = new List<Task<(int Key, string Result)>>(totalCallers);
                for (var k = 0; k < keyCount; k++)
                {
                    var keyIndex = k;
                    var key = $"k{k}";
                    for (var c = 0; c < callersPerKey; c++)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            await start.Task.ConfigureAwait(false);

                            // RunAsync executes its GetOrAdd synchronously before returning the Task,
                            // so once the call returns this caller has definitely joined the shared
                            // in-flight entry. Counting here is therefore race-free.
                            var pending = sf.RunAsync(
                                key,
                                async () =>
                                {
                                    Interlocked.Increment(ref perKeyExecutions[keyIndex]);
                                    await release.Task.ConfigureAwait(false);
                                    return $"result:{key}";
                                });

                            Interlocked.Increment(ref joined);
                            var result = await pending.ConfigureAwait(false);
                            return (keyIndex, result);
                        }));
                    }
                }

                start.SetResult();

                // Wait until every caller has joined (deterministic: each increments only after the
                // synchronous GetOrAdd). Bounded so a regression surfaces as a failure, not a hang.
                Assert.True(
                    SpinWait.SpinUntil(() => Volatile.Read(ref joined) == totalCallers, TimeSpan.FromSeconds(30)),
                    "All callers should join the in-flight operation.");

                release.SetResult();

                var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

                // Exactly-once per key, and total executions equal the number of distinct keys.
                Assert.Equal(keyCount, sf.ExecutionCount);
                for (var k = 0; k < keyCount; k++)
                {
                    Assert.Equal(1, perKeyExecutions[k]);
                }

                // Every caller observed the shared result for its own key.
                foreach (var (key, result) in results)
                {
                    Assert.Equal($"result:k{key}", result);
                }
            },
            iter: 100);
    }

    // Feature: kaset-winui3, Property 32: Round-trip persistensi pengaturan dan kredensial
    // Validates: Requirements 18.1, 18.2, 18.4, 22.1
    [Fact]
    public void Property32_Settings_and_credentials_round_trip_through_their_stores()
    {
        // For any settings state, applying it and then loading through a FRESH service over the
        // same store reproduces an equal state (enums and bools survive the JSON round-trip); and
        // for any key/secret, saving then loading from the credential store yields the exact secret.
        var stateGen =
            from launchPage in Gen.OneOfConst(Enum.GetValues<LaunchPage>())
            from remember in Gen.Bool
            from syncedLyrics in Gen.Bool
            from quality in Gen.OneOfConst(Enum.GetValues<AudioQuality>())
            from repeat in Gen.OneOfConst(Enum.GetValues<RepeatMode>())
            from shuffle in Gen.Bool
            select new SettingsState(launchPage, remember, syncedLyrics, quality, repeat, shuffle);

        var scenario =
            from state in stateGen
            from credentialKey in PbtGenerators.ShortToken
            from secret in PbtGenerators.Token
            select (state, credentialKey, secret);

        scenario.Sample(
            s =>
            {
                var (state, credentialKey, secret) = s;

                // --- Settings round-trip (18.1, 18.2, 18.4) ---
                var store = new InMemorySettingsStore();
                var service = new SettingsService(store);
                service.Apply(state);

                // A brand new service over the same persisted store reproduces the state exactly.
                var reloaded = new SettingsService(store).Snapshot();
                Assert.Equal(state, reloaded);

                // Re-hydrating the same instance from the store reproduces it too.
                service.Load();
                Assert.Equal(state, service.Snapshot());

                // --- Credential round-trip (22.1) ---
                var credentials = new InMemoryCredentialStore();
                credentials.SaveAsync(credentialKey, secret).GetAwaiter().GetResult();
                var loadedSecret = credentials.LoadAsync(credentialKey).GetAwaiter().GetResult();
                Assert.Equal(secret, loadedSecret);
            },
            iter: 100);
    }
}
