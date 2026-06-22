using System.ComponentModel;
using KasetWin.Core.Abstractions;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Adapts the music <see cref="IPlayerService"/> to the arbitrated <see cref="IPausableAudioSource"/>
/// contract (Req 32.3). Observes the player's <c>IsPlaying</c> property and raises
/// <see cref="PlaybackStarted"/> on each paused→playing transition so <see cref="PlaybackArbiter"/>
/// can pause the YouTube video when music starts.
/// </summary>
/// <remarks>
/// Purely additive — the music player is not modified beyond the existing <c>PauseAsync</c> seam.
/// Lives in <c>KasetWin.Core</c> with no WinUI/WinRT dependency.
/// </remarks>
public sealed class MusicAudioSource : IPausableAudioSource, IDisposable
{
    private readonly IPlayerService _player;
    private readonly PropertyChangedEventHandler _handler;
    private bool _lastIsPlaying;

    /// <summary>Wires the adapter to the music player and begins tracking play transitions.</summary>
    public MusicAudioSource(IPlayerService player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _lastIsPlaying = _player.IsPlaying;
        _handler = OnPlayerPropertyChanged;
        _player.PropertyChanged += _handler;
    }

    /// <inheritdoc />
    public event EventHandler? PlaybackStarted;

    /// <inheritdoc />
    public bool IsPlaying => _player.IsPlaying;

    /// <inheritdoc />
    public Task PauseAsync() => _player.PauseAsync();

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only react to IsPlaying changes.
        if (e.PropertyName != nameof(IPlayerService.IsPlaying))
        {
            return;
        }

        bool now = _player.IsPlaying;
        bool wasPlaying = _lastIsPlaying;
        _lastIsPlaying = now;

        if (now && !wasPlaying)
        {
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Unsubscribes from the player's property notifications.</summary>
    public void Dispose() => _player.PropertyChanged -= _handler;
}
