using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Microsoft.UI.Dispatching;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Backs the queue tabs of the <see cref="Controls.NowPlayingPanel"/> (Task 14.8, Req 6.1–6.4;
/// originally the standalone QueuePage). Presents the playback queue
/// from the single-source-of-truth <see cref="IQueueService"/>, highlights the track that is
/// currently playing (via <see cref="CurrentIndex"/>), and exposes the three queue affordances —
/// drag-to-reorder, clear, and shuffle — plus click-to-play-from-here.
/// </summary>
/// <remarks>
/// <para>
/// The bound <see cref="Tracks"/> collection mirrors <see cref="IQueueService.Tracks"/>. The queue
/// service is observable, so the ViewModel subscribes to its
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> and re-mirrors whenever the queue, the
/// active index, or the active track changes (Req 6.1). Service notifications can arrive off the UI
/// thread (e.g. a <c>TRACK_ENDED</c> advance driven from the JS bridge), so the re-mirror is
/// marshalled onto the captured <see cref="DispatcherQueue"/>.
/// </para>
/// <para>
/// <b>Reorder mapping (Req 6.2).</b> The page's <c>ListView</c> reorders items in this
/// <see cref="ObservableCollection{T}"/> directly, which raises a
/// <see cref="NotifyCollectionChangedAction.Move"/>. The handler translates that single move into
/// <see cref="IQueueService.Move(int, int)"/> using the event's
/// <see cref="NotifyCollectionChangedEventArgs.OldStartingIndex"/> →
/// <see cref="NotifyCollectionChangedEventArgs.NewStartingIndex"/>, keeping the queue service
/// authoritative. Two guards prevent feedback loops: re-mirroring from the service suppresses the
/// collection-changed handler, and pushing a UI move into the service suppresses the re-mirror it
/// would otherwise trigger.
/// </para>
/// <para>
/// Tracks keep stable identity (<see cref="Song.Id"/> == videoId) so the bound list virtualizes
/// without container churn (Req 16.1).
/// </para>
/// </remarks>
public sealed partial class QueueViewModel : ViewModelBase, IDisposable
{
    private readonly IQueueService _queue;
    private readonly IPlayerService _player;
    private readonly DispatcherQueue? _dispatcher;

    // True while we mirror the service queue into Tracks: suppresses OnTracksCollectionChanged so
    // the rebuild is not mistaken for a user reorder.
    private bool _suppressCollectionChanged;

    // True while we push a UI-originated reorder into the service: suppresses the re-mirror that the
    // service's PropertyChanged would otherwise trigger (the collection already matches).
    private bool _suppressQueueSync;

    // True while we rebuild the Apple-Music sections (History/UpNext), so the rebuild is not mistaken
    // for a user reorder of the Up-Next list.
    private bool _suppressSectionChanged;

    private bool _disposed;

    /// <summary>Creates the ViewModel from the DI-resolved queue and player (resolved by the page).</summary>
    public QueueViewModel(IQueueService queue, IPlayerService player)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        Tracks.CollectionChanged += OnTracksCollectionChanged;
        UpNext.CollectionChanged += OnUpNextCollectionChanged;
        _queue.PropertyChanged += OnQueuePropertyChanged;

        SyncFromQueue();
    }

    /// <summary>The queued tracks, in order. Mirrors <see cref="IQueueService.Tracks"/>.</summary>
    public ObservableCollection<Song> Tracks { get; } = [];

    /// <summary>
    /// Tracks before the current one (already played). Feeds two surfaces: the "Riwayat" sub-tab and
    /// — since the queue should read as one continuous strip, played → now playing → up next — the
    /// dimmed "Sudah diputar" section rendered above the now-playing card in the "Berikutnya" tab.
    /// The rows stay clickable there so an earlier track can be played again (<see cref="PlayTrackCommand"/>
    /// resolves the click against <see cref="Tracks"/>, so a played track works exactly like an upcoming one).
    /// </summary>
    public ObservableCollection<Song> History { get; } = [];

    /// <summary>Apple-Music "Selanjutnya" section: tracks after the current one (reorderable).</summary>
    public ObservableCollection<Song> UpNext { get; } = [];

    /// <summary>The track playing now, shown prominently at the top of the panel.</summary>
    [ObservableProperty]
    private Song? _nowPlaying;

    /// <summary>Whether there are previously-played tracks to show.</summary>
    public bool HasHistory => History.Count > 0;

    /// <summary>Whether there are upcoming tracks to show.</summary>
    public bool HasUpNext => UpNext.Count > 0;

    /// <summary>Whether a track is currently playing (drives the now-playing card).</summary>
    public bool HasNowPlaying => NowPlaying is not null;

    /// <summary>Total number of tracks in the queue, shown as "12 lagu" in the header.</summary>
    public string QueueCount => $"{Tracks.Count} lagu";

    /// <summary>
    /// Index of the track currently playing, or <c>-1</c> when the queue is empty. Bound to the
    /// list's selection so the active track is highlighted (Req 6.1).
    /// </summary>
    [ObservableProperty]
    private int _currentIndex = -1;

    /// <summary>Whether the queue has any tracks; gates the Clear/Shuffle affordances.</summary>
    public bool HasTracks => Tracks.Count > 0;

    /// <summary>Whether the queue is empty; drives the empty-state placeholder.</summary>
    public bool IsEmpty => Tracks.Count == 0;

    /// <summary>Clears the queue, stopping what would play next (Req 6.3).</summary>
    [RelayCommand]
    private void Clear() => _queue.Clear();

    /// <summary>Shuffles the queue while keeping the active track in place (Req 6.4).</summary>
    [RelayCommand]
    private void Shuffle() => _queue.Shuffle();

    /// <summary>
    /// Plays from the clicked track's position. The player has no "play existing queue from index"
    /// entry point, so the current order is replayed through
    /// <see cref="IPlayerService.PlayCollectionAsync"/> starting at the chosen index (Req 6.1).
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

    private void OnQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressQueueSync)
        {
            return;
        }

        if (e.PropertyName is nameof(IQueueService.Tracks)
            or nameof(IQueueService.CurrentIndex)
            or nameof(IQueueService.CurrentTrack)
            or null)
        {
            RunOnUi(SyncFromQueue);
        }
    }

    private void SyncFromQueue()
    {
        _suppressCollectionChanged = true;
        try
        {
            Tracks.Clear();
            foreach (var track in _queue.Tracks)
            {
                Tracks.Add(track);
            }
        }
        finally
        {
            _suppressCollectionChanged = false;
        }

        CurrentIndex = _queue.CurrentIndex;
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(IsEmpty));
        RebuildSections();
    }

    /// <summary>
    /// Rebuilds the Apple-Music sections from <see cref="Tracks"/> and <see cref="CurrentIndex"/>:
    /// the now-playing track, the "Sebelumnya" history (before it) and the "Selanjutnya" up-next
    /// (after it).
    /// </summary>
    private void RebuildSections()
    {
        _suppressSectionChanged = true;
        try
        {
            History.Clear();
            UpNext.Clear();

            var ci = CurrentIndex;
            NowPlaying = ci >= 0 && ci < Tracks.Count ? Tracks[ci] : null;

            for (var i = 0; i < Tracks.Count; i++)
            {
                if (ci >= 0 && i < ci)
                {
                    History.Add(Tracks[i]);
                }
                else if (i > ci || ci < 0)
                {
                    UpNext.Add(Tracks[i]);
                }
            }
        }
        finally
        {
            _suppressSectionChanged = false;
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasUpNext));
        OnPropertyChanged(nameof(HasNowPlaying));
        OnPropertyChanged(nameof(QueueCount));
    }

    private void OnUpNextCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressSectionChanged || e.Action != NotifyCollectionChangedAction.Move)
        {
            return;
        }

        // Up-Next starts right after the current track; map the section-local move to the queue.
        var baseIndex = CurrentIndex + 1;
        _suppressQueueSync = true;
        try
        {
            _queue.Move(baseIndex + e.OldStartingIndex, baseIndex + e.NewStartingIndex);
            CurrentIndex = _queue.CurrentIndex;
        }
        finally
        {
            _suppressQueueSync = false;
        }

        // Re-mirror so Tracks and the sections match the authoritative queue after the move.
        RunOnUi(SyncFromQueue);
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressCollectionChanged || e.Action != NotifyCollectionChangedAction.Move)
        {
            return;
        }

        _suppressQueueSync = true;
        try
        {
            // Map the list's drag-reorder onto the queue's source of truth (Req 6.2). The queue
            // preserves the active track, so re-read its index to keep the highlight correct.
            _queue.Move(e.OldStartingIndex, e.NewStartingIndex);
            CurrentIndex = _queue.CurrentIndex;
        }
        finally
        {
            _suppressQueueSync = false;
        }
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    /// <summary>Unsubscribes from the singleton queue service so the page can be collected.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.PropertyChanged -= OnQueuePropertyChanged;
        Tracks.CollectionChanged -= OnTracksCollectionChanged;
        UpNext.CollectionChanged -= OnUpNextCollectionChanged;
    }
}
