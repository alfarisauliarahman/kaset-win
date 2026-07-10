using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Sharing;
using Microsoft.UI.Xaml;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Flattened, view-friendly projection of a single <see cref="HomeSectionItem"/> for the Home /
/// Explore shelves (Task 14.3, Req 11.1/31.1).
/// </summary>
/// <remarks>
/// <para>
/// The Core <see cref="HomeSectionItem"/> is a polymorphic record union (song / album / playlist /
/// artist), each wrapping a different model type. Binding a uniform card template directly to that
/// union would require a <c>DataTemplateSelector</c> and per-type bindings; instead this wrapper
/// exposes the common display fields (<see cref="Title"/>, <see cref="Subtitle"/>,
/// <see cref="ThumbnailUrl"/>) plus the original <see cref="Model"/> so the page code-behind can
/// route activation by concrete kind.
/// </para>
/// <para>
/// Identity is the union's kind-prefixed <see cref="HomeSectionItem.Id"/> (e.g. <c>song-…</c>,
/// <c>artist-…</c>), giving virtualized lists a stable key (Req 16.1).
/// </para>
/// </remarks>
public sealed class HomeCardItem
{
    private const double SquareRadius = 6;
    private const double RoundRadius = 80; // artist avatars read as circular

    private HomeCardItem(HomeSectionItem model, string title, string subtitle, Uri? thumbnailUrl, bool isArtist, bool canPlay, string? artistId = null, bool isMood = false, int rank = 0, TrendDirection trend = TrendDirection.None)
    {
        Model = model;
        Title = title;
        Subtitle = subtitle;
        ThumbnailUrl = thumbnailUrl;
        IsArtistCard = isArtist;
        ImageCornerRadius = new CornerRadius(isArtist ? RoundRadius : SquareRadius);
        CanShare = ShareUrlBuilder.TryCreate(model) is not null;
        CanPlay = canPlay;
        ArtistId = artistId;
        IsMood = isMood;
        ChipBrush = isMood ? MoodBrush(title) : null;
        Rank = rank;
        Trend = trend;
    }

