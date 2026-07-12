using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
