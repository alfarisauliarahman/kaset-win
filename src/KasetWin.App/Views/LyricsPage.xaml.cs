using KasetWin.App.ViewModels;
using KasetWin.Core.Services.Lyrics;
using KasetWin.Core.Services.Player;
using KasetWin.App.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Lyrics panel page (Task 14.9, Req 17.2/17.3/9.1). Shows synced lyrics with the current line
/// highlighted and auto-scrolled, falls back to plain lyrics when no synced lyrics exist, shows an
/// empty-state message when none are available, and surfaces a LIVE indicator plus the active
/// provider and loading state.
/// </summary>
/// <remarks>
/// Follows the App page convention: a parameterless constructor resolves the ViewModel's
/// dependencies (the singleton player + lyrics services) from <c>App.Services</c>. The ViewModel
/// observes both services; the page subscribes to <see cref="LyricsViewModel.ActiveLineChanged"/> to
/// scroll the highlighted line into view (Req 17.2), refreshes lyrics for the currently playing
/// track on navigation, and disposes the ViewModel's subscriptions when navigated away.
/// </remarks>
public sealed partial class LyricsPage : Page
{
    /// <summary>The page ViewModel, bound from XAML via <c>x:Bind</c>.</summary>
    public LyricsViewModel ViewModel { get; }

    public LyricsPage()
    {
        var services = ((App)Application.Current).Services;
        ViewModel = new LyricsViewModel(
            services.GetRequiredService<IPlayerService>(),
            services.GetRequiredService<ILyricsService>());

        this.InitializeComponent();
        ViewModel.ActiveLineChanged += OnActiveLineChanged;
    }

    /// <inheritdoc />
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    /// <inheritdoc />
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.ActiveLineChanged -= OnActiveLineChanged;
        ViewModel.Dispose();
    }

    /// <summary>Scrolls the newly active synced line into view so it stays centred (Req 17.2).</summary>
    private void OnActiveLineChanged(object? sender, LyricLineItem? line)
    {
        if (line is not null)
        {
            LyricsList.ScrollIntoView(line, ScrollIntoViewAlignment.Leading);
        }
    }

    // ── Clickable header (Feature C) ─────────────────────────────────────────────────────────────

    private void OnTitleClick(object sender, RoutedEventArgs e) =>
        NavigationHelper.NavigateToAlbum(ViewModel.AlbumBrowseId);

    private void OnArtistClick(object sender, RoutedEventArgs e) =>
        NavigationHelper.NavigateToArtist(ViewModel.ArtistChannelId);
}
