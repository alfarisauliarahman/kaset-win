using KasetWin.App.Navigation;
using KasetWin.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Controls;

/// <summary>
/// Reusable two-line track label (title over artist) that renders the title and artist name as
/// clickable, inline hyperlink-style affordances when — and only when — a real navigation id is
/// available (Feature C). Used by every track row across the shell (Album/Playlist/Artist top
/// songs/Queue/History/Search) so the clickable behaviour and navigation routing live in one place.
/// </summary>
/// <remarks>
/// <para>
/// The title navigates to the song's album/single page when <see cref="Song.AlbumBrowseId"/> exists;
/// the artist navigates to the first artist carrying a channel id (<see cref="Song.PrimaryArtistId"/>).
/// When the corresponding id is absent the label degrades to plain, non-interactive text — ids are
/// never fabricated. Navigation routes through the shared <see cref="NavigationHelper"/>.
/// </para>
/// <para>
/// Bound via a single <see cref="Song"/> dependency property so it slots into existing
/// <c>x:DataType="Song"</c> row templates as <c>&lt;controls:TrackInfo Song="{x:Bind}" /&gt;</c>.
/// </para>
/// </remarks>
public sealed partial class TrackInfo : UserControl
{
    /// <summary>Backing <see cref="DependencyProperty"/> for <see cref="Song"/>.</summary>
    public static readonly DependencyProperty SongProperty = DependencyProperty.Register(
        nameof(Song),
        typeof(object),
        typeof(TrackInfo),
        new PropertyMetadata(null, OnSongChanged));

    public TrackInfo()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// The track whose title/artist are shown; drives all the bound display properties. Typed as
    /// <see cref="object"/> (rather than <see cref="Core.Models.Song"/>) so the XAML type generator
    /// never emits an activator for the required-member <c>Song</c> record; consumers bind a
    /// <c>Song</c> directly and it is read back through <see cref="Track"/>.
    /// </summary>
    public object? Song
    {
        get => GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    private Song? Track => GetValue(SongProperty) as Song;

    /// <summary>The track title text (empty when no track is set).</summary>
    public string TitleText => Track?.Title ?? string.Empty;

    /// <summary>The comma-joined artist display text (empty when no track is set).</summary>
    public string ArtistText => Track?.ArtistsDisplay ?? string.Empty;

    /// <summary>Whether the title should render as a clickable album link.</summary>
    public bool HasAlbumLink => Track?.HasAlbumLink == true;

    /// <summary>Whether the artist name should render as a clickable artist link.</summary>
    public bool HasArtistLink => Track?.HasArtistLink == true;

    /// <summary>Whether the title should render as plain (non-clickable) text.</summary>
    public bool TitleIsPlain => !HasAlbumLink;

    /// <summary>Whether the artist name should render as plain (non-clickable) text.</summary>
    public bool ArtistIsPlain => !HasArtistLink;

    private static void OnSongChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Refresh every x:Bind OneWay binding against the new Song. Containers are recycled by the
        // virtualizing list, so this fires on each rebind.
        ((TrackInfo)d).Bindings.Update();
    }

    private void OnTitleClick(object sender, RoutedEventArgs e) => NavigationHelper.NavigateToSongAlbum(Track);

    private void OnArtistClick(object sender, RoutedEventArgs e) => NavigationHelper.NavigateToSongArtist(Track);
}
