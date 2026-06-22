using KasetWin.Core.Services.Api;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for the cookie-source and credential-store contracts using in-memory fakes
/// (task 9.5, Req 22.1). The platform implementations (<c>WebView2CookieSource</c> over
/// <c>CoreWebView2.CookieManager</c>, and <c>DpapiCredentialStore</c> over Windows DPAPI) need
/// WinRT, so these tests exercise the WinRT-free seams the app and tests share: cookie
/// extraction / SAPISID selection (<see cref="CookieSapisidResolver"/>) and credential
/// save → load → delete round-trips.
///
/// SECURITY: every value below is a synthetic placeholder — never a real cookie / SAPISID / token.
/// </summary>
public class CookieSourceAndCredentialStoreTests
{
    private const string MusicOrigin = "https://music.youtube.com";

    // Synthetic placeholders only — NOT real secrets.
    private const string Primary3PapisidPlaceholder = "primary-3papisid-placeholder";
    private const string SapisidPlaceholder = "sapisid-placeholder";

    // ── Cookie extraction (Req 3.3 / 22.1) ─────────────────────────────────────────────────

    [Fact]
    public async Task Cookie_source_returns_the_cookies_for_the_requested_origin()
    {
        var source = new InMemoryCookieSource(
        [
            new CookiePair("__Secure-3PAPISID", Primary3PapisidPlaceholder),
            new CookiePair("SID", "sid-placeholder"),
        ]);

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        Assert.Equal(MusicOrigin, snapshot.Origin);
        Assert.Equal(2, snapshot.Cookies.Count);
        Assert.Equal(MusicOrigin, source.LastRequestedOrigin);
    }

    [Fact]
    public async Task Empty_cookie_source_returns_a_non_null_unauthenticated_snapshot()
    {
        var source = new InMemoryCookieSource([]);

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        Assert.Empty(snapshot.Cookies);
        Assert.False(CookieSapisidResolver.TryResolve(snapshot.Cookies, out _));
    }

    [Fact]
    public async Task Cookie_source_carries_multi_account_selectors()
    {
        var source = new InMemoryCookieSource(
            [new CookiePair("SAPISID", SapisidPlaceholder)],
            authUserIndex: 2,
            onBehalfOfUser: "123456789012345678901");

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        Assert.Equal(2, snapshot.AuthUserIndex);
        Assert.Equal("123456789012345678901", snapshot.OnBehalfOfUser);
    }

    // ── SAPISID selection (Req 3.3) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Sapisid_resolution_prefers_secure_3papisid()
    {
        var source = new InMemoryCookieSource(
        [
            new CookiePair("SAPISID", SapisidPlaceholder),
            new CookiePair("__Secure-3PAPISID", Primary3PapisidPlaceholder),
        ]);

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        Assert.True(CookieSapisidResolver.TryResolve(snapshot.Cookies, out var resolved));
        Assert.Equal(Primary3PapisidPlaceholder, resolved); // primary wins over fallback
    }

    [Fact]
    public async Task Sapisid_resolution_falls_back_to_sapisid()
    {
        var source = new InMemoryCookieSource(
        [
            new CookiePair("SID", "sid-placeholder"),
            new CookiePair("SAPISID", SapisidPlaceholder),
        ]);

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        Assert.Equal(SapisidPlaceholder, CookieSapisidResolver.Resolve(snapshot.Cookies));
    }

    [Fact]
    public async Task Sapisid_resolution_fails_when_neither_cookie_is_present()
    {
        var source = new InMemoryCookieSource(
            [new CookiePair("SID", "sid-placeholder")]);

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        Assert.Null(CookieSapisidResolver.Resolve(snapshot.Cookies));
        Assert.False(CookieSapisidResolver.TryResolve(snapshot.Cookies, out var sapisid));
        Assert.Equal(string.Empty, sapisid);
    }

    [Fact]
    public async Task Sapisid_resolution_treats_empty_primary_value_as_absent()
    {
        var source = new InMemoryCookieSource(
        [
            new CookiePair("__Secure-3PAPISID", string.Empty),
            new CookiePair("SAPISID", SapisidPlaceholder),
        ]);

        var snapshot = await source.GetCookiesAsync(MusicOrigin);

        // Empty primary cannot authenticate; deterministic fallback to SAPISID.
        Assert.Equal(SapisidPlaceholder, CookieSapisidResolver.Resolve(snapshot.Cookies));
    }

    // ── Credential round-trip (Req 22.1) ───────────────────────────────────────────────────

    [Fact]
    public async Task Credential_round_trips_save_then_load()
    {
        var store = new InMemoryCredentialStore();
        const string key = "sapisid-backup";
        const string secret = "stored-secret-placeholder";

        await store.SaveAsync(key, secret);
        var loaded = await store.LoadAsync(key);

        Assert.Equal(secret, loaded);
    }

    [Fact]
    public async Task Credential_save_overwrites_existing_value()
    {
        var store = new InMemoryCredentialStore();
        const string key = "sapisid-backup";

        await store.SaveAsync(key, "first-placeholder");
        await store.SaveAsync(key, "second-placeholder");

        Assert.Equal("second-placeholder", await store.LoadAsync(key));
    }

    [Fact]
    public async Task Credential_load_returns_null_for_an_unknown_key()
    {
        var store = new InMemoryCredentialStore();

        Assert.Null(await store.LoadAsync("never-stored"));
    }

    [Fact]
    public async Task Credential_delete_removes_the_value()
    {
        var store = new InMemoryCredentialStore();
        const string key = "sapisid-backup";

        await store.SaveAsync(key, "to-be-deleted-placeholder");
        await store.DeleteAsync(key);

        Assert.Null(await store.LoadAsync(key));
    }

    [Fact]
    public async Task Credential_delete_is_a_no_op_for_an_unknown_key()
    {
        var store = new InMemoryCredentialStore();

        // Must not throw.
        await store.DeleteAsync("never-stored");

        Assert.Null(await store.LoadAsync("never-stored"));
    }
}
