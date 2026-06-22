using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// YouTube (full mode) watch page (Task 25.1, Req 32.2/32.5). Shows the video surface, metadata, the
/// related rail, and the comments section, and exposes the like / dislike / subscribe / Watch Later
/// mutations. Opening the page starts playback in the arbitrated YouTube video player, which pauses
/// music (Req 32.3).
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the
/// <see cref="YouTubeWatchViewModel"/>'s dependencies from <c>App.Services</c>; the videoId arrives
/// as the navigation parameter and the load is single-flight-guarded by the ViewModel (Req 16.3).
/// </remarks>
public sealed partial class YouTubeWatchPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public YouTubeWatchViewModel ViewModel { get; }

    public YouTubeWatchPage()
    {
        var services = App.Current.Services;
        ViewModel = new YouTubeWatchViewModel(
            services.GetRequiredService<IYouTubeClient>(),
            services.GetRequiredService<YouTubePlayerService>(),
            services.GetService<ISingleFlight>());

        this.InitializeComponent();
    }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string videoId && !string.IsNullOrWhiteSpace(videoId))
        {
            await ViewModel.LoadAsync(videoId);
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
