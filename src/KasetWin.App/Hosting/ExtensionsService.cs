using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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
    private static readonly Uri LatestUBlockReleaseUri = new("https://api.github.com/repos/gorhill/uBlock/releases/latest");
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

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

    private static string UBlockFolderPath => Path.Combine(ExtensionsFolderPath, "uBlockOrigin");

    private static string UBlockMetadataPath => Path.Combine(ExtensionsFolderPath, "uBlockOrigin.kaset.json");

    /// <summary>Creates the extensions folder if it does not exist and returns its path.</summary>
    public static string EnsureExtensionsFolder()
    {
        var path = ExtensionsFolderPath;
        Directory.CreateDirectory(path);
        return path;
    }

    public static string UBlockStatusText
    {
        get
        {
            string manifestPath = Path.Combine(UBlockFolderPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return "uBlock Origin will install automatically when the playback WebView starts.";
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;
                string name = root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String
                    ? nameProp.GetString() ?? "uBlock Origin"
                    : "uBlock Origin";
                string? version = root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == System.Text.Json.JsonValueKind.String
                    ? versionProp.GetString()
                    : null;
                return string.IsNullOrWhiteSpace(version)
                    ? $"{name} is installed. Auto-update checks daily."
                    : $"{name} {version} is installed. Auto-update checks daily.";
            }
            catch
            {
                return "uBlock Origin is installed. Auto-update checks daily.";
            }
        }
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
            await EnsureUBlockOriginAsync().ConfigureAwait(false);
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

    private async Task EnsureUBlockOriginAsync()
    {
        var metadata = await ReadUBlockMetadataAsync().ConfigureAwait(false);
        bool hasInstalledManifest = File.Exists(Path.Combine(UBlockFolderPath, "manifest.json"));
        if (hasInstalledManifest &&
            metadata?.CheckedAtUtc is { } checkedAt &&
            DateTimeOffset.UtcNow - checkedAt < UpdateCheckInterval)
        {
            return;
        }

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KasetWin/0.1");

        UBlockRelease? release = await http.GetFromJsonAsync<UBlockRelease>(LatestUBlockReleaseUri).ConfigureAwait(false);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return;
        }

        UBlockAsset? asset = release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".chromium.zip", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains("chromium", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null ||
            string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) ||
            !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            _logger.LogWarning("Latest uBlock Origin release did not contain a Chromium zip asset.");
            return;
        }

        if (hasInstalledManifest &&
            metadata is not null &&
            string.Equals(metadata.TagName, release.TagName, StringComparison.Ordinal))
        {
            await WriteUBlockMetadataAsync(metadata with { CheckedAtUtc = DateTimeOffset.UtcNow }).ConfigureAwait(false);
            return;
        }

        string workRoot = Path.Combine(ExtensionsFolderPath, ".uBlock-download");
        string zipPath = Path.Combine(workRoot, asset.Name);
        string extractRoot = Path.Combine(workRoot, "extract");
        string stageFolder = Path.Combine(workRoot, "uBlockOrigin");

        if (Directory.Exists(workRoot))
        {
            Directory.Delete(workRoot, recursive: true);
        }

        Directory.CreateDirectory(workRoot);

        try
        {
            await using (Stream remote = await http.GetStreamAsync(downloadUri).ConfigureAwait(false))
            await using (var local = File.Create(zipPath))
            {
                await remote.CopyToAsync(local).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(zipPath, extractRoot);
            string manifestFolder = FindManifestFolder(extractRoot)
                ?? throw new InvalidOperationException("Downloaded uBlock Origin zip did not contain manifest.json.");

            Directory.Move(manifestFolder, stageFolder);
            ReplaceDirectory(stageFolder, UBlockFolderPath);

            await WriteUBlockMetadataAsync(new UBlockInstallMetadata(
                release.TagName,
                asset.Name,
                DateTimeOffset.UtcNow)).ConfigureAwait(false);

            _logger.LogInformation("Installed uBlock Origin {Version}.", release.TagName);
        }
        finally
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    private static string? FindManifestFolder(string root)
    {
        if (File.Exists(Path.Combine(root, "manifest.json")))
        {
            return root;
        }

        return Directory
            .EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(path => !string.IsNullOrEmpty(path));
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        string backup = destination + ".old";
        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, recursive: true);
        }

        if (Directory.Exists(destination))
        {
            Directory.Move(destination, backup);
        }

        try
        {
            Directory.Move(source, destination);
        }
        catch
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            if (Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }

            throw;
        }

        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, recursive: true);
        }
    }

    private static async Task<UBlockInstallMetadata?> ReadUBlockMetadataAsync()
    {
        if (!File.Exists(UBlockMetadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(UBlockMetadataPath);
            return await System.Text.Json.JsonSerializer.DeserializeAsync<UBlockInstallMetadata>(stream).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteUBlockMetadataAsync(UBlockInstallMetadata metadata)
    {
        Directory.CreateDirectory(ExtensionsFolderPath);
        await using var stream = File.Create(UBlockMetadataPath);
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, metadata).ConfigureAwait(false);
    }

    private sealed record UBlockRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] IReadOnlyList<UBlockAsset> Assets);

    private sealed record UBlockAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    private sealed record UBlockInstallMetadata(
        string TagName,
        string AssetName,
        DateTimeOffset CheckedAtUtc);
}
