using KasetWin.App.Accessibility;
using KasetWin.App.Auth;
using KasetWin.App.Hosting;
using KasetWin.App.ViewModels;
using KasetWin.App.Views;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Activation;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Auth;
using KasetWin.Core.Services.Localization;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Globalization;
using System.Linq;
using Windows.Foundation;
using Windows.System;

namespace KasetWin.App;

public sealed partial class MainWindow
{
    // ── Sidebar: playlists tree + create dialog ───────────────────────────────────────────────────

    /// <summary>Fills the Playlists nav item's children: All Playlists / new playlist / each playlist.</summary>
    private async Task LoadSidebarPlaylistsAsync()
    {
        System.Collections.Generic.IReadOnlyList<Playlist> playlists;
        try
        {
            var client = App.Current.Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return;
            }

            // Same landing source as the Library page (the dedicated liked_playlists browse parses
            // empty), but UNCACHED: an early anonymous-empty response must not poison the 5-minute
            // library cache — and retries must be able to see fresh data.
            if (client is not YTMusicClient concrete)
            {
                return;
            }

            var raw = await concrete.BrowseRawAsync("FEmusic_library_landing");
            playlists = Core.Services.Api.Parsers.LibraryContentParser.Parse(raw).Playlists;
        }
        catch (Exception ex)
        {
            KasetWin.Core.Diag.Write($"sidebar-playlists FAILED: {ex.GetType().Name}: {ex.Message}");
            return; // Signed out / offline: keep the static children only.
        }

        KasetWin.Core.Diag.Write($"sidebar-playlists count={playlists.Count}");

