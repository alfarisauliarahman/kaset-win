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
    // OEM virtual-key codes that have no named VirtualKey member.
    private const VirtualKey KeySemicolon = (VirtualKey)186; // ';' (OEM_1)
    private const VirtualKey KeyPlus = (VirtualKey)187;      // '='/'+' (OEM_Plus)
    private const VirtualKey KeyComma = (VirtualKey)188;     // ','/'<' (OEM_Comma)
    private const VirtualKey KeyMinus = (VirtualKey)189;     // '-'/'_' (OEM_Minus)
    private const VirtualKey KeyPeriod = (VirtualKey)190;    // '.'/'>' (OEM_Period)
    private const VirtualKey KeySlash = (VirtualKey)191;     // '/'/'?' (OEM_2)

    /// <summary>Playback-rate presets stepped by the '&lt;' / '&gt;' shortcuts.</summary>
    private static readonly double[] PlaybackRates = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0];
    private int _playbackRateIndex = 2; // 1.0×

    /// <summary>How far a single seek shortcut jumps (10s for l/h, 1s for the Shift variants).</summary>
    private const double SeekStepSeconds = 10;
    private const double SeekFineSeconds = 1;

    // 'g' begins a two-key navigation chord (gh / ge / gl / g,) — YouTube-Music style. The next key
    // pressed within the window is interpreted as the destination; a timer clears a stale prefix.
    private bool _pendingGChord;
    private DispatcherTimer? _gChordTimer;

    /// <summary>
    /// Registers Ctrl-based accelerators that avoid clobbering standard Windows shortcuts:
    /// Ctrl+F (Search), Space (play/pause), Ctrl+Right/Left (next/prev), Ctrl+Up/Down (volume),
    /// Ctrl+, (Settings) and Ctrl+Q (explicit Quit). The single-key YouTube-Music shortcut scheme
    /// (j/k/l/h, s, r, m, f, +/-, gh/ge/gl, Shift+/ for help, …) is handled in <see cref="OnGlobalMediaKeyDown"/>.
    /// </summary>
    private void RegisterKeyboardAccelerators()
    {
        void Add(VirtualKeyModifiers modifiers, VirtualKey key, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
        {
            var accelerator = new KeyboardAccelerator { Modifiers = modifiers, Key = key };
            accelerator.Invoked += handler;
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }

        Add(VirtualKeyModifiers.Control, VirtualKey.F, OnSearchAccelerator);
        Add(VirtualKeyModifiers.None, VirtualKey.Space, OnPlayPauseAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Right, OnNextAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Left, OnPreviousAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Up, OnVolumeUpAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Down, OnVolumeDownAccelerator);
        // OEM comma (',') has no named VirtualKey member; its virtual-key code is 188.
        Add(VirtualKeyModifiers.Control, KeyComma, OnSettingsAccelerator);
        Add(VirtualKeyModifiers.Control, VirtualKey.Q, OnQuitAccelerator);

        // Suppress the framework's automatic accelerator tooltips (the stray "Ctrl+F" chip that
        // floated over the content). We surface the full list via Shift+/ instead.
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        // PreviewKeyDown tunnels BEFORE the focused control, so Space/Ctrl+Arrows control playback
        // even when a track row / sidebar item has focus (a plain KeyboardAccelerator loses that
        // race — the focused Button re-invokes on Space, restarting the song / re-opening the item).
        RootGrid.PreviewKeyDown += OnGlobalMediaKeyDown;
        // Buttons / list items invoke on Space *KeyUp*, so also swallow the Space KeyUp or the
        // focused item still re-activates (restarting the song / re-opening the item).
        RootGrid.PreviewKeyUp += OnGlobalMediaKeyUp;
    }

    private void OnGlobalMediaKeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!IsTextInputFocused() && e.Key == VirtualKey.Space && !IsCtrlDown())
        {
            e.Handled = true;
        }
    }

    private static bool IsCtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// Global playback / navigation shortcuts, YouTube-Music style (merged with our Ctrl-based ones).
    /// Runs on <see cref="UIElement.PreviewKeyDown"/> so it wins over a focused control; never fires
    /// while a text field has focus. See <see cref="ShowShortcutsHelpAsync"/> (Shift+/) for the list.
    /// </summary>
    private void OnGlobalMediaKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Never steal keys from a text field (search box, dialog inputs).
        if (IsTextInputFocused())
        {
            return;
        }

        var ctrl = IsCtrlDown();
        var shift = IsShiftDown();

        // ── 'g' navigation chord: the key after a bare 'g' selects a destination. Selecting the nav
        // item (not navigating the frame directly) mirrors a real sidebar click, so the correct page
        // loads and stays selected. ──
        if (_pendingGChord)
        {
            _pendingGChord = false;
            _gChordTimer?.Stop();
            switch (e.Key)
            {
                case VirtualKey.H: SelectMenuItemByTag("Home"); e.Handled = true; return;
                case VirtualKey.E: SelectMenuItemByTag("Explore"); e.Handled = true; return;
                case VirtualKey.L: SelectMenuItemByTag("Library"); e.Handled = true; return;
                case VirtualKey.S: SelectMenuItemByTag("LikedSongs"); e.Handled = true; return;
                case VirtualKey.R: SelectMenuItemByTag("History"); e.Handled = true; return;
                case VirtualKey.P: SelectMenuItemByTag("Podcasts"); e.Handled = true; return;
                case KeyComma: OpenSettingsShortcut(); e.Handled = true; return;
                // Any other key ends the chord and is processed as a normal shortcut below.
            }
        }

        // Begin a 'g' chord (bare 'g', no modifiers).
        if (e.Key == VirtualKey.G && !ctrl && !shift)
        {
            _pendingGChord = true;
            (_gChordTimer ??= CreateGChordTimer()).Start();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            // Play / pause: Space or ';'
            case VirtualKey.Space when !ctrl:
            case KeySemicolon when !ctrl && !shift:
                _ = _player?.TogglePlayPauseAsync();
                e.Handled = true;
                break;

            // Next song: 'j', Shift+N, or Ctrl+Right (ours)
            case VirtualKey.J when !ctrl && !shift:
            case VirtualKey.N when shift && !ctrl:
            case VirtualKey.Right when ctrl && !shift:
                _ = _player?.NextAsync();
                e.Handled = true;
                break;

            // Previous song: 'k', Shift+P, or Ctrl+Left (ours)
            case VirtualKey.K when !ctrl && !shift:
            case VirtualKey.P when shift && !ctrl:
            case VirtualKey.Left when ctrl && !shift:
                _ = _player?.PreviousAsync();
                e.Handled = true;
                break;

            // Seek forward 10s: 'l' or Shift+Right
            case VirtualKey.L when !ctrl && !shift:
            case VirtualKey.Right when shift && !ctrl:
                SeekBy(SeekStepSeconds);
                e.Handled = true;
                break;

            // Seek forward 1s: Shift+L or Ctrl+Shift+Right
            case VirtualKey.L when shift && !ctrl:
            case VirtualKey.Right when ctrl && shift:
                SeekBy(SeekFineSeconds);
                e.Handled = true;
                break;

            // Seek back 10s: 'h' or Shift+Left
            case VirtualKey.H when !ctrl && !shift:
            case VirtualKey.Left when shift && !ctrl:
                SeekBy(-SeekStepSeconds);
                e.Handled = true;
                break;

            // Seek back 1s: Shift+H or Ctrl+Shift+Left
            case VirtualKey.H when shift && !ctrl:
            case VirtualKey.Left when ctrl && shift:
                SeekBy(-SeekFineSeconds);
                e.Handled = true;
                break;

            // Shuffle queue / cycle repeat
            case VirtualKey.S when !ctrl && !shift:
                _player?.ToggleShuffle();
                e.Handled = true;
                break;
            case VirtualKey.R when !ctrl && !shift:
                _player?.CycleRepeat();
                e.Handled = true;
                break;

            // Volume up: '=' or Ctrl+Up (ours)
            case KeyPlus when !ctrl && !shift:
            case VirtualKey.Up when ctrl:
                _player?.SetVolume((_player?.Volume ?? 0) + VolumeStep);
                e.Handled = true;
                break;

            // Volume down: '-' or Ctrl+Down (ours)
            case KeyMinus when !ctrl && !shift:
            case VirtualKey.Down when ctrl:
                _player?.SetVolume((_player?.Volume ?? 0) - VolumeStep);
                e.Handled = true;
                break;

            // Mute
            case VirtualKey.M when !ctrl && !shift:
                _player?.ToggleMute();
                e.Handled = true;
                break;

            // Toggle the queue side panel: 'q' or Esc
            case VirtualKey.Q when !ctrl && !shift:
            case VirtualKey.Escape when !ctrl && !shift:
                App.Current.Services.GetService<Controls.SidePanelController>()?.ToggleQueue();
                e.Handled = true;
                break;

            // Fullscreen
            case VirtualKey.F when !ctrl && !shift:
                ToggleFullScreen();
                e.Handled = true;
                break;

            // Toggle the lyrics side panel: 'c' (captions / lyriCs)
            case VirtualKey.C when !ctrl && !shift:
                App.Current.Services.GetService<Controls.SidePanelController>()?.ToggleLyrics();
                e.Handled = true;
                break;

            // Playback speed: '>' faster, '<' slower (YouTube convention)
            case KeyPeriod when shift && !ctrl:
                AdjustPlaybackRate(1);
                e.Handled = true;
                break;
            case KeyComma when shift && !ctrl:
                AdjustPlaybackRate(-1);
                e.Handled = true;
                break;

            // Like this song: '+' (Shift+=)
            case KeyPlus when shift && !ctrl:
                PlayerBarControl.ToggleLikeFromShortcut();
                e.Handled = true;
                break;

            // Dislike this song: '_' (Shift+-)
            case KeyMinus when shift && !ctrl:
                PlayerBarControl.DislikeFromShortcut();
                e.Handled = true;
                break;

            // Show shortcuts help: Shift+/
            case KeySlash when shift && !ctrl:
                _ = ShowShortcutsHelpAsync();
                e.Handled = true;
                break;

            // Search: '/'
            case KeySlash when !shift && !ctrl:
                FocusSidebarSearch();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Seeks the current track by <paramref name="deltaSeconds"/>, clamped and skipped for live.</summary>
    private void SeekBy(double deltaSeconds)
    {
        if (_player is null || _player.IsLive || _player.Duration <= 0)
        {
            return;
        }

        var target = Math.Clamp(_player.Progress + deltaSeconds, 0, _player.Duration);
        _ = _player.SeekAsync(target);
    }

    /// <summary>Steps the playback rate through the presets ('&lt;' slower, '&gt;' faster) and toasts it.</summary>
    private void AdjustPlaybackRate(int direction)
    {
        if (App.Current.Services.GetService<IPlaybackController>() is not { } controller)
        {
            return;
        }

        _playbackRateIndex = Math.Clamp(_playbackRateIndex + direction, 0, PlaybackRates.Length - 1);
        var rate = PlaybackRates[_playbackRateIndex];
        _ = controller.SetPlaybackRateAsync(rate);
        App.Current.Services.GetService<Notifications.IInAppNotifier>()?
            .Show($"{rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}×");
    }

    /// <summary>Focuses the sidebar search box (the '/' shortcut).</summary>
    private void FocusSidebarSearch() => SidebarSearchBox.Focus(FocusState.Programmatic);

    /// <summary>Toggles the window between full-screen and the normal overlapped presenter ('f').</summary>
    private void ToggleFullScreen()
    {
        if (AppWindow is not { } appWindow)
        {
            return;
        }

        appWindow.SetPresenter(appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Overlapped
            : AppWindowPresenterKind.FullScreen);
    }

    /// <summary>Navigates to Settings (shared by Ctrl+, and the g, chord).</summary>
    private void OpenSettingsShortcut()
    {
        if (NavView.SettingsItem is NavigationViewItem settings)
        {
            NavView.SelectedItem = settings;
        }
        else
        {
            NavigateToTag("Settings");
        }
    }

    private DispatcherTimer CreateGChordTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pendingGChord = false;
        };
        return timer;
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusSidebarSearch();
        args.Handled = true;
    }

    private void OnPlayPauseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Do not hijack Space while the user is typing or focused on an activatable control.
        if (IsTextInputFocused())
        {
            args.Handled = false;
            return;
        }

        _ = _player?.TogglePlayPauseAsync();
        args.Handled = true;
    }

    private void OnNextAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = _player?.NextAsync();
        args.Handled = true;
    }

    private void OnPreviousAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = _player?.PreviousAsync();
        args.Handled = true;
    }

    private void OnVolumeUpAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_player is not null)
        {
            _player.SetVolume(_player.Volume + VolumeStep);
        }

        args.Handled = true;
    }

    private void OnVolumeDownAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_player is not null)
        {
            _player.SetVolume(_player.Volume - VolumeStep);
        }

        args.Handled = true;
    }

    private void OnSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenSettingsShortcut();
        args.Handled = true;
    }

    private void OnQuitAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = QuitAsync();
    }

    private void SelectMenuItemByTag(string tag)
    {
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private bool IsTextInputFocused()
    {
        // Only genuine TEXT-ENTRY controls suppress the global shortcuts. A focused Button/list item
        // must NOT (it used to include ButtonBase, so after clicking any button the shortcuts went
        // dead — "nyangkut" — until you clicked empty space). PreviewKeyDown handling + the Space
        // KeyUp swallow already stop a focused button from re-activating on Space.
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        return focused is TextBox or AutoSuggestBox or PasswordBox or RichEditBox;
    }

    // ── Shortcuts help overlay (Shift+/) ──────────────────────────────────────────────────────────

    private ContentDialog? _shortcutsDialog;

    /// <summary>Shows the keyboard-shortcuts cheat sheet (Shift+/), grouped like YouTube Music's help.</summary>
    private async System.Threading.Tasks.Task ShowShortcutsHelpAsync()
    {
        if (Content?.XamlRoot is null)
        {
            return;
        }

        // Reuse a single dialog instance; re-pressing Shift+/ while it's open is a no-op.
        if (_shortcutsDialog is not null)
        {
            return;
        }

        var or = Localization.UiStrings.ScOr;

        var playback = BuildShortcutSection(Localization.UiStrings.ShortcutsPlayback,
        [
            (Localization.UiStrings.ScPlayPause, $"Space {or} ;"),
            (Localization.UiStrings.ScNextSong, $"j {or} Shift+N"),
            (Localization.UiStrings.ScPrevSong, $"k {or} Shift+P"),
            (Localization.UiStrings.ScFwd10, $"l {or} Shift+→"),
            (Localization.UiStrings.ScBack10, $"h {or} Shift+←"),
            (Localization.UiStrings.ScFwd1, $"Shift+L {or} Ctrl+Shift+→"),
            (Localization.UiStrings.ScBack1, $"Shift+H {or} Ctrl+Shift+←"),
            (Localization.UiStrings.ScShuffle, "s"),
            (Localization.UiStrings.ScRepeat, "r"),
        ]);

        var general = BuildShortcutSection(Localization.UiStrings.ShortcutsGeneral,
        [
            (Localization.UiStrings.ScVolUp, "="),
            (Localization.UiStrings.ScVolDown, "-"),
            (Localization.UiStrings.ScMute, "m"),
            (Localization.UiStrings.ScToggleQueue, $"q {or} Esc"),
            (Localization.UiStrings.ScLyrics, "c"),
            (Localization.UiStrings.ScSpeedUp, ">"),
            (Localization.UiStrings.ScSpeedDown, "<"),
            (Localization.UiStrings.ScFullscreen, "f"),
            (Localization.UiStrings.ScLike, "+"),
            (Localization.UiStrings.ScDislike, "_"),
            (Localization.UiStrings.ScShowHelp, "Shift+/"),
        ]);

        var navigation = BuildShortcutSection(Localization.UiStrings.ShortcutsNavigation,
        [
            (Localization.UiStrings.ScOpenHome, "g h"),
            (Localization.UiStrings.ScOpenExplore, "g e"),
            (Localization.UiStrings.ScOpenLibrary, "g l"),
            (Localization.UiStrings.ScOpenLiked, "g s"),
            (Localization.UiStrings.ScOpenHistory, "g r"),
            (Localization.UiStrings.ScOpenPodcasts, "g p"),
            (Localization.UiStrings.ScOpenSettings, "g ,"),
            (Localization.UiStrings.ScSearch, "/"),
        ]);

        var rightColumn = new StackPanel { Spacing = 24 };
        rightColumn.Children.Add(general);
        rightColumn.Children.Add(navigation);

        var columns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 40,
            // Right padding keeps the key chips clear of the scroll bar; bottom padding gives breathing room.
            Padding = new Thickness(0, 0, 20, 8),
        };
        columns.Children.Add(playback);
        columns.Children.Add(rightColumn);

        // ── Sizing: budget the CONTENT, never the dialog ───────────────────────────────────────
        // Why the two previous attempts looked like they did nothing:
        //  * A ContentDialog does not shrink itself to the window. Its template gives the visible
        //    "BackgroundElement" border its MinWidth/MaxWidth/MinHeight/MaxHeight from *theme
        //    resources* (ContentDialogMaxHeight & co.), not from the ContentDialog's own MaxHeight
        //    property — so setting MaxHeight on the dialog instance changes nothing at all, and the
        //    only reliable lever is how tall the content we hand it is.
        //  * The previous budget was `windowHeight − 220`. Because the sheet always wants more rows
        //    than that, the content took every pixel it was offered at *every* window size, so the
        //    dialog ended up roughly as tall as the window — indistinguishable from the original
        //    "fills the screen top to bottom" bug no matter how the window was sized.
        // So: subtract the dialog's own chrome *and* a deliberate window margin, and cap the result
        // well below the window so the sheet always reads as a sheet, with the list scrolling inside.
        var xamlRoot = Content.XamlRoot;
        double availableHeight = xamlRoot.Size.Height > 0 ? xamlRoot.Size.Height : MainWindowLayout.MinimumHeight;
        double availableWidth = xamlRoot.Size.Width > 0 ? xamlRoot.Size.Width : MainWindowLayout.MinimumWidth;

        var scroller = new ScrollViewer
        {
            Content = columns,
            VerticalScrollMode = ScrollMode.Enabled,
            // Not Auto: the Fluent auto-hiding scroll bar is a 2px sliver until the pointer is over
            // it, which is exactly why an overflowing sheet reads as "cannot be scrolled". The list
            // here overflows by design at small window sizes, so show the bar and say so.
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            // A narrow window cannot fit the two 320px columns side by side either, so let the
            // sheet pan sideways rather than clip the right-hand column.
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = ShortcutsContentMaxHeight(availableHeight),
            // Tab-reachable so the list can also be scrolled with the arrow keys / Page Up-Down,
            // not only with the wheel.
            IsTabStop = true,
        };

        _shortcutsDialog = new ContentDialog
        {
            Title = Localization.UiStrings.ShortcutsTitle,
            Content = scroller,
            CloseButtonText = Localization.UiStrings.DialogClose,
            XamlRoot = xamlRoot,
        };

        // The dialog template wraps our content in a ScrollViewer of its own. Two nested scrollers
        // around the same overflow is how a wheel spin ends up doing nothing visible, and the outer
        // one is the one that never has an overflow (our content is bounded). Turn it off so every
        // wheel notch lands on the scroller that actually moves. These are inheritable attached
        // properties, but the local values set on `scroller` above outrank them.
        ScrollViewer.SetVerticalScrollMode(_shortcutsDialog, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_shortcutsDialog, ScrollBarVisibility.Disabled);

        // The default ContentDialog max width (~548px) clips the two-column layout; widen it generously
        // so both columns and their key chips fit without the right column being cut off — but never
        // wider than the window itself. These theme-resource overrides are a second line of defence
        // only: whether a {ThemeResource} inside the template resolves against an instance's own
        // Resources is not something to bet the fix on, and the content budget above already keeps
        // the sheet inside the window on its own.
        _shortcutsDialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(availableWidth - 48, 320.0, 1000.0);
        _shortcutsDialog.Resources["ContentDialogMaxHeight"] = Math.Max(240.0, availableHeight - ShortcutsWindowMargin);

        // Resizing the window while the sheet is open must not put it back outside the window.
        void OnRootSizeChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            if (sender.Size.Height > 0)
            {
                scroller.MaxHeight = ShortcutsContentMaxHeight(sender.Size.Height);
            }
        }

        xamlRoot.Changed += OnRootSizeChanged;

        try
        {
            await _shortcutsDialog.ShowAsync();
        }
        finally
        {
            xamlRoot.Changed -= OnRootSizeChanged;
            _shortcutsDialog = null;
        }
    }

    /// <summary>
    /// Vertical space the ContentDialog template spends on itself around our content: padding top and
    /// bottom, the title row plus its margin, and the command (button) area plus its margin. Measured
    /// against the default theme resources and rounded up — over-reserving costs a few rows of scroll,
    /// under-reserving costs the bottom of the sheet.
    /// </summary>
    private const double ShortcutsDialogChrome = 200.0;

    /// <summary>Gap kept between the sheet and the window edges, so it visibly floats *in* the window
    /// instead of ending flush with it (the previous budget left none, which is why the sheet still
    /// looked like it filled the screen).</summary>
    private const double ShortcutsWindowMargin = 96.0;

    /// <summary>How tall the scrolling shortcut list may be inside a window of <paramref name="windowHeight"/>.</summary>
    private static double ShortcutsContentMaxHeight(double windowHeight) =>
        Math.Clamp(windowHeight - ShortcutsDialogChrome - ShortcutsWindowMargin, 160.0, 520.0);

    /// <summary>Builds one titled group of "action → keys" rows for the shortcuts help.</summary>
    private static StackPanel BuildShortcutSection(string title, (string Action, string Keys)[] rows)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var (action, keys) in rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var actionText = new TextBlock
            {
                Text = action,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 16, 0),
            };
            Grid.SetColumn(actionText, 0);

            var keyChip = new Border
            {
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = keys,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                },
            };
            Grid.SetColumn(keyChip, 1);

            grid.Children.Add(actionText);
            grid.Children.Add(keyChip);
            panel.Children.Add(grid);
        }

        return panel;
    }
}
