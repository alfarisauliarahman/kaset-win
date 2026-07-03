using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// YouTube (full mode) Home page (Task 25.1, Req 32.1/32.4). Renders the recommended video grid and
/// a Shorts rail from the <c>FEwhat_to_watch</c> feed; clicking a video opens the watch page, while
/// a Short opens the vertical snap-paging Shorts surface.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies from <c>App.Services</c> and constructs the <see cref="YouTubeHomeViewModel"/>
/// directly (ViewModels are not registered in the container). The initial load is kicked off from
/// <c>Loaded</c> and is single-flight-guarded by the ViewModel (Req 16.3).
/// </remarks>
public sealed partial class YouTubeHomePage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public YouTubeHomeViewModel ViewModel { get; }

    public YouTubeHomePage()
    {
        var services = App.Current.Services;
        ViewModel = new YouTubeHomeViewModel(
            services.GetRequiredService<IYouTubeClient>(),
            services.GetRequiredService<YouTubePlayerService>(),
            services.GetService<ISingleFlight>());

        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadHomeAsync();

    private void OnVideoClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YouTubeVideo video)
        {
            YouTubeNavigation.OpenWatch(Frame, video);
        }
    }

    private void OnShortClick(object sender, ItemClickEventArgs e)
    {
        // A Short opens the dedicated vertical snap-paging Shorts surface (Req 32.4).
        if (Navigation.NavigationHelper.ResolvePageType("KasetWin.App.Views.YouTubeShortsPage") is { } pageType)
        {
            Frame?.Navigate(pageType);
        }
    }
}
