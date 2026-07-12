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
/// Listening history page (Task 23.1, Req 30.1/30.2). Renders the user's recently played songs from
/// the InnerTube <c>FEmusic_history</c> surface and plays the selected item into the queue.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies from <c>App.Services</c> and constructs the <see cref="HistoryViewModel"/> directly
/// (ViewModels are not registered in the container). The initial load is kicked off from
/// <c>Loaded</c> and is single-flight-guarded by the ViewModel (Req 16.3).
/// </remarks>
public sealed partial class HistoryPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        var services = App.Current.Services;
        ViewModel = new HistoryViewModel(
            services.GetRequiredService<IYTMusicClient>(),
            services.GetRequiredService<IPlayerService>(),
            services.GetService<ISingleFlight>());

        this.InitializeComponent();
        PageTitleText.Text = Localization.UiStrings.HistoryTitle;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadHistoryAsync();

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            // THROWAWAY DIAGNOSTIC (Bug A): confirm a history track-row click reaches the player.
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }
}
