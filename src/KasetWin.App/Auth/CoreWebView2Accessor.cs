using Microsoft.Web.WebView2.Core;

namespace KasetWin.App.Auth;

/// <summary>
/// Mutable, app-wide holder for the <see cref="CoreWebView2"/> whose cookie store should back
/// authentication reads (Task 9.3). It is the seam that lets <c>WebView2CookieSource</c>
/// (Task 9.1, Req 3.3) read session cookies from whichever interactive/hidden WebView2 is
/// currently authoritative.
/// </summary>
/// <remarks>
/// <para>
/// Two producers publish into this accessor:
/// </para>
/// <list type="bullet">
///   <item><description>
///   The interactive <see cref="LoginDialog"/> sets <see cref="Current"/> to its own core while
///   the Google sign-in flow is on screen so <c>AuthService.CheckLoginStatusAsync</c> can detect
///   the freshly-set <c>__Secure-3PAPISID</c> cookie and flip to <c>LoggedIn</c> (Req 4.3/4.4).
///   </description></item>
///   <item><description>
///   TODO (Task 8.3): the <c>PlaybackWebViewHost</c> may publish its hidden playback core here
///   once initialized, so authenticated InnerTube requests keep working after the dialog closes.
///   Because WebView2 controls in the same app share a user-data folder (and therefore the same
///   cookie store), reading from either core resolves the same SAPISID.
///   </description></item>
/// </list>
/// <para>
/// The DI registration wraps this in a <see cref="System.Func{TResult}"/> so the WinRT-free
/// <c>WebView2CookieSource</c> never takes a hard dependency on this App type. Access is expected
/// from the UI thread; the field is simply the latest published core. Cookie values are secrets
/// and are never read or logged here.
/// </para>
/// </remarks>
internal sealed class CoreWebView2Accessor
{
    /// <summary>
    /// The current authoritative <see cref="CoreWebView2"/> for cookie reads, or
    /// <see langword="null"/> when no WebView2 has been published yet (signed-out, returns an
    /// empty cookie snapshot).
    /// </summary>
    public CoreWebView2? Current { get; set; }
}
