using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Diagnostics;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace KasetWin.Platform.Playback;

/// <summary>
/// WinRT adapter that drives DRM playback through a single hidden <see cref="CoreWebView2"/>
/// and bridges the injected JavaScript observer back to the native player. Implements both
/// <see cref="IPlaybackController"/> (native → web control) and <see cref="IJsBridge"/>
/// (web → native events) (Req 1, 2, 7).
/// </summary>
/// <remarks>
/// <para>
/// Creating a <see cref="CoreWebView2"/> requires a UI element / environment, so this
/// controller does <b>not</b> create the WebView2 in its constructor (DI-friendly). Instead the
/// App-layer <c>PlaybackWebViewHost</c> (task 8.3) owns the XAML <c>WebView2</c> element and,
/// once its <see cref="CoreWebView2"/> is ready, calls <see cref="AttachAsync"/>. Alternatively,
/// an environment factory may be supplied to the constructor so <see cref="EnsureInitializedAsync"/>
/// can create the core itself (used by smoke tests / headless hosts).
/// </para>
/// <para>
/// The single core lives for the lifetime of the app (Req 1.1). All script execution is
/// marshalled to the UI thread captured at attach time, because WebView2 is single-threaded.
/// Web page content is untrusted: every <c>WebMessageReceived</c> payload is shape-validated
/// before it is mapped to a strongly-typed event. Cookies / tokens are never logged.
/// </para>
/// </remarks>
public sealed class WebView2PlaybackController : IPlaybackController, IJsBridge, IAsyncDisposable
{
    /// <summary>Watch page origin used for playback (Req 1.2).</summary>
    public const string WatchUrlFormat = "https://music.youtube.com/watch?v={0}";

    /// <summary>Browser-style user agent so YouTube Music does not warn about an unsupported browser.</summary>
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";

    private const string ObserverResourceName = "KasetWin.Platform.Playback.Scripts.observer.js";
    private const string AudioQualityResourceName = "KasetWin.Platform.Playback.Scripts.audioQuality.js";

    private readonly Func<CancellationToken, Task<CoreWebView2>>? _coreWebViewFactory;
    private readonly ILogger<WebView2PlaybackController> _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private CoreWebView2? _core;
    private SynchronizationContext? _uiContext;
    private bool _isDrmAvailable = true; // Evergreen runtime ships Widevine; probe may flip to false (Req 1.7).
    private string? _currentVideoId;
    private int _targetVolume = 100; // 0..100
    private bool _isMuted;
    private string? _pendingAudioQualityValue;
    private PlaybackDisplayMode _displayMode = PlaybackDisplayMode.Hidden;
    private bool _disposed;

