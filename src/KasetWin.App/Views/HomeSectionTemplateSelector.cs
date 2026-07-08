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

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        HomeSectionView { IsTrending: true } => ChartTemplate!,
        HomeSectionView { IsVideoSection: true } => VideoTemplate!,
        HomeSectionView { IsMoodSection: true } when MoodTemplate is not null => MoodTemplate,
        _ => ShelfTemplate!,
    };

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
