using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Lyrics;
using KasetWin.Core.Services.Player;
using Microsoft.UI.Dispatching;

namespace KasetWin.App.ViewModels;

/// <summary>
/// Backs the <see cref="Views.LyricsPage"/> (Task 14.9, Req 17.2/17.3/9.1). Observes the singleton
/// <see cref="IPlayerService"/> and <see cref="ILyricsService"/> and projects their state into a
/// view-friendly shape:
/// </summary>
/// <remarks>
/// <para>
/// <b>Lyric resolution.</b> When the player's <see cref="IPlayerService.CurrentTrack"/> changes the
/// ViewModel builds a <see cref="LyricsSearchInfo"/> (title/artist/album/duration/videoId) and asks
/// <see cref="ILyricsService.LoadForTrackAsync"/> to resolve it (Req 17.1). The service applies the
/// <c>synced → plain → unavailable</c> priority and caches by videoId; this ViewModel only mirrors
/// the resolved <see cref="ILyricsService.CurrentLyrics"/> onto its bindable collections.
/// </para>
/// <para>
/// <b>Synced highlighting (Req 17.2).</b> For synced lyrics each line is wrapped in a
/// <see cref="LyricLineItem"/> with an observable <see cref="LyricLineItem.IsActive"/> flag. As the
/// player's <see cref="IPlayerService.Progress"/> advances, <see cref="SyncedLyricsNavigator"/>
/// resolves the current line index (monotonic in time); the previously active line is cleared and
/// the new one is marked active, and <see cref="ActiveLineChanged"/> is raised so the page can
/// auto-scroll the current line into view.
/// </para>
/// <para>
/// <b>Fallbacks.</b> When only plain lyrics exist they are shown as a single text block (Req 17.3);
/// when nothing is available an empty-state message is shown. A <c>LIVE</c> indicator mirrors
/// <see cref="IPlayerService.IsLive"/> (Req 9.1), and <see cref="ActiveProvider"/> /
/// <see cref="IsLoadingLyrics"/> surface the service's lookup state.
/// </para>
/// <para>
/// Both services raise <see cref="INotifyPropertyChanged"/> from arbitrary threads (the player is
/// driven by WebView2 JS-bridge messages), so every handler marshals its state mutation back onto
/// the UI thread captured at construction. The page must call <see cref="Dispose"/> on unload to
/// detach the subscriptions.
/// </para>
/// </remarks>
public sealed partial class LyricsViewModel : ViewModelBase, IDisposable
{
    private readonly IPlayerService _player;
    private readonly ILyricsService _lyrics;
    private readonly DispatcherQueue? _dispatcher;

    private SyncedLyrics? _syncedLyrics;
    private int _activeLineIndex = -1;
    private string? _loadedVideoId;
    private bool _disposed;

    /// <summary>Creates the ViewModel from the DI-resolved player and lyrics services (resolved by the page).</summary>
    public LyricsViewModel(IPlayerService player, ILyricsService lyrics)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _lyrics = lyrics ?? throw new ArgumentNullException(nameof(lyrics));
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _player.PropertyChanged += OnPlayerPropertyChanged;
        _lyrics.PropertyChanged += OnLyricsPropertyChanged;