    /// <summary>Colours for the chart movement arrows: green rising, red falling, grey steady.</summary>
    private static readonly Microsoft.UI.Xaml.Media.Brush UpBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B));

    private static readonly Microsoft.UI.Xaml.Media.Brush DownBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x3E, 0x52));

    private static readonly Microsoft.UI.Xaml.Media.Brush NeutralBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A));

    /// <summary>The chart movement direction for this card; <see cref="TrendDirection.None"/> outside charts.</summary>
    public TrendDirection Trend { get; }

    /// <summary>Whether a movement arrow should be shown beside the rank.</summary>
    public bool HasTrend => Trend != TrendDirection.None;

    /// <summary>Segoe Fluent glyph for the movement arrow (up caret / down caret / dash).</summary>
    public string TrendGlyph => Trend switch
    {
        TrendDirection.Up => "",      // CaretSolidUp
        TrendDirection.Down => "",    // CaretSolidDown
        TrendDirection.Neutral => "", // Remove (dash) - steady
        _ => string.Empty,
    };

    /// <summary>Colour for the movement arrow: green up, red down, grey steady.</summary>
    public Microsoft.UI.Xaml.Media.Brush TrendBrush => Trend switch
    {
        TrendDirection.Up => UpBrush,
        TrendDirection.Down => DownBrush,
        _ => NeutralBrush,
    };

    /// <summary>Colours used for mood/genre chips (they carry no cover art), keyed by a title hash.</summary>
    private static readonly Windows.UI.Color[] MoodPalette =
    [
        Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x3E, 0x52),
        Windows.UI.Color.FromArgb(0xFF, 0xD9, 0x6A, 0x2B),
        Windows.UI.Color.FromArgb(0xFF, 0xC9, 0x9A, 0x00),
        Windows.UI.Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B),
        Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x8A, 0x9E),
        Windows.UI.Color.FromArgb(0xFF, 0x2C, 0x6B, 0xD6),
        Windows.UI.Color.FromArgb(0xFF, 0x6A, 0x4C, 0xC4),
        Windows.UI.Color.FromArgb(0xFF, 0xB0, 0x3C, 0xA8),
    ];

    private static Microsoft.UI.Xaml.Media.Brush MoodBrush(string title)
    {
        // Deterministic FNV-1a hash: string.GetHashCode() is randomized per process, which made the
        // chip colours change on every launch.
        uint hash = 2166136261;
        foreach (var c in title ?? string.Empty)
        {
            hash = (hash ^ c) * 16777619;
        }

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(MoodPalette[hash % (uint)MoodPalette.Length]);
    }

    /// <summary>Whether this card is a mood/genre category (rendered as a solid colour chip).</summary>
    public bool IsMood { get; }

    /// <summary>Whether this card is NOT a mood chip (drives the normal cover art layout).</summary>
    public bool IsNotMood => !IsMood;

    /// <summary>The chip colour for a mood/genre card; <c>null</c> for normal cards.</summary>
    public Microsoft.UI.Xaml.Media.Brush? ChipBrush { get; }

    /// <summary>Whether this card is a video (OMV/UGC song) — rendered with a wide 16:9 thumbnail.</summary>
    public bool IsVideo => Model is HomeSectionItem.SongItem si
        && si.Song.VideoType is MusicVideoType.Omv or MusicVideoType.Ugc;

    /// <summary>Outer card width: videos are wide, moods slightly wide, the rest square.</summary>
    public double CardWidth => IsVideo ? 286 : IsMood ? 176 : 168;

    /// <summary>Tile width: 16:9 for videos, wide-but-short chips for moods, square otherwise.</summary>
    public double TileWidth => IsVideo ? 270 : IsMood ? 160 : 152;

    /// <summary>
    /// Tile height. Videos share the SAME height as square covers (152) — only their width is wider
    /// (16:9) — so a mixed shelf keeps every card's title on the same baseline (no misalignment
    /// between album and video cards). Moods are short chips.
    /// </summary>
    public double TileHeight => IsMood ? 84 : 152;

    /// <summary>Whether this card is an artist avatar (drives circular art + centred text).</summary>
    public bool IsArtistCard { get; }

    /// <summary>Text alignment for the card labels: centred for artist cards, left otherwise.</summary>
    public TextAlignment CardTextAlignment => IsArtistCard ? TextAlignment.Center : TextAlignment.Left;

    /// <summary>Alignment for the clickable subtitle link: centred for artist cards, left otherwise.</summary>
    public HorizontalAlignment SubtitleHorizontalAlignment => IsArtistCard ? HorizontalAlignment.Center : HorizontalAlignment.Left;

    /// <summary>
    /// The subtitle artist's navigable channel id (when the card's subtitle names a real artist), so
    /// the subtitle can be a clickable link to the artist page. <c>null</c> for artist cards (the card
    /// itself navigates) and when no navigable id exists.
    /// </summary>
    public string? ArtistId { get; }

    /// <summary>Whether the subtitle should render as a clickable artist link.</summary>
    public bool HasArtistLink => Core.Services.Api.Parsers.ParsingHelpers.IsNavigableArtistId(ArtistId);

    /// <summary>Whether the subtitle should render as plain text.</summary>
    public bool SubtitleIsPlain => !HasArtistLink;

    /// <summary>The original Core union item, used by the page to route activation/navigation.</summary>
    public HomeSectionItem Model { get; }

    /// <summary>Whether this item has a shareable URL; gates the Share affordance (Req 34.2).</summary>
    public bool CanShare { get; }

    /// <summary>
    /// Whether the card offers a cheap Play action (Feature A): songs, albums and non-mood
    /// playlists do; artist cards and moods/genres entry points do not (navigation only). Gates the
    /// hover/keyboard Play overlay on the card art.
    /// </summary>
    public bool CanPlay { get; }

    /// <summary>Stable, kind-prefixed identity for list virtualization (Req 16.1).</summary>
    public string Key => Model.Id;

    /// <summary>1-based chart position for a Trending/chart card; 0 for non-chart cards.</summary>
    public int Rank { get; set; }

    /// <summary>Whether this card should show its chart rank number.</summary>
    public bool HasRank => Rank > 0;

    /// <summary>The rank shown in the Trending grid (e.g. "1").</summary>
    public string RankText => Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string Title { get; }

    public string Subtitle { get; }

    public Uri? ThumbnailUrl { get; }

    /// <summary>Circular for artists, lightly rounded otherwise.</summary>
    public CornerRadius ImageCornerRadius { get; }

    /// <summary>Projects a Core <see cref="HomeSectionItem"/> into a uniform display card.</summary>
    public static HomeCardItem FromModel(HomeSectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            HomeSectionItem.SongItem s => new HomeCardItem(
                item,
                s.Song.Title,
                SongSubtitle(s.Song),
                s.Song.ThumbnailUrl ?? s.Song.FallbackThumbnailUrl,
                isArtist: false,
                canPlay: true,
                artistId: s.Song.PrimaryArtistId),

            HomeSectionItem.AlbumItem a => new HomeCardItem(
                item,
                a.Album.Title,
                AlbumSubtitle(a.Album),
                a.Album.ThumbnailUrl,
                isArtist: false,
                canPlay: true,
                artistId: FirstArtistId(a.Album.Artists)),

            HomeSectionItem.PlaylistItem p => new HomeCardItem(
                item,
                p.Pl.Title,
                p.Pl.Author?.Name ?? "Playlist",
                p.Pl.ThumbnailUrl,
                isArtist: false,
                // Moods/genres entry points browse like an Explore section rather than play.
                canPlay: !MoodCategoryId.IsMoodCategory(p.Pl.Id),
                artistId: p.Pl.Author?.Id,
                isMood: MoodCategoryId.IsMoodCategory(p.Pl.Id) && p.Pl.ThumbnailUrl is null),

            HomeSectionItem.ArtistItem ar => new HomeCardItem(
                item,
                ar.Artist.Name,
                ar.Artist.HasSubtitle ? ar.Artist.SubtitleText! : "Artist",
                ar.Artist.ThumbnailUrl,
                isArtist: true,
                // No cheap artist play action — the card navigates to the artist page (Feature A).
                canPlay: false,
                rank: ar.Artist.Rank,
                trend: ar.Artist.Trend),

            _ => new HomeCardItem(item, "Unknown", string.Empty, null, isArtist: false, canPlay: false),
        };
    }

    private static string? FirstArtistId(System.Collections.Generic.IReadOnlyList<Artist> artists)
    {
        foreach (var a in artists)
        {
            if (!string.IsNullOrEmpty(a.Id))
            {
                return a.Id;
            }
        }

        return null;
    }

    /// <summary>Artist(s), plus the view count for video cards ("Artis • 2,5 jt x ditonton").</summary>
    private static string SongSubtitle(Song song)
    {
        var artists = song.ArtistsDisplay;
        return string.IsNullOrEmpty(song.ListenerCountText)
            ? artists
            : string.IsNullOrEmpty(artists) ? song.ListenerCountText : $"{artists} • {song.ListenerCountText}";
    }

    private static string AlbumSubtitle(Album album)
    {
        var artists = album.ArtistsDisplay;
        if (string.IsNullOrEmpty(album.Year))
        {
            return string.IsNullOrEmpty(artists) ? "Album" : artists;
        }

        return string.IsNullOrEmpty(artists) ? album.Year : $"{artists} • {album.Year}";
    }
}

