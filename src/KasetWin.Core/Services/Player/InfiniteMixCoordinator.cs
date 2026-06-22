using System.ComponentModel;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Drives the infinite mix / radio continuation flow (Task 18.1, Req 25). It owns the mutable
/// mix continuation token and decides — purely from the queue size — when to top up the queue
/// with the next continuation page, deduplicating before appending (Req 25.2/25.3). Starting a
/// regular queue, a song radio, or clearing the queue resets the token so no further mix append
/// happens until a new mix starts (Req 25.4).
/// </summary>
/// <remarks>
/// <para>
/// The type lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency and takes its
/// continuation fetch as a <c>Func</c> seam, so the threshold and reset logic are fully
/// exercisable headless (Property 18) against a deterministic fake source.
/// </para>
/// <para>
/// The threshold and "remaining upcoming" computation are exposed as pure <c>static</c> helpers
/// so callers (and the property test) can reason about the decision without any I/O. All token
/// mutations are guarded by a single lock; a re-entrant fetch is coalesced via an in-flight flag.
/// </para>
/// </remarks>
public sealed class InfiniteMixCoordinator : IDisposable
{
    /// <summary>
    /// The queue is topped up while the number of upcoming tracks (those after the active one)
    /// is at or below this threshold (Req 25.2).
    /// </summary>
    public const int LoadMoreThreshold = 10;

    private readonly object _gate = new();
    private readonly IQueueService _queue;
    private readonly Func<string, CancellationToken, Task<RadioQueueResult>> _fetchContinuation;

    private string? _continuationToken;
    private bool _isLoading;
    private bool _disposed;

    /// <summary>
    /// Creates a coordinator over the shared <paramref name="queue"/> using
    /// <paramref name="fetchContinuation"/> to fetch the next mix continuation page. The
    /// coordinator subscribes to the queue so that clearing it (from any caller) resets the
    /// mix token (Req 25.4).
    /// </summary>
    /// <param name="queue">The queue source of truth that mix songs are appended to.</param>
    /// <param name="fetchContinuation">
    /// Fetches the next continuation page for the supplied token (e.g.
    /// <c>IYTMusicClient.GetMixContinuationAsync</c>). Injected as a seam so the flow is testable
    /// without a live network.
    /// </param>
    public InfiniteMixCoordinator(
        IQueueService queue,
        Func<string, CancellationToken, Task<RadioQueueResult>> fetchContinuation)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _fetchContinuation = fetchContinuation ?? throw new ArgumentNullException(nameof(fetchContinuation));
        _queue.PropertyChanged += OnQueuePropertyChanged;
    }

    /// <summary>The active mix continuation token, or <c>null</c> when no mix is in progress.</summary>
    public string? ContinuationToken
    {
        get
        {
            lock (_gate)
            {
                return _continuationToken;
            }
        }
    }

    /// <summary>Whether a mix is currently active (a continuation token is held).</summary>
    public bool IsMixActive => ContinuationToken is not null;

    /// <summary>
    /// Whether more mix tracks should be loaded: <c>true</c> if and only if the number of
    /// upcoming tracks is at or below <see cref="LoadMoreThreshold"/> (Req 25.2, Property 18).
    /// This is a pure decision and does not consider token presence.
    /// </summary>
    public static bool ShouldLoadMore(int remainingUpcoming) => remainingUpcoming <= LoadMoreThreshold;

    /// <summary>
    /// The number of upcoming tracks after the active one for a queue of
    /// <paramref name="trackCount"/> tracks with the active track at
    /// <paramref name="currentIndex"/> (<c>-1</c> when empty). Never negative.
    /// </summary>
    public static int RemainingUpcoming(int trackCount, int currentIndex) =>
        currentIndex < 0 ? 0 : Math.Max(0, trackCount - currentIndex - 1);

    /// <summary>
    /// Starts a mix from the initial <c>next</c> result (Req 25.1): replaces the queue with its
    /// songs and stores its continuation token to drive subsequent top-ups.
    /// </summary>
    /// <param name="initial">The initial mix queue (songs + continuation token).</param>
    /// <param name="startIndex">The index to start playback from (clamped by the queue).</param>
    public void StartMix(RadioQueueResult initial, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(initial);

        _queue.SetQueue(initial.Songs, startIndex);
        SetToken(initial.ContinuationToken);
    }

    /// <summary>Resets the mix token because a regular (non-mix) queue was started (Req 25.4).</summary>
    public void OnRegularQueueStarted() => SetToken(null);

    /// <summary>Resets the mix token because a song radio was started (Req 25.4).</summary>
    public void OnSongRadioStarted() => SetToken(null);

    /// <summary>
    /// Tops up the queue when it has fallen to the threshold (Req 25.2): if a mix is active and
    /// <see cref="ShouldLoadMore"/> holds for the current queue, fetches the next continuation
    /// page, appends only previously-absent songs (Req 25.3), and stores the next token. Returns
    /// the number of songs actually appended (0 when no load was needed or no new songs arrived).
    /// </summary>
    public async Task<int> MaybeLoadMoreAsync(CancellationToken ct = default)
    {
        string token;
        lock (_gate)
        {
            if (_disposed || _isLoading || _continuationToken is null)
            {
                return 0;
            }

            int remaining = RemainingUpcoming(_queue.Tracks.Count, _queue.CurrentIndex);
            if (!ShouldLoadMore(remaining))
            {
                return 0;
            }

            token = _continuationToken;
            _isLoading = true;
        }

        try
        {
            RadioQueueResult page = await _fetchContinuation(token, ct).ConfigureAwait(false);
            int added = _queue.AppendDeduplicated(page.Songs);
            SetToken(page.ContinuationToken);
            return added;
        }
        finally
        {
            lock (_gate)
            {
                _isLoading = false;
            }
        }
    }

    private void OnQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Clearing the queue empties Tracks; a regular append never does. Treat an empty queue
        // as a Clear and reset the mix token regardless of which caller cleared it (Req 25.4).
        if ((e.PropertyName is null or "" or nameof(IQueueService.Tracks)) && _queue.Tracks.Count == 0)
        {
            SetToken(null);
        }
    }

    private void SetToken(string? token)
    {
        lock (_gate)
        {
            _continuationToken = string.IsNullOrEmpty(token) ? null : token;
        }
    }

    /// <summary>Unsubscribes from the queue's change notifications.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _queue.PropertyChanged -= OnQueuePropertyChanged;
    }
}
