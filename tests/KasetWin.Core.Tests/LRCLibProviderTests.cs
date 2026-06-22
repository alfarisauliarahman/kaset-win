using System.Net;
using System.Text;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Lyrics;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LRCLibProvider"/> (Task 6.2, Req 17.1). The LRCLib HTTP API is
/// faked through a stubbed <see cref="HttpMessageHandler"/> so no network access is required.
/// </summary>
public class LRCLibProviderTests
{
    private static readonly LyricsSearchInfo Info = new(
        Title: "Test Song",
        Artist: "Test Artist",
        Album: "Test Album",
        Duration: TimeSpan.FromSeconds(200),
        VideoId: "vid123");

    private static HttpClient ClientReturning(HttpStatusCode status, string? body, Action<HttpRequestMessage>? capture = null)
    {
        var handler = new StubHandler((req, _) =>
        {
            capture?.Invoke(req);
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return response;
        });

        return new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net/") };
    }

    [Fact]
    public async Task SearchAsync_maps_synced_lyrics_to_Synced()
    {
        const string json = """
            { "instrumental": false, "plainLyrics": "plain text", "syncedLyrics": "[00:01.00] hello\n[00:02.00] world" }
            """;
        var provider = new LRCLibProvider(ClientReturning(HttpStatusCode.OK, json));

        var result = await provider.SearchAsync(Info);

        var synced = Assert.IsType<LyricResult.Synced>(result);
        Assert.Equal(2, synced.Lyrics.Lines.Count);
        Assert.Equal(LRCLibProvider.ProviderName, synced.Lyrics.Source);
        Assert.Equal(1_000, synced.Lyrics.Lines[0].TimeInMs);
    }

    [Fact]
    public async Task SearchAsync_falls_back_to_plain_when_no_synced()
    {
        const string json = """
            { "instrumental": false, "plainLyrics": "just plain", "syncedLyrics": null }
            """;
        var provider = new LRCLibProvider(ClientReturning(HttpStatusCode.OK, json));

        var result = await provider.SearchAsync(Info);

        var plain = Assert.IsType<LyricResult.Plain>(result);
        Assert.Equal("just plain", plain.Lyrics.Text);
        Assert.Equal(LRCLibProvider.ProviderName, plain.Lyrics.Source);
    }

    [Fact]
    public async Task SearchAsync_instrumental_is_unavailable()
    {
        const string json = """
            { "instrumental": true, "plainLyrics": null, "syncedLyrics": null }
            """;
        var provider = new LRCLibProvider(ClientReturning(HttpStatusCode.OK, json));

        var result = await provider.SearchAsync(Info);

        // Exact get is unavailable -> falls back to search (also 200 instrumental) -> unavailable.
        Assert.IsType<LyricResult.Unavailable>(result);
    }

    [Fact]
    public async Task SearchAsync_404_on_both_endpoints_is_unavailable()
    {
        var provider = new LRCLibProvider(ClientReturning(HttpStatusCode.NotFound, null));

        var result = await provider.SearchAsync(Info);

        Assert.IsType<LyricResult.Unavailable>(result);
    }

    [Fact]
    public async Task SearchAsync_sends_title_artist_and_no_auth_headers()
    {
        HttpRequestMessage? captured = null;
        var provider = new LRCLibProvider(
            ClientReturning(HttpStatusCode.NotFound, null, req => captured ??= req));

        await provider.SearchAsync(Info);

        Assert.NotNull(captured);
        var query = captured!.RequestUri!.Query;
        Assert.Contains("track_name=Test%20Song", query, StringComparison.Ordinal);
        Assert.Contains("artist_name=Test%20Artist", query, StringComparison.Ordinal);
        // No credentials/cookies are ever attached (Req: no secrets, public API).
        Assert.False(captured.Headers.Contains("Authorization"));
        Assert.False(captured.Headers.Contains("Cookie"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request, cancellationToken));
    }
}
