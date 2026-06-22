using System.Globalization;
using System.Runtime.InteropServices;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace KasetWin.Platform.Playback;

/// <summary>
/// WinRT adapter that drives regular-YouTube (video) playback through a dedicated watch
/// <see cref="CoreWebView2"/> that loads <c>www.youtube.com/watch?v={id}</c> (Req 32.2). It is the
/// real implementation of <see cref="IYouTubeWatchController"/> that replaces
/// <see cref="NullYouTubeWatchController"/>; parallel to <see cref="WebView2PlaybackController"/>
/// (music) by design (ADR-0020), because the watch-page DOM and observer script differ from the
/// music player.
/// </summary>
/// <remarks>
/// <para>
/// Creating a <see cref="CoreWebView2"/> requires a live UI element, so this controller does
/// <b>not</b> create the WebView2 itself: the App-layer <c>YouTubeWatchWebViewHost</c> owns the
/// XAML <c>WebView2</c> element on the watch page and calls <see cref="AttachAsync"/> once its core
/// is ready, and <see cref="ReleaseAsync"/> when the page is navigated away. Unlike the always-on
/// hidden music WebView2, the watch surface is created and torn down with the watch page, so
/// <see cref="AttachAsync"/> tolerates re-attaching a fresh core after a release.
/// </para>
/// <para>
/// The injected <c>youtubeWatch.js</c> observer is the only channel from the (untrusted) page back
/// to native; every <c>WebMessageReceived</c> payload is shape-validated by the pure, headless
/// <see cref="YouTubeWatchMessageParser"/> before <see cref="PlaybackStateObserved"/> is raised.
/// <see cref="YouTubePlayerService"/> subscribes to that event so in-page play/pause is reflected
/// into the arbitrated audio source (Req 32.3). Cookies / tokens are never logged.
/// </para>
/// </remarks>
public sealed class YouTubeWatchController : IYouTubeWatchController, IAsyncDisposable
{
    /// <summary>Regular-YouTube watch page origin used for video playback (Req 32.2).</summary>
    public const string WatchUrlFormat = "https://www.youtube.com/watch?v={0}";

    /// <summary>Browser-style user agent so YouTube does not warn about an unsupported browser.</summary>
    public const string BrowserUserAgent = WebView2PlaybackController.BrowserUserAgent;

    private const string ObserverResourceName = "KasetWin.Platform.Playback.Scripts.youtubeWatch.js";

    private const string VideoSelector = "document.querySelector('#movie_player video') || document.querySelector('video')";

    private readonly ILogger<YouTubeWatchController> _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private CoreWebView2? _core;
    private SynchronizationContext? _uiContext;
    private string? _currentVideoId;
    private string? _pendingVideoId;
    private bool _disposed;

    /// <summary>Creates the controller. The App host supplies the core via <see cref="AttachAsync"/>.</summary>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public YouTubeWatchController(ILogger<YouTubeWatchController>? logger = null)
    {
        _logger = logger ?? NullLogger<YouTubeWatchController>.Instance;
    }

    /// <summary>
    /// Raised when the watch-page observer reports a play-state change (Req 32.2/32.3).
    /// <see cref="YouTubePlayerService"/> subscribes so in-page play/pause is reflected into the
    /// arbitrated audio source. The boolean payload is whether the video is currently playing.
    /// </summary>
    public event EventHandler<bool>? PlaybackStateObserved;

    /// <inheritdoc />
    public string? CurrentVideoId => _currentVideoId;

    /// <summary>The live watch <see cref="CoreWebView2"/> once attached, or <see langword="null"/>.</summary>
    public CoreWebView2? CoreWebView2 => _core;

    /// <summary>
    /// Connects the controller to a host-owned watch <see cref="CoreWebView2"/>. Idempotent for the
    /// same core. Because the watch surface is recreated per page, attaching a <em>different</em>
    /// core after a previous one detaches the old wiring and connects the new core (rather than
    /// throwing). Wires the observer script, the message channel, and the user agent.
    /// </summary>
    /// <param name="core">The ready core created and owned by the App watch host.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AttachAsync(CoreWebView2 core, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(core);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initGate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            if (ReferenceEquals(_core, core))
            {
                return;
            }

            if (_core is not null)
            {
                // A stale core from a previous page instance — drop its wiring before re-attaching.
                _core.WebMessageReceived -= OnWebMessageReceived;
                _core.NavigationCompleted -= OnNavigationCompleted;
            }

            await ConfigureCoreAsync(core).ConfigureAwait(true);
            _core = core;
            _logger.LogInformation("YouTube watch WebView2 attached and observer script installed.");

