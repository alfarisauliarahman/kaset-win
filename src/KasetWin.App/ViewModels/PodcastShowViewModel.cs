using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Podcasts;
using KasetWin.Core.Services.Sharing;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the podcast show detail page (Req 27). Loads a single show (<c>MPSPP...</c>) via
/// <see cref="IYTMusicClient.GetPodcastShowAsync"/> and exposes its header (title / author / cover)
/// plus the episode list. Playing an episode routes through the shared player as a normal track.
/// </summary>
public sealed partial class PodcastShowViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;
    private readonly IQueueService? _queue;
    private readonly IEpisodeProgressStore? _progress;
    private readonly Notifications.IInAppNotifier? _notifier;
    private string _showId = string.Empty;
    private readonly List<PodcastEpisode> _allEpisodes = [];

    public PodcastShowViewModel(
        IYTMusicClient client,
        IPlayerService player,
        ISingleFlight? singleFlight = null,
        IQueueService? queue = null,
        IEpisodeProgressStore? progress = null,
        Notifications.IInAppNotifier? notifier = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _queue = queue;
        _progress = progress;
        _notifier = notifier;
    }

    /// <summary>Show title (header).</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Author / publisher line (header).</summary>
    [ObservableProperty]
    private string? _author;

    /// <summary>Show cover art (header).</summary>
    [ObservableProperty]
    private Uri? _thumbnailUrl;

    /// <summary>Show/playlist description (header, truncated).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    /// <summary>Whether a non-empty description is available.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>"N episode" metadata line for the header.</summary>
    [ObservableProperty]
    private string _episodeCountText = string.Empty;

    /// <summary>Author channel id (<c>UC…</c>) for artist-page navigation, when known.</summary>
    [ObservableProperty]
    private string? _authorChannelId;

    /// <summary>Whether the show is saved to the library (session-local; server state unknown on load).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    private bool _isSaved;

    /// <summary>Save-button label reflecting the saved state.</summary>
    public string SaveLabel => IsSaved ? "Tersimpan" : "Simpan";

    /// <summary>Toggles saving the show/playlist to the user's library (like/like on its playlist id).</summary>
    [RelayCommand]
    private async Task SaveShowAsync()
    {
        if (string.IsNullOrEmpty(_showId))
        {
            return;
        }

        // like/like wants the bare playlist id: strip the MPSP (show) / VL (playlist) browse prefix.
        var likeId = _showId.StartsWith("MPSP", StringComparison.Ordinal) ? _showId[4..]
            : _showId.StartsWith("VL", StringComparison.Ordinal) ? _showId[2..]
            : _showId;
        var saving = !IsSaved;
        try
        {
            await _client.RatePlaylistAsync(likeId, saving ? LikeStatus.Like : LikeStatus.Indifferent)
                .ConfigureAwait(true);
            IsSaved = saving;
            _notifier?.Show(saving ? "Disimpan ke koleksi" : "Dihapus dari koleksi");
        }
        catch (Exception)
        {
            _notifier?.Show(saving ? "Gagal menyimpan ke koleksi" : "Gagal menghapus dari koleksi");
        }
    }

    /// <summary>The show's episodes.</summary>
    public ObservableCollection<PodcastEpisode> Episodes { get; } = [];

    /// <summary>Loads the show detail once per in-flight load (single-flight guarded).</summary>
    public Task<bool> LoadShowAsync(string showId, CancellationToken ct = default) =>
        LoadAsync("podcast-show:" + showId, async token =>
        {
            _showId = showId;
            var show = await _client.GetPodcastShowAsync(showId, token).ConfigureAwait(true);
            Title = show.Title;
            Author = show.Author;
            Description = show.Description;
            AuthorChannelId = show.AuthorChannelId;
            IsSaved = show.IsSaved;
            ThumbnailUrl = show.ThumbnailUrl;
            EpisodeCountText = show.Episodes.Count > 0 ? $"{show.Episodes.Count} episode" : string.Empty;

            _allEpisodes.Clear();
            foreach (var episode in show.Episodes)
            {
                // Overlay the locally persisted played state (Req 27.3) over what YT sent.
                _allEpisodes.Add(_progress?.Get(episode.Id) is { Played: true }
                    ? episode with { IsPlayed = true, Progress = 1.0 }
                    : episode);
            }

            ApplyEpisodeView();
        }, ct);

    // ── Find-in-show + sort (client-side over the loaded episodes) ───────────────────────────────

    /// <summary>Find-in-show text; filters the episode list by title/description.</summary>
    [ObservableProperty]
    private string _findQuery = string.Empty;

    /// <summary>0 = Terbaru (as sent, newest first), 1 = Terlama (reversed).</summary>
    [ObservableProperty]
    private int _sortIndex;

    /// <summary>0 = Semua, 1 = Belum diputar, 2 = Telah diputar, 3 = Belum selesai.</summary>
    [ObservableProperty]
    private int _playedFilterIndex;

    partial void OnFindQueryChanged(string value) => ApplyEpisodeView();

    partial void OnSortIndexChanged(int value) => ApplyEpisodeView();

    partial void OnPlayedFilterIndexChanged(int value) => ApplyEpisodeView();

    /// <summary>Rebuilds <see cref="Episodes"/> from the master list applying find + sort.</summary>
    private void ApplyEpisodeView()
    {
        IEnumerable<PodcastEpisode> view = _allEpisodes;

        var q = FindQuery?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            view = view.Where(e =>
                e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        view = PlayedFilterIndex switch
        {
            1 => view.Where(e => !e.IsPlayed && e.Progress <= 0),          // Belum diputar
            2 => view.Where(e => e.IsPlayed),                              // Telah diputar
            3 => view.Where(e => !e.IsPlayed && e.Progress > 0),           // Belum selesai
            _ => view,
        };

        if (SortIndex == 1)
        {
            view = view.Reverse();
        }

        Episodes.Clear();
        foreach (var episode in view)
        {
            Episodes.Add(episode);
        }
    }

    /// <summary>Adds the episode to YT's built-in "Episodes for Later" playlist (id <c>SE</c>).</summary>
    [RelayCommand]
    private async Task SaveForLaterAsync(PodcastEpisode? episode)
    {
        if (episode is null)
        {
            return;
        }

        try
        {
            await _client.AddSongToPlaylistAsync(episode.Id, "SE").ConfigureAwait(true);
            _notifier?.Show("Ditambahkan ke Episode untuk Nanti");
        }
        catch (Exception)
        {
            _notifier?.Show("Gagal menambahkan ke Episode untuk Nanti");
        }
    }

    /// <summary>Share target for the show itself (header share button).</summary>
    public ShareTarget? ShareTarget =>
        ShareUrlBuilder.TryCreate(new PodcastShow { Id = _showId, Title = Title });

    /// <summary>Share target for one episode (its watch URL).</summary>
    public ShareTarget? ShareEpisode(PodcastEpisode? episode) =>
        episode is null ? null : ShareUrlBuilder.TryCreate(ToTrack(episode));

    /// <summary>Inserts the episode directly after the current track (Req queue).</summary>
    [RelayCommand]
    private void PlayEpisodeNext(PodcastEpisode? episode)
    {
        if (episode is not null)
        {
            _queue?.InsertNext([ToTrack(episode)]);
        }
    }

    /// <summary>Appends the episode to the end of the queue.</summary>
    [RelayCommand]
    private void EnqueueEpisode(PodcastEpisode? episode)
    {
        if (episode is not null)
        {
            _queue?.AppendDeduplicated([ToTrack(episode)]);
        }
    }

    /// <summary>Toggles the episode's played state and persists it locally.</summary>
    [RelayCommand]
    private void TogglePlayed(PodcastEpisode? episode)
    {
        if (episode is null)
        {
            return;
        }

        var index = Episodes.IndexOf(episode);
        if (index < 0)
        {
            return;
        }

        var nowPlayed = !episode.IsPlayed;
        if (nowPlayed)
        {
            _progress?.Save(episode.Id, 0, played: true);
        }
        else
        {
            _progress?.Remove(episode.Id);
        }

        var updated = episode with { IsPlayed = nowPlayed, Progress = nowPlayed ? 1.0 : 0.0 };
        Episodes[index] = updated;
        var masterIndex = _allEpisodes.FindIndex(e => e.Id == episode.Id);
        if (masterIndex >= 0)
        {
            _allEpisodes[masterIndex] = updated;
        }

        // Sync to the YT Music account when the row carried the mark-played feedback tokens
        // (best-effort — the local state above is the source of truth for the UI either way).
        var token = nowPlayed ? episode.PlayedFeedback?.Add : episode.PlayedFeedback?.Remove;
        if (!string.IsNullOrEmpty(token))
        {
            _ = SyncPlayedToServerAsync(token!, nowPlayed);
        }
    }

    private async Task SyncPlayedToServerAsync(string feedbackToken, bool nowPlayed)
    {
        try
        {
            await _client.SendFeedbackAsync([feedbackToken]).ConfigureAwait(true);
            _notifier?.Show(nowPlayed ? "Ditandai telah diputar" : "Ditandai belum diputar");
        }
        catch (Exception)
        {
            _notifier?.Show("Gagal menyinkronkan status diputar");
        }
    }

    /// <summary>Plays a selected episode through the shared player (as a normal track).</summary>
    [RelayCommand]
    private Task PlayEpisodeAsync(PodcastEpisode? episode)
    {
        if (episode is null || string.IsNullOrEmpty(episode.Id))
        {
            return Task.CompletedTask;
        }

        return _player.PlaySongAsync(ToTrack(episode));
    }

    /// <summary>
    /// Maps an episode to a playable <see cref="Song"/>, surfacing the show name as the "artist"
    /// so the player bar has a second line for podcasts.
    /// </summary>
    private Song ToTrack(PodcastEpisode episode)
    {
        // Artist line = the CREATOR (channel author, e.g. "Raditya Dika"); the show name is the
        // "album". Using the show title for both duplicated it in the player bar.
        var showName = Author ?? episode.ShowTitle ?? Title;
        return new Song
        {
            Id = episode.Id,
            VideoId = episode.Id,
            Title = episode.Title,
            Duration = episode.Duration,
            // Marks the track as an episode so the player surfaces podcast affordances
            // (CC captions instead of song lyrics, podcast-only controls).
            VideoType = MusicVideoType.PodcastEpisode,
            ThumbnailUrl = episode.ThumbnailUrl ?? ThumbnailUrl,
            // Artist link prefers the real channel (artist page); the show browse id is the
            // fallback (routed to the podcast page). Album carries the show id so the player-bar
            // title link opens this show.
            Artists = string.IsNullOrWhiteSpace(showName)
                ? []
                : [new Artist { Id = AuthorChannelId ?? episode.ShowBrowseId ?? string.Empty, Name = showName }],
            Album = string.IsNullOrEmpty(_showId)
                ? null
                : new Album { Id = _showId, Title = Title },
        };
    }
}
