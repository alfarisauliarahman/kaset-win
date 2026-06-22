using System.Runtime.InteropServices;
using KasetWin.Core.Services.Sharing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using WinRT;

namespace KasetWin.App.Sharing;

/// <summary>
/// Presents the native Windows share UI for a resolved <see cref="ShareTarget"/> (Req 34.1).
/// </summary>
/// <remarks>
/// <para>
/// WinUI 3 desktop apps cannot call <see cref="DataTransferManager.GetForCurrentView"/> (there is no
/// <c>CoreWindow</c>), so the share sheet is shown through the documented <b>HWND interop</b> path:
/// the <c>IDataTransferManagerInterop</c> COM interface obtained from the
/// <see cref="DataTransferManager"/> activation factory exposes
/// <c>GetForWindow</c> / <c>ShowShareUIForWindow</c>, both taking the window handle of the shell
/// (<see cref="WinRT.Interop.WindowNative.GetWindowHandle(object)"/> on the <see cref="Window"/>).
/// </para>
/// <para>
/// On the <see cref="DataTransferManager.DataRequested"/> callback we set the package title +
/// description and attach both a web link and a plain-text fallback so every share-target app
/// (Mail, Teams, clipboard, …) receives the URL. The handler unsubscribes itself so a fresh share
/// request is fully described each time.
/// </para>
/// </remarks>
internal static class ShareInvoker
{
    // IID of Windows.ApplicationModel.DataTransfer.DataTransferManager (required by GetForWindow).
    private static readonly Guid DataTransferManagerIid =
        new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    /// <summary>
    /// Shows the native share UI for <paramref name="target"/> anchored to <paramref name="window"/>.
    /// No-op (returns <see langword="false"/>) when the window or target is unavailable — callers
    /// disable the affordance up front when there is nothing to share (Req 34.2), this is the final
    /// defensive guard.
    /// </summary>
    public static bool TryShow(Window? window, ShareTarget? target)
    {
        if (window is null || target is null)
        {
            return false;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        return TryShow(hwnd, target);
    }

    /// <summary>
    /// Shows the native share UI for <paramref name="target"/> anchored to the window handle
    /// <paramref name="hwnd"/>. Returns <see langword="false"/> when the handle is null or the
    /// share UI could not be presented.
    /// </summary>
    public static bool TryShow(IntPtr hwnd, ShareTarget? target)
    {
        if (hwnd == IntPtr.Zero || target is null)
        {
            return false;
        }

        try
        {
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();

            var riid = DataTransferManagerIid;
            var abi = interop.GetForWindow(hwnd, ref riid);
            var manager = MarshalInterface<DataTransferManager>.FromAbi(abi);

            void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= OnDataRequested;

                var data = args.Request.Data;
                data.Properties.Title = target.Title;
                if (!string.IsNullOrEmpty(target.Subtitle))
                {
                    data.Properties.Description = target.Subtitle;
                }

                // Provide both a web link (rich preview) and text (universal fallback) — Req 34.1.
                data.SetWebLink(target.Url);
                data.SetText(target.ShareText + " " + target.Url);
            }

            manager.DataRequested += OnDataRequested;
            interop.ShowShareUIForWindow(hwnd);
            return true;
        }
        catch
        {
            // Sharing is best-effort: a locked-down/unavailable share broker must never crash the
            // shell. The affordance simply does nothing in that (rare) case.
            return false;
        }
    }

    /// <summary>
    /// COM interop interface for showing the share UI from a desktop (HWND-based) window. Mirrors
    /// the <c>IDataTransferManagerInterop</c> definition published by Microsoft for WinUI 3 apps.
    /// </summary>
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow([In] IntPtr appWindow);
    }
}
