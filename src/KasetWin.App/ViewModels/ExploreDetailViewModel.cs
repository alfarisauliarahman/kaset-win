using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Api.Parsers;
using KasetWin.Core.Services.Player;

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
    private const string ChartsBrowseId = "FEmusic_charts";

    /// <summary>Persisted country pick for the Charts surface (ISO 3166-1 alpha-2, "ZZ" = Global).</summary>
    private const string ChartsCountrySettingKey = "explore.chartsCountry";

    private string _browseId = "FEmusic_explore";

    // The country the NEXT charts load will request; null defers to the server (account/IP region),
    // which is the desired first-run default — the response then tells us what it picked.
    private string? _chartsCountry;

    // Repopulating the ComboBox raises SelectedItem callbacks; only a USER pick may persist +
    // reload, so programmatic (re)selection is fenced off with this guard.
    private bool _applyingChartCountry;

    private readonly IQueueService? _queue;
    private readonly Notifications.IInAppNotifier? _notifier;

    public ExploreDetailViewModel(
        IYTMusicClient client,
        ISingleFlight? singleFlight = null,
        IQueueService? queue = null,
        Notifications.IInAppNotifier? notifier = null)
        : base(client, singleFlight)
    {
        _queue = queue;
        _notifier = notifier;
    }

    /// <summary>Queues a song card to play right after the current one ("Putar setelah ini").</summary>
    [RelayCommand]
    private void PlayTrackNext(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (_queue is null)
        {
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.InsertNext([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastPlayingNext(song.Title));
    }

    /// <summary>Appends a song card to the play queue ("Tambahkan ke antrean").</summary>
    [RelayCommand]
    private void AddTrackToQueue(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (_queue is null)
        {
            _notifier?.Show(Localization.UiStrings.ToastQueueUnavailable);
            return;
        }

        var added = _queue.AppendDeduplicated([song]);
        _notifier?.Show(added == 0
            ? Localization.UiStrings.ToastSongAlreadyQueued
            : Localization.UiStrings.ToastAddedToQueue(song.Title));
    }

    /// <summary>The detail page header title (e.g. "Charts", "Moods &amp; Genres", a mood name).</summary>
    [ObservableProperty]
    private string _title = "Explore";

    // ── Charts country dropdown ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The selectable chart countries, parsed out of the charts response itself
    /// (<c>musicSortFilterButtonRenderer</c>) — never a hardcoded list. Empty on non-Charts
    /// destinations, which keeps the dropdown collapsed there.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<ChartCountry> ChartCountries { get; } = [];

    /// <summary>The country currently shown/selected in the Charts dropdown.</summary>
    [ObservableProperty]
    private ChartCountry? _selectedChartCountry;

    /// <summary>Whether the response carried a country menu (gates the dropdown's visibility).</summary>
    [ObservableProperty]
    private bool _hasChartCountries;

    partial void OnSelectedChartCountryChanged(ChartCountry? value)
    {
        if (_applyingChartCountry || value is null
            || string.Equals(value.Code, _chartsCountry, StringComparison.Ordinal))
        {
            return;
        }

        _chartsCountry = value.Code;
        SaveChartsCountry(value.Code);

        // Reload ONLY this charts feed with the new formData; the Explore landing page is a
        // separate surface and stays untouched. Errors surface via the base ErrorMessage.
        _ = LoadInitialAsync();
    }

    private async Task<HomeResponse> FetchChartsAsync(CancellationToken ct)
    {
        var page = await Client.GetChartsAsync(_chartsCountry, ct).ConfigureAwait(true);
        ApplyChartCountrySelector(page.Countries);
        return page.Home;
    }

    private void ApplyChartCountrySelector(ChartCountrySelector selector)
    {
        _applyingChartCountry = true;
        try
        {
            ChartCountries.Clear();
            foreach (var option in selector.Options)
            {
                ChartCountries.Add(option);
            }

            // Prefer the server-declared selection (the filter button's own label): when the
            // request sent no formData, that is the only way to learn the account's region.
            var effectiveCode = selector.SelectedCode ?? _chartsCountry;
            SelectedChartCountry = selector.Options.FirstOrDefault(
                o => string.Equals(o.Code, effectiveCode, StringComparison.Ordinal));

            // Track what is now applied so re-picking the same country is a no-op.
            _chartsCountry = SelectedChartCountry?.Code ?? _chartsCountry;
            HasChartCountries = ChartCountries.Count > 0;
        }
        finally
        {
            _applyingChartCountry = false;
        }
    }

    // Read/write straight through the Platform settings bag (works packaged and unpackaged),
    // guarded like AppHost's lyrics pin: a storage hiccup may cost the preference, never the page.
    private static string? ReadSavedChartsCountry()
    {
        try
        {
            return KasetWin.Platform.Storage.AppData.Settings[ChartsCountrySettingKey] as string;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveChartsCountry(string code)
    {
        try
        {
            KasetWin.Platform.Storage.AppData.Settings[ChartsCountrySettingKey] = code;
        }
        catch
        {
            // Preference only — losing it costs one re-pick on the next visit.
        }
    }

    /// <inheritdoc />
    // Keyed by browse id so navigating between different Explore destinations loads each
    // independently while re-entrant loads of the same destination still coalesce (Req 16.3).
    // Charts also keys on the country: without it, picking a second country while the first
    // load is still in flight would silently JOIN that load and never fetch the new country.
    protected override string SurfaceKey => _browseId == ChartsBrowseId
        ? $"explore-detail:{_browseId}:{_chartsCountry ?? "auto"}"
        : $"explore-detail:{_browseId}";

    /// <summary>
    /// Points the ViewModel at <paramref name="destination"/>. Must be called before
    /// <see cref="SectionFeedViewModel.LoadInitialAsync"/> (from the page's <c>OnNavigatedTo</c>).
    /// </summary>
    public void Configure(ExploreDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _browseId = string.IsNullOrEmpty(destination.BrowseId) ? "FEmusic_explore" : destination.BrowseId;
        Title = string.IsNullOrEmpty(destination.Title) ? "Explore" : destination.Title;

        if (_browseId == ChartsBrowseId)
        {
            // The saved pick (if any) rides on the very first load; absent, the server decides
            // from the account region and the response tells the dropdown what it picked.
            _chartsCountry = ReadSavedChartsCountry();
        }
    }

    /// <inheritdoc />
    protected override Task<HomeResponse> FetchInitialAsync(CancellationToken ct) => _browseId switch
    {
        "FEmusic_new_releases" => Client.GetNewReleasesAsync(ct),
        ChartsBrowseId => FetchChartsAsync(ct),
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
