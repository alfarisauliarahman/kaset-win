using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Shared base for the section-based browse surfaces — Home (Task 14.3, Req 11.1/11.2) and
/// Explore (Req 31.1). Loads a <see cref="HomeResponse"/>, projects its sections into
/// <see cref="HomeSectionView"/> shelves, and (when the surface exposes a continuation token)
/// appends further pages via <see cref="IYTMusicClient.GetHomeContinuationAsync"/> (Req 11.2).
/// </summary>
/// <remarks>
/// The initial load runs through <see cref="ViewModelBase.LoadAsync"/> keyed by
/// <see cref="SurfaceKey"/> so re-entrant navigation/Loaded triggers join a single in-flight load
/// (Req 16.3) rather than re-fetching. Continuation pages are coalesced behind
/// <c>$"{SurfaceKey}:more"</c>.
/// </remarks>
public abstract partial class SectionFeedViewModel : ViewModelBase
{
    private protected readonly IYTMusicClient Client;

    private string? _continuationToken;

    private protected SectionFeedViewModel(IYTMusicClient client, ISingleFlight? singleFlight = null)
        : base(singleFlight)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>The rendered shelves, in source order.</summary>
    public ObservableCollection<HomeSectionView> Sections { get; } = [];

    /// <summary>True when a continuation token is available to load more shelves (Req 11.2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    private bool _hasMore;

    /// <summary>Convenience flag for binding the "load more" affordance visibility.</summary>
    public bool CanLoadMore => HasMore && !IsLoading;

    /// <summary>Stable single-flight key for the surface's initial load (e.g. <c>"home"</c>).</summary>
    protected abstract string SurfaceKey { get; }

    /// <summary>Fetches the initial section page for the surface.</summary>
    protected abstract Task<HomeResponse> FetchInitialAsync(CancellationToken ct);

    /// <summary>
    /// Loads the surface once per in-flight <see cref="SurfaceKey"/>. Safe to call repeatedly (e.g.
    /// from the page <c>Loaded</c> handler); concurrent calls join the same load (Req 16.3).
    /// </summary>
    public Task<bool> LoadInitialAsync(CancellationToken ct = default) =>
        LoadAsync(SurfaceKey, async token =>
        {
            var response = await FetchInitialAsync(token).ConfigureAwait(true);
            Sections.Clear();
            AppendSections(response);
            SetContinuation(response.ContinuationToken);
        }, ct);

    /// <summary>
    /// Appends the next continuation page (Req 11.2). No-op when no token is available; concurrent
    /// invocations are coalesced and any error surfaces via <see cref="ViewModelBase.ErrorMessage"/>.
    /// </summary>
    public Task<bool> LoadMoreAsync(CancellationToken ct = default)
    {
        var token = _continuationToken;
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(true);
        }

        return LoadAsync($"{SurfaceKey}:more", async innerCt =>
        {
            var response = await Client.GetHomeContinuationAsync(token, innerCt).ConfigureAwait(true);
            AppendSections(response);
            SetContinuation(response.ContinuationToken);
        }, ct);
    }

    /// <summary>
    /// Loads the initial page, then keeps pulling continuation pages until the feed is exhausted, so
    /// the surface shows everything at once without a "Load more" button. Bounded by a page guard.
    /// </summary>
    public async Task LoadAllAsync(CancellationToken ct = default)
    {
        await LoadInitialAsync(ct).ConfigureAwait(true);

        var guard = 0;
        while (HasMore && !ct.IsCancellationRequested && guard++ < 100)
        {
            await LoadMoreAsync(ct).ConfigureAwait(true);
        }
    }

    private void AppendSections(HomeResponse response)
    {
        foreach (var section in response.Sections)
        {
            if (section.Items.Count == 0)
            {
                continue; // skip empty shelves so the surface stays dense
            }

            if (IsNavShortcutSection(section))
            {
                continue; // drop the redundant "New releases / Charts / Moods" shortcut shelf
            }

            Sections.Add(HomeSectionView.FromModel(section));
        }
    }

    /// <summary>
    /// Whether a shelf is just the top-level Explore navigation shortcuts (New Releases / Charts /
    /// Moods &amp; Genres) surfaced as empty playlist cards — those duplicate the entry-point buttons
    /// and are dropped.
    /// </summary>
    private static readonly string[] NavShortcutIds =
    [
        "FEmusic_new_releases",
        "FEmusic_charts",
        "FEmusic_moods_and_genres",
    ];

    private static bool IsNavShortcutSection(HomeSection section) =>
        section.Items.Any(i => i is HomeSectionItem.PlaylistItem { Pl.Id: { } id }
            && NavShortcutIds.Contains(id));

    private void SetContinuation(string? token)
    {
        _continuationToken = token;
        HasMore = !string.IsNullOrEmpty(token);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // CanLoadMore is derived from both HasMore and IsLoading; the latter is declared on the
        // base type so re-raise the dependent flag when it changes.
        if (e.PropertyName == nameof(IsLoading))
        {
            OnPropertyChanged(nameof(CanLoadMore));
        }
    }
}
