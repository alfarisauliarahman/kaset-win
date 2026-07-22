using System.Collections.Concurrent;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Lyrics;

/// <summary>
/// Observable implementation of <see cref="ILyricsService"/> (Task 6.2, ADR-0012). Resolves lyrics
/// for a track through the registered <see cref="ILyricsProvider"/>s, caches results by
/// <c>videoId</c> (Req 17.4), applies the <c>synced → plain → unavailable</c> priority (Req 17.2,
/// 17.3), and uses a monotonic generation counter to discard stale async results when the user
/// skips tracks quickly (Req 17.4).
/// </summary>
/// <remarks>
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency. Property mutation raises
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> via <see cref="ObservableObject"/>;
/// the WinUI layer is responsible for invoking <see cref="LoadForTrackAsync"/> from, or marshalling
/// updates onto, the UI thread.
/// </remarks>
public sealed partial class LyricsService : ObservableObject, ILyricsService
{
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly ConcurrentDictionary<string, LyricResult> _cache = new(StringComparer.Ordinal);
    private readonly ILogger<LyricsService> _logger;

    // Monotonic token incremented on every load; results from an older token are ignored.
    private int _generation;

    [ObservableProperty]
    private LyricResult? _currentLyrics;

    [ObservableProperty]
    private string? _activeProvider;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Creates the service over the registered providers (DI injects all <see cref="ILyricsProvider"/>).
    /// </summary>
    /// <param name="providers">The lyric providers, queried in registration order.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public LyricsService(IEnumerable<ILyricsProvider> providers, ILogger<LyricsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
        _logger = logger ?? NullLogger<LyricsService>.Instance;
    }

    /// <inheritdoc />
    public void Invalidate(string videoId)
    {
        if (!string.IsNullOrEmpty(videoId))
        {
            _cache.TryRemove(videoId, out _);
        }
    }

    // In-flight lookups keyed by videoId: concurrent loads for the same track (panel open +
    // track-change + refresh racing) share ONE provider query instead of re-fetching each time.
    private readonly ConcurrentDictionary<string, Task<LyricResult>> _inFlight = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task LoadForTrackAsync(LyricsSearchInfo info, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        // Claim a generation up front: any load started later supersedes this one.
        var myGeneration = Interlocked.Increment(ref _generation);

        // Cache hit (Req 17.4): publish immediately without touching the network.
        if (_cache.TryGetValue(info.VideoId, out var cached))
        {
            Publish(cached, myGeneration);
            return;
        }

        _latestVideoId = info.VideoId;
        IsLoading = true;
        LyricResult result;
        try
        {
            // Single-flight: piggyback on an identical lookup already running.
            var task = _inFlight.GetOrAdd(info.VideoId, _ => ResolveAsync(info));
            try
            {
                result = await task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _inFlight.TryRemove(info.VideoId, out _);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled load is implicitly stale; leave existing state untouched.
            if (myGeneration == Volatile.Read(ref _generation))
            {
                IsLoading = false;
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lyrics lookup failed for {VideoId}.", info.VideoId);
            result = new LyricResult.Unavailable();
        }

        // Cache only positive results so an "unavailable" can be retried/upgraded later (ADR-0012).
        if (result is not LyricResult.Unavailable)
        {
            _cache[info.VideoId] = result;
        }

        Publish(result, myGeneration);
    }

    /// <summary>
    /// Queries every provider and selects the best result with the priority
    /// <c>synced → plain → unavailable</c> (Req 17.2, 17.3). Provider order breaks ties within a tier.
    /// </summary>
    /// <summary>The videoId of the most recent load request; interim publishes gate on it.</summary>
    private string? _latestVideoId;

    /// <summary>Hard per-provider deadline so one hung provider can never stall the lookup.</summary>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(15);

    private async Task<LyricResult> ResolveAsync(LyricsSearchInfo info)
    {
        if (_providers.Count == 0)
        {
            return new LyricResult.Unavailable();
        }

        // Registration order IS the priority order within a tier: index 0 is the most trusted
        // provider. Racing alone used to make "which provider won" a coin toss decided by network
        // latency, which is exactly why nobody could tell where a given lyric came from.
        var pending = new List<(int Priority, Task<LyricResult> Task)>(_providers.Count);
        for (var i = 0; i < _providers.Count; i++)
        {
            pending.Add((i, SafeSearchAsync(_providers[i], info)));
        }

        // Race the providers instead of waiting for ALL of them (one slow provider used to delay
        // every lyric by its full latency). Results are published PROGRESSIVELY: the first hit
        // (plain or synced) shows immediately and is upgraded when a better one lands, so the
        // panel never sits empty waiting for the slowest provider. The final answer, however, is
        // deterministic: it is the best tier, then the highest-priority provider inside that tier.
        LyricResult? bestSynced = null;
        var bestSyncedPriority = int.MaxValue;
        LyricResult? bestPlain = null;
        var bestPlainPriority = int.MaxValue;

        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending.ConvertAll(static p => p.Task)).ConfigureAwait(false);
            var entry = pending.Find(p => ReferenceEquals(p.Task, done));
            pending.Remove(entry);
            var result = await done.ConfigureAwait(false);

            if (result is LyricResult.Synced)
            {
                // An explicit user preference always wins outright.
                if (ResultSource(result) == PreferredProvider)
                {
                    return result;
                }

                if (entry.Priority < bestSyncedPriority)
                {
                    bestSynced = result;
                    bestSyncedPriority = entry.Priority;
                    PublishInterim(info.VideoId, bestSynced);
                }

                // Stop as soon as no higher-priority provider can still improve on this, and no
                // preferred provider is still outstanding.
                if (string.IsNullOrEmpty(PreferredProvider)
                    && !pending.Exists(p => p.Priority < bestSyncedPriority))
                {
                    return bestSynced!;
                }
            }
            else if (result is LyricResult.Plain)
            {
                if (ResultSource(result) == PreferredProvider)
                {
                    bestPlain = result;
                    bestPlainPriority = -1;
                }
                else if (entry.Priority < bestPlainPriority)
                {
                    bestPlain = result;
                    bestPlainPriority = entry.Priority;
                }

                if (bestSynced is null && bestPlain is not null)
                {
                    PublishInterim(info.VideoId, bestPlain);
                }
            }
        }

        return bestSynced ?? bestPlain ?? new LyricResult.Unavailable();
    }

    /// <summary>
    /// Shows a provisional result while other providers are still running — only when the load it
    /// belongs to is still the track the UI asked about last. <see cref="IsLoading"/> stays on.
    /// </summary>
    private void PublishInterim(string videoId, LyricResult result)
    {
        if (string.Equals(videoId, _latestVideoId, StringComparison.Ordinal))
        {
            CurrentLyrics = result;
            ActiveProvider = ResultSource(result);
        }
    }

    /// <summary>The provider name the user prefers; its results are tried first. <c>null</c> = auto.</summary>
    public string? PreferredProvider { get; set; }

    private static string? ResultSource(LyricResult result) => result switch
    {
        LyricResult.Synced s => s.Lyrics.Source,
        LyricResult.Plain p => p.Lyrics.Source,
        _ => null,
    };

    private async Task<LyricResult> SafeSearchAsync(ILyricsProvider provider, LyricsSearchInfo info)
    {
        try
        {
            // A hard deadline: a hung provider must not keep the lookup (and its spinner) alive.
            var result = await provider.SearchAsync(info, CancellationToken.None)
                .WaitAsync(ProviderTimeout).ConfigureAwait(false);

            // Guarantee attribution: whatever a provider forgot to label is stamped with its Name,
            // so ActiveProvider (and the "Sumber: …" line the panel renders) is never blank for a
            // result that actually carries lyrics.
            return StampSource(result, provider.Name);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Provider {Provider} timed out for {VideoId}.", provider.Name, info.VideoId);
            return new LyricResult.Unavailable();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} failed for {VideoId}.", provider.Name, info.VideoId);
            return new LyricResult.Unavailable();
        }
    }

    /// <summary>
    /// Publishes a result only when this load is still the latest (stale-result protection, Req 17.4).
    /// </summary>
    private void Publish(LyricResult result, int myGeneration)
    {
        if (myGeneration != Volatile.Read(ref _generation))
        {
            return;
        }

        CurrentLyrics = result;
        ActiveProvider = ProviderLabel(result);
        IsLoading = false;
    }

    /// <summary>
    /// Returns <paramref name="result"/> with its <c>Source</c> set to <paramref name="providerName"/>
    /// when the provider left it blank. Results that already carry a source are returned unchanged.
    /// </summary>
    public static LyricResult StampSource(LyricResult result, string providerName) => result switch
    {
        LyricResult.Synced synced when string.IsNullOrWhiteSpace(synced.Lyrics.Source)
            => new LyricResult.Synced(synced.Lyrics with { Source = providerName }),
        LyricResult.Plain plain when string.IsNullOrWhiteSpace(plain.Lyrics.Source)
            => new LyricResult.Plain(plain.Lyrics with { Source = providerName }),
        _ => result,
    };

    private static string? ProviderLabel(LyricResult result) => result switch
    {
        LyricResult.Synced synced => synced.Lyrics.Source,
        LyricResult.Plain plain => plain.Lyrics.Source,
        _ => null,
    };
}
