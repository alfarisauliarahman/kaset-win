using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Navigation parameter for <c>ExploreDetailPage</c> (Req 31). Identifies which Explore surface to
/// render: a top-level section endpoint (<c>FEmusic_new_releases</c> / <c>FEmusic_charts</c> /
/// <c>FEmusic_moods_and_genres</c>) or a specific moods/genres category (an id encoded by
/// <see cref="HomeResponseParser"/> and recognised by <see cref="MoodCategoryId"/>).
/// </summary>
/// <param name="BrowseId">The Explore browse id (or combined mood-category id).</param>
/// <param name="Title">The display title for the detail page header.</param>
public sealed record ExploreDestination(string BrowseId, string Title);

/// <summary>
/// ViewModel for the reusable Explore detail surface (Task 24.1, Req 31.1/31.2/31.3). Renders the
/// shelves for one Explore destination — New Releases, Charts, Moods &amp; Genres, or a selected
/// mood/genre category — through the shared <see cref="SectionFeedViewModel"/> base, reusing the
/// same <see cref="HomeResponseParser"/> shape as Home/Explore. Item activation is routed by the
/// page through <c>FeedNavigation</c> to the correct detail page (Req 31.3).
/// </summary>
public sealed partial class ExploreDetailViewModel : SectionFeedViewModel
{
    private string _browseId = "FEmusic_explore";

    public ExploreDetailViewModel(IYTMusicClient client, ISingleFlight? singleFlight = null)
        : base(client, singleFlight)
    {
    }

    /// <summary>The detail page header title (e.g. "Charts", "Moods &amp; Genres", a mood name).</summary>
    [ObservableProperty]
    private string _title = "Explore";

    /// <inheritdoc />
    // Keyed by browse id so navigating between different Explore destinations loads each
    // independently while re-entrant loads of the same destination still coalesce (Req 16.3).
    protected override string SurfaceKey => $"explore-detail:{_browseId}";

    /// <summary>
    /// Points the ViewModel at <paramref name="destination"/>. Must be called before
    /// <see cref="SectionFeedViewModel.LoadInitialAsync"/> (from the page's <c>OnNavigatedTo</c>).
    /// </summary>
    public void Configure(ExploreDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _browseId = string.IsNullOrEmpty(destination.BrowseId) ? "FEmusic_explore" : destination.BrowseId;
        Title = string.IsNullOrEmpty(destination.Title) ? "Explore" : destination.Title;
    }

    /// <inheritdoc />
    protected override Task<HomeResponse> FetchInitialAsync(CancellationToken ct) => _browseId switch
    {
        "FEmusic_new_releases" => Client.GetNewReleasesAsync(ct),
        "FEmusic_charts" => Client.GetChartsAsync(ct),
        "FEmusic_moods_and_genres" => Client.GetMoodsAndGenresAsync(ct),
        _ when MoodCategoryId.IsMoodCategory(_browseId) => FetchMoodCategoryAsync(ct),
        _ => Client.GetExploreAsync(ct),
    };

    private Task<HomeResponse> FetchMoodCategoryAsync(CancellationToken ct)
    {
        var (browseId, paramsToken) = MoodCategoryId.Parse(_browseId);
        return Client.GetMoodCategoryAsync(browseId, paramsToken, ct);
    }
}
