using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Podcasts;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the Podcasts discovery surface (Task 20.1, Req 27.1–27.4). Loads
/// <c>FEmusic_podcasts</c> via <see cref="IYTMusicClient.GetPodcastsAsync"/>, renders shows /
/// episodes, plays a selected episode, persists per-episode progress, and subscribes /
/// unsubscribes from a show.
/// </summary>
/// <remarks>
/// <para>
/// Region availability (Req 27.1/27.2) is surfaced via <see cref="IsAvailable"/>: a 404 region
/// yields <c>IsAvailable == false</c> with no sections (the shell hides the tab). The load is
/// single-flight guarded keyed by <c>"podcasts"</c> so re-entrant navigation joins one load.
/// </para>
/// <para>
/// Playing an episode synthesizes a minimal <see cref="Song"/> (so the player bar can render its
/// title/thumbnail) and persists the episode's progress + played state through
/// <see cref="IEpisodeProgressStore"/> (Req 27.3).
/// </para>
/// </remarks>
public sealed partial class PodcastsViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;
    private readonly IEpisodeProgressStore _progress;

    public PodcastsViewModel(
        IYTMusicClient client,
        IPlayerService player,
        IEpisodeProgressStore progress,
        ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    /// <summary>The discovery shelves (shows + episodes), with stable identity for virtualization.</summary>
    public ObservableCollection<PodcastSectionView> Sections { get; } = [];

    /// <summary>
    /// Whether the Podcasts discovery surface is available for the user's region (Req 27.1/27.2).
    /// <see langword="false"/> after a 404 — the shell hides the tab.
    /// </summary>
    [ObservableProperty]
    private bool _isAvailable = true;

    /// <summary>True when the surface loaded but produced no sections (available but empty).</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Loads the podcasts discovery surface once per in-flight load (Req 27.1). On a 404 the result
    /// is marked unavailable (Req 27.2) without surfacing an error.
    /// </summary>
    public Task<bool> LoadAsync(CancellationToken ct = default) =>
        LoadAsync("podcasts", async token =>
        {
            var result = await _client.GetPodcastsAsync(token).ConfigureAwait(true);

            IsAvailable = result.IsAvailable;

            Sections.Clear();
            foreach (var section in result.Sections)
            {
                Sections.Add(PodcastSectionView.FromModel(section));
            }

            IsEmpty = result.IsAvailable && Sections.Count == 0;
        }, ct);

    /// <summary>
    /// Plays <paramref name="episode"/> (Req 27.3): replaces the queue with a synthesized song for
    /// the episode and records its progress / played state in the persistent store.
    /// </summary>
    public Task PlayEpisodeAsync(PodcastEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        // Persist that the episode has been listened to, carrying any known progress fraction into
        // an absolute position (seconds) when the duration is known (Req 27.3).
        var positionSeconds = episode.Duration is { } duration
            ? episode.Progress * duration.TotalSeconds
            : 0;
        _progress.Save(episode.Id, positionSeconds, episode.IsPlayed);

        var song = new Song
        {
            Id = episode.Id,
            VideoId = episode.Id,
            Title = episode.Title,
            Artists = string.IsNullOrEmpty(episode.ShowTitle)
                ? []
                : [new Artist { Id = episode.ShowBrowseId ?? "podcast", Name = episode.ShowTitle! }],
            Duration = episode.Duration,
            ThumbnailUrl = episode.ThumbnailUrl,
            VideoType = MusicVideoType.PodcastEpisode,
        };

        return _player.PlaySongAsync(song);
    }

    /// <summary>Records playback progress for an episode (Req 27.3).</summary>
    public void SaveProgress(string episodeId, double positionSeconds, bool played) =>
        _progress.Save(episodeId, positionSeconds, played);

    /// <summary>Subscribes to a podcast show (Req 27.4).</summary>
    public Task SubscribeAsync(PodcastShow show, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(show);
        return RunSafeAsync(token => _client.SubscribePodcastAsync(show.Id, token), ct);
    }

    /// <summary>Unsubscribes from a podcast show (Req 27.4).</summary>
    public Task UnsubscribeAsync(PodcastShow show, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(show);
        return RunSafeAsync(token => _client.UnsubscribePodcastAsync(show.Id, token), ct);
    }
}
