using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;

namespace KasetWin.App.Hosting;

/// <summary>
/// Loads user-supplied WebView2 browser extensions (e.g. uBlock Origin) into the shared browser
/// profile — the Windows port of the macOS user-managed Web Extensions (ADR 0014). Extensions are
/// not bundled; the user drops each unpacked extension (a folder containing <c>manifest.json</c>)
/// into <see cref="ExtensionsFolderPath"/>, and every enabled one is added to the profile the hidden
/// playback + watch WebViews share, so their content/network rules apply to YouTube Music and
/// YouTube playback.
/// </summary>
/// <remarks>
/// Requires the shared environment created with <c>AreBrowserExtensionsEnabled = true</c>
/// (<see cref="KasetWin.Platform.Playback.WebViewEnvironmentProvider"/>). Adding to one core's
/// <see cref="CoreWebView2Profile"/> is enough because all WebViews share that profile.
/// </remarks>
public sealed class ExtensionsService
{
    private readonly ILogger<ExtensionsService> _logger;
    private bool _loaded;

    public ExtensionsService(ILogger<ExtensionsService>? logger = null)
    {
        _logger = logger ?? NullLogger<ExtensionsService>.Instance;
    }

    /// <summary>
    /// The folder the user places unpacked extensions in: <c>…\LocalState\Extensions</c>. Each
    /// direct sub-folder that contains a <c>manifest.json</c> is treated as one extension.
    /// </summary>
    public static string ExtensionsFolderPath =>
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "Extensions");

    /// <summary>Creates the extensions folder if it does not exist and returns its path.</summary>
    public static string EnsureExtensionsFolder()
    {
        var path = ExtensionsFolderPath;
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Loads every unpacked extension under <see cref="ExtensionsFolderPath"/> into
    /// <paramref name="profile"/>. Idempotent per app run and best-effort: a malformed or already
    /// present extension is skipped, never thrown. A no-op when the runtime lacks extension support.
    /// </summary>
    public async Task LoadIntoAsync(CoreWebView2Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        string folder;
        try
        {
            folder = EnsureExtensionsFolder();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not prepare the extensions folder.");
            return;
        }

        IReadOnlyList<CoreWebView2BrowserExtension> installed;
        try
        {
            installed = await profile.GetBrowserExtensionsAsync();
        }
        catch (Exception ex)
        {
            // Older WebView2 runtimes without extension support land here — adblock simply stays off.
            _logger.LogWarning(ex, "Browser extensions are not supported by the installed WebView2 runtime.");
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            if (!File.Exists(Path.Combine(dir, "manifest.json")))
            {
                continue;
            }

            try
            {
                await profile.AddBrowserExtensionAsync(dir);
                _logger.LogInformation("Loaded browser extension from {Folder}.", Path.GetFileName(dir));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load browser extension from {Folder}.", Path.GetFileName(dir));
            }
        }

        _ = installed; // presence check kept for future enable/disable management.
    }
}