    /// <summary>
    /// Creates the controller. Pass no factory when the App host will supply the
    /// <see cref="CoreWebView2"/> via <see cref="AttachAsync"/> (the normal path); pass a factory
    /// when <see cref="EnsureInitializedAsync"/> should create the core itself.
    /// </summary>
    /// <param name="coreWebViewFactory">
    /// Optional factory that creates a ready <see cref="CoreWebView2"/>. When <see langword="null"/>,
    /// <see cref="EnsureInitializedAsync"/> waits for <see cref="AttachAsync"/> to be called.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public WebView2PlaybackController(
        Func<CancellationToken, Task<CoreWebView2>>? coreWebViewFactory = null,
        ILogger<WebView2PlaybackController>? logger = null)
    {
        _coreWebViewFactory = coreWebViewFactory;
        _logger = logger ?? NullLogger<WebView2PlaybackController>.Instance;
    }

    /// <inheritdoc />
    public event EventHandler<PlaybackStateMessage>? StateUpdated;

    /// <inheritdoc />
    public event EventHandler<TrackEndedMessage>? TrackEnded;

    /// <summary>
    /// Raised when <see cref="SetDisplayModeAsync"/> changes the desired surface mode so the
    /// App host (task 8.3) can resize / show / hide the owning WebView2 element (Req 26 seam).
    /// </summary>
    public event EventHandler<PlaybackDisplayMode>? DisplayModeChanged;

    /// <inheritdoc />
    public bool IsDrmAvailable => _isDrmAvailable;

    /// <inheritdoc />
    public string? CurrentVideoId => _currentVideoId;

    /// <summary>The current desired display mode (Req 26 seam).</summary>
    public PlaybackDisplayMode DisplayMode => _displayMode;

    /// <summary>
    /// The live <see cref="CoreWebView2"/> once attached, or <see langword="null"/>. Exposed so the
    /// host can wire a <c>WebView2CookieSource</c> provider to the same instance (task 9.1).
    /// </summary>
    public CoreWebView2? CoreWebView2 => _core;

    /// <summary>
    /// Connects the controller to a host-owned <see cref="CoreWebView2"/> (task 8.3). Idempotent:
    /// re-attaching the same core is a no-op; attaching a different core after one is already
    /// connected throws. Wires the observer + audio-quality scripts, the message channel, the user
    /// agent, and (in Debug) dev tools.
    /// </summary>
    /// <param name="core">The ready core created and owned by the App host.</param>
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
                throw new InvalidOperationException(
                    "A CoreWebView2 is already attached; the playback WebView2 is a singleton (Req 1.1).");
            }

            await ConfigureCoreAsync(core).ConfigureAwait(true);
            _core = core;
            _logger.LogInformation("Playback WebView2 attached and observer scripts installed.");
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task EnsureInitializedAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_core is not null)
        {
            return;
        }

        await _initGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_core is not null)
            {
                return;
            }

            if (_coreWebViewFactory is null)
            {
                // The host (task 8.3) is responsible for calling AttachAsync once its XAML-owned
                // WebView2 is ready. Without a factory there is nothing to create here.
                throw new InvalidOperationException(
                    "No CoreWebView2 attached and no factory was supplied. The App host must call AttachAsync (Req 1.1).");
            }

            var core = await _coreWebViewFactory(CancellationToken.None).ConfigureAwait(true);
            await ConfigureCoreAsync(core).ConfigureAwait(true);
            _core = core;
            _logger.LogInformation("Playback WebView2 created via factory and observer scripts installed.");
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
        var core = RequireCore();

        // Idempotent: re-loading the currently loaded video is a no-op (Req 1.2 / Property 6).
        if (string.Equals(_currentVideoId, videoId, StringComparison.Ordinal))
        {
            return;
        }

        // Pause-before-load: pause current audio and prime the target volume before navigating
        // to the new videoId (Req 1.6).
        _currentVideoId = videoId;
        var url = string.Format(CultureInfo.InvariantCulture, WatchUrlFormat, Uri.EscapeDataString(videoId));

        await InvokeOnUiAsync(async () =>
        {
            await core.ExecuteScriptAsync("document.querySelector('video')?.pause()");
            await core.ExecuteScriptAsync(
                $"window.__kasetTargetVolume = {VolumeToUnitLiteral(_targetVolume)};");
            await core.ExecuteScriptAsync(
                $"window.__kasetMuted = {(_isMuted ? "true" : "false")};");
            core.Navigate(url);
        }).ConfigureAwait(true);

        _logger.LogInformation("Loading playback page for a new video.");
    }

    /// <inheritdoc />
    public Task PlayAsync() =>
        ExecuteVideoScriptAsync("document.querySelector('video')?.play()");

    /// <inheritdoc />
    public Task PauseAsync() =>
        ExecuteVideoScriptAsync("document.querySelector('video')?.pause()");

    /// <inheritdoc />
    public Task SeekAsync(double positionSeconds)
    {
        var safe = positionSeconds < 0 ? 0 : positionSeconds;
        var literal = safe.ToString("0.###", CultureInfo.InvariantCulture);
        return ExecuteVideoScriptAsync(
            $"(function(){{var v=document.querySelector('video');if(v){{v.currentTime={literal};}}}})()");
    }

    /// <inheritdoc />
    public Task SetVolumeAsync(int volume0to100)
    {
        _targetVolume = Math.Clamp(volume0to100, 0, 100);
        var unit = VolumeToUnitLiteral(_targetVolume);
        return ExecuteVideoScriptAsync(
            $"(function(){{window.__kasetTargetVolume={unit};var v=document.querySelector('video');if(v){{v.volume={unit};}}}})()");
    }

    /// <inheritdoc />
    public Task SetMutedAsync(bool muted)
    {
        _isMuted = muted;
        var literal = muted ? "true" : "false";
        return ExecuteVideoScriptAsync(
            $"(function(){{window.__kasetMuted={literal};var v=document.querySelector('video');if(v){{v.muted={literal};}}}})()");
    }

    /// <inheritdoc />
    public Task SetAudioQualityAsync(AudioQuality quality)
    {
        // Pure mapping lives in Core so it stays headless-testable (Req 7.1/7.3).
        var ytValue = AudioQualityMap.ToYouTubeValue(quality);
        _pendingAudioQualityValue = ytValue;
        var encoded = JsonSerializer.Serialize(ytValue);
        // Store the preference (honored on next load) and re-apply to the running player (Req 7.2).
        return ExecuteVideoScriptAsync(
            $"(function(){{window.__kasetPlaybackAudioQuality={encoded};" +
            $"if(typeof window.__kasetApplyAudioQuality==='function'){{window.__kasetApplyAudioQuality({encoded});}}}})()");
    }

    /// <inheritdoc />
    public Task SetDisplayModeAsync(PlaybackDisplayMode mode)
    {
        // Seam (Req 26): the controller tracks the desired mode and notifies the host which owns
        // the WebView2 element sizing/visibility. Full Video-mode extraction is a later phase.
        ObjectDisposedException.ThrowIf(_disposed, this);
        _displayMode = mode;
        DisplayModeChanged?.Invoke(this, mode);
        _logger.LogInformation("Playback display mode set to {Mode}.", mode);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ReleaseAsync()
    {
        // Stop audio and release the core when the app quits (Req 1.5).
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
                await core.ExecuteScriptAsync("document.querySelector('video')?.pause()");
                core.Navigate("about:blank");
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The host may already be tearing down the WebView2; releasing is best-effort.
            _logger.LogDebug("Playback WebView2 release encountered a benign teardown error.");
        }

        core.WebMessageReceived -= OnWebMessageReceived;
        _core = null;
        _currentVideoId = null;
        _logger.LogInformation("Playback WebView2 released.");
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
        core.Settings.AreDevToolsEnabled = true;   // inspectable in Debug builds
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.WebMessageReceived -= OnWebMessageReceived;
        core.WebMessageReceived += OnWebMessageReceived;

        // If an audio-quality preference was requested before attach, seed it so it loads with the page.
        var audioQualityScript = LoadScript(AudioQualityResourceName);
        if (_pendingAudioQualityValue is not null)
        {
            var encoded = JsonSerializer.Serialize(_pendingAudioQualityValue);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.__kasetPlaybackAudioQuality={encoded};");
        }

        await core.AddScriptToExecuteOnDocumentCreatedAsync(LoadScript(ObserverResourceName));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(audioQualityScript);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Page content is UNTRUSTED. Parse defensively and validate the message shape before use.
        string json;
        try
        {
            json = e.WebMessageAsJson;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return;
        }

        PlaybackStateMessage? state = null;
        TrackEndedMessage? ended = null;
        bool? drmAvailable = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!doc.RootElement.TryGetProperty("type", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String)
            {
                return;
            }

            switch (typeProp.GetString())
            {
                case "STATE_UPDATE":
                    state = ParseStateUpdate(doc.RootElement);
                    break;
                case "TRACK_ENDED":
                    {
                        var videoId = ReadString(doc.RootElement, "videoId");
                        if (!string.IsNullOrEmpty(videoId))
                        {
                            ended = new TrackEndedMessage(videoId);
                        }

                        break;
                    }

                case "DRM_STATUS":
                    drmAvailable = ReadBool(doc.RootElement, "available");
                    break;
                default:
                    return; // unknown type — ignore
            }
        }
        catch (JsonException)
        {
            // Malformed payload from the page — ignore.
            return;
        }

        if (drmAvailable is { } available)
        {
            if (_isDrmAvailable != available)
            {
                _isDrmAvailable = available;
                _logger.LogInformation("Widevine DRM availability reported as {Available}.", available);
            }
        }

        // Raise events outside the parse/try so handler exceptions are not mistaken for parse errors.
        if (state is not null)
        {
            StateUpdated?.Invoke(this, state);
        }

        if (ended is not null)
        {
            TrackEnded?.Invoke(this, ended);
        }
    }

    private static PlaybackStateMessage ParseStateUpdate(JsonElement root) => new(
        IsPlaying: ReadBool(root, "isPlaying") ?? false,
        Progress: ReadDouble(root, "progress"),
        Duration: ReadDouble(root, "duration"),
        VideoId: ReadString(root, "videoId") ?? string.Empty,
        Title: ReadString(root, "title") ?? string.Empty,
        Artist: ReadString(root, "artist") ?? string.Empty,
        TrackChanged: ReadBool(root, "trackChanged") ?? false,
        HasVideo: ReadBool(root, "hasVideo"),
        VideoType: null);

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool? ReadBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static double ReadDouble(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.Number &&
            p.TryGetDouble(out var value) &&
            double.IsFinite(value))
        {
            return value < 0 ? 0 : value;
        }

        return 0;
    }

    private CoreWebView2 RequireCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _core ?? throw new KasetError(
            KasetErrorKind.PlaybackError,
            "Playback WebView2 is not initialized. Call EnsureInitializedAsync/AttachAsync first (Req 1.1).");
    }

    private Task ExecuteVideoScriptAsync(string script)
    {
        var core = RequireCore();
        return InvokeOnUiAsync(async () =>
        {
            await core.ExecuteScriptAsync(script);
        });
    }

    private static string VolumeToUnitLiteral(int volume0to100) =>
        (Math.Clamp(volume0to100, 0, 100) / 100.0).ToString("0.###", CultureInfo.InvariantCulture);

    private static string LoadScript(string resourceName)
    {
        var assembly = typeof(WebView2PlaybackController).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded playback script '{resourceName}' was not found. Check the .csproj EmbeddedResource items.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private Task InvokeOnUiAsync(Func<Task> action)
    {
        var context = _uiContext;
        if (context is null || context == SynchronizationContext.Current)
        {
            // Already on the WebView2 thread (or no context captured) — run inline.
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
