using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the History surface (Task 23.1, Req 30.1/30.2). Fetches the user's listening
/// history via <see cref="IYTMusicClient.GetHistoryAsync"/> (InnerTube <c>FEmusic_history</c>) and
/// exposes a flat, recency-ordered list of songs that the page can render and play.
/// </summary>
/// <remarks>
/// <para>
/// Selecting an item plays it through <see cref="IPlayerService.PlayCollectionAsync"/> starting at
/// the chosen index, so the rest of the history acts as the queue (Req 30.2) — mirroring the
/// playlist/queue play affordances.
/// </para>
/// <para>
/// The load routes through <see cref="ViewModelBase.LoadAsync(string, Func{CancellationToken, Task}, CancellationToken)"/>
/// keyed by <c>"history"</c> so re-entrant navigation/Loaded triggers join a single in-flight load
/// (Req 16.3). Songs keep stable identity (<see cref="Song.Id"/> == videoId) for virtualization.
/// </para>
/// </remarks>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;

    /// <summary>Creates the ViewModel from the DI-resolved client and player (resolved by the page).</summary>
    public HistoryViewModel(IYTMusicClient client, IPlayerService player, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>The listening-history songs, most-recent first. Stable identity via <see cref="Song.Id"/>.</summary>
    public ObservableCollection<Song> Tracks { get; } = [];

    /// <summary>
    /// Loads the listening history once per in-flight load (Req 30.1). Safe to call repeatedly from
    /// the page <c>Loaded</c> handler; concurrent calls join the same load (Req 16.3).
    /// </summary>
    public Task<bool> LoadHistoryAsync(CancellationToken ct = default) =>
        LoadAsync("history", async token =>
        {
            var songs = await _client.GetHistoryAsync(token).ConfigureAwait(true);
            Tracks.Clear();
            foreach (var song in songs)
            {
                Tracks.Add(song);
            }
        }, ct);

    /// <summary>
    /// Plays the history starting at <paramref name="song"/>, loading it into the queue (Req 30.2).
    /// </summary>
    [RelayCommand]
    private Task PlayTrackAsync(Song? song)
    {
        if (song is null)
        {
            return Task.CompletedTask;
        }

        var index = Tracks.IndexOf(song);
        return index < 0 ? Task.CompletedTask : _player.PlayCollectionAsync([.. Tracks], startIndex: index);
    }
}
