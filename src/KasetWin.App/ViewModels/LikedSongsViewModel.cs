using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the dedicated Liked Songs page. Loads the user's Liked Music playlist (InnerTube
/// <c>VLLM</c>) via <see cref="IYTMusicClient.GetLikedSongsAsync"/> — a DIFFERENT dataset from the
/// library saved songs shown on the Library page — and plays the selected track into the queue.
/// </summary>
public sealed partial class LikedSongsViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;
    private readonly IQueueService? _queue;
    private readonly Notifications.IInAppNotifier? _notifier;

    /// <summary>Creates the ViewModel from the DI-resolved client and player (resolved by the page).</summary>
    public LikedSongsViewModel(
        IYTMusicClient client,
        IPlayerService player,
        ISingleFlight? singleFlight = null,
        IQueueService? queue = null,
        Notifications.IInAppNotifier? notifier = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _queue = queue;
        _notifier = notifier;
    }

    /// <summary>The liked songs, in playlist order. Stable identity via <see cref="Song.Id"/>.</summary>
    public ObservableCollection<Song> Tracks { get; } = [];

    /// <summary>Loads the Liked Music playlist once per in-flight load (single-flight guarded).</summary>
    public Task<bool> LoadLikedAsync(CancellationToken ct = default) =>
        LoadAsync("liked-songs", async token =>
        {
            var detail = await _client.GetLikedSongsAsync(token).ConfigureAwait(true);
            Tracks.Clear();
            foreach (var song in detail.Tracks)
            {
                Tracks.Add(song);
            }
        }, ct);

    /// <summary>Plays the liked songs starting at <paramref name="song"/>, loading them into the queue.</summary>
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

    /// <summary>Queues a single track to play right after the current one ("Putar setelah ini").</summary>
    [RelayCommand]
    private void PlayTrackNext(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (_queue is null)
        {
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.InsertNext([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastPlayingNext(song.Title));
    }

    /// <summary>Appends a single track to the play queue ("Tambahkan ke antrean").</summary>
    [RelayCommand]
    private void AddTrackToQueue(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (_queue is null)
        {
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.AppendDeduplicated([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastAddedToQueue(song.Title));
    }
}
