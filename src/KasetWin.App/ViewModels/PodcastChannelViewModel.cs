using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Podcasts;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for a podcast creator's channel page (browseId <c>UC…</c>), modelled on the YT Music
/// web channel layout: round-avatar header with a red Subscribe button, the "Episode terbaru"
/// grid, and the channel's show sections. Episodes play through the shared player.
/// </summary>
public sealed partial class PodcastChannelViewModel : ViewModelBase
{
    private readonly IYTMusicClient _client;
    private readonly IPlayerService _player;
    private readonly IEpisodeProgressStore? _progress;
    private readonly Notifications.IInAppNotifier? _notifier;
    private string _channelId = string.Empty;

    public PodcastChannelViewModel(
        IYTMusicClient client,
        IPlayerService player,
        ISingleFlight? singleFlight = null,
        IEpisodeProgressStore? progress = null,
        Notifications.IInAppNotifier? notifier = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _progress = progress;
        _notifier = notifier;
    }

    /// <summary>Channel name (header).</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Round avatar image (header).</summary>
    [ObservableProperty]
    private Uri? _avatarUrl;

    /// <summary>Channel description, when present.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    /// <summary>Whether a non-empty description is available.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Subscribe-button label: "Subscribe 11,7 jt" / "Disubscribe".</summary>
    [ObservableProperty]
    private string _subscribeLabel = "Subscribe";

    /// <summary>Whether the account subscribes to the channel (drives the button style).</summary>
    [ObservableProperty]
    private bool _isSubscribed;

    private string? _subscriberText;

    /// <summary>The "Episode terbaru" rows (first episode shelf of the page).</summary>
    public ObservableCollection<PodcastEpisode> LatestEpisodes { get; } = [];

    /// <summary>Title of the episode shelf as YT sent it ("Episode terbaru").</summary>
    [ObservableProperty]
    private string _episodesTitle = "Episode terbaru";

    /// <summary>Whether the episode shelf has rows (section visibility).</summary>
    public bool HasEpisodes => LatestEpisodes.Count > 0;

    /// <summary>The channel's show shelves (title + show cards), in page order.</summary>
    public ObservableCollection<ChannelShowSection> ShowSections { get; } = [];

    /// <summary>Loads the channel page.</summary>
    public Task<bool> LoadChannelAsync(string channelId, CancellationToken ct = default) =>
        LoadAsync("podcast-channel:" + channelId, async token =>
        {
            _channelId = channelId;
            var channel = await _client.GetPodcastChannelAsync(channelId, token).ConfigureAwait(true);
            Title = channel.Title;
            AvatarUrl = channel.AvatarUrl;
            Description = channel.Description;
            IsSubscribed = channel.IsSubscribed;
            _subscriberText = channel.SubscriberText;
            UpdateSubscribeLabel();

            LatestEpisodes.Clear();
            ShowSections.Clear();
            foreach (var section in channel.Sections)
            {
                var episodes = section.Items.OfType<PodcastSectionItem.EpisodeItem>()
                    .Select(e => _progress?.Get(e.Episode.Id) is { Played: true }
                        ? e.Episode with { IsPlayed = true, Progress = 1.0 }
                        : e.Episode)
                    .ToList();
                var shows = section.Items.OfType<PodcastSectionItem.ShowItem>()
                    .Select(s => s.Show)
                    .ToList();

                if (episodes.Count > 0 && LatestEpisodes.Count == 0)
                {
                    EpisodesTitle = section.Title;
                    foreach (var episode in episodes)
                    {
                        LatestEpisodes.Add(episode);
                    }
                }
                else if (shows.Count > 0)
                {
                    ShowSections.Add(new ChannelShowSection(section.Title, shows));
                }
            }

            OnPropertyChanged(nameof(HasEpisodes));
        }, ct);

    private void UpdateSubscribeLabel() =>
        SubscribeLabel = IsSubscribed
            ? "Disubscribe"
            : string.IsNullOrEmpty(_subscriberText) ? "Subscribe" : $"Subscribe {_subscriberText}";

    /// <summary>Toggles the channel subscription (optimistic, reverts on failure).</summary>
    [RelayCommand]
    private async Task ToggleSubscribeAsync()
    {
        if (string.IsNullOrEmpty(_channelId))
        {
            return;
        }

        var subscribing = !IsSubscribed;
        IsSubscribed = subscribing;
        UpdateSubscribeLabel();
        try
        {
            if (subscribing)
            {
                await _client.SubscribeArtistAsync(_channelId).ConfigureAwait(true);
            }
            else
            {
                await _client.UnsubscribeArtistAsync(_channelId).ConfigureAwait(true);
            }

            _notifier?.Show(subscribing ? "Disubscribe" : "Berhenti subscribe");
        }
        catch (Exception)
        {
            IsSubscribed = !subscribing;
            UpdateSubscribeLabel();
            _notifier?.Show("Gagal mengubah subscription");
        }
    }

    /// <summary>Plays an episode through the shared player (as a podcast track).</summary>
    [RelayCommand]
    private Task PlayEpisodeAsync(PodcastEpisode? episode)
    {
        if (episode is null || string.IsNullOrEmpty(episode.Id))
        {
            return Task.CompletedTask;
        }

        return _player.PlaySongAsync(new Song
        {
            Id = episode.Id,
            VideoId = episode.Id,
            Title = episode.Title,
            Duration = episode.Duration,
            ThumbnailUrl = episode.ThumbnailUrl ?? AvatarUrl,
            VideoType = MusicVideoType.PodcastEpisode,
            Artists = [new Artist { Id = _channelId, Name = Title }],
            Album = string.IsNullOrEmpty(episode.ShowBrowseId)
                ? null
                : new Album { Id = episode.ShowBrowseId!, Title = episode.ShowTitle ?? Title },
        });
    }
}

/// <summary>One show shelf of a podcast channel: YT's shelf title plus its show cards.</summary>
public sealed record ChannelShowSection(string Title, IReadOnlyList<PodcastShow> Shows);
