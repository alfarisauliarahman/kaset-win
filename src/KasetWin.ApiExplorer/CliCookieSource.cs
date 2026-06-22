using KasetWin.Core.Abstractions;
using KasetWin.Core.Services.Api;

namespace KasetWin.ApiExplorer;

/// <summary>
/// Console-only <see cref="ICookieSource"/> for the API Explorer (Req 24). The production app
/// resolves cookies from <c>CoreWebView2.CookieManager</c> (a WinRT type unavailable in a pure
/// console host), so the CLI substitutes a runtime-supplied source instead:
/// <list type="bullet">
///   <item>the <c>--cookies &lt;path&gt;</c> argument pointing at a file containing a raw
///   <c>Cookie:</c> header, or</item>
///   <item>the <c>KASET_COOKIE</c> environment variable holding the same header value.</item>
/// </list>
/// </summary>
/// <remarks>
/// SECURITY: cookie names/values are secrets. This type never logs or prints them, and nothing
/// here is ever persisted or committed — values are read only from the runtime environment or a
/// caller-supplied file path. SAPISID resolution reuses <see cref="CookieSapisidResolver"/> so
/// the CLI authenticates identically to the app.
/// </remarks>
internal sealed class CliCookieSource : ICookieSource
{
    private readonly IReadOnlyList<CookiePair> _cookies;
    private readonly int? _authUserIndex;
    private readonly string? _onBehalfOfUser;

    public CliCookieSource(
        IReadOnlyList<CookiePair> cookies,
        int? authUserIndex = null,
        string? onBehalfOfUser = null)
    {
        _cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
        _authUserIndex = authUserIndex;
        _onBehalfOfUser = onBehalfOfUser;
    }

    /// <summary>Where the cookies came from, for a non-secret status line (never the values).</summary>
    public string SourceDescription { get; private init; } = "none";

    /// <summary>Number of cookie pairs parsed (count only — never the names or values).</summary>
    public int CookieCount => _cookies.Count;

    /// <summary>True when the parsed cookies can resolve a SAPISID value (value itself is hidden).</summary>
    public bool CanResolveSapisid => CookieSapisidResolver.TryResolve(_cookies, out _);

    public Task<CookieSnapshot> GetCookiesAsync(string origin, CancellationToken ct = default)
        => Task.FromResult(new CookieSnapshot(origin, _cookies, _authUserIndex, _onBehalfOfUser));

    /// <summary>
    /// Builds a cookie source from the runtime environment. Precedence: an explicit
    /// <paramref name="cookiePath"/> file wins over the <c>KASET_COOKIE</c> environment variable.
    /// When neither supplies a value the source is empty (unauthenticated, public endpoints only).
    /// </summary>
    public static CliCookieSource FromRuntime(string? cookiePath, int? authUserIndex, string? onBehalfOfUser)
    {
        string? raw = null;
        string source = "none";

        if (!string.IsNullOrWhiteSpace(cookiePath))
        {
            // Read only from the caller-supplied path; the contents are treated as a secret.
            raw = File.ReadAllText(cookiePath);
            source = "file (--cookies)";
        }
        else
        {
            var env = Environment.GetEnvironmentVariable("KASET_COOKIE");
            if (!string.IsNullOrWhiteSpace(env))
            {
                raw = env;
                source = "env (KASET_COOKIE)";
            }
        }

        var cookies = ParseCookieHeader(raw);
        return new CliCookieSource(cookies, authUserIndex, onBehalfOfUser)
        {
            SourceDescription = cookies.Count > 0 ? source : "none",
        };
    }

    /// <summary>
    /// Parses a raw <c>Cookie</c> header (<c>name=value; name2=value2</c>) into neutral
    /// <see cref="CookiePair"/> values. A leading <c>Cookie:</c> label is tolerated. Empty
    /// names/values are skipped. Returns an empty list for null/blank input.
    /// </summary>
    internal static IReadOnlyList<CookiePair> ParseCookieHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return [];
        }

        var trimmed = header.Trim();
        if (trimmed.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Cookie:".Length..].Trim();
        }

        var pairs = new List<CookiePair>();
        foreach (var segment in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var name = segment[..eq].Trim();
            var value = segment[(eq + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            pairs.Add(new CookiePair(name, value));
        }

        return pairs;
    }
}
