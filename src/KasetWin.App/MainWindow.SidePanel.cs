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
    /// Renders an in-app notification in the InfoBar above the account footer. Plain messages
    /// auto-dismiss after a few seconds; actionable ones stay open (with an action button and the
    /// built-in close X) until the user acts or dismisses. Marshals to the UI thread.
    /// </summary>
    private void OnInAppNotification(Notifications.InAppNotification n)
    {
        var queue = this.DispatcherQueue;
        if (queue is null)
        {
            return;
        }

        queue.TryEnqueue(() =>
        {
            _toastTimer?.Stop();

            ToastBar.Title = n.Title ?? string.Empty;
            ToastBar.Message = n.Message;
            ToastBar.Severity = InfoBarSeverity.Informational;

            if (n.IsActionable)
            {
                var button = new Button { Content = n.ActionText };
                button.Click += (_, _) =>
                {
                    ToastBar.IsOpen = false;
                    n.OnAction!.Invoke();
                };
                ToastBar.ActionButton = button;
                ToastBar.IsOpen = true;
                // Actionable notifications persist until the user acts or closes them.
            }
            else
            {
                ToastBar.ActionButton = null;
                ToastBar.IsOpen = true;

                _toastTimer ??= queue.CreateTimer();
                _toastTimer.Interval = TimeSpan.FromSeconds(5);
                _toastTimer.IsRepeating = false;
                _toastTimer.Tick -= OnToastTimerTick;
                _toastTimer.Tick += OnToastTimerTick;
                _toastTimer.Start();
            }
        });
    }

    private void OnToastTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ToastBar.IsOpen = false;
    }

    /// <summary>Slides the docked now-playing side panel in/out as the shared controller mode changes.</summary>
    private void OnSidePanelChanged()
    {
        var open = _sidePanel?.Mode is not (null or Controls.SidePanelMode.None);
        if (open)
        {
            // Shrink the sidebar to icons-only while the panel is open so the content + panel share
            // the width (the whole shell reflows), mirroring Apple Music / the reference behaviour.
            ShrinkSidebarForPanel(true);
            SidePanelColumn.Width = new GridLength(380);
            SidePanel.Visibility = Visibility.Visible;
            AnimateSidePanel(fromX: 380, toX: 0, fromOpacity: 0, toOpacity: 1, collapseOnFinish: false);
        }
        else if (SidePanel.Visibility == Visibility.Visible)
        {
            ShrinkSidebarForPanel(false);
            AnimateSidePanel(fromX: 0, toX: 380, fromOpacity: 1, toOpacity: 0, collapseOnFinish: true);
        }
    }

    /// <summary>Remembers the pane mode chosen before a panel opened so it can be restored on close.</summary>
    private NavigationViewPaneDisplayMode _paneModeBeforePanel = NavigationViewPaneDisplayMode.Auto;
    private bool _sidebarShrunkForPanel;

    /// <summary>
    /// Collapses the NavigationView pane to compact (icons only) while a now-playing panel is open,
    /// then restores the previous pane mode when it closes. The NavView animates the pane width, so
    /// the content region reflows smoothly around both the shrunken sidebar and the docked panel.
    /// </summary>
    private void ShrinkSidebarForPanel(bool shrink)
    {
        if (shrink)
        {
            if (_sidebarShrunkForPanel)
            {
                return;
            }

            _paneModeBeforePanel = NavView.PaneDisplayMode;
            _sidebarShrunkForPanel = true;
            NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;

            // Compact pane: reveal the hamburger (so the pane can still be expanded) and swap the wide
            // source pill for the round icon-only toggle so it fits the narrow rail.
            NavView.IsPaneToggleButtonVisible = true;
            SourceTogglePill.Visibility = Visibility.Collapsed;
            SourceToggleCompact.Visibility = Visibility.Visible;
            UpdateSourceCompactIcon();
        }
        else if (_sidebarShrunkForPanel)
        {
            _sidebarShrunkForPanel = false;
            NavView.PaneDisplayMode = _paneModeBeforePanel;

            NavView.IsPaneToggleButtonVisible = false;
            SourceTogglePill.Visibility = Visibility.Visible;
            SourceToggleCompact.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Slide + fade the side panel (Queue / Lyrics) for a smooth open/close.</summary>
    private void AnimateSidePanel(double fromX, double toX, double fromOpacity, double toOpacity, bool collapseOnFinish)
    {
        if (SidePanel.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform tt)
        {
            tt = new Microsoft.UI.Xaml.Media.TranslateTransform();
            SidePanel.RenderTransform = tt;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        var slide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = fromX,
            To = toX,
            Duration = duration,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, tt);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "X");
        storyboard.Children.Add(slide);

        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = fromOpacity,
            To = toOpacity,
            Duration = duration,
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, SidePanel);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);

        if (collapseOnFinish)
        {
            storyboard.Completed += (_, _) =>
            {
                SidePanel.Visibility = Visibility.Collapsed;
                SidePanelColumn.Width = new GridLength(0);
            };
        }

        storyboard.Begin();
    }
}
