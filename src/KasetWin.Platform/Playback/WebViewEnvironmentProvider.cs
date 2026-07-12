using KasetWin.Platform.Storage;
using Microsoft.Web.WebView2.Core;

namespace KasetWin.Platform.Playback;

/// <summary>
/// Lazily creates and shares a single <see cref="CoreWebView2Environment"/> for every WebView2 in
/// the app (login, hidden playback, YouTube watch). Two things depend on a shared, explicitly
/// configured environment:
/// <list type="bullet">
///   <item><description>
///   <b>Browser extensions</b> (Task: uBlock/adblock, mirrors macOS ADR 0014) — enabled via
///   <see cref="CoreWebView2EnvironmentOptions.AreBrowserExtensionsEnabled"/>, which must be set at
///   environment-creation time and cannot be toggled later.
///   </description></item>
///   <item><description>
///   <b>Cookie / session sharing</b> — pinning one user-data folder guarantees the login WebView and
///   the playback WebView read the same session cookies.
///   </description></item>
/// </list>
/// Register as a singleton and hand the environment to each <c>EnsureCoreWebView2Async(env)</c> call.
/// </summary>
public sealed class WebViewEnvironmentProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CoreWebView2Environment? _environment;

    /// <summary>
    /// Returns the shared environment, creating it on first use. Thread-safe and idempotent.
    /// </summary>
    public async Task<CoreWebView2Environment> GetAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _environment ??= await CreateAsync().ConfigureAwait(false);
            return _environment;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        // Use the app's local folder as the user-data base so WebView2 creates/reuses the SAME
        // `EBWebView` profile across launches — keeping the signed-in session while still enabling
        // extensions. AppData.LocalFolder is the package LocalState when packaged and
        // %LOCALAPPDATA%\Kaset when the app runs standalone (the standalone build has its own session).
        var userDataFolder = AppData.LocalFolder;

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
        };

        return await CoreWebView2Environment
            .CreateWithOptionsAsync(browserExecutableFolder: string.Empty, userDataFolder, options)
            .AsTask()
            .ConfigureAwait(false);
    }
}
