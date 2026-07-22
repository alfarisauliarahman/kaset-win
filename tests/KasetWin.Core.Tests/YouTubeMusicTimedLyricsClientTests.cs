using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Errors;
using KasetWin.Core.Services.Api;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// End-to-end tests for <see cref="YTMusicClient.GetYouTubeMusicLyricsAsync"/> — the two-client
/// lyrics lookup (<c>next</c> → <c>MPLYt…</c> → one <c>browse</c> as <c>ANDROID_MUSIC</c> for the
/// timings and one as <c>WEB_REMIX</c> for the text/attribution).
/// <para>
/// The point of testing at the client level rather than at the parser level is that every
/// interesting failure lives HERE: the Android identity going stale does not produce a parse error,
/// it produces a different (or an error) HTTP response, and the rule that matters is that all of
/// those degrade to plain text and never to an exception. The InnerTube endpoint is faked with a
/// stub handler that routes on the request body's <c>clientName</c>, exactly as YouTube does.
/// </para>
/// <para>
/// Payload shapes are the ones captured live on 2026-07-22 via
/// <c>ApiExplorer lyrics &lt;videoId&gt;</c> (see ADR 0005), including the detail that cue times
/// arrive as decimal STRINGS.
/// </para>
/// </summary>
public class YouTubeMusicTimedLyricsClientTests
{
    private const string NextWithLyricsTab = """
    {
      "contents": { "singleColumnMusicWatchNextResultsRenderer": { "tabbedRenderer": {
        "watchNextTabbedResultsRenderer": { "tabs": [
          { "tabRenderer": { "title": "Up next", "selected": true } },
          { "tabRenderer": { "title": "Lyrics",
              "endpoint": { "browseEndpoint": { "browseId": "MPLYt_live_shape" } } } }
        ] } } } }
    }
    """;

    private const string NextWithoutLyricsTab = """
    {
      "contents": { "singleColumnMusicWatchNextResultsRenderer": { "tabbedRenderer": {
        "watchNextTabbedResultsRenderer": { "tabs": [
          { "tabRenderer": { "title": "Up next", "selected": true } },
          { "tabRenderer": { "title": "Lyrics", "unselectable": true } }
        ] } } } }
    }
    """;

    // ANDROID_MUSIC 7.21.50 + Android context. Verbatim field names from the live capture:
    // cueRange = { startTimeMilliseconds, endTimeMilliseconds, metadata: { id } }, all strings.
    // Note there is no footer and no sourceMessage anywhere — the timed shape carries NO attribution.
    private const string AndroidTimedBrowse = """
    {
      "contents": { "elementRenderer": { "newElement": { "type": { "componentType": {
        "model": { "timedLyricsModel": { "lyricsData": { "timedLyricsData": [
          { "lyricLine": "Ayy", "cueRange": { "startTimeMilliseconds": "1980", "endTimeMilliseconds": "3750", "metadata": { "id": "0" } } },
          { "lyricLine": "second line", "cueRange": { "startTimeMilliseconds": "3750", "endTimeMilliseconds": "6000", "metadata": { "id": "1" } } }
        ] } } } } } } } }
    }
    """;

    // The same timed shape for a track that has no synced version at all: lines, zero cueRange.
    private const string AndroidTimedBrowseWithoutCues = """
    {
      "contents": { "elementRenderer": { "newElement": { "type": { "componentType": {
        "model": { "timedLyricsModel": { "lyricsData": { "timedLyricsData": [
          { "lyricLine": "uncued one" },
          { "lyricLine": "uncued two" }
        ] } } } } } } } }
    }
    """;

    private const string WebPlainBrowse = """
    {
      "contents": { "sectionListRenderer": { "contents": [
        { "musicDescriptionShelfRenderer": {
            "description": { "runs": [ { "text": "web line one\nweb line two" } ] },
            "footer": { "runs": [ { "text": "Source: LyricFind" } ] }
        } }
      ] } }
    }
    """;