        // Startup races the WebView2 cookie jar: an early request comes back anonymous (empty
        // library) without failing. Retry a few times with a delay until real data arrives.
        if (playlists.Count == 0 && _sidebarPlaylistAttempts < 5)
        {
            _sidebarPlaylistAttempts++;
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                await LoadSidebarPlaylistsAsync();
            });
            return;
        }

        _sidebarPlaylistAttempts = 0;

        DispatcherQueue.TryEnqueue(() =>
        {
            PlaylistsNavItem.MenuItems.Clear();

            PlaylistsNavItem.MenuItems.Add(new NavigationViewItem
            {
                Content = "All Playlists",
                Tag = "AllPlaylists",
                SelectsOnInvoked = false,
                Icon = new FontIcon { Glyph = "" },
            });

            foreach (var playlist in playlists)
            {
                if (string.IsNullOrEmpty(playlist.Id))
                {
                    continue;
                }

                var coverPath = PlaylistCoverStore.Get(playlist.Id);
                // YT serves a generic gstatic placeholder for cover-less playlists; treat it as "no cover".
                var remote = playlist.ThumbnailUrl is { } t && !t.Host.Contains("gstatic", StringComparison.OrdinalIgnoreCase) ? t : null;
                var thumb = coverPath is not null ? new Uri(coverPath) : remote;
                PlaylistsNavItem.MenuItems.Add(new NavigationViewItem
                {
                    Content = playlist.Title,
                    Tag = $"Playlist:{playlist.Id}",
                    SelectsOnInvoked = false,
                    ContextFlyout = BuildPlaylistContextMenu(playlist),
                    // Prefer the playlist cover (local override first) over a generic glyph.
                    Icon = thumb is not null
                        ? new ImageIcon { Source = new BitmapImage(thumb) }
                        : new FontIcon { Glyph = "" },
                });
            }

            // Dropdown feel: the children slide/fade in via the framework's own entrance
            // transition. NOTE: do NOT animate these items from the NavigationView.Expanding
            // event — touching Translation/transitions mid-expand crashes Microsoft.UI.Xaml
            // with 0xc000027b (composition failure).
            // The rebuilt "All Playlists" child must re-pick the language-dependent label.
            ApplySidebarLanguage();

            // Clear+rebuild threw away any selected child; if a playlist page is showing, put the
            // highlight back on its (new) sidebar item.
            SyncSidebarPlaylistSelection();
        });
    }

    /// <summary>
    /// Visible dropdown animation for the Playlists expander. Runs AFTER NavigationView's own
    /// expand pass (low-priority dispatch) and uses classic Storyboards over Opacity +
    /// RenderTransform — the composition path (Translation / ScalarTransition set mid-expand)
    /// fail-fasts XAML with 0xc000027b, and EntranceThemeTransition never fires because the
    /// children stay in the visual tree across collapse/expand.
    /// </summary>
    private void OnNavItemExpanding(NavigationView sender, NavigationViewItemExpandingEventArgs args)
    {
        if (ReferenceEquals(args.ExpandingItemContainer, PlaylistsNavItem))
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AnimatePlaylistChildren);
        }
    }

    private void AnimatePlaylistChildren()
    {
        var delayMs = 0;
        foreach (var item in PlaylistsNavItem.MenuItems)
        {
            if (item is not NavigationViewItem child)
            {
                continue;
            }

            var shift = new Microsoft.UI.Xaml.Media.TranslateTransform();
            child.RenderTransform = shift;
            child.Opacity = 0;

            var duration = new Duration(TimeSpan.FromMilliseconds(240));
            var easing = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            };

            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = duration,
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = easing,
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, child);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");

            var slide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = -16,
                To = 0,
                Duration = duration,
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = easing,
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, shift);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "Y");

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Children.Add(slide);
            try
            {
                storyboard.Begin();
            }
            catch (Exception)
            {
                // Animation is decoration; never let it take the sidebar down.
                child.Opacity = 1;
            }

            delayMs += 40;
        }
    }

    /// <summary>Guard so the source-change callback is registered exactly once.</summary>
    private bool _compactSourceLabelHooked;

    /// <summary>Names the icon-only source button after the source that is currently active.</summary>
    private void ApplyCompactSourceLabel() =>
        A11y.Label(
            SourceToggleCompact,
            Localization.UiStrings.A11ySourceCompact(YouTubeSourceItem.IsChecked == true));


    /// <summary>
    /// Applies the app's content-language setting to the shell's own labels (sidebar items,
    /// search placeholder). The page/server content is localised by the API's <c>hl</c> pin;
    /// this covers the strings we hardcode ourselves so the shell doesn't stay half-English.
    /// Safe to call repeatedly (startup, language change, sidebar rebuild).
    /// </summary>
    internal void ApplySidebarLanguage()
    {
        var indo = ViewModels.SettingsViewModel.LoadLanguageSetting() != "en";
        SidebarSearchBox.PlaceholderText = Localization.UiStrings.SearchPlaceholderShort;
        PlaylistsRootLabel.Text = Localization.UiStrings.PlaylistsRoot;
        A11y.Label(NewPlaylistButton, Localization.UiStrings.TipNewPlaylist);

        // Chrome that no other relabel path reaches: the title-bar back button and the offline
        // banner were previously pinned to one language (ID and EN respectively) because nothing
        // ever rewrote their XAML text.
        A11y.Label(TitleBarBackButton, Localization.UiStrings.TipBack);
        OfflineInfoBar.Title = Localization.UiStrings.OfflineTitle;
        OfflineInfoBar.Message = Localization.UiStrings.OfflineMessage;

        // Source toggle: two icon+text segments plus an icon-only compact variant, none of which
        // announce which source they select.
        A11y.Name(MusicSourceItem, Localization.UiStrings.A11ySourceMusic);
        A11y.Name(YouTubeSourceItem, Localization.UiStrings.A11ySourceYouTube);

        // The compact button is icon-only AND stateless to a screen reader — it is a Button, not a
        // ToggleButton, so there is no on/off for Narrator to announce. Its name therefore has to
        // say which source is active and what a click does, and has to be refreshed whenever the
        // source changes rather than only on a language change.
        ApplyCompactSourceLabel();
        if (!_compactSourceLabelHooked)
        {
            _compactSourceLabelHooked = true;
            YouTubeSourceItem.RegisterPropertyChangedCallback(
                Microsoft.UI.Xaml.Controls.Primitives.ToggleButton.IsCheckedProperty,
                (_, _) => ApplyCompactSourceLabel());
        }

        // These controls live for the window's lifetime (pages are recreated on navigation and
        // re-read their labels in the ctor, but these persist), so relabel them on every language change.
        PlayerBarControl.ApplyLanguage();
        SidePanel.ApplyLanguage();

        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string tag } nav && SidebarLabel(tag, indo) is { } label)
            {
                nav.Content = label;
            }
        }

        foreach (var item in PlaylistsNavItem.MenuItems)
        {
            if (item is NavigationViewItem { Tag: "AllPlaylists" } all)
            {
                all.Content = indo ? "Semua Playlist" : "All Playlists";
            }
        }
    }

    /// <summary>The sidebar label for a nav <paramref name="tag"/>, or null for custom-content items.</summary>
    private static string? SidebarLabel(string tag, bool indo) => tag switch
    {
        "Home" => indo ? "Beranda" : "Home",
        "Explore" => indo ? "Jelajahi" : "Explore",
        "Library" => indo ? "Koleksi" : "Library",
        "LikedSongs" => indo ? "Lagu yang disukai" : "Liked Songs",
        "History" => indo ? "Riwayat" : "History",
        "Podcasts" => indo ? "Podcast" : "Podcasts",
        "YouTube.Home" => indo ? "Beranda YouTube" : "YouTube Home",
        "YouTube.Explore" => indo ? "Jelajahi" : "Explore",
        "YouTube.Subscriptions" => indo ? "Langganan" : "Subscriptions",
        "YouTube.Shorts" => "Shorts",
        "YouTube.History" => indo ? "Riwayat YouTube" : "YouTube History",
        _ => null,
    };

    /// <summary>
    /// Apple-Music-style cover picker: an accent-outlined rounded square with a centred ⊕ that opens
    /// a file picker; the chosen image previews inside and its path is reported to the caller.
    /// </summary>
    private Button MakeCoverPicker(string? existingCoverPath, Action<string> onPicked)
    {
        var coverImage = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        if (existingCoverPath is not null)
        {
            coverImage.Source = new BitmapImage(new Uri(existingCoverPath));
        }

        // Plain "+" (no circle), accent-coloured, centred - matching the Apple Music mock.
        var plus = new FontIcon
        {
            Glyph = "",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        };

        var button = new Button
        {
            Width = 150,
            Height = 150,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            Content = new Grid { Children = { coverImage, plus } },
        };

        button.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                onPicked(file.Path);
                coverImage.Source = new BitmapImage(new Uri(file.Path));
            }
        };

        return button;
    }

    /// <summary>Right-click menu for a sidebar playlist (mirrors YT Music's playlist menu).</summary>
    private MenuFlyout BuildPlaylistContextMenu(Playlist playlist)
    {
        var menu = new MenuFlyout();

        void Add(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add(Localization.UiStrings.MenuShufflePlay, "", () => _ = PlaylistActionAsync(playlist.Id, shuffle: true));
        Add(Localization.UiStrings.MenuStartMix, "", () => _ = PlaylistMixAsync(playlist.Id));
        Add(Localization.UiStrings.MenuEditPlaylist, "", () => _ = ShowEditPlaylistDialogAsync(playlist.Id));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(Localization.UiStrings.MenuPlayNext, "", () => _ = PlaylistQueueAsync(playlist.Id, playNext: true));
        Add(Localization.UiStrings.MenuAddToQueue, "", () => _ = PlaylistQueueAsync(playlist.Id, playNext: false));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(Localization.UiStrings.MenuShare, "", () =>
        {
            if (Core.Services.Sharing.ShareUrlBuilder.TryCreate(playlist) is { } target)
            {
                Sharing.ShareInvoker.TryShow(this, target);
            }
        });
        Add(Localization.UiStrings.MenuDeletePlaylist, "", () => _ = DeletePlaylistWithConfirmAsync(playlist));

        return menu;
    }

    private async Task PlaylistActionAsync(string playlistId, bool shuffle)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null || _player is null)
        {
            return;
        }

        try
        {
            var detail = await client.GetPlaylistAsync(playlistId);
            var tracks = detail.Tracks.ToList();
            if (shuffle)
            {
                // Deterministic-enough client shuffle for "Putar acak".
                var rng = new Random();
                tracks = [.. tracks.OrderBy(_ => rng.Next())];
            }

            if (tracks.Count > 0)
            {
                await _player.PlayCollectionAsync(tracks, startIndex: 0);
            }
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastPlayFailed(ex.Message));
        }
    }

    private async Task PlaylistMixAsync(string playlistId)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null || _player is null)
        {
            return;
        }

        try
        {
            var mix = await client.GetMixQueueAsync(playlistId);
            if (mix.Songs.Count > 0)
            {
                await _player.PlayCollectionAsync(mix.Songs, startIndex: 0);
            }
            else
            {
                App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastMixUnavailablePlaylist);
            }
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastMixFailed(ex.Message));
        }
    }

    private async Task PlaylistQueueAsync(string playlistId, bool playNext)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        var queue = App.Current.Services.GetService<Core.Services.Player.IQueueService>();
        if (client is null || queue is null)
        {
            return;
        }

        try
        {
            var detail = await client.GetPlaylistAsync(playlistId);
            var added = playNext ? queue.InsertNext(detail.Tracks) : queue.AppendDeduplicated(detail.Tracks);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(
                added == 0 ? Localization.UiStrings.ToastAllAlreadyQueued : Localization.UiStrings.ToastQueuedCount(added, playNext));
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastGenericFailed(ex.Message));
        }
    }

    private async Task DeletePlaylistWithConfirmAsync(Playlist playlist)
    {
        var confirm = new ContentDialog
        {
            Title = Localization.UiStrings.MenuDeletePlaylist,
            Content = Localization.UiStrings.DeletePlaylistConfirm(playlist.Title),
            PrimaryButtonText = Localization.UiStrings.DialogDelete,
            CloseButtonText = Localization.UiStrings.DialogCancel,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var client = App.Current.Services.GetService<IYTMusicClient>();
            if (client is null)
            {
                return;
            }

            await client.DeletePlaylistAsync(playlist.Id);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastPlaylistDeleted(playlist.Title));
            _ = LoadSidebarPlaylistsAsync();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastDeleteFailed(ex.Message));
        }
    }

    /// <summary>
    /// "Edit playlist" dialog with UMUM / KOLABORASI tabs (mirrors YT Music): title, description,
    /// privacy (persisted via the edit API); voting + collaboration are UI-only until an API exists.
    /// </summary>
    internal async Task ShowEditPlaylistDialogAsync(string playlistId)
    {
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null)
        {
            return;
        }

        string currentTitle = "";
        string currentDescription = "";
        try
        {
            var detail = await client.GetPlaylistAsync(playlistId);
            currentTitle = detail.Playlist.Title;
            currentDescription = detail.Playlist.Description ?? "";
        }
        catch (Exception)
        {
            // Editable fields simply start blank when the lookup fails.
        }

        var titleBox = new TextBox { Header = Localization.UiStrings.DialogTitleHeader, Text = currentTitle };
        var descriptionBox = new TextBox { Header = Localization.UiStrings.DialogDescriptionHeader, Text = currentDescription, AcceptsReturn = true };

        static ComboBoxItem TwoLine(string name, string description, object? tag = null)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7 });
            return new ComboBoxItem { Content = panel, Tag = tag };
        }

        var privacyBox = new ComboBox { Header = Localization.UiStrings.DialogPrivacyHeader, HorizontalAlignment = HorizontalAlignment.Stretch };
        privacyBox.Items.Add(TwoLine(Localization.UiStrings.PrivacyPublic, Localization.UiStrings.PrivacyPublicDesc, PlaylistPrivacy.Public));
        privacyBox.Items.Add(TwoLine(Localization.UiStrings.PrivacyUnlisted, Localization.UiStrings.PrivacyUnlistedDesc, PlaylistPrivacy.Unlisted));
        privacyBox.Items.Add(TwoLine(Localization.UiStrings.PrivacyPrivate, Localization.UiStrings.PrivacyPrivateDesc, PlaylistPrivacy.Private));

        var votingBox = new ComboBox { Header = Localization.UiStrings.VotingHeader, HorizontalAlignment = HorizontalAlignment.Stretch };
        votingBox.Items.Add(TwoLine(Localization.UiStrings.VotingEveryone, Localization.UiStrings.VotingEveryoneDesc));
        votingBox.Items.Add(TwoLine(Localization.UiStrings.VotingCollaborators, Localization.UiStrings.VotingCollaboratorsDesc));
        votingBox.Items.Add(TwoLine(Localization.UiStrings.VotingOff, Localization.UiStrings.VotingOffDesc));
        votingBox.SelectedIndex = 2;

        string? pickedCoverPath = null;
        var editCoverButton = MakeCoverPicker(PlaylistCoverStore.Get(playlistId), path => pickedCoverPath = path);

        var umum = new StackPanel { Spacing = 12 };
        umum.Children.Add(editCoverButton);
        umum.Children.Add(titleBox);
        umum.Children.Add(descriptionBox);
        umum.Children.Add(privacyBox);
        umum.Children.Add(votingBox);

        var collabSwitch = new ToggleSwitch { Header = Localization.UiStrings.CollaborationHeader, OffContent = Localization.UiStrings.CollaborationOff, OnContent = Localization.UiStrings.CollaborationOn };
        var copyLink = new Button { Content = Localization.UiStrings.DialogCopyInviteLink };
        copyLink.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText($"https://music.youtube.com/playlist?list={playlistId.TrimStart('V', 'L')}&feature=share");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastInviteLinkCopied);
        };
        var kolaborasi = new StackPanel { Spacing = 12 };
        kolaborasi.Children.Add(collabSwitch);
        kolaborasi.Children.Add(copyLink);

        // Pivot headers default to a huge font; use small semibold tab labels instead.
        static TextBlock Tab(string text) => new()
        {
            Text = text,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };

        var pivot = new Pivot { MinWidth = 400 };
        pivot.Items.Add(new PivotItem { Header = Tab(Localization.UiStrings.DialogTabGeneral), Content = umum });
        pivot.Items.Add(new PivotItem { Header = Tab(Localization.UiStrings.DialogTabCollaboration), Content = kolaborasi });

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(currentTitle) ? Localization.UiStrings.DialogEditPlaylistTitle : currentTitle,
            Content = pivot,
            PrimaryButtonText = Localization.UiStrings.DialogSave,
            CloseButtonText = Localization.UiStrings.DialogCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };

        titleBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(titleBox.Text);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(titleBox.Text))
        {
            return;
        }

        try
        {
            var privacy = (privacyBox.SelectedItem as ComboBoxItem)?.Tag as PlaylistPrivacy?;
            await client.EditPlaylistMetadataAsync(playlistId, titleBox.Text.Trim(), descriptionBox.Text, privacy);
            if (pickedCoverPath is not null)
            {
                PlaylistCoverStore.Set(playlistId, pickedCoverPath);
            }

            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastPlaylistUpdated);
            _ = LoadSidebarPlaylistsAsync();
        }
        catch (Exception ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastSaveFailed(ex.Message));
        }
    }

    /// <summary>The inline "+" on the Playlists header row opens the create dialog (and suppresses
    /// the row's expand toggle for this click, which also fires ItemInvoked).</summary>
    private void OnNewPlaylistClick(object sender, RoutedEventArgs e)
    {
        _suppressPlaylistsToggleOnce = true;
        _ = ShowCreatePlaylistDialogAsync();
    }

    /// <summary>
    /// "Playlist baru" dialog (judul + deskripsi + privasi + kolaborasi), creating via the API and
    /// refreshing the sidebar tree; when collaboration is on the Kolaborasi dialog follows.
    /// </summary>
    private async Task ShowCreatePlaylistDialogAsync()
    {
        var titleBox = new TextBox { Header = Localization.UiStrings.DialogTitleHeader, PlaceholderText = Localization.UiStrings.DialogPlaylistTitlePlaceholder };
        var descriptionBox = new TextBox { Header = Localization.UiStrings.DialogDescriptionHeader, PlaceholderText = Localization.UiStrings.DialogDescriptionPlaceholder, AcceptsReturn = true };

        // Cover picker (stored locally — no cover-upload endpoint has been found in InnerTube).
        string? pickedCoverPath = null;
        var coverButton = MakeCoverPicker(existingCoverPath: null, path => pickedCoverPath = path);

        static ComboBoxItem PrivacyOption(string name, string description, PlaylistPrivacy value)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7 });
            return new ComboBoxItem { Content = panel, Tag = value };
        }

        var privacyBox = new ComboBox
        {
            Header = Localization.UiStrings.DialogPrivacyHeader,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        privacyBox.Items.Add(PrivacyOption(Localization.UiStrings.PrivacyPublic, Localization.UiStrings.PrivacyPublicDesc, PlaylistPrivacy.Public));
        privacyBox.Items.Add(PrivacyOption(Localization.UiStrings.PrivacyUnlisted, Localization.UiStrings.PrivacyUnlistedDesc, PlaylistPrivacy.Unlisted));
        privacyBox.Items.Add(PrivacyOption(Localization.UiStrings.PrivacyPrivate, Localization.UiStrings.PrivacyPrivateDesc, PlaylistPrivacy.Private));
        privacyBox.SelectedIndex = 2;

        var collabSwitch = new ToggleSwitch { Header = Localization.UiStrings.CollaborationHeader, OffContent = Localization.UiStrings.CollaborationOff, OnContent = Localization.UiStrings.CollaborationOnShort };

        var panelRoot = new StackPanel { MinWidth = 380, Spacing = 12 };
        panelRoot.Children.Add(coverButton);
        panelRoot.Children.Add(titleBox);
        panelRoot.Children.Add(descriptionBox);
        panelRoot.Children.Add(privacyBox);
        panelRoot.Children.Add(collabSwitch);

        var dialog = new ContentDialog
        {
            Title = Localization.UiStrings.DialogNewPlaylistTitle,
            Content = panelRoot,
            PrimaryButtonText = Localization.UiStrings.DialogCreate,
            CloseButtonText = Localization.UiStrings.DialogCancel,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false, // judul wajib diisi
            XamlRoot = RootGrid.XamlRoot,
        };

        titleBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(titleBox.Text);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(titleBox.Text))
        {
            return;
        }

        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (client is null)
        {
            return;
        }

        try
        {
            var privacy = (privacyBox.SelectedItem as ComboBoxItem)?.Tag is PlaylistPrivacy p ? p : PlaylistPrivacy.Private;
            var playlistId = await client.CreatePlaylistAsync(
                titleBox.Text.Trim(),
                string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim(),
                privacy,
                videoIds: null);

            if (pickedCoverPath is not null)
            {
                PlaylistCoverStore.Set(playlistId, pickedCoverPath);
            }

            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastPlaylistCreated(titleBox.Text.Trim()));
            _ = LoadSidebarPlaylistsAsync();

            if (collabSwitch.IsOn)
            {
                await ShowCollaborationDialogAsync(playlistId);
            }
        }
        catch (Core.Errors.KasetError ex)
        {
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastCreatePlaylistFailed(ex.Message));
        }
    }

    /// <summary>Kolaborasi dialog: collaboration switches + invite-link copy + the owner row.</summary>
    private async Task ShowCollaborationDialogAsync(string playlistId)
    {
        var collabSwitch = new ToggleSwitch { Header = Localization.UiStrings.CollaborationHeader, IsOn = true, OnContent = Localization.UiStrings.CollaborationOn, OffContent = Localization.UiStrings.CollaborationOff };
        var newCollabSwitch = new ToggleSwitch { Header = Localization.UiStrings.CollaborationAllowNew, IsOn = true, OnContent = Localization.UiStrings.CollaborationOnShort, OffContent = Localization.UiStrings.CollaborationOff };

        var copyButton = new Button { Content = Localization.UiStrings.DialogCopyInviteLink };
        copyButton.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText($"https://music.youtube.com/playlist?list={playlistId.TrimStart('V', 'L')}&feature=share");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            App.Current.Services.GetService<Notifications.IInAppNotifier>()?.Show(Localization.UiStrings.ToastInviteLinkCopied);
        };

        var owner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        owner.Children.Add(new PersonPicture
        {
            Width = 36,
            Height = 36,
            DisplayName = _currentAccount?.Name,
            ProfilePicture = _currentAccount?.AvatarUrl is { } avatarUrl ? new BitmapImage(avatarUrl) : null,
        });
        var ownerText = new StackPanel();
        ownerText.Children.Add(new TextBlock { Text = _currentAccount?.Name ?? Localization.UiStrings.AccountFallback, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        ownerText.Children.Add(new TextBlock { Text = Localization.UiStrings.DialogOwnerLabel, FontSize = 12, Opacity = 0.7 });
        owner.Children.Add(ownerText);

        var panel = new StackPanel { MinWidth = 380, Spacing = 12 };
        panel.Children.Add(collabSwitch);
        panel.Children.Add(newCollabSwitch);
        panel.Children.Add(copyButton);
        panel.Children.Add(owner);
        panel.Children.Add(new TextBlock
        {
            Text = Localization.UiStrings.DialogCollabNote,
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title = Localization.UiStrings.CollaborationHeader,
            Content = panel,
            CloseButtonText = Localization.UiStrings.DialogClose,
            XamlRoot = RootGrid.XamlRoot,
        };

        await dialog.ShowAsync();
    }
}
