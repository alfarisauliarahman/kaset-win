using KasetWin.App.Hosting;
using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using KasetWin.Platform.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// YouTube (full mode) watch page (Task 25.1, Req 32.2/32.5). Shows the real video surface (a
/// dedicated watch WebView2 that loads <c>www.youtube.com/watch?v={id}</c>), metadata, the related
/// rail, and the comments section, and exposes the like / dislike / subscribe / Watch Later
/// mutations. Opening the page starts playback in the arbitrated YouTube video player, which pauses
/// music (Req 32.3); navigating away tears the watch WebView2 down so the video stops and releases
/// its audio.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the
/// <see cref="YouTubeWatchViewModel"/>'s dependencies from <c>App.Services</c>; the videoId arrives
/// as the navigation parameter and the load is single-flight-guarded by the ViewModel (Req 16.3).
/// The visible video surface is owned by a per-page <see cref="YouTubeWatchWebViewHost"/> that
/// attaches its core to the singleton <see cref="YouTubeWatchController"/>.
/// </remarks>
public sealed partial class YouTubeWatchPage : Page
{
    private readonly YouTubeWatchWebViewHost? _watchHost;

    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public YouTubeWatchViewModel ViewModel { get; }

    public YouTubeWatchPage()
    {
        var services = App.Current.Services;
        ViewModel = new YouTubeWatchViewModel(
            services.GetRequiredService<IYouTubeClient>(),
            services.GetRequiredService<YouTubePlayerService>(),
            services.GetService<ISingleFlight>());

        // Own a visible watch WebView2 for the page lifetime. The controller is the DI singleton
        // (it outlives pages so the arbiter keeps enforcing a single audio source); the host is
        // per-page and torn down on navigation away (Req 32.2/32.3). Resolved defensively so the
        // page still renders metadata if the watch controller is unavailable.
        if (services.GetService<YouTubeWatchController>() is { } controller)
        {
            _watchHost = new YouTubeWatchWebViewHost(controller);
        }

        this.InitializeComponent();

        if (_watchHost is not null)
        {
            VideoHost.Children.Add(_watchHost.Element);
        }
    }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Create the watch WebView2 core and attach it to the controller now that the element is
        // live in the visual tree; LoadVideoAsync defers until the core is ready, so ordering with
        // the ViewModel load below is safe either way.
        if (_watchHost is not null)
        {
            await _watchHost.InitializeAsync();
        }

        if (e.Parameter is string videoId && !string.IsNullOrWhiteSpace(videoId))
        {
            await ViewModel.LoadAsync(videoId);
        }
    }

    /// <inheritdoc />
    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // Stop the video and release its audio when leaving the watch page (Req 32.3).
        if (_watchHost is not null)
        {
            await _watchHost.DisposeAsync();
        }
    }

    private void OnRelatedClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YouTubeVideo video)
        {
            YouTubeNavigation.OpenWatch(Frame, video);
        }
    }
}
