namespace KasetWin.Core.Abstractions;

/// <summary>
/// Cross-layer abstraction for securely persisting and retrieving small secrets such as a
/// backup of the SAPISID/<c>__Secure-3PAPISID</c> value (Req 22.1).
/// </summary>
/// <remarks>
/// <para>
/// The platform-layer implementation (<c>DpapiCredentialStore</c>) protects secrets at rest
/// using Windows DPAPI (<c>CurrentUser</c> scope) or the Windows Credential Locker. This Core
/// interface carries no WinUI/WinRT dependency so auth logic can be exercised against an
/// in-memory fake.
/// </para>
/// <para>
/// Secrets passed to and returned from this store must never be written to logs, fixtures, or
/// documentation.
/// </para>
/// </remarks>
public interface ICredentialStore
{
    /// <summary>
    /// Persists <paramref name="secret"/> under <paramref name="key"/>, replacing any existing
    /// value for that key.
    /// </summary>
    /// <param name="key">A non-secret logical identifier for the credential.</param>
    /// <param name="secret">The secret value to protect at rest. Never logged.</param>
    Task SaveAsync(string key, string secret);

    /// <summary>
    /// Loads the secret previously stored under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The logical identifier supplied to <see cref="SaveAsync"/>.</param>
    /// <returns>
    /// The decrypted secret, or <see langword="null"/> when no value is stored for the key (or
    /// the stored value can no longer be decrypted by the current user).
    /// </returns>
    Task<string?> LoadAsync(string key);

    /// <summary>
    /// Removes any secret stored under <paramref name="key"/>. A no-op when the key is absent.
    /// </summary>
    /// <param name="key">The logical identifier to delete.</param>
    Task DeleteAsync(string key);
}
