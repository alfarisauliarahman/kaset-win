using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Queue page (Task 14.8, Req 6.1–6.4). Shows the playback queue with the active track highlighted,
/// reorders via drag, and offers Clear and Shuffle. Clicking a track plays from its position.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor (so the <see cref="Frame"/> can
/// instantiate it, including by type name) composes the <see cref="QueueViewModel"/> from the
/// already-registered Core singletons resolved from <c>App.Services</c>. The ViewModel subscribes to
/// the shared <see cref="IQueueService"/>, so it is disposed when the page unloads to drop that
/// subscription. The list reorders the ViewModel's collection in place, which the ViewModel maps to
/// <see cref="IQueueService.Move(int, int)"/> (Req 6.2).
/// </remarks>
public sealed partial class QueuePage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public QueueViewModel ViewModel { get; }

    public QueuePage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = new QueueViewModel(
            services.GetRequiredService<IQueueService>(),
            services.GetRequiredService<IPlayerService>());

        this.InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song song)
        {
            // THROWAWAY DIAGNOSTIC (Bug A): confirm a queue track-row click reaches the player.
            KasetWin.Core.Diagnostics.KasetTrace.Log("Play:Queue.SongClick", $"tracks={ViewModel.Tracks.Count}");
            ViewModel.PlayTrackCommand.Execute(song);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
