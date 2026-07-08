using System.Collections.Concurrent;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Process-lifetime memory of the user's like state per <c>videoId</c>. A like/dislike/remove made
/// on any surface is written here so every other surface reflects it, and — crucially — so the
/// state survives navigating away from a page and back within the session (the mutation already hit
/// the server; this bridges the gap until an authoritative re-fetch). Per product decision, "like",
/// "love" and "add to collection" are the same state (like == library).
/// </summary>
public interface ILikeStateStore
{
    /// <summary>Raised (with the affected <c>videoId</c>) whenever a track's like state changes, so
    /// every surface showing that track can refresh live.</summary>
    event Action<string>? Changed;

    /// <summary>Records the like state for a track (including <see cref="LikeStatus.Indifferent"/>).</summary>
    void Set(string videoId, LikeStatus status);

    /// <summary>Returns the remembered like state for a track; <see langword="false"/> when unknown.</summary>
    bool TryGet(string videoId, out LikeStatus status);
}

/// <summary>Thread-safe in-memory <see cref="ILikeStateStore"/>. Registered as a singleton.</summary>
public sealed class LikeStateStore : ILikeStateStore
{
    private readonly ConcurrentDictionary<string, LikeStatus> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event Action<string>? Changed;

    /// <inheritdoc />
    public void Set(string videoId, LikeStatus status)
    {
        if (!string.IsNullOrEmpty(videoId))
        {
            _states[videoId] = status;
            Changed?.Invoke(videoId);
        }
    }

    /// <inheritdoc />
    public bool TryGet(string videoId, out LikeStatus status)
    {
        if (!string.IsNullOrEmpty(videoId))
        {
            return _states.TryGetValue(videoId, out status);
        }

        status = LikeStatus.Indifferent;
        return false;
    }
}
