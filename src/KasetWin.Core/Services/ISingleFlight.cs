namespace KasetWin.Core.Services;

/// <summary>
/// Coalesces concurrent, identical asynchronous requests so the underlying operation runs
/// at most once per key while it is in flight (Req 16.3, Property 31).
/// </summary>
/// <remarks>
/// Callers that arrive while an operation for the same <c>key</c> is already running share the
/// single in-flight <see cref="Task{TResult}"/> and observe the same result. Once the operation
/// completes (successfully or with a fault) the entry is cleared, so a subsequent request for the
/// same key triggers a fresh execution.
/// </remarks>
public interface ISingleFlight
{
    /// <summary>
    /// Runs <paramref name="factory"/> for <paramref name="key"/>, joining any in-flight operation
    /// for the same key instead of starting a new one.
    /// </summary>
    /// <typeparam name="T">Result type produced by the operation. A given key must always be used
    /// with the same <typeparamref name="T"/>.</typeparam>
    /// <param name="key">Identity of the request; concurrent calls with an equal key are coalesced.</param>
    /// <param name="factory">Produces the underlying operation. Invoked at most once per in-flight key.</param>
    /// <param name="ct">Cancels this caller's wait without disturbing the shared operation or other waiters.</param>
    /// <returns>The shared result of the (single) underlying operation.</returns>
    Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct = default);
}