    [Fact]
    public async Task Timed_lyrics_win_and_borrow_the_attribution_from_the_desktop_browse()
    {
        var recorder = new RequestRecorder();
        var client = ClientFor(recorder, (endpoint, clientName) => (endpoint, clientName) switch
        {
            ("next", _) => Ok(NextWithLyricsTab),
            ("browse", InnerTubeSupport.ClientNameAndroidMusic) => Ok(AndroidTimedBrowse),
            _ => Ok(WebPlainBrowse),
        });

        var lyrics = await client.GetYouTubeMusicLyricsAsync("vid123");

        Assert.NotNull(lyrics);
        Assert.True(lyrics!.HasTimings);
        Assert.Equal(2, lyrics.TimedLines!.Count);
        Assert.Equal(1_980, lyrics.TimedLines[0].TimeInMs);
        Assert.Equal(1_770, lyrics.TimedLines[0].Duration);
        Assert.Equal("Ayy", lyrics.TimedLines[0].Text);

        // The timed payload has no attribution of its own; the licensor credit YouTube requires be
        // shown is carried over from the desktop browse instead of being dropped.
        Assert.Equal("Source: LyricFind", lyrics.Attribution);

        // The identity spoof is confined to the lyrics browse: `next` stays WEB_REMIX.
        Assert.Equal(InnerTubeSupport.ClientNameMusic, recorder.ClientNameFor("next"));
        Assert.Contains(InnerTubeSupport.ClientNameAndroidMusic, recorder.BrowseClientNames);
        Assert.Contains(InnerTubeSupport.ClientNameMusic, recorder.BrowseClientNames);
    }

