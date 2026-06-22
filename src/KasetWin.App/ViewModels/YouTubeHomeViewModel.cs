using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the YouTube (full mode) Home surface (Task 25.1, Req 32.1). Fetches the
/// recommended home feed via <see cref="IYouTubeClient.GetHomeFeedAsync"/> (<c>FEwhat_to_watch</c>),
/// exposing the regular video grid and the Shorts split out for the dedicated Shorts surface
/// (Req 32.4). Mirrors the music <see cref="HistoryViewModel"/> conventions (single-flight load,
/// stable item identity).
/// </summary>
public sealed partial class YouTubeHomeViewModel : ViewModelBase
{
    private readonly IYouTubeClient _client;
    private readonly YouTubePlayerService _player;
    private string? _continuationToken;

    public YouTubeHomeViewModel(IYouTubeClient client, YouTubePlayerService player, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>Recommended videos (Req 32.1). Stable identity via <see cref="YouTubeVideo.Id"/>.</summary>
    public ObservableCollection<YouTubeVideo> Videos { get; } = [];

    /// <summary>Shorts surfaced from the home response (Req 32.4).</summary>
    public ObservableCollection<YouTubeVideo> Shorts { get; } = [];

    /// <summary>Whether the home response surfaced any Shorts (drives the Shorts rail's visibility, Req 32.4).</summary>
    public bool HasShorts => Shorts.Count > 0;

    /// <summary>Whether a further page of the home feed is available.</summary>
    public bool HasMore => !string.IsNullOrEmpty(_continuationToken);

    /// <summary>Loads the home feed once per in-flight load (Req 32.1).</summary>
    public Task<bool> LoadHomeAsync(CancellationToken ct = default) =>
        LoadAsync("yt-home", async token =>
        {
            var feed = await _client.GetHomeFeedAsync(token).ConfigureAwait(true);
            Videos.Clear();
            foreach (var video in feed.Videos)
            {
                Videos.Add(video);
            }

            Shorts.Clear();
            foreach (var shortVideo in feed.Shorts)
            {
                Shorts.Add(shortVideo);
            }

            _continuationToken = feed.ContinuationToken;
            OnPropertyChanged(nameof(HasShorts));
        }, ct);

    /// <summary>Loads the next page of the home feed (Req 32.1), appending to <see cref="Videos"/>.</summary>
    public Task<bool> LoadMoreAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_continuationToken))
        {
            return Task.FromResult(true);
        }

        var token = _continuationToken;
        return LoadAsync($"yt-home-more:{token}", async ct2 =>
        {
            var feed = await _client.GetFeedContinuationAsync(token, ct2).ConfigureAwait(true);
            foreach (var video in feed.Videos)
            {
                Videos.Add(video);
            }

            _continuationToken = feed.ContinuationToken;
        }, ct);
    }

    /// <summary>Opens a video in the YouTube video player, which pauses music via the arbiter (Req 32.3).</summary>
    [RelayCommand]
    private Task PlayVideoAsync(YouTubeVideo? video) =>
        video is null ? Task.CompletedTask : _player.PlayVideoAsync(video);
}
