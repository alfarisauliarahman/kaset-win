using KasetWin.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Controls;

/// <summary>
/// Picks the layout for a Related-panel section: all-song sections ("Kamu mungkin juga suka",
/// "Penampilan lainnya") render as compact rows in a column-major grid; everything else (playlists,
/// similar artists, more-from-artist albums) keeps the horizontal card rail.
/// </summary>
public sealed partial class RelatedSectionTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for an all-song section (column-major compact rows).</summary>
    public DataTemplate? SongsTemplate { get; set; }

    /// <summary>Template for a card rail section (playlists / artists / albums).</summary>
    public DataTemplate? CardsTemplate { get; set; }

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item) =>
        item is HomeSectionView { IsAllSongs: true } ? SongsTemplate! : CardsTemplate!;

    /// <inheritdoc />
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