/// <summary>
/// View projection of a <see cref="HomeSection"/>: a title plus its shelf of
/// <see cref="HomeCardItem"/> cards (Task 14.3).
/// </summary>
public sealed class HomeSectionView
{
    public HomeSectionView(string title, IReadOnlyList<HomeCardItem> items, bool isChart = false, bool isTrending = false, bool isVideoSection = false)
    {
        Title = title;
        Items = items;
        IsChart = isChart;
        IsTrending = isTrending;
        IsVideoSection = isVideoSection;
    }

    public string Title { get; }

    public IReadOnlyList<HomeCardItem> Items { get; }

    /// <summary>Whether this is a chart section (any chart keyword).</summary>
    public bool IsChart { get; }

    /// <summary>
    /// Whether this is specifically the "Trending" shelf, which alone is rendered as the numbered
    /// column-major grid (other chart sections like "Charts" stay normal shelves).
    /// </summary>
    public bool IsTrending { get; }

    /// <summary>Whether this is a "New music videos" style shelf, rendered as wide 16:9 video cards.</summary>
    public bool IsVideoSection { get; }

    /// <summary>Whether every card is a mood/genre chip — rendered as a wrapping grid of colour chips.</summary>
    public bool IsMoodSection => Items.Count > 0 && Items.All(i => i.IsMood);

