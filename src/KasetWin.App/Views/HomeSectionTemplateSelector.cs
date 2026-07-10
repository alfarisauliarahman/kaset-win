using KasetWin.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Picks the section layout on Explore: chart/Trending sections render as a numbered, column-major
/// grid (<see cref="ChartTemplate"/>); every other section renders as the standard horizontal shelf
/// (<see cref="ShelfTemplate"/>).
/// </summary>
public sealed partial class HomeSectionTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for a normal horizontal shelf of cards.</summary>
    public DataTemplate? ShelfTemplate { get; set; }

    /// <summary>Template for a Trending/chart section (numbered column-major grid).</summary>
    public DataTemplate? ChartTemplate { get; set; }

    /// <summary>Template for a "New music videos" section (wide 16:9 video cards).</summary>
    public DataTemplate? VideoTemplate { get; set; }

    /// <summary>Template for a moods/genres section (wrapping colour chips); falls back to the shelf.</summary>
    public DataTemplate? MoodTemplate { get; set; }

    /// <summary>Template for a "Top artists" chart (numbered column-major list with trend arrows).</summary>
    public DataTemplate? ArtistChartTemplate { get; set; }

    /// <summary>Template for the "Quick picks" shelf (compact column-major song list).</summary>
    public DataTemplate? QuickPicksTemplate { get; set; }

    /// <summary>Template for the "Listen again" shelf (account header + card shelf).</summary>
    public DataTemplate? ListenAgainTemplate { get; set; }

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        HomeSectionView { IsArtistChart: true } when ArtistChartTemplate is not null => ArtistChartTemplate,
        HomeSectionView { IsQuickPicks: true } when QuickPicksTemplate is not null => QuickPicksTemplate,
        HomeSectionView { IsListenAgain: true } when ListenAgainTemplate is not null => ListenAgainTemplate,
        HomeSectionView { IsTrending: true } when ChartTemplate is not null => ChartTemplate,
        HomeSectionView { IsVideoSection: true } when VideoTemplate is not null => VideoTemplate,
        HomeSectionView { IsMoodSection: true } when MoodTemplate is not null => MoodTemplate,
        _ => ShelfTemplate!,
    };

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
