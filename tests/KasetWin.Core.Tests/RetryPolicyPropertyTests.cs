using CsCheck;
using KasetWin.Core.Services.Api;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Property-based test for <see cref="ExponentialBackoffRetryPolicy"/> (Task 4.3).
/// A no-op delay keeps attempt counts deterministic and the test fast.
/// </summary>
public class RetryPolicyPropertyTests
{
    // Feature: kaset-winui3, Property 35: RetryPolicy mematuhi batas percobaan dan retryability
    // Validates: Requirements 20.4
    [Fact]
    public void Property35_RetryPolicy_honours_attempt_budget_and_retryability()
    {
        // For any maxAttempts and a failing operation:
        //   - a retryable failure is attempted exactly maxAttempts times, then gives up;
        //   - a non-retryable failure is attempted exactly once.
        // In both cases the final exception propagates.
        Gen.Select(Gen.Int[1, 10], Gen.Bool)
            .Sample(
                t =>
                {
                    var (maxAttempts, retryable) = t;

                    var calls = 0;
                    var policy = new ExponentialBackoffRetryPolicy((_, _) => Task.CompletedTask);

                    var thrown = Assert.ThrowsAny<Exception>(() =>
                        policy.ExecuteAsync<int>(
                                () =>
                                {
                                    calls++;
                                    throw new InvalidOperationException("boom");
                                },
                                _ => retryable,
                                maxAttempts,
                                TimeSpan.Zero)
                            .GetAwaiter().GetResult());

                    Assert.IsType<InvalidOperationException>(thrown);
                    Assert.Equal(retryable ? maxAttempts : 1, calls);
                },
                iter: 100);
    }
}
