using KasetWin.App.ViewModels;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// YouTube (full mode) Shorts page (Task 25.3, Req 32.4): a vertical snap-paging player. A
/// <c>FlipView</c> with a vertical items panel snaps one Short per page; as each page settles its
/// Short autoplays through the arbitrated YouTube video player (which pauses music, Req 32.3).
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies from <c>App.Services</c>. The Shorts feed is loaded from <c>Loaded</c> and the load
/// is single-flight-guarded by the ViewModel (Req 16.3).
/// </remarks>
public sealed partial class YouTubeShortsPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public YouTubeShortsViewModel ViewModel { get; }

    public YouTubeShortsPage()
    {
        var services = App.Current.Services;
        ViewModel = new YouTubeShortsViewModel(
            services.GetRequiredService<IYouTubeClient>(),
            services.GetRequiredService<YouTubePlayerService>(),
            services.GetService<ISingleFlight>());

        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadAsync();
}