            // If a video was requested before the core was ready, load it now (Req 32.2).
            if (_pendingVideoId is { } pending)
            {
                _pendingVideoId = null;
                await NavigateToVideoAsync(core, pending).ConfigureAwait(true);
            }
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task LoadVideoAsync(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Idempotent: re-loading the currently loaded video is a no-op (mirrors macOS loadVideo).
        if (string.Equals(_currentVideoId, videoId, StringComparison.Ordinal))
        {
            return;
        }

        _currentVideoId = videoId;

        var core = _core;
        if (core is null)
        {
            // The watch host has not attached its core yet; remember the request and load on attach.
            _pendingVideoId = videoId;
            _logger.LogInformation("YouTube watch video requested before the WebView2 was ready; deferring load.");
            return;
        }

        await NavigateToVideoAsync(core, videoId).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task PlayAsync() =>
        ExecuteVideoScriptAsync($"(function(){{var v={VideoSelector};if(v&&v.paused){{v.play();}}}})()");

    /// <inheritdoc />
    public Task PauseAsync() =>
        ExecuteVideoScriptAsync($"(function(){{var v={VideoSelector};if(v&&!v.paused){{v.pause();}}}})()");

    /// <summary>Seeks the loaded video to <paramref name="positionSeconds"/> (best-effort).</summary>
    public Task SeekAsync(double positionSeconds)
    {
        var safe = double.IsFinite(positionSeconds) && positionSeconds > 0 ? positionSeconds : 0;
        var literal = safe.ToString("0.###", CultureInfo.InvariantCulture);
        return ExecuteVideoScriptAsync(
            $"(function(){{var v={VideoSelector};if(v){{v.currentTime={literal};}}}})()");
    }

    /// <inheritdoc />
    public async Task ReleaseAsync()
    {
        _pendingVideoId = null;

        var core = _core;
        if (core is null)
        {
            _currentVideoId = null;
            return;
        }

        try
        {
            await InvokeOnUiAsync(async () =>
            {
                await core.ExecuteScriptAsync($"(function(){{var v={VideoSelector};if(v){{v.pause();}}}})()");
                core.Navigate("about:blank");
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The host may already be tearing the WebView2 down; releasing is best-effort.
            _logger.LogDebug("YouTube watch WebView2 release encountered a benign teardown error.");
        }

        core.WebMessageReceived -= OnWebMessageReceived;
        core.NavigationCompleted -= OnNavigationCompleted;
        _core = null;
        _currentVideoId = null;

        // The surface is gone — reflect "not playing" so the arbiter's audio state stays consistent.
        PlaybackStateObserved?.Invoke(this, false);
        _logger.LogInformation("YouTube watch WebView2 released.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ReleaseAsync().ConfigureAwait(true);
        _initGate.Dispose();
    }

    // ---- internal wiring -------------------------------------------------------------------

    private async Task ConfigureCoreAsync(CoreWebView2 core)
    {
        _uiContext = SynchronizationContext.Current;

        core.Settings.UserAgent = BrowserUserAgent;
        core.Settings.IsWebMessageEnabled = true; // required for window.chrome.webview.postMessage
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.WebMessageReceived -= OnWebMessageReceived;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.NavigationCompleted += OnNavigationCompleted;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(LoadScript(ObserverResourceName));
    }

    private async Task NavigateToVideoAsync(CoreWebView2 core, string videoId)
    {
        var url = string.Format(CultureInfo.InvariantCulture, WatchUrlFormat, Uri.EscapeDataString(videoId));
        await InvokeOnUiAsync(async () =>
        {
            // Pause the current element before navigating to the new video (mirrors macOS).
            await core.ExecuteScriptAsync($"(function(){{var v={VideoSelector};if(v){{v.pause();}}}})()");
            core.Navigate(url);
        }).ConfigureAwait(true);

        _logger.LogInformation("Loading YouTube watch page for a new video.");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // Re-run the video-surface extraction for cached/fast loads where document-start injection
        // ran before the player DOM existed (mirrors the macOS didFinish hook).
        var core = _core;
        if (core is null)
        {
            return;
        }

        _ = InvokeOnUiAsync(async () =>
        {
            try
            {
                await core.ExecuteScriptAsync(
                    "if (typeof window.__kasetExtractVideo === 'function') { window.__kasetExtractVideo(); }");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // Best-effort extraction; never surface a teardown race as an error.
            }
        });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Page content is UNTRUSTED. Read the raw payload, then delegate shape validation to the
        // pure, headless-testable Core parser (Req 32.2).
        string json;
        try
        {
            json = e.WebMessageAsJson;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return;
        }

        var message = YouTubeWatchMessageParser.Parse(json);
        switch (message.Kind)
        {
            case YouTubeWatchMessageKind.StateUpdate:
                PlaybackStateObserved?.Invoke(this, message.IsPlaying);
                break;

            case YouTubeWatchMessageKind.VideoEnded:
                PlaybackStateObserved?.Invoke(this, false);
                break;

            default:
                break; // Ignored / malformed — nothing to do.
        }
    }

    private Task ExecuteVideoScriptAsync(string script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var core = _core;
        if (core is null)
        {
            // No surface yet (e.g. play requested before attach) — the page autoplays on load.
            return Task.CompletedTask;
        }

        return InvokeOnUiAsync(async () =>
        {
            try
            {
                await core.ExecuteScriptAsync(script);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // The watch WebView2 may be tearing down (navigating away); ignore benign races.
            }
        });
    }

    private static string LoadScript(string resourceName)
    {
        var assembly = typeof(YouTubeWatchController).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded watch script '{resourceName}' was not found. Check the .csproj EmbeddedResource items.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private Task InvokeOnUiAsync(Func<Task> action)
    {
        var context = _uiContext;
        if (context is null || context == SynchronizationContext.Current)
        {
            return action();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            },
            null);
        return tcs.Task;
    }
}