    /// <summary>Whether every item is a song/video — the Related panel lists these as compact rows.</summary>
    public bool IsAllSongs => Items.Count > 0 && Items.All(i => i.Model is HomeSectionItem.SongItem);

    /// <summary>
    /// Whether this is a "Top artists" chart shelf — every item is a ranked artist. Rendered as the
    /// numbered, column-major list with a movement arrow + subscriber count (YT Music "Artis teratas").
    /// </summary>
    public bool IsArtistChart => Items.Count > 0
        && Items.All(i => i.Model is HomeSectionItem.ArtistItem)
        && Items.Any(i => i.Rank > 0);

    /// <summary>
    /// Whether this is the "Quick picks" / "Pilihan cepat" shelf — rendered as a compact,
    /// column-major song list (thumb + title + subtitle, 4 rows per column) like YT Music.
    /// </summary>
    public bool IsQuickPicks => Items.Count > 0
        && Items.All(i => i.Model is HomeSectionItem.SongItem)
        && (Title.Contains("quick picks", StringComparison.OrdinalIgnoreCase)
            || Title.Contains("pilihan cepat", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether this is the "Listen again" / "Dengarkan lagi" shelf (gets the account header).</summary>
    public bool IsListenAgain =>
        Title.Contains("listen again", StringComparison.OrdinalIgnoreCase)
        || Title.Contains("dengarkan lagi", StringComparison.OrdinalIgnoreCase);

    /// <summary>The signed-in account name, upper-cased for the "Listen again" strapline (e.g. "ALFARIS").</summary>
    public string AccountName => (AccountContext.Name ?? string.Empty).ToUpperInvariant();

    /// <summary>
    /// The "Listen again" title with the leading account name removed, so the header does not repeat
    /// the name (strapline already shows it). YT sends the title as "Alfaris Listen again" →
    /// strapline "ALFARIS" + title "Listen again".
    /// </summary>
    public string ListenAgainTitle
    {
        get
        {
            var name = AccountContext.Name;
            if (!string.IsNullOrWhiteSpace(name)
                && Title.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
            {
                return Title[(name.Length + 1)..].TrimStart();
            }

            return Title;
        }
    }

    /// <summary>Whether an account name is available to show in the header strapline.</summary>
    public bool HasAccountName => !string.IsNullOrWhiteSpace(AccountContext.Name);

    /// <summary>The signed-in account avatar for the "Listen again" header.</summary>
    public Uri? AccountAvatarUrl => AccountContext.AvatarUrl;

    /// <summary>Projects a Core <see cref="HomeSection"/> (skipping empty shelves).</summary>
    public static HomeSectionView FromModel(HomeSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var cards = section.Items.Select(HomeCardItem.FromModel).ToList();

        var isTrending = section.Title.Contains("trending", StringComparison.OrdinalIgnoreCase);
        // "video" in the title alone is fragile (could match playlist shelves); require the items to
        // actually be songs/videos so only genuine video rails get the wide 16:9 cards.
        var isVideoSection = section.Title.Contains("video", StringComparison.OrdinalIgnoreCase)
            && section.Items.All(i => i is HomeSectionItem.SongItem);

        // Only the Trending shelf shows a 1-based rank number in its numbered grid.
        if (isTrending)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].Rank = i + 1;
            }
        }

        return new HomeSectionView(section.Title, cards, section.IsChart, isTrending, isVideoSection);
    }
}
