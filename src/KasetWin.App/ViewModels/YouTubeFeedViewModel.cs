using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// The YouTube (full mode) feed surface a <see cref="YouTubeFeedViewModel"/> renders (Task 25.1,
/// Req 32.1). Subscriptions and History map to dedicated browse ids; <see cref="Destination"/>
/// renders one of the public Explore destination feeds.
/// </summary>
public enum YouTubeFeedKind
{
    /// <summary>The Subscriptions feed (<c>FEsubscriptions</c>).</summary>
    Subscriptions,

    /// <summary>The watch History feed (<c>FEhistory</c>).</summary>
    History,

    /// <summary>An Explore destination feed (gaming/news/sports/…), selected by <see cref="YouTubeDestination"/>.</summary>
    Destination,
}

/// <summary>
/// ViewModel for a list-style YouTube (full mode) feed — Subscriptions, History, or an Explore
/// destination (Task 25.1, Req 32.1). Mirrors the music feed ViewModels (single-flight load, stable
/// item identity via <see cref="YouTubeVideo.Id"/>) and pages forward via the feed continuation
/// token. Clicking a video navigates to the watch page; the inline play command routes through the
/// YouTube video player so the arbiter pauses music (Req 32.3).
/// </summary>
public sealed partial class YouTubeFeedViewModel : ViewModelBase
{
    private readonly IYouTubeClient _client;
    private readonly YouTubePlayerService _player;
    private readonly YouTubeFeedKind _kind;
    private YouTubeDestination _destination;
    private string? _continuationToken;

    public YouTubeFeedViewModel(
        IYouTubeClient client,
        YouTubePlayerService player,
        YouTubeFeedKind kind,
        ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _kind = kind;
    }

    /// <summary>Videos in this feed (Req 32.1). Stable identity via <see cref="YouTubeVideo.Id"/>.</summary>
    public ObservableCollection<YouTubeVideo> Videos { get; } = [];

    /// <summary>A human-readable title for the page header.</summary>
    public string Title => _kind switch
    {
        YouTubeFeedKind.Subscriptions => "Subscriptions",
        YouTubeFeedKind.History => "History",
        YouTubeFeedKind.Destination => _destination.ToString(),
        _ => "YouTube",
    };

    /// <summary>Whether a further page of the feed is available.</summary>
    public bool HasMore => !string.IsNullOrEmpty(_continuationToken);

    /// <summary>Selects the Explore destination rendered when <see cref="YouTubeFeedKind.Destination"/>.</summary>
    public void SetDestination(YouTubeDestination destination)
    {
        _destination = destination;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Loads the feed once per in-flight load (Req 32.1).</summary>
    public Task<bool> LoadAsync(CancellationToken ct = default) =>
        LoadAsync($"yt-feed:{_kind}:{_destination}", async token =>
        {
            KasetWin.Core.Diagnostics.KasetTrace.Log("BugB:FeedViewModel.LoadAsync.fetch.start", $"kind={_kind}");
            var feed = await FetchAsync(token).ConfigureAwait(true);
            KasetWin.Core.Diagnostics.KasetTrace.Log(
                "BugB:FeedViewModel.LoadAsync.fetch.done", $"videos={feed.Videos.Count}");

            Videos.Clear();
            foreach (var video in feed.Videos)
            {
                Videos.Add(video);
            }

            _continuationToken = feed.ContinuationToken;
            OnPropertyChanged(nameof(HasMore));
            KasetWin.Core.Diagnostics.KasetTrace.Log("BugB:FeedViewModel.LoadAsync.populated", $"count={Videos.Count}");
        }, ct);

    /// <summary>Loads the next page of the feed (Req 32.1), appending to <see cref="Videos"/>.</summary>
    public Task<bool> LoadMoreAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_continuationToken))
        {
            return Task.FromResult(true);
        }

        var token = _continuationToken;
        return LoadAsync($"yt-feed-more:{token}", async ct2 =>
        {
            var feed = await _client.GetFeedContinuationAsync(token, ct2).ConfigureAwait(true);
            foreach (var video in feed.Videos)
            {
                Videos.Add(video);
            }

            _continuationToken = feed.ContinuationToken;
            OnPropertyChanged(nameof(HasMore));
        }, ct);
    }

    private Task<YouTubeFeed> FetchAsync(CancellationToken ct) => _kind switch
    {
        YouTubeFeedKind.Subscriptions => _client.GetSubscriptionsFeedAsync(ct),
        YouTubeFeedKind.History => _client.GetHistoryAsync(ct),
        YouTubeFeedKind.Destination => _client.GetDestinationFeedAsync(_destination, ct),
        _ => Task.FromResult(YouTubeFeed.Empty),
    };

    /// <summary>Opens a video in the YouTube video player, which pauses music via the arbiter (Req 32.3).</summary>
    [RelayCommand]
    private Task PlayVideoAsync(YouTubeVideo? video) =>
        video is null ? Task.CompletedTask : _player.PlayVideoAsync(video);
}
