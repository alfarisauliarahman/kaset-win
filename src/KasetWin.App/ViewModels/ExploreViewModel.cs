using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;

namespace KasetWin.App.ViewModels;

/// <summary>
/// ViewModel for the Explore surface (Task 14.3, Req 31.1). Loads <c>FEmusic_explore</c> via
/// <see cref="IYTMusicClient.GetExploreAsync"/> — New Releases / Charts / Moods &amp; Genres
/// shortcut shelves — and renders them through the shared <see cref="SectionFeedViewModel"/> base.
/// If the Explore response carries a continuation token it paginates like Home (Req 11.2).
/// </summary>
public sealed class ExploreViewModel : SectionFeedViewModel
{
    public ExploreViewModel(IYTMusicClient client, ISingleFlight? singleFlight = null)
        : base(client, singleFlight)
    {
    }

    /// <inheritdoc />
    protected override string SurfaceKey => "explore";

    /// <inheritdoc />
    protected override Task<HomeResponse> FetchInitialAsync(CancellationToken ct) =>
        Client.GetExploreAsync(ct);
}
