using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for a YouTube (full mode) watch page (Task 25.1, Req 32.2/32.5). Loads watch metadata,
/// the related rail, and the comments section via <see cref="IYouTubeClient"/>, drives playback in
/// the YouTube video player, and exposes the like / dislike / subscribe / Watch Later mutations
/// (Req 32.5).
/// </summary>
public sealed partial class YouTubeWatchViewModel : ViewModelBase
{
    private readonly IYouTubeClient _client;
    private readonly YouTubePlayerService _player;

    private string _videoId = string.Empty;
    private string? _channelId;
    private string? _commentsContinuationToken;

    public YouTubeWatchViewModel(IYouTubeClient client, YouTubePlayerService player, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>The video title (Req 32.2).</summary>
    [ObservableProperty]
    private string? _title;

    /// <summary>The video thumbnail shown in the watch surface (fallback derived from the videoId).</summary>
    [ObservableProperty]
    private Uri? _thumbnailUrl;

    /// <summary>Display view count, e.g. <c>"29,754 views"</c>.</summary>
    [ObservableProperty]
    private string? _viewCountText;

    /// <summary>Relative publish date.</summary>
    [ObservableProperty]
    private string? _publishedText;

    /// <summary>The video's channel.</summary>
    [ObservableProperty]
    private YouTubeChannel? _channel;

    /// <summary>Whether the user is subscribed to the channel (Req 32.5).</summary>
    [ObservableProperty]
    private bool _isSubscribed;

    /// <summary>The current like/dislike rating (optimistic from <see cref="YouTubeRating.None"/>, Req 32.5).</summary>
    [ObservableProperty]
    private YouTubeRating _rating = YouTubeRating.None;

    /// <summary>Related videos (Req 32.2).</summary>
    public ObservableCollection<YouTubeVideo> Related { get; } = [];

    /// <summary>Top-level comments (Req 32.2).</summary>
    public ObservableCollection<YouTubeComment> Comments { get; } = [];

    /// <summary>Whether a further page of comments is available.</summary>
    public bool HasMoreComments => !string.IsNullOrEmpty(_commentsContinuationToken);

    /// <summary>
    /// Opens <paramref name="videoId"/>: starts video playback (which pauses music via the arbiter),
    /// then loads metadata, the related rail, and the first comments page (Req 32.2/32.3).
    /// </summary>
    public Task<bool> LoadAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);
        _videoId = videoId;

        return LoadAsync($"yt-watch:{videoId}", async token =>
        {
            ThumbnailUrl = new Uri($"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");
            var data = await _client.GetWatchNextAsync(videoId, token).ConfigureAwait(true);

            Title = data.Title;
            ViewCountText = data.ViewCountText;
            PublishedText = data.PublishedText;
            Channel = data.Channel;
            _channelId = data.Channel?.ChannelId;
            IsSubscribed = data.IsSubscribed ?? false;

            Related.Clear();
            foreach (var related in data.Related)
            {
                Related.Add(related);
            }

            // Start playback in the YouTube video player (arbiter pauses music, Req 32.3).
            await _player.PlayVideoAsync(new YouTubeVideo
            {
                Id = videoId,
                VideoId = videoId,
                Title = data.Title ?? string.Empty,
                ChannelName = data.Channel?.Name,
                ChannelId = data.Channel?.ChannelId,
            }).ConfigureAwait(true);

            _commentsContinuationToken = data.CommentsContinuationToken;
            await LoadCommentsCoreAsync(token).ConfigureAwait(true);
        }, ct);
    }

    /// <summary>Loads the next page of comments (Req 32.2).</summary>
    [RelayCommand]
    private Task LoadMoreCommentsAsync() => RunSafeAsync(LoadCommentsCoreAsync);

    private async Task LoadCommentsCoreAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_commentsContinuationToken))
        {
            return;
        }

        var page = await _client.GetCommentsAsync(_commentsContinuationToken, ct).ConfigureAwait(true);
        foreach (var comment in page.Comments)
        {
            Comments.Add(comment);
        }

        _commentsContinuationToken = page.ContinuationToken;
        OnPropertyChanged(nameof(HasMoreComments));
    }

    /// <summary>Likes the video, or removes an existing like (toggle, Req 32.5).</summary>
    [RelayCommand]
    private Task LikeAsync() => RateAsync(YouTubeRating.Like);

    /// <summary>Dislikes the video, or removes an existing dislike (toggle, Req 32.5).</summary>
    [RelayCommand]
    private Task DislikeAsync() => RateAsync(YouTubeRating.Dislike);

    private Task RateAsync(YouTubeRating target) => RunSafeAsync(async ct =>
    {
        // Toggle: tapping the active rating clears it.
        var next = Rating == target ? YouTubeRating.None : target;
        await _client.RateVideoAsync(_videoId, next, ct).ConfigureAwait(true);
        Rating = next;
    });

    /// <summary>Subscribes to / unsubscribes from the channel (Req 32.5).</summary>
    [RelayCommand]
    private Task ToggleSubscribeAsync() => RunSafeAsync(async ct =>
    {
        if (string.IsNullOrEmpty(_channelId))
        {
            return;
        }

        var next = !IsSubscribed;
        await _client.SetSubscribedAsync(next, _channelId, ct).ConfigureAwait(true);
        IsSubscribed = next;
    });

    /// <summary>Adds the current video to Watch Later (Req 32.5).</summary>
    [RelayCommand]
    private Task AddToWatchLaterAsync() => RunSafeAsync(ct =>
        string.IsNullOrEmpty(_videoId) ? Task.CompletedTask : _client.AddToWatchLaterAsync(_videoId, ct));
}
