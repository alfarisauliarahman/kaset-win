using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Core.Services.Podcasts;

/// <summary>
/// Default <see cref="IEpisodeProgressStore"/> implementation that persists per-episode podcast
/// progress through an <see cref="ISettingsStore"/> (Task 20.1, Req 27.3).
/// </summary>
/// <remarks>
/// <para>
/// The whole map (episodeId → <see cref="EpisodeProgress"/>) is serialized to a single store key with
/// <see cref="KasetJson"/> (<see cref="System.Text.Json"/>). This makes the persisted state round-trip
/// exactly — for any set of saved entries, a fresh service over the same store reproduces an equal
/// map (Property 41) — and keeps stored values resilient to schema reordering.
/// </para>
/// <para>
/// Entries are keyed by episode <c>videoId</c> (ordinal), so <see cref="Save"/> overwrites the prior
/// value for the same episode rather than duplicating it. <see cref="Save"/> clamps the position to a
/// non-negative value so a spurious negative seek can never persist. The backing map is hydrated from
/// the store at construction and every mutation writes through immediately and raises
/// <see cref="Changed"/>.
/// </para>
/// </remarks>
public sealed class EpisodeProgressStore : IEpisodeProgressStore
{
    private const string StorageKey = "podcasts.episodeProgress";

    private readonly ISettingsStore _store;
    private readonly ILogger<EpisodeProgressStore> _logger;
    private readonly Dictionary<string, EpisodeProgress> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the service over <paramref name="store"/> and immediately hydrates the progress map
    /// from it (empty when nothing has been persisted).
    /// </summary>
    /// <param name="store">Backing persistence store (in-memory for tests, LocalSettings in the app).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public EpisodeProgressStore(ISettingsStore store, ILogger<EpisodeProgressStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _logger = logger ?? NullLogger<EpisodeProgressStore>.Instance;
        Reload();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyCollection<EpisodeProgress> Entries => _entries.Values;

    /// <inheritdoc />
    public EpisodeProgress? Get(string episodeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(episodeId);
        return _entries.TryGetValue(episodeId, out var progress) ? progress : null;
    }

    /// <inheritdoc />
    public bool Contains(string episodeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(episodeId);
        return _entries.ContainsKey(episodeId);
    }

    /// <inheritdoc />
    public EpisodeProgress Save(string episodeId, double positionSeconds, bool played)
    {
        ArgumentException.ThrowIfNullOrEmpty(episodeId);

        // Clamp the position to be non-negative; a negative position is never meaningful and must
        // not round-trip (clamping is part of the persisted contract, Property 41).
        var clamped = positionSeconds < 0 ? 0 : positionSeconds;
        var progress = new EpisodeProgress(episodeId, clamped, played);

        _entries[episodeId] = progress; // overwrite prior value for the same episode id
        Persist();
        RaiseChanged();
        return progress;
    }

    /// <inheritdoc />
    public bool Remove(string episodeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(episodeId);

        if (!_entries.Remove(episodeId))
        {
            return false;
        }

        Persist();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public void Reload()
    {
        _entries.Clear();

        var raw = _store.Get(StorageKey);
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        try
        {
            var decoded = KasetJson.Deserialize<List<EpisodeProgress>>(raw);
            if (decoded is null)
            {
                return;
            }

            foreach (var entry in decoded)
            {
                if (entry is not null && !string.IsNullOrEmpty(entry.EpisodeId))
                {
                    // Last write wins on a duplicate id; the clamp is re-applied defensively.
                    var position = entry.PositionSeconds < 0 ? 0 : entry.PositionSeconds;
                    _entries[entry.EpisodeId] = entry with { PositionSeconds = position };
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt blob: start empty rather than throwing so a single bad value cannot prevent
            // the app from starting.
            _logger.LogWarning("Stored episode progress is unreadable; starting empty.");
        }
    }

    private void Persist() => _store.Set(StorageKey, KasetJson.Serialize(_entries.Values.ToList()));

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
