using System.Security.Cryptography;
using System.Text;
using KasetWin.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasetWin.Platform.Security;

/// <summary>
/// <see cref="ICredentialStore"/> implementation that protects secrets at rest using Windows
/// DPAPI (<see cref="DataProtectionScope.CurrentUser"/>) and stores each protected blob as a
/// file under the local application data folder (task 9.1, Req 22.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why DPAPI + file (chosen over <c>Windows.Security.Credentials.PasswordVault</c>):</b>
/// DPAPI is available in both packaged and unpackaged processes, has no per-app credential
/// count limit, and lets the backing directory be overridden for headless tests. Secrets are
/// encrypted with the current user's key so another user (or the same user on another machine)
/// cannot decrypt them; on failure to decrypt, <see cref="LoadAsync"/> reports the key as absent.
/// </para>
/// <para>
/// Secret values are never logged. The on-disk file name is derived from a SHA-256 hash of the
/// logical key so arbitrary key text never touches the file system path.
/// </para>
/// </remarks>
public sealed class DpapiCredentialStore : ICredentialStore
{
    private const string DefaultAppFolderName = "Kaset";
    private const string CredentialsFolderName = "Credentials";
    private const string FileExtension = ".bin";

    private readonly string _directory;
    private readonly ILogger<DpapiCredentialStore> _logger;

    /// <summary>
    /// Creates a credential store rooted at the per-user local application data folder
    /// (<c>%LOCALAPPDATA%\Kaset\Credentials</c>).
    /// </summary>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public DpapiCredentialStore(ILogger<DpapiCredentialStore>? logger = null)
        : this(DefaultDirectory(), logger)
    {
    }

    /// <summary>
    /// Creates a credential store rooted at an explicit directory. Intended for tests so the
    /// store can write to a temporary location instead of the real user profile.
    /// </summary>
    /// <param name="directory">The directory that will hold the protected credential files.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public DpapiCredentialStore(string directory, ILogger<DpapiCredentialStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _logger = logger ?? NullLogger<DpapiCredentialStore>.Instance;
    }

    /// <inheritdoc />
    public Task SaveAsync(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        Directory.CreateDirectory(_directory);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(plaintext, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
        finally
        {
            // Avoid leaving the plaintext lingering in memory longer than necessary.
            Array.Clear(plaintext, 0, plaintext.Length);
        }

        var path = PathForKey(key);
        // Write atomically: write to a temp file then move over the destination.
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);
        File.Move(tempPath, path, overwrite: true);

        _logger.LogDebug("Stored protected credential for key '{Key}'.", key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> LoadAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = PathForKey(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plaintext = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            try
            {
                return Task.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
            }
            finally
            {
                Array.Clear(plaintext, 0, plaintext.Length);
            }
        }
        catch (CryptographicException)
        {
            // Blob cannot be decrypted by the current user (e.g. profile/machine changed or the
            // file is corrupt). Report as absent without leaking any secret material.
            _logger.LogWarning("Stored credential for key '{Key}' could not be decrypted; treating as absent.", key);
            return Task.FromResult<string?>(null);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = PathForKey(key);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogDebug("Deleted stored credential for key '{Key}'.", key);
        }

        return Task.CompletedTask;
    }

    private static string DefaultDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, DefaultAppFolderName, CredentialsFolderName);
    }

    private string PathForKey(string key)
    {
        // Derive a stable, filesystem-safe file name from the key without exposing its text.
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(keyBytes);
        var fileName = Convert.ToHexString(hash) + FileExtension;
        return Path.Combine(_directory, fileName);
    }
}
