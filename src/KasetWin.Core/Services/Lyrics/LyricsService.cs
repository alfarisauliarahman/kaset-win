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

        IsLoading = true;
        LyricResult result;
        try
        {
            result = await ResolveAsync(info, ct).ConfigureAwait(false);
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
    private async Task<LyricResult> ResolveAsync(LyricsSearchInfo info, CancellationToken ct)
    {
        if (_providers.Count == 0)
        {
            return new LyricResult.Unavailable();
        }

        var tasks = new List<Task<LyricResult>>(_providers.Count);
        foreach (var provider in _providers)
        {
            tasks.Add(SafeSearchAsync(provider, info, ct));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Synced wins outright; otherwise the first plain result; otherwise unavailable.
        foreach (var result in results)
        {
            if (result is LyricResult.Synced)
            {
                return result;
            }
        }

        foreach (var result in results)
        {
            if (result is LyricResult.Plain)
            {
                return result;
            }
        }

        return new LyricResult.Unavailable();
    }

    private async Task<LyricResult> SafeSearchAsync(
        ILyricsProvider provider,
        LyricsSearchInfo info,
        CancellationToken ct)
    {
        try
        {
            return await provider.SearchAsync(info, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
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

    private static string? ProviderLabel(LyricResult result) => result switch
    {
        LyricResult.Synced synced => synced.Lyrics.Source,
        LyricResult.Plain plain => plain.Lyrics.Source,
        _ => null,
    };
}
