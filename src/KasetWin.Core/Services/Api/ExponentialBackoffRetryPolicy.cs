using System.Globalization;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// An <see cref="IRetryPolicy"/> that retries retryable failures with exponentially growing
/// backoff delays: <c>initialDelay * 2^(attempt-1)</c> (Req 20.4).
/// </summary>
/// <remarks>
/// <para>
/// Semantics:
/// <list type="bullet">
///   <item>The operation is invoked, and on success its result is returned immediately.</item>
///   <item>
///     When the operation throws and the failure is retryable (per the supplied predicate),
///     the policy waits a backoff delay and retries — up to <c>maxAttempts</c> total invocations.
///   </item>
///   <item>
///     When the operation throws and the failure is <em>not</em> retryable, the exception is
///     rethrown immediately, so the operation runs exactly once.
///   </item>
///   <item>After exhausting <c>maxAttempts</c> retryable failures, the last exception is rethrown.</item>
///   <item>The <see cref="CancellationToken"/> is honored before each attempt and during each delay.</item>
/// </list>
/// </para>
/// <para>
/// The delay function is injectable so tests (task 4.3, Property 35) can substitute a no-op
/// delay. This makes attempt counts deterministic and keeps tests fast (no real wall-clock waits).
/// </para>
/// </remarks>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    /// <summary>Default base delay used when callers pass <c>initialDelay = null</c>.</summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(500);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Creates a policy using <see cref="Task.Delay(TimeSpan, CancellationToken)"/> for backoff.
    /// </summary>
    public ExponentialBackoffRetryPolicy()
        : this(Task.Delay)
    {
    }

    /// <summary>
    /// Creates a policy with an injectable delay function. Tests can supply a no-op delay to
    /// keep retries instantaneous while still observing exact attempt counts.
    /// </summary>
    /// <param name="delay">
    /// Invoked between attempts with the computed backoff duration and the caller's token.
    /// </param>
    public ExponentialBackoffRetryPolicy(Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(delay);
        _delay = delay;
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        Func<Exception, bool> shouldRetry,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldRetry);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                maxAttempts,
                "maxAttempts must be at least 1.");
        }

        var baseDelay = initialDelay ?? DefaultInitialDelay;

        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Cancellation is never a retryable transient failure.
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                var isLastAttempt = attempt >= maxAttempts;

                // Stop immediately for non-retryable errors (operation runs exactly once)
                // or once the attempt budget is exhausted (rethrow the last exception).
                if (isLastAttempt || !shouldRetry(ex))
                {
                    throw;
                }

                var backoff = ComputeBackoff(baseDelay, attempt);
                await _delay(backoff, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Computes the backoff delay for the given (1-based) attempt:
    /// <c>initialDelay * 2^(attempt-1)</c>, saturating at <see cref="TimeSpan.MaxValue"/>.
    /// </summary>
    internal static TimeSpan ComputeBackoff(TimeSpan initialDelay, int attempt)
    {
        // attempt is 1-based; exponent is attempt-1 so the first retry waits exactly initialDelay.
        var multiplier = Math.Pow(2, attempt - 1);
        var ticks = initialDelay.Ticks * multiplier;

        if (double.IsInfinity(ticks) || ticks >= TimeSpan.MaxValue.Ticks)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromTicks((long)ticks);
    }
}
