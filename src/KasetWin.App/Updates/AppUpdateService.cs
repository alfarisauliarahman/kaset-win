using System;
using System.Threading;
using System.Threading.Tasks;
using KasetWin.App.Localization;
using KasetWin.App.Notifications;
using Microsoft.UI.Dispatching;
using Velopack;
using Velopack.Sources;

namespace KasetWin.App.Updates;

/// <summary>
/// Checks GitHub Releases for a newer Velopack package on startup, downloads it in the background, and
/// — once staged — surfaces an actionable in-app notification whose action applies the update and
/// restarts the app. Entirely best-effort: any failure (offline, not installed via the Velopack
/// installer, a GitHub rate-limit) is swallowed so it never disturbs a normal session. The check runs
/// at most once per process.
/// </summary>
public sealed class AppUpdateService
{
    // The public releases repo. Velopack's GithubSource reads its Releases (and the RELEASES /
    // releases.win.json feed uploaded alongside each tag) to discover newer packages.
    private const string RepositoryUrl = "https://github.com/alfarisauliarahman/kaset-win";

    private readonly IInAppNotifier _notifier;
    private readonly DispatcherQueue _dispatcher;
    private int _started; // 0 until the single background check has been kicked off.

    public AppUpdateService(IInAppNotifier notifier, DispatcherQueue dispatcher)
    {
        _notifier = notifier;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Fire-and-forget: checks for and downloads an update on a background thread, then raises the
    /// "restart to update" notification on the UI thread. Safe to call from the shell startup path.
    /// </summary>
    public void StartBackgroundCheck()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = Task.Run(CheckAsync);
    }

    private async Task CheckAsync()
    {
        try
        {
            // prerelease: true — the 0.x releases are published as GitHub pre-releases; without this
            // the source would ignore them and the app would never discover an update.
            var source = new GithubSource(RepositoryUrl, null, true, null);
            var manager = new UpdateManager(source, null, null);

            // Only an installed (Velopack) copy can update itself. A dev / unpackaged / portable run
            // reports false and is skipped — there is nothing to swap out in that case.
            if (!manager.IsInstalled)
            {
                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return; // already on the latest release.
            }

            await manager.DownloadUpdatesAsync(update, null, CancellationToken.None).ConfigureAwait(false);

            var version = update.TargetFullRelease.Version.ToString();
            _dispatcher.TryEnqueue(() => NotifyReady(manager, update, version));
        }
        catch
        {
            // Auto-update is a convenience; a failed check must never surface an error to the user.
        }
    }

    private void NotifyReady(UpdateManager manager, UpdateInfo update, string version)
    {
        _notifier.Show(new InAppNotification(
            Message: UiStrings.UpdateReadyMessage(version),
            Title: UiStrings.UpdateAvailableTitle,
            ActionText: UiStrings.UpdateRestartAction,
            OnAction: () =>
            {
                try
                {
                    // Hands off to the Velopack updater, which waits for this process to exit, swaps in
                    // the new version, and relaunches it.
                    manager.ApplyUpdatesAndRestart(update.TargetFullRelease, null);
                }
                catch
                {
                    // If applying fails (e.g. files locked), leave the running app untouched.
                }
            }));
    }
}
