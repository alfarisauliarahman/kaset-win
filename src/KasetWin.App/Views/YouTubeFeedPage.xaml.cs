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
/// Reusable YouTube (full mode) feed page (Task 25.1, Req 32.1) for the Subscriptions, History, and
/// Explore destination surfaces. Which feed it renders is supplied as the navigation parameter (a
/// <see cref="YouTubeFeedRequest"/>); clicking a video opens the watch page (Req 32.2).
/// </summary>
/// <remarks>
/// The feed kind is only known once navigated, so the <see cref="YouTubeFeedViewModel"/> is built in
/// <see cref="OnNavigatedTo"/> and the generated <c>Bindings.Update()</c> rebinds the page to it.
/// The list's items source is assigned directly (the collection is stable for the page's lifetime).
/// </remarks>
public sealed partial class YouTubeFeedPage : Page
{
    /// <summary>The page ViewModel, assigned on navigation and bound via <c>x:Bind</c>.</summary>
    public YouTubeFeedViewModel? ViewModel { get; private set; }

    public YouTubeFeedPage()
    {
        this.InitializeComponent();
    }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var request = e.Parameter as YouTubeFeedRequest
            ?? new YouTubeFeedRequest(YouTubeFeedKind.Subscriptions);

        var services = App.Current.Services;
        ViewModel = new YouTubeFeedViewModel(
            services.GetRequiredService<IYouTubeClient>(),
            services.GetRequiredService<YouTubePlayerService>(),
            request.Kind,
            services.GetService<ISingleFlight>());

        if (request.Kind == YouTubeFeedKind.Destination)
        {
            ViewModel.SetDestination(request.Destination);
        }

        HeaderText.Text = ViewModel.Title;
        VideosList.ItemsSource = ViewModel.Videos;
        this.Bindings.Update();

        await ViewModel.LoadAsync();
    }

    private void OnVideoClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YouTubeVideo video)
        {
            YouTubeNavigation.OpenWatch(Frame, video);
        }
    }
}
