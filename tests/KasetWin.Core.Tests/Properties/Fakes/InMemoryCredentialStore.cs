using System.Collections.Concurrent;
using KasetWin.Core.Abstractions;

namespace KasetWin.Core.Tests.Properties.Fakes;

/// <summary>
/// Headless, in-memory fake of <see cref="ICredentialStore"/> for the credential round-trip part of
/// Property 32. Stands in for the platform <c>DpapiCredentialStore</c> (which needs WinRT) so the
/// store/load contract can be exercised on the plain .NET runner.
///
/// SECURITY: only ever populated with synthetic placeholder secrets in tests — never real
/// cookies/tokens/SAPISID values.
/// </summary>
internal sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task SaveAsync(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(secret);
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> LoadAsync(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Task.FromResult(_secrets.TryGetValue(key, out var value) ? value : null);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _secrets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
