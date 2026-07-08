using KasetWin.App.ViewModels;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Full "Selengkapnya" list for a Trending shelf: the same items shown as a numbered vertical list
/// (rank + cover + title + subtitle). Receives the <see cref="HomeSectionView"/> as its navigation
/// parameter; activation routes the same way as the Explore cards.
/// </summary>
public sealed partial class TrendingListPage : Page
{
    private readonly IPlayerService? _player;

    /// <summary>
    /// Section handed off by the caller right before navigating. A complex object must not travel as
    /// the <see cref="Frame"/> navigation parameter (it breaks navigation-state serialization), so
    /// callers set this and navigate with the section title string instead.
    /// </summary>
    public static HomeSectionView? PendingSection { get; set; }

    private HomeSectionView? _section;

    public TrendingListPage()
    {
        this.InitializeComponent();
        _player = App.Current.Services.GetService<IPlayerService>();
    }

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Prefer the handoff; keep the last shown section on back-navigation re-entry.
        _section = PendingSection ?? _section;
        PendingSection = null;

        if (_section is not null)
        {
            TitleText.Text = _section.Title;

            // Video shelves show a wrapping grid of 16:9 cards; everything else the numbered list.
            if (_section.IsVideoSection)
            {
                VideoGrid.ItemsSource = _section.Items;
                VideoGrid.Visibility = Visibility.Visible;
                ItemsHost.Visibility = Visibility.Collapsed;
            }
            else
            {
                ItemsHost.ItemsSource = _section.Items;
                ItemsHost.Visibility = Visibility.Visible;
                VideoGrid.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnItemClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeCardItem card })
        {
            FeedNavigation.Activate(this.Frame, card.Model, _player);
        }
    }
}
