using System;
using System.Threading.Tasks;
using KasetWin.App.Navigation;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

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

    /// <summary>Backing property for <see cref="ShowAlbum"/>.</summary>
    public static readonly DependencyProperty ShowAlbumProperty = DependencyProperty.Register(
        nameof(ShowAlbum),
        typeof(bool),
        typeof(TrackInfo),
        new PropertyMetadata(false, OnSongChanged));

    /// <summary>Backing property for <see cref="Large"/>.</summary>
    public static readonly DependencyProperty LargeProperty = DependencyProperty.Register(
        nameof(Large),
        typeof(bool),
        typeof(TrackInfo),
        new PropertyMetadata(false, OnSongChanged));

    public TrackInfo()
    {
        this.InitializeComponent();
        ApplyLanguage();
    }

    /// <summary>
    /// Applies the app language to the link tooltips. Instances inside pages are recreated on
    /// navigation; the player-bar instance persists and is relabelled via the bar's own
    /// <c>ApplyLanguage</c> cascade on language change.
    ///
    /// Deliberately tooltip-only, unlike the icon-only buttons elsewhere: each of these links wraps
    /// a <c>TextBlock</c> holding the song / artist / album name, so the accessible name is already
    /// derived from that content. Setting <c>AutomationProperties.Name</c> to "Go to album" here
    /// would <b>replace</b> the title with a generic verb and make the link less usable, not more.
    /// </summary>
    internal void ApplyLanguage()
    {
        ToolTipService.SetToolTip(StaticTitleLink, Localization.UiStrings.MenuGoToAlbum);
        ToolTipService.SetToolTip(MarqueeTitleLink, Localization.UiStrings.MenuGoToAlbum);
        ToolTipService.SetToolTip(StaticAlbumLink, Localization.UiStrings.MenuGoToAlbum);
        ToolTipService.SetToolTip(MarqueeAlbumLink, Localization.UiStrings.MenuGoToAlbum);
        ToolTipService.SetToolTip(StaticArtistLink, Localization.UiStrings.MenuGoToArtist);
        ToolTipService.SetToolTip(MarqueeArtistLink, Localization.UiStrings.MenuGoToArtist);
    }

    /// <summary>
    /// Whether to show a third line with the album name (clickable to the album). Off by default so
    /// track rows stay two-line; the player bar turns it on.
    /// </summary>
    public bool ShowAlbum
    {
        get => (bool)GetValue(ShowAlbumProperty);
        set => SetValue(ShowAlbumProperty, value);
    }

    /// <summary>Slightly larger title/subtitle text; opted in by the player bar for readability.</summary>
    public bool Large
    {
        get => (bool)GetValue(LargeProperty);
        set => SetValue(LargeProperty, value);
    }

    /// <summary>Font size for the title line. In the player bar (<see cref="Large"/>) title and
    /// artist share one size (only the weight differs); track rows keep the compact 14.</summary>
    public double TitleFontSize => 14;

    /// <summary>Font size for the artist/album line (matches the title in the player bar).</summary>
    public double SubFontSize => Large ? 14 : 12;

    /// <summary>Whether to use the scrolling marquee title (player bar only).</summary>
    public bool ShowMarqueeTitle => Large;

    /// <summary>Whether to use the static, ellipsis-trimmed title (track rows).</summary>
    public bool ShowStaticTitle => !Large;

    /// <summary>The "Artist — Album" second line for the marquee variant.</summary>
    public string SubText => ShowAlbumLine && !string.IsNullOrEmpty(AlbumText)
        ? $"{ArtistText} — {AlbumText}"
        : ArtistText;

    private Storyboard? _marquee;
    private Storyboard? _marqueeSub;

    private void OnMarqueeSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarquee();

    /// <summary>Re-runs both the title and subtitle marquees (called on size/text changes).</summary>
    private void UpdateMarquee()
    {
        if (!Large)
        {
            return;
        }

        _marquee = RunMarquee(MarqueeViewport, MarqueeRow, MarqueeShift, _marquee);
        _marqueeSub = RunMarquee(SubViewport, SubRow, SubShift, _marqueeSub);
    }

    /// <summary>
    /// Clips a marquee viewport and, when its content is wider, runs a looping slide
    /// (hold → scroll left → hold → scroll back). Returns the (possibly null) running storyboard.
    /// </summary>
    private static Storyboard? RunMarquee(Grid? viewport, FrameworkElement? row, TranslateTransform shift, Storyboard? existing)
    {
        if (viewport is null || row is null)
        {
            return existing;
        }

        double width = viewport.ActualWidth;
        double content = row.ActualWidth;

        viewport.Clip = width > 0
            ? new RectangleGeometry { Rect = new Rect(0, 0, width, viewport.ActualHeight) }
            : null;

        existing?.Stop();
        shift.X = 0;

        double overflow = content - width;
        if (width <= 0 || overflow <= 4)
        {
            return null; // Fits — no scrolling.
        }

        double distance = overflow + 12;
        double scrollSeconds = distance / 40.0; // ~40 px/second — a gentle marquee.

        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)), Value = 0 });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollSeconds)), Value = -distance });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.0 + scrollSeconds)), Value = -distance });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.0 + 2 * scrollSeconds)), Value = 0 });

        Storyboard.SetTarget(anim, shift);
        Storyboard.SetTargetProperty(anim, "X");

        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        storyboard.Children.Add(anim);
        storyboard.Begin();
        return storyboard;
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

    /// <summary>Whether to show the small "E" explicit badge next to the title (Req: explicit tag).</summary>
    public bool ShowExplicit => Track?.IsExplicit == true;

    /// <summary>Whether the title should render as a clickable album link.</summary>
    public bool HasAlbumLink => Track?.HasAlbumLink == true || CanResolveIds;

    /// <summary>Whether the artist name should render as a clickable artist link.</summary>
    public bool HasArtistLink => Track?.HasArtistLink == true || CanResolveIds;

    /// <summary>
    /// Tracks re-materialised by the queue/radio/web bridge often lose their browse ids while still
    /// carrying a videoId. Those ids can be recovered on click via the song-metadata endpoint, so
    /// the link affordance stays available instead of degrading to dead plain text.
    /// </summary>
    private bool CanResolveIds => !string.IsNullOrEmpty(Track?.VideoId);

    /// <summary>Whether the title should render as plain (non-clickable) text.</summary>
    public bool TitleIsPlain => !HasAlbumLink;

    /// <summary>Whether the artist name should render as plain (non-clickable) text.</summary>
    public bool ArtistIsPlain => !HasArtistLink;

    /// <summary>The album title text (empty when no album is set).</summary>
    public string AlbumText => Track?.Album?.Title ?? string.Empty;

    /// <summary>Whether the album line should be shown (opted in via <see cref="ShowAlbum"/> and present).</summary>
    public bool ShowAlbumLine => ShowAlbum && !string.IsNullOrEmpty(Track?.Album?.Title);

    /// <summary>Whether the album line should render as a clickable album link.</summary>
    public bool AlbumLineIsLink => ShowAlbumLine && HasAlbumLink;

    /// <summary>Whether the album line should render as plain text.</summary>
    public bool AlbumLineIsPlain => ShowAlbumLine && !HasAlbumLink;

    private static void OnSongChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Refresh every x:Bind OneWay binding against the new Song. Containers are recycled by the
        // virtualizing list, so this fires on each rebind.
        ((TrackInfo)d).Bindings.Update();
    }

    private async void OnTitleClick(object sender, RoutedEventArgs e)
    {
        var track = Track;
        if (!NavigationHelper.NavigateToSongAlbum(track))
        {
            NavigationHelper.NavigateToSongAlbum(await ResolveSongAsync(track));
        }
    }

    private async void OnArtistClick(object sender, RoutedEventArgs e)
    {
        var track = Track;
        if (!NavigationHelper.NavigateToSongArtist(track))
        {
            NavigationHelper.NavigateToSongArtist(await ResolveSongAsync(track));
        }
    }

    /// <summary>
    /// Recovers the navigable ids of a track that lost them (queue/radio bridge) by fetching its
    /// song metadata. Best-effort: <c>null</c> on failure, and the click stays a no-op.
    /// </summary>
    private static async Task<Song?> ResolveSongAsync(Song? track)
    {
        if (string.IsNullOrEmpty(track?.VideoId))
        {
            return null;
        }

        try
        {
            var client = ((App)Application.Current).Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return null;
            }

            var meta = await client.GetSongMetadataAsync(track.VideoId);
            return meta.Song;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
