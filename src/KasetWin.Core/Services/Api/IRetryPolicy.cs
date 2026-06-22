namespace KasetWin.Core.Services.Api;

/// <summary>
/// Retries a transient operation according to a retryability predicate and an attempt
/// budget. Implementations decide the delay strategy between attempts.
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> and has no WinUI/WinRT dependency, so it can be exercised
/// headless and via property-based tests (see task 4.3, Property 35).
/// </remarks>
public interface IRetryPolicy
{
    /// <summary>
    /// Executes <paramref name="operation"/>, retrying when it throws an exception that
    /// <paramref name="shouldRetry"/> classifies as retryable.
    /// </summary>
    /// <typeparam name="T">The result type produced by <paramref name="operation"/>.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="shouldRetry">
    /// Predicate deciding whether a thrown exception is retryable. When it returns
    /// <see langword="false"/>, the exception is rethrown immediately (operation runs exactly once).
    /// </param>
    /// <param name="maxAttempts">
    /// The maximum number of times <paramref name="operation"/> may be invoked for a retryable
    /// failure. Must be at least 1.
    /// </param>
    /// <param name="initialDelay">
    /// The base delay used for the first retry; subsequent retries grow exponentially. When
    /// <see langword="null"/>, a sensible default is used by the implementation.
    /// </param>
    /// <param name="ct">A token used to cancel the operation and any pending backoff delay.</param>
    /// <returns>The result of the first successful invocation of <paramref name="operation"/>.</returns>
    Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        Func<Exception, bool> shouldRetry,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken ct = default);
}
