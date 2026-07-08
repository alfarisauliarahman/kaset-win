using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Navigation parameter for an artist rail "See all". Carries the rail's ALREADY-PARSED items so the
/// grid shows exactly that rail's content (no re-browse, so nothing gets mixed up or swapped).
/// Exactly one of the item lists is populated per rail.
/// </summary>
public sealed record ArtistSeeAllRequest(
    string Title,
    IReadOnlyList<Song>? Videos = null,
    IReadOnlyList<Album>? Albums = null,
    IReadOnlyList<Playlist>? Playlists = null,
    IReadOnlyList<Artist>? Artists = null);

/// <summary>
/// "See all" surface for an artist rail (Videos / Live / Albums / Singles / Featured). Fetches the
/// rail's browse target, classifies the items, and shows videos as 16:9 cards (title, artist, views)
/// or albums/playlists as square cards. Videos play into the queue; albums/playlists navigate.
/// </summary>
public sealed partial class ArtistVideosPage : Page, INotifyPropertyChanged
{
    private const string AlbumPageTypeName = "KasetWin.App.Views.AlbumPage";
    private const string PlaylistPageTypeName = "KasetWin.App.Views.PlaylistPage";

    private readonly IPlayerService _player;

    private readonly ObservableCollection<Song> _videos = [];
    private readonly ObservableCollection<object> _cards = [];
    private readonly ObservableCollection<Artist> _artists = [];

    public ArtistVideosPage()
    {
        var services = App.Current.Services;
        _player = services.GetRequiredService<IPlayerService>();
        this.InitializeComponent();
        // Bind in code so the XAML type generator never tries to construct the required-member models.
        VideosGrid.ItemsSource = _videos;
        CardsGrid.ItemsSource = _cards;
        ArtistsGrid.ItemsSource = _artists;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _title = "Lihat semua";
    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { Set(ref _isLoading, value); Raise(nameof(IsEmpty)); }
    }

    public bool IsEmpty => !IsLoading && _videos.Count == 0 && _cards.Count == 0 && _artists.Count == 0;

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not ArtistSeeAllRequest req)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.Title))
        {
            Title = req.Title;
        }

        _videos.Clear();
        if (req.Videos is not null)
        {
            foreach (var v in req.Videos)
            {
                _videos.Add(v);
            }
        }

        _cards.Clear();
        if (req.Albums is not null)
        {
            foreach (var a in req.Albums)
            {
                _cards.Add(a);
            }
        }

        if (req.Playlists is not null)
        {
            foreach (var p in req.Playlists)
            {
                _cards.Add(p);
            }
        }

        _artists.Clear();
        if (req.Artists is not null)
        {
            foreach (var a in req.Artists)
            {
                _artists.Add(a);
            }
        }

        VideosGrid.Visibility = _videos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsGrid.Visibility = _cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ArtistsGrid.Visibility = _artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Raise(nameof(IsEmpty));
    }

    private void OnVideoClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Song video && _videos.Count > 0)
        {
            var index = _videos.IndexOf(video);
            _ = _player.PlayCollectionAsync([.. _videos], index < 0 ? 0 : index);
        }
    }

    private void OnCardClick(object sender, ItemClickEventArgs e)
    {
        switch (e.ClickedItem)
        {
            case Album album:
                NavigateByTypeName(AlbumPageTypeName, album.Id);
                break;
            case Playlist playlist:
                NavigateByTypeName(PlaylistPageTypeName, playlist.Id);
                break;
        }
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Artist artist)
        {
            Navigation.NavigationHelper.NavigateToArtist(artist.Id);
        }
    }

    private void NavigateByTypeName(string typeName, string parameter)
    {
        if (Navigation.NavigationHelper.ResolvePageType(typeName) is { } pageType && !string.IsNullOrEmpty(parameter))
        {
            Frame?.Navigate(pageType, parameter);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        Raise(name);
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