        IsLive = _player.IsLive;
        ActiveProvider = _lyrics.ActiveProvider;
        IsLoadingLyrics = _lyrics.IsLoading;
    }

    /// <summary>The synced lyric lines (each with its own active flag), in time order (Req 17.2).</summary>
    public ObservableCollection<LyricLineItem> Lines { get; } = [];

    /// <summary>Title of the track lyrics are shown for, or <c>null</c> when nothing is playing.</summary>
    [ObservableProperty]
    private string? _trackTitle;

    /// <summary>Artist display line for the current track, or <c>null</c>.</summary>
    [ObservableProperty]
    private string? _trackArtist;

    /// <summary>
    /// Album browseId of the current track (for the clickable title affordance, Feature C), or
    /// <c>null</c> when the track carries no album. Never fabricated.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlbumLink))]
    [NotifyPropertyChangedFor(nameof(TitleIsPlain))]
    private string? _albumBrowseId;

    /// <summary>
    /// Channel id of the current track's primary artist (for the clickable artist affordance), or
    /// <c>null</c> when no artist carries a navigable id.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtistLink))]
    [NotifyPropertyChangedFor(nameof(ArtistIsPlain))]
    private string? _artistChannelId;

    /// <summary>Whether the header title should render as a clickable album link.</summary>
    public bool HasAlbumLink => !string.IsNullOrEmpty(AlbumBrowseId);

    /// <summary>Whether the header title should render as plain text.</summary>
    public bool TitleIsPlain => !HasAlbumLink;

    /// <summary>Whether the header artist should render as a clickable artist link.</summary>
    public bool HasArtistLink => !string.IsNullOrEmpty(ArtistChannelId);

    /// <summary>Whether the header artist should render as plain text.</summary>
    public bool ArtistIsPlain => !HasArtistLink;

    /// <summary>The plain (unsynced) lyric text shown as a fallback (Req 17.3), or <c>null</c>.</summary>
    [ObservableProperty]
    private string? _plainText;

    /// <summary>Whether synced lyrics are available and should be shown (Req 17.2).</summary>
    [ObservableProperty]
    private bool _showSynced;

    /// <summary>Whether plain lyrics are available and should be shown as a fallback (Req 17.3).</summary>
    [ObservableProperty]
    private bool _showPlain;

    /// <summary>Whether no lyrics are available and the empty-state message should be shown.</summary>
    [ObservableProperty]
    private bool _showEmpty;

    /// <summary>Whether the current content is a live stream; drives the LIVE indicator (Req 9.1).</summary>
    [ObservableProperty]
    private bool _isLive;

    /// <summary>Label of the provider that produced the current lyrics (e.g. <c>"LRCLib"</c>), or <c>null</c>.</summary>
    [ObservableProperty]
    private string? _activeProvider;

    /// <summary>True while a lyrics lookup is in flight; bind to a progress affordance.</summary>
    [ObservableProperty]
    private bool _isLoadingLyrics;

    /// <summary>
    /// Raised on the UI thread when the active synced line changes so the hosting page can scroll
    /// the current line into view (Req 17.2). The argument is the newly active line, or <c>null</c>
    /// when the position precedes the first line.
    /// </summary>
    public event EventHandler<LyricLineItem?>? ActiveLineChanged;

    /// <summary>
    /// Resolves lyrics for whatever is currently playing. Called by the page on navigation so the
    /// panel populates immediately for an already-playing track (Req 17.1).
    /// </summary>
    public Task RefreshAsync(CancellationToken ct = default)
        => RunSafeAsync(c => LoadForCurrentTrackAsync(force: true, c), ct);

    private async Task LoadForCurrentTrackAsync(bool force, CancellationToken ct)
    {
        var track = _player.CurrentTrack;
        TrackTitle = track?.Title;
        TrackArtist = track?.ArtistsDisplay;
        AlbumBrowseId = track?.AlbumBrowseId;
        ArtistChannelId = track?.PrimaryArtistId;

        var info = BuildSearchInfo(track);
        if (info is null)
        {
            _loadedVideoId = null;
            ClearLyrics();
            return;
        }

        // Skip a redundant reload for the same track unless the caller forces it (navigation).
        if (!force && string.Equals(info.VideoId, _loadedVideoId, StringComparison.Ordinal))
        {
            return;
        }

        _loadedVideoId = info.VideoId;
        await _lyrics.LoadForTrackAsync(info, ct).ConfigureAwait(true);
    }

    private static LyricsSearchInfo? BuildSearchInfo(Song? track)
    {
        if (track is null || string.IsNullOrEmpty(track.VideoId) || string.IsNullOrEmpty(track.Title))
        {
            return null;
        }

        return new LyricsSearchInfo(
            Title: track.Title,
            Artist: track.ArtistsDisplay,
            Album: track.Album?.Title,
            Duration: track.Duration,
            VideoId: track.VideoId);
    }

    // ── Player observation ──────────────────────────────────────────────────────────────────────

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IPlayerService.CurrentTrack):
                RunOnUi(() => _ = RunSafeAsync(c => LoadForCurrentTrackAsync(force: false, c)));
                break;
            case nameof(IPlayerService.Progress):
                RunOnUi(UpdateHighlight);
                break;
            case nameof(IPlayerService.IsLive):
                RunOnUi(() => IsLive = _player.IsLive);
                break;
            case null or "":
                // A bulk change notification: re-sync everything.
                RunOnUi(() =>
                {
                    IsLive = _player.IsLive;
                    _ = RunSafeAsync(c => LoadForCurrentTrackAsync(force: false, c));
                });
                break;
        }
    }

    private void OnLyricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ILyricsService.CurrentLyrics):
                RunOnUi(RebuildFromResult);
                break;
            case nameof(ILyricsService.ActiveProvider):
                RunOnUi(() => ActiveProvider = _lyrics.ActiveProvider);
                break;
            case nameof(ILyricsService.IsLoading):
                RunOnUi(() => IsLoadingLyrics = _lyrics.IsLoading);
                break;
            case null or "":
                RunOnUi(() =>
                {
                    ActiveProvider = _lyrics.ActiveProvider;
                    IsLoadingLyrics = _lyrics.IsLoading;
                    RebuildFromResult();
                });
                break;
        }
    }

    // ── Projection of the resolved lyric result ──────────────────────────────────────────────────

    private void RebuildFromResult()
    {
        switch (_lyrics.CurrentLyrics)
        {
            case LyricResult.Synced synced:
                ShowSyncedLyrics(synced.Lyrics);
                break;
            case LyricResult.Plain plain:
                ShowPlainLyrics(plain.Lyrics.Text);
                break;
            default:
                // Unavailable or null → empty state (Req 17.3 fallthrough).
                ClearLyrics();
                break;
        }
    }

    private void ShowSyncedLyrics(SyncedLyrics lyrics)
    {
        _syncedLyrics = lyrics;
        _activeLineIndex = -1;

        Lines.Clear();
        for (var i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            Lines.Add(new LyricLineItem(i, line.TimeInMs, line.Text));
        }

        PlainText = null;
        ShowSynced = lyrics.Lines.Count > 0;
        ShowPlain = false;
        ShowEmpty = lyrics.Lines.Count == 0;

        // Establish the initial highlight for the current position.
        UpdateHighlight();
    }

    private void ShowPlainLyrics(string text)
    {
        _syncedLyrics = null;
        _activeLineIndex = -1;
        Lines.Clear();

        PlainText = text;
        ShowSynced = false;
        ShowPlain = !string.IsNullOrWhiteSpace(text);
        ShowEmpty = string.IsNullOrWhiteSpace(text);
    }

    private void ClearLyrics()
    {
        _syncedLyrics = null;
        _activeLineIndex = -1;
        Lines.Clear();
        PlainText = null;
        ShowSynced = false;
        ShowPlain = false;
        ShowEmpty = true;
    }

    /// <summary>
    /// Recomputes which synced line is current for the player's position and moves the active flag,
    /// raising <see cref="ActiveLineChanged"/> when the active line changes (Req 17.2).
    /// </summary>
    private void UpdateHighlight()
    {
        if (_syncedLyrics is null || Lines.Count == 0)
        {
            return;
        }

        var positionMs = (long)Math.Round(_player.Progress * 1000.0);
        var index = SyncedLyricsNavigator.CurrentLineIndex(_syncedLyrics, positionMs);

        if (index == _activeLineIndex)
        {
            return;
        }

        if (_activeLineIndex >= 0 && _activeLineIndex < Lines.Count)
        {
            Lines[_activeLineIndex].IsActive = false;
        }

        _activeLineIndex = index;

        LyricLineItem? active = null;
        if (index >= 0 && index < Lines.Count)
        {
            active = Lines[index];
            active.IsActive = true;
        }

        ActiveLineChanged?.Invoke(this, active);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    /// <summary>Detaches the service subscriptions. Called by the page on unload.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _lyrics.PropertyChanged -= OnLyricsPropertyChanged;
    }
}

/// <summary>
/// One synced lyric line projected for the view, carrying an observable
/// <see cref="IsActive"/> flag the page binds to for highlighting (Req 17.2).
/// </summary>
public sealed partial class LyricLineItem : ObservableObject
{
    /// <summary>Creates a line item.</summary>
    public LyricLineItem(int index, int timeInMs, string text)
    {
        Index = index;
        TimeInMs = timeInMs;
        Text = text;
    }

    /// <summary>Stable zero-based index of the line within the synced lyrics (identity for the list).</summary>
    public int Index { get; }

    /// <summary>Line start time in milliseconds.</summary>
    public int TimeInMs { get; }

    /// <summary>The line text. Empty lines render as spacing.</summary>
    public string Text { get; }

    /// <summary>Whether this is the line currently being sung; drives the highlight style.</summary>
    [ObservableProperty]
    private bool _isActive;
}
