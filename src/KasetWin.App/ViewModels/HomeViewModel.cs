using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Favorites;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the Home surface (Task 14.3, Req 11.1/11.2/11.3). Loads <c>FEmusic_home</c> via
/// <see cref="IYTMusicClient.GetHomeAsync"/> and paginates with
/// <see cref="IYTMusicClient.GetHomeContinuationAsync"/> through the shared
/// <see cref="SectionFeedViewModel"/> base.
/// </summary>
/// <remarks>
/// Home additionally owns the Favorites shelf (Task 22.1, Req 29.4): it mirrors
/// <see cref="IFavoritesService.Items"/> into <see cref="Favorites"/> and keeps it in sync via the
/// service's <see cref="IFavoritesService.Changed"/> event. The shelf is shown only while
/// <see cref="HasFavorites"/> is <see langword="true"/> (Req 29.4).
/// </remarks>
public sealed partial class HomeViewModel : SectionFeedViewModel
{
    private readonly IFavoritesService? _favorites;

    public HomeViewModel(IYTMusicClient client, ISingleFlight? singleFlight = null, IFavoritesService? favorites = null)
        : base(client, singleFlight)
    {
        _favorites = favorites;
        if (_favorites is not null)
        {
            _favorites.Changed += OnFavoritesChanged;
            RefreshFavorites();
        }
    }

    /// <summary>The favorited items shown at the top of Home, in pin order (Req 29.4).</summary>
    public ObservableCollection<FavoriteCardItem> Favorites { get; } = [];

    /// <summary>True when there is at least one favorite to show the Favorites shelf (Req 29.4).</summary>
    [ObservableProperty]
    private bool _hasFavorites;

    /// <inheritdoc />
    protected override string SurfaceKey => "home";

    /// <inheritdoc />
    protected override Task<HomeResponse> FetchInitialAsync(CancellationToken ct) =>
        Client.GetHomeAsync(ct);

    /// <summary>Whether <paramref name="contentId"/> is currently favorited.</summary>
    public bool IsFavorite(string contentId) =>
        _favorites is not null && !string.IsNullOrEmpty(contentId) && _favorites.Contains(contentId);

    /// <summary>Pins or unpins <paramref name="item"/> (Req 29.1/29.2).</summary>
    public void ToggleFavorite(FavoriteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _favorites?.Toggle(item);
    }

    /// <summary>Removes the favorite identified by <paramref name="contentId"/> (Req 29.2).</summary>
    public void RemoveFavorite(string contentId) => _favorites?.Remove(contentId);

    /// <summary>Persists a reorder of the Favorites shelf (Req 29.3).</summary>
    public void MoveFavorite(int fromIndex, int toIndex) => _favorites?.Move(fromIndex, toIndex);

    private void OnFavoritesChanged(object? sender, EventArgs e) => RefreshFavorites();

    private void RefreshFavorites()
    {
        Favorites.Clear();
        if (_favorites is not null)
        {
            foreach (var item in _favorites.Items)
            {
                Favorites.Add(new FavoriteCardItem(item));
            }
        }

        HasFavorites = Favorites.Count > 0;
    }
}