    [Fact]
    public async Task The_android_browse_carries_the_pinned_version_and_the_android_context()
    {
        // Both variables matter (verified live): 6.33.52 stays plain even WITH the context, and
        // 7.21.50 falls back to the web shape WITHOUT it. If either stops being sent, timings die
        // silently, so the request identity is asserted rather than assumed.
        var recorder = new RequestRecorder();
        var client = ClientFor(recorder, (endpoint, _) => endpoint == "next" ? Ok(NextWithLyricsTab) : Ok(WebPlainBrowse));

        await client.GetYouTubeMusicLyricsAsync("vid123");

        var android = recorder.AndroidBrowseContext;
        Assert.NotNull(android);
        Assert.Equal(InnerTubeSupport.ClientVersionAndroidMusic, android!["clientVersion"]!.GetValue<string>());
        foreach (var pair in InnerTubeSupport.AndroidMusicClientExtras)
        {
            Assert.Equal(pair.Value, android[pair.Key]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task A_rejected_android_client_version_degrades_to_plain_text()
    {
        // The 400/404 shape a stale or fabricated pinned clientVersion produces. It must never
        // escape as an exception, and it must never cost the user the lyrics the desktop has.
        var client = ClientFor(new RequestRecorder(), (endpoint, clientName) => (endpoint, clientName) switch
        {
            ("next", _) => Ok(NextWithLyricsTab),
            ("browse", InnerTubeSupport.ClientNameAndroidMusic) => new HttpResponseMessage(HttpStatusCode.BadRequest),
            _ => Ok(WebPlainBrowse),
        });

        var lyrics = await client.GetYouTubeMusicLyricsAsync("vid123");

        Assert.NotNull(lyrics);
        Assert.False(lyrics!.HasTimings);
        Assert.Equal("web line one\nweb line two", lyrics.Text);
        Assert.Equal("Source: LyricFind", lyrics.Attribution);
    }

    [Fact]
    public async Task A_silent_downgrade_to_the_plain_shape_degrades_to_plain_text()
    {
        // The nastiest failure: HTTP 200, no error, just the web-shaped payload (what an older
        // pinned version such as 6.33.52 returns). Nothing throws — the lyrics are merely untimed.
        var client = ClientFor(new RequestRecorder(), (endpoint, _) =>
            endpoint == "next" ? Ok(NextWithLyricsTab) : Ok(WebPlainBrowse));

        var lyrics = await client.GetYouTubeMusicLyricsAsync("vid123");

        Assert.NotNull(lyrics);
        Assert.False(lyrics!.HasTimings);
        Assert.Equal("Source: LyricFind", lyrics.Attribution);
    }

    [Fact]
    public async Task A_timed_shape_with_no_cue_ranges_is_plain_not_a_broken_synced_result()
    {
        // Verified live (BiQIc7fG9pA): a full timedLyricsData array with zero cueRange. A "Synced"
        // result built from that would render as lyrics that never advance.
        var client = ClientFor(new RequestRecorder(), (endpoint, clientName) => (endpoint, clientName) switch
        {
            ("next", _) => Ok(NextWithLyricsTab),
            ("browse", InnerTubeSupport.ClientNameAndroidMusic) => Ok(AndroidTimedBrowseWithoutCues),
            _ => Ok(WebPlainBrowse),
        });

        var lyrics = await client.GetYouTubeMusicLyricsAsync("vid123");

        Assert.NotNull(lyrics);
        Assert.False(lyrics!.HasTimings);
        Assert.False(lyrics.IsEmpty);
    }

    [Fact]
    public async Task An_uncued_timed_shape_still_yields_text_when_the_desktop_browse_also_fails()
    {
        // Last line of defence: both the timings and the desktop browse are unavailable, but the
        // Android payload still carried readable lines. Plain text beats nothing.
        var client = ClientFor(new RequestRecorder(), (endpoint, clientName) => (endpoint, clientName) switch
        {
            ("next", _) => Ok(NextWithLyricsTab),
            ("browse", InnerTubeSupport.ClientNameAndroidMusic) => Ok(AndroidTimedBrowseWithoutCues),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });

        var lyrics = await client.GetYouTubeMusicLyricsAsync("vid123");

        Assert.NotNull(lyrics);
        Assert.False(lyrics!.HasTimings);
        Assert.Equal("uncued one\nuncued two", lyrics.Text);
    }

    [Fact]
    public async Task Both_browses_failing_yields_null_rather_than_throwing()
    {
        var client = ClientFor(new RequestRecorder(), (endpoint, _) => endpoint == "next"
            ? Ok(NextWithLyricsTab)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetYouTubeMusicLyricsAsync("vid123"));
    }

    [Fact]
    public async Task A_track_without_a_lyrics_tab_costs_exactly_one_request()
    {
        var recorder = new RequestRecorder();
        var client = ClientFor(recorder, (_, _) => Ok(NextWithoutLyricsTab));

        Assert.Null(await client.GetYouTubeMusicLyricsAsync("vid123"));
        Assert.Equal(1, recorder.Count);
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A client whose transport answers from <paramref name="respond"/>, keyed on the InnerTube
    /// endpoint and the <c>clientName</c> in the request body — the same two things the real
    /// service routes on.
    /// </summary>
    private static YTMusicClient ClientFor(
        RequestRecorder recorder,
        Func<string, string, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(request =>
        {
            var endpoint = request.RequestUri!.AbsolutePath.TrimEnd('/');
            endpoint = endpoint[(endpoint.LastIndexOf('/') + 1)..];

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = JsonNode.Parse(body)!;
            var context = payload["context"]!["client"]!.AsObject();
            var clientName = context["clientName"]!.GetValue<string>();

            recorder.Record(endpoint, context);
            return respond(endpoint, clientName);
        });

        return new YTMusicClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://music.youtube.com/") },
            new EmptyCookieSource(),
            new ApiCache(),
            new NoRetryPolicy());
    }

    /// <summary>Captures the client identity each request was issued under.</summary>
    private sealed class RequestRecorder
    {
        private readonly List<(string Endpoint, JsonObject Client)> _requests = [];

        public int Count => _requests.Count;

        public IReadOnlyList<string> BrowseClientNames =>
            _requests.Where(r => r.Endpoint == "browse")
                .Select(r => r.Client["clientName"]!.GetValue<string>())
                .ToList();

        public JsonObject? AndroidBrowseContext =>
            _requests.Find(r => r.Endpoint == "browse"
                && r.Client["clientName"]!.GetValue<string>() == InnerTubeSupport.ClientNameAndroidMusic).Client;

        public void Record(string endpoint, JsonObject client) => _requests.Add((endpoint, client.DeepClone().AsObject()));

        public string? ClientNameFor(string endpoint) =>
            _requests.Find(r => r.Endpoint == endpoint).Client?["clientName"]?.GetValue<string>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    /// <summary>Unauthenticated snapshot — the lyrics surface is public (verified live).</summary>
    private sealed class EmptyCookieSource : ICookieSource
    {
        public Task<CookieSnapshot> GetCookiesAsync(string origin, CancellationToken ct = default)
            => Task.FromResult(CookieSnapshot.Empty(origin));
    }

    /// <summary>Runs the operation exactly once: these tests assert degradation, not backoff.</summary>
    private sealed class NoRetryPolicy : IRetryPolicy
    {
        public Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            Func<Exception, bool> shouldRetry,
            int maxAttempts = 3,
            TimeSpan? initialDelay = null,
            CancellationToken ct = default) => operation();
    }
}
