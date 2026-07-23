using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.Api;
using Microsoft.UI.Dispatching;

namespace KasetWin.App.Composition;

/// <summary>
/// Runs cookie reads on the UI thread, whoever asks.
/// </summary>
/// <remarks>
/// <para>
/// <c>CoreWebView2.CookieManager</c> is a COM object with thread affinity: touching it from a
/// thread-pool thread throws <c>COMException: This method can only be called from the thread that
/// created the object</c>. Every InnerTube request reads cookies to sign itself, so any API call
/// made off the UI thread failed outright for a signed-in user — and failed <em>silently</em>
/// wherever the caller treats metadata as a nicety and swallows exceptions.
/// </para>
/// <para>
/// That is exactly how the missing album line survived a round of "fixes": the background
/// enrichment introduced to fetch it could never have worked, no matter how correct the code above
/// it was. The log proved it — 100% <c>FAILED: COMException</c>, one per track.
/// </para>
/// <para>
/// The marshalling lives here, in the App layer, rather than in
/// <c>WebView2CookieSource</c>: the Platform project has no WinUI dependency, and dragging a
/// dispatcher into it to satisfy one caller would be the wrong direction. Callers already on the
/// UI thread pay nothing — the fast path is a straight pass-through.
/// </para>
/// </remarks>
internal sealed class UiThreadCookieSource : ICookieSource
{
    private readonly ICookieSource _inner;
    private readonly DispatcherQueue _dispatcher;

    public UiThreadCookieSource(ICookieSource inner, DispatcherQueue dispatcher)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public Task<CookieSnapshot> GetCookiesAsync(string origin, CancellationToken ct = default)
    {
        if (_dispatcher.HasThreadAccess)
        {
            return _inner.GetCookiesAsync(origin, ct);
        }

        var completion = new TaskCompletionSource<CookieSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_dispatcher.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await _inner.GetCookiesAsync(origin, ct).ConfigureAwait(true));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(ct);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            // The UI thread is gone (shutdown). An empty snapshot keeps public endpoints working
            // instead of turning teardown into a crash — same contract the inner source promises
            // when the WebView2 does not exist yet.
            return Task.FromResult(CookieSnapshot.Empty(origin));
        }

        return completion.Task;
    }
}
