using System.Collections.Concurrent;

namespace KasetWin.Core.Services;

/// <summary>
/// Thread-safe single-flight helper that coalesces concurrent, identical requests by string key
/// (Task 13.2, Req 16.3, Property 31). Faithful Windows counterpart of the macOS single-flight
/// load pattern.
/// </summary>
/// <remarks>
/// <para>
/// For any number (≥1) of concurrent triggers for the same key, the underlying operation runs
/// <em>exactly once</em> and every caller receives the same result. After completion the entry is
/// removed so the next request re-executes.
/// </para>
/// <para>
/// <strong>Concurrency design — guaranteeing exactly-once + cleanup:</strong>
/// <list type="bullet">
///   <item>
///     State lives in a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by the request key.
///     The stored value is a boxed <see cref="Lazy{T}"/> of <see cref="Task{TResult}"/>.
///   </item>
///   <item>
///     <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/> may invoke
///     its value factory more than once under contention, but only one produced value is stored and
///     returned to all callers. Wrapping the work in a <see cref="Lazy{T}"/> (with
///     <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>) means the underlying
///     <paramref name="factory"/> is forced by the <em>winning</em> Lazy only — so it runs once even
///     though several throw-away Lazy instances may have been constructed.
///   </item>
///   <item>
///     The shared task removes its own entry in a <c>finally</c> block when it completes. Because
///     <c>GetOrAdd</c> never replaces an existing key, our entry is the only one stored under that
///     key for its whole lifetime, so removing by key removes exactly our entry (no risk of evicting
///     a newer in-flight call).
///   </item>
///   <item>
///     Per-caller cancellation uses <see cref="Task.WaitAsync(CancellationToken)"/>, which lets an
///     individual caller stop waiting without cancelling the shared operation or affecting the other
///     joined callers.
///   </item>
/// </list>
/// </para>
/// <para>
/// The dictionary value is stored as <see cref="object"/> because the result type <c>T</c> varies
/// per call. A given key must always be used with the same <c>T</c>; mixing types for one key is a
/// programming error and surfaces as an <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public sealed class SingleFlight : ISingleFlight
{
    // Value is a boxed Lazy<Task<T>>; T is recovered by the calling RunAsync<T>.
    private readonly ConcurrentDictionary<string, object> _inFlight = new(StringComparer.Ordinal);

    private long _executionCount;

    /// <summary>
    /// Number of distinct keys currently in flight. Primarily a diagnostic/testing hook
    /// (e.g. Property 31) for observing coalescing.
    /// </summary>
    public int InFlightCount => this._inFlight.Count;

    /// <summary>
    /// Total number of times an underlying <c>factory</c> has actually been invoked across the
    /// lifetime of this instance. A counting hook for tests: N concurrent triggers for one key must
    /// increment this by exactly 1.
    /// </summary>
    public long ExecutionCount => Interlocked.Read(ref this._executionCount);

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        ct.ThrowIfCancellationRequested();

        // Wrap in Lazy so the underlying factory is forced exactly once even if GetOrAdd's value
        // factory runs multiple times under contention (only the stored Lazy is ever forced).
        var boxed = this._inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<T>>(
                () => this.RunAndCleanupAsync(key, factory),
                LazyThreadSafetyMode.ExecutionAndPublication));

        Task<T> shared;
        try
        {
            shared = ((Lazy<Task<T>>)boxed).Value;
        }
        catch (InvalidCastException ex)
        {
            throw new InvalidOperationException(
                $"SingleFlight key '{key}' is already in flight with a different result type; " +
                $"a key must always be used with the same result type (requested '{typeof(T)}').",
                ex);
        }

        // Honor this caller's cancellation without cancelling the shared operation for others.
        return ct.CanBeCanceled
            ? await shared.WaitAsync(ct).ConfigureAwait(false)
            : await shared.ConfigureAwait(false);
    }

    private async Task<T> RunAndCleanupAsync<T>(string key, Func<Task<T>> factory)
    {
        Interlocked.Increment(ref this._executionCount);
        try
        {
            return await factory().ConfigureAwait(false);
        }
        finally
        {
            // Clear the entry so the next request for this key re-executes. Safe to remove by key:
            // GetOrAdd never overwrites an existing key, so this entry is the only one under it.
            this._inFlight.TryRemove(key, out _);
        }
    }
}
