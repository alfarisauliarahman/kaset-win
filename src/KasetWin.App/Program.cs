using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Velopack;

namespace KasetWin.App;

/// <summary>
/// Custom process entry point. Replaces the XAML-generated <c>Main</c> (via the
/// <c>DISABLE_XAML_GENERATED_MAIN</c> constant) so Velopack's install / update / uninstall hooks run
/// <b>before</b> any WinUI is created: on such an invocation <see cref="VelopackApp"/> does its work
/// and exits the process; on a normal launch it is a no-op and the usual WinUI startup proceeds. The
/// body mirrors the standard generated Main so ordinary startup is unchanged.
/// </summary>
public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main(string[] args)
    {
        // Must be the very first thing to run — handles Setup/Update/Uninstall lifecycle events.
        VelopackApp.Build().Run();

        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single-instance: if Kaset is already running, forward this launch (and any kaset:// URI it
        // carried) to that instance — which surfaces its window, hidden-to-tray or not — then exit.
        // Without this each launch spawns a second process with its own WebView2, so the window that
        // was hidden for background audio could never be brought back.
        if (RedirectToPrimaryInstance())
        {
            return;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    /// <summary>
    /// Registers this process as the primary instance under a fixed key, or — if another instance
    /// already owns that key — redirects the current activation to it and reports that the caller
    /// should exit. Best-effort: if the app-lifecycle API is unavailable, launches normally.
    /// </summary>
    private static bool RedirectToPrimaryInstance()
    {
        try
        {
            var keyInstance = AppInstance.FindOrRegisterForKey("KasetWin-main");
            if (keyInstance.IsCurrent)
            {
                return false; // we are the primary instance — carry on with normal startup.
            }

            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            RedirectActivationTo(activatedArgs, keyInstance);
            return true;
        }
        catch
        {
            // Lifecycle activation is unavailable in this environment; fall back to launching normally.
            return false;
        }
    }

    // Redirecting activation is async, but Main is a plain STA thread with no message pump yet. The
    // canonical WinAppSDK pattern runs the redirect on a worker and pumps COM here until it signals.
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        var redirectEvent = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(redirectEvent);
        });

        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, new[] { redirectEvent }, out _);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);
}
