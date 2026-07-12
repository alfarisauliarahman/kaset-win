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
    /// <summary>
    /// Presents the Google sign-in dialog via <see cref="ILoginFlow"/>, then refreshes the account
    /// footer (name + avatar) and posts a small success/failure toast (Req 4.2/4.3).
    /// </summary>
    private async Task SignInAsync()
    {
        var login = App.Current.Services.GetService<ILoginFlow>();
        if (login is null || Content?.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        bool loggedIn = false;
        bool errored = false;
        try
        {
            loggedIn = await login.ShowAsync(xamlRoot);
        }
        catch
        {
            // A failed sign-in attempt must not crash the shell.
            errored = true;
        }

        if (loggedIn)
        {
            await RefreshAccountAsync();
            ShowLoginTip(success: true);
        }
        else
        {
            UpdateSignInLabel();

            // Only surface a failure notification on an actual error; a plain user cancel is silent.
            if (errored)
            {
                ShowLoginTip(success: false);
            }
        }
    }

    /// <summary>
    /// Shows a small in-app sign-in result notification anchored above the account footer item
    /// (Req 4.3) â€” the in-app version the user asked for instead of a system toast. Auto-dismisses.
    /// </summary>
    private void ShowLoginTip(bool success)
    {
        void Apply()
        {
            var name = _currentAccount?.Name;
            LoginTip.Target = SignInItem;
            LoginTip.Title = success ? Localization.UiStrings.LoginSuccessTitle : Localization.UiStrings.LoginFailedTitle;
            LoginTip.Subtitle = success
                ? (string.IsNullOrEmpty(name) ? Localization.UiStrings.LoginSuccessGeneric : Localization.UiStrings.LoginSuccessNamed(name))
                : Localization.UiStrings.LoginFailedSubtitle;
            LoginTip.IconSource = new SymbolIconSource
            {
                Symbol = success ? Symbol.Accept : Symbol.Important,
            };
            LoginTip.IsOpen = true;

            // Auto-dismiss after a few seconds so it behaves like a transient notification.
            _loginTipTimer ??= CreateLoginTipTimer();
            _loginTipTimer.Stop();
            _loginTipTimer.Start();
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Apply);
        }
    }

    private DispatcherTimer? _loginTipTimer;

    private DispatcherTimer CreateLoginTipTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LoginTip.IsOpen = false;
        };
        return timer;
    }

    /// <summary>
    /// Fetches the currently-selected account (name + avatar) via <c>account/accounts_list</c> and
    /// refreshes the footer item. Best-effort: a network/auth failure leaves the footer unchanged.
    /// </summary>
    private async Task RefreshAccountAsync()
    {
        var auth = App.Current.Services.GetService<IAuthService>();
        var client = App.Current.Services.GetService<IYTMusicClient>();
        if (auth is not { State: AuthState.LoggedIn } || client is null)
        {
            _currentAccount = null;
            UpdateSignInLabel();
            return;
        }

        try
        {
            // Prefer account/account_menu â€” it returns the active account's name + avatar directly.
            // accounts_list is a brand-account switcher and can be empty for a personal account.
            _currentAccount = await client.GetAccountInfoAsync();

            if (_currentAccount is null)
            {
                var accounts = await client.GetAccountsListAsync();
                _currentAccount = accounts.FirstOrDefault(a => a.IsCurrent)
                    ?? accounts.FirstOrDefault(a => a.IsPrimary)
                    ?? accounts.FirstOrDefault();
            }
        }
        catch
        {
            // Keep whatever we had; the footer still shows a generic "Account" label if unknown.
        }

        // Publish the account so item templates (e.g. Home "Listen again" header) can show it.
        ViewModels.AccountContext.Name = _currentAccount?.Name;
        ViewModels.AccountContext.AvatarUrl = _currentAccount?.AvatarUrl;

        UpdateSignInLabel();
    }

    /// <summary>Reflects the current auth state on the footer item (Sign in â‡„ account name + avatar).</summary>
    private void UpdateSignInLabel()
    {
        var auth = App.Current.Services.GetService<IAuthService>();
        var signedIn = auth is { State: AuthState.LoggedIn };

        // The sidebar playlists tree needs an authenticated session; (re)load it whenever the auth
        // state resolves to signed-in (the constructor-time attempt runs before auth is ready).
        // Marshalled to the UI thread: the API client's WebView2 cookie source is thread-affine.
        if (signedIn)
        {
            DispatcherQueue.TryEnqueue(() => _ = LoadSidebarPlaylistsAsync());
        }

        void Apply()
        {
            if (signedIn && _currentAccount?.AvatarUrl is { } avatar)
            {
                // Circular profile photo (PersonPicture is round by design), enlarged, + name. The
                // Icon slot is cleared because the avatar lives in the content row.
                SignInItem.Icon = null;
                SignInItem.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new PersonPicture
                        {
                            Width = 28,
                            Height = 28,
                            ProfilePicture = new BitmapImage(avatar),
                            DisplayName = _currentAccount.Name,
                        },
                        new TextBlock
                        {
                            Text = _currentAccount.Name,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                };
            }
            else if (signedIn)
            {
                SignInItem.Content = _currentAccount?.Name ?? Localization.UiStrings.AccountFallback;
                SignInItem.Icon = _currentAccount?.AvatarUrl is { } avatarIcon
                    ? new ImageIcon { Source = new BitmapImage(avatarIcon) }
                    : new FontIcon { Glyph = "î»" };
            }
            else
            {
                SignInItem.Content = Localization.UiStrings.SignIn;
                SignInItem.Icon = new FontIcon { Glyph = "î»" };
            }
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Apply);
        }
    }

    /// <summary>
    /// Shows a small account card (avatar + name + handle) anchored to the footer item, so a
    /// signed-in user can confirm who they are signed in as without re-triggering the login flow.
    /// </summary>
    private void ShowAccountFlyout(FrameworkElement anchor)
    {
        var account = _currentAccount;

        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(4), MinWidth = 220 };

        var flyout = new Flyout { Content = panel };

        // Header row: a settings gear aligned to the top-right, so Settings lives inside the account
        // card (merged, per the reference account menu) instead of a separate nav item.
        var settingsGear = new Button
        {
            Content = new FontIcon { Glyph = "" }, // Settings gear
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(6),
            Background = null,
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(settingsGear, Localization.UiStrings.SettingsTitle);
        settingsGear.Click += (_, _) =>
        {
            flyout.Hide();
            ContentFrame.Navigate(typeof(Views.SettingsPage));
        };
        panel.Children.Add(settingsGear);

        if (account?.AvatarUrl is { } avatar)
        {
            panel.Children.Add(new PersonPicture
            {
                Width = 72,
                Height = 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                ProfilePicture = new BitmapImage(avatar),
                DisplayName = account.Name,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = account?.Name ?? Localization.UiStrings.LoginSuccessTitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        if (!string.IsNullOrEmpty(account?.Handle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = account!.Handle,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var signOut = new Button
        {
            Content = Localization.UiStrings.SignOut,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        signOut.Click += async (_, _) =>
        {
            flyout.Hide();
            await SignOutAsync();
        };
        panel.Children.Add(signOut);

        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Signs the user out: clears the session (cookies) via the playback host, re-evaluates the
    /// auth state, and resets the footer to "Sign in". Best-effort; a failure never crashes the shell.
    /// </summary>
    private async Task SignOutAsync()
    {
        try
        {
            await _playbackHost.SignOutAsync();
        }
        catch
        {
            // Clearing the session is best-effort.
        }

        try
        {
            if (App.Current.Services.GetService<IAuthService>() is { } auth)
            {
                await auth.CheckLoginStatusAsync();
            }
        }
        catch
        {
            // A failed re-check must not block the UI reset below.
        }

        _currentAccount = null;
        UpdateSignInLabel();
    }

    /// <summary>Posts a small system toast for the sign-in outcome. Best-effort; never throws.</summary>
    private void PostLoginToast(bool success)
    {
        try
        {
            var name = _currentAccount?.Name;
            var text = success
                ? (string.IsNullOrEmpty(name) ? "Signed in to YouTube Music" : $"Signed in as {name}")
                : "Sign-in failed";

            var builder = new AppNotificationBuilder().AddText("Kaset").AddText(text);
            if (success && _currentAccount?.AvatarUrl is { } avatar)
            {
                builder.SetAppLogoOverride(avatar, AppNotificationImageCrop.Circle);
            }

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Toast is a nicety; a notification-platform failure must not affect sign-in.
        }
    }
}
