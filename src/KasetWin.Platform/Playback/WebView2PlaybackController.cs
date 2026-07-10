using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Diagnostics;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace KasetWin.Platform.Playback;

/// <summary>
/// WinRT adapter that drives DRM playback through a single hidden <see cref="CoreWebView2"/>
/// and bridges the injected JavaScript observer back to the native player. Implements both
/// <see cref="IPlaybackController"/> (native â†’ web control) and <see cref="IJsBridge"/>
/// (web â†’ native events) (Req 1, 2, 7).
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
    private const string EqualizerResourceName = "KasetWin.Platform.Playback.Scripts.equalizer.js";

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
    private bool _eqEnabled;
    private int[] _eqGains = new int[9];
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
            await core.ExecuteScriptAsync(BuildPlaybackPreferencesScript());
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
    public Task SkipToNextAsync() =>
        ExecuteVideoScriptAsync(
            "(function(){" +
            "var b=document.querySelector('.next-button.ytmusic-player-bar');" +
            "if(b){b.click();return 'clicked';}" +
            "var p=document.querySelector('ytmusic-player');" +
            "if(p&&p.playerApi&&p.playerApi.nextVideo){p.playerApi.nextVideo();return 'api';}" +
            "return 'no-button';" +
            "})()");

    /// <inheritdoc />
    public Task SkipToPreviousAsync() =>
        ExecuteVideoScriptAsync(
            "(function(){" +
            "var b=document.querySelector('.previous-button.ytmusic-player-bar');" +
            "if(b){b.click();return 'clicked';}" +
            "var p=document.querySelector('ytmusic-player');" +
            "if(p&&p.playerApi&&p.playerApi.previousVideo){p.playerApi.previousVideo();return 'api';}" +
            "return 'no-button';" +
            "})()");

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
        return ApplyPlaybackPreferencesAsync();
    }

    /// <inheritdoc />
    public Task SetMutedAsync(bool muted)
    {
        _isMuted = muted;
        return ApplyPlaybackPreferencesAsync();
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
    public Task SetEqualizerAsync(bool enabled, IReadOnlyList<int> gainsDb)
    {
        _eqEnabled = enabled;
        var gains = new int[9];
        if (gainsDb is not null)
        {
            for (var i = 0; i < gains.Length && i < gainsDb.Count; i++)
            {
                gains[i] = Math.Clamp(gainsDb[i], -12, 12);
            }
        }

        _eqGains = gains;
        return ApplyEqualizerAsync();
    }

    private Task ApplyEqualizerAsync()
    {
        if (_core is null)
        {
            return Task.CompletedTask; // Applied on next attach/navigation instead.
        }

        var gainsLiteral = JsonSerializer.Serialize(_eqGains);
        var enabledLiteral = _eqEnabled ? "true" : "false";
        return ExecuteVideoScriptAsync(
            $"(function(){{if(typeof window.__kasetSetEq==='function'){{window.__kasetSetEq({enabledLiteral},{gainsLiteral});}}}})()");
    }

    /// <summary>The user's chosen playback rate; re-applied after navigations (1 = normal).</summary>
    private double _playbackRate = 1.0;

    /// <inheritdoc />
    public Task SetPlaybackRateAsync(double rate)
    {
        _playbackRate = Math.Clamp(rate, 0.25, 3.0);
        return ApplyPlaybackRateAsync();
    }

    private Task ApplyPlaybackRateAsync()
    {
        var literal = _playbackRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ExecuteVideoScriptAsync(
            $"(function(){{var v=document.querySelector('video');if(v){{v.playbackRate={literal};}}}})()");
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
        core.NavigationCompleted -= OnNavigationCompleted;
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
        core.NavigationCompleted -= OnNavigationCompleted;
        core.NavigationCompleted += OnNavigationCompleted;

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
        await core.AddScriptToExecuteOnDocumentCreatedAsync(LoadScript(EqualizerResourceName));
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            await ApplyPlaybackPreferencesAsync().ConfigureAwait(true);

            // The doc-created EQ script re-installs fresh on each navigation; re-push the current
            // bands so an enabled equalizer survives page/track changes.
            if (_eqEnabled)
            {
                await ApplyEqualizerAsync().ConfigureAwait(true);
            }

            // A new watch page resets the media element's playbackRate; restore the user's choice.
            if (Math.Abs(_playbackRate - 1.0) > 0.001)
            {
                await ApplyPlaybackRateAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.LogDebug("Playback preference re-apply after navigation failed benignly.");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Page content is UNTRUSTED. Read the raw payload here, then delegate shape validation to
        // the pure, headless-testable Core parser (Req 2 / Req 1.7).
        string json;
        try
        {
            json = e.WebMessageAsJson;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return;
        }

        var message = PlaybackMessageParser.Parse(json);

        switch (message.Kind)
        {
            case PlaybackMessageKind.DrmStatus when message.DrmAvailable is { } available:
                if (_isDrmAvailable != available)
                {
                    _isDrmAvailable = available;
                    _logger.LogInformation("Widevine DRM availability reported as {Available}.", available);
                }

                break;

            // Raise events for the strongly-typed messages. Handler exceptions are intentionally not
            // caught here so they are not mistaken for parse errors.
            case PlaybackMessageKind.StateUpdate when message.State is not null:
                StateUpdated?.Invoke(this, message.State);
                break;

            case PlaybackMessageKind.TrackEnded when message.TrackEndedMessage is not null:
                TrackEnded?.Invoke(this, message.TrackEndedMessage);
                break;

            default:
                break; // Ignored / malformed â€” nothing to do.
        }
    }

    private CoreWebView2 RequireCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_core is null)
        {
        }

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

    private Task ApplyPlaybackPreferencesAsync() => ExecuteVideoScriptAsync(BuildPlaybackPreferencesScript());

    private string BuildPlaybackPreferencesScript()
    {
        var volume = VolumeToUnitLiteral(_targetVolume);
        var muted = _isMuted ? "true" : "false";
        var ytVolume = Math.Clamp(_targetVolume, 0, 100).ToString(CultureInfo.InvariantCulture);

        return
            "(function(){" +
            $"window.__kasetTargetVolume={volume};" +
            $"window.__kasetMuted={muted};" +
            "var v=document.querySelector('video');" +
            "if(v){" +
            $"v.volume={volume};" +
            $"v.muted={muted};" +
            "}" +
            "var p=document.querySelector('ytmusic-player');" +
            $"if(p&&p.playerApi&&p.playerApi.setVolume){{p.playerApi.setVolume({ytVolume});}}" +
            "var mp=document.getElementById('movie_player');" +
            $"if(mp&&mp.setVolume){{mp.setVolume({ytVolume});}}" +
            "})()";
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
            // Already on the WebView2 thread (or no context captured) â€” run inline.
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
