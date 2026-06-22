using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the YouTube (full mode) Shorts surface (Task 25.3, Req 32.4). Loads the Shorts
/// split out of the home feed and drives the vertical snap-paging player: as the current page
/// settles, the settled Short autoplays through the YouTube video player (which pauses music via
/// the arbiter, Req 32.3).
/// </summary>
/// <remarks>
/// The view is a vertical, snap-paging surface (a vertical <c>FlipView</c>); this ViewModel owns the
/// ordered <see cref="Shorts"/> collection and the <see cref="CurrentIndex"/> the view two-way binds
/// to its selected index. Whenever the index changes the settled Short autoplays.
/// </remarks>
public sealed partial class YouTubeShortsViewModel : ViewModelBase
{
    private readonly IYouTubeClient _client;
    private readonly YouTubePlayerService _player;

    public YouTubeShortsViewModel(IYouTubeClient client, YouTubePlayerService player, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>The ordered Shorts paged through vertically (Req 32.4).</summary>
    public ObservableCollection<YouTubeVideo> Shorts { get; } = [];

    /// <summary>
    /// The index of the currently-settled Short (two-way bound to the snap-paging view's selected
    /// index). Setting it to a valid index autoplays that Short (Req 32.4).
    /// </summary>
    [ObservableProperty]
    private int _currentIndex = -1;

    /// <summary>Loads the Shorts feed once per in-flight load and autoplays the first Short (Req 32.4).</summary>
    public Task<bool> LoadAsync(CancellationToken ct = default) =>
        LoadAsync("yt-shorts", async token =>
        {
            var shorts = await _client.GetShortsAsync(token).ConfigureAwait(true);

            Shorts.Clear();
            foreach (var shortVideo in shorts)
            {
                Shorts.Add(shortVideo);
            }

            // Settle on the first Short, which autoplays via OnCurrentIndexChanged.
            CurrentIndex = Shorts.Count > 0 ? 0 : -1;
        }, ct);

    /// <summary>Autoplays the Short that just settled into view (Req 32.4/32.3).</summary>
    partial void OnCurrentIndexChanged(int value)
    {
        if (value < 0 || value >= Shorts.Count)
        {
            return;
        }

        _ = _player.PlayVideoAsync(Shorts[value]);
    }

    /// <summary>Pages to the next Short (used by an on-screen affordance / keyboard).</summary>
    [RelayCommand]
    private void Next()
    {
        if (CurrentIndex + 1 < Shorts.Count)
        {
            CurrentIndex++;
        }
    }

    /// <summary>Pages to the previous Short.</summary>
    [RelayCommand]
    private void Previous()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
        }
    }
}
