using KasetWin.App.Composition;
using KasetWin.App.Auth;
using KasetWin.App.Hosting;
using KasetWin.App.Navigation;
using KasetWin.App.Notifications;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Diagnostics;
using KasetWin.Core.Services;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Auth;
using KasetWin.Core.Services.Favorites;
using KasetWin.Core.Services.Lyrics;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Services.Podcasts;
using KasetWin.Core.Services.Settings;
using KasetWin.Platform.Auth;
using KasetWin.Platform.Imaging;
using KasetWin.Platform.Network;
using KasetWin.Platform.Playback;
using KasetWin.Platform.Security;
using KasetWin.Platform.Settings;
using KasetWin.Platform.Smtc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace KasetWin.App.Composition;

/// <summary>
/// Generic Host + DI composition root (Task 1.3, Req 23.1). Builds the application-wide
/// <see cref="IHost"/>, wiring Kaset logging (Serilog + secret redaction) and the services that
/// already exist in <c>KasetWin.Core</c> / <c>KasetWin.Platform</c>.
/// </summary>
/// <remarks>
/// Registrations are added incrementally as services land (tasks 12.1, 13.x, 28.1). Types
/// that do not exist yet are intentionally <em>not</em> registered and are called out with
/// <c>TODO</c> markers below so the App keeps building while those tasks run in parallel. The
/// real <see cref="WebView2PlaybackController"/> backs both <see cref="IPlaybackController"/> and
/// <see cref="IJsBridge"/>; the App-owned <see cref="PlaybackWebViewHost"/> attaches its hidden
/// WebView2 to that controller once the shell mounts it (Req 1.1).
/// </remarks>
internal static class AppHost
{
    /// <summary>Builds the configured (but not yet started) application host.</summary>
    public static IHost Build() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(static logging =>
            {
                // Serilog pipeline with mandatory secret redaction (Req 21.3/22.3); a daily
                // rolling file under %LOCALAPPDATA%\Kaset\logs plus the debug sink.
                logging.AddKasetLogging(new KasetLoggingOptions
                {
                    MinimumLevel = KasetLogLevel.Info,
                    FilePath = DefaultLogFilePath(),
                    WriteToDebug = true,
                });
            })
            .ConfigureServices(static (_, services) => ConfigureServices(services))
            .Build();

    /// <summary>Registers every service currently available for dependency injection.</summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Shared infrastructure ────────────────────────────────────────────────────────
        // System clock seam consumed by ApiCache / YTMusicClient (deterministic in tests).
        services.AddSingleton(TimeProvider.System);

        // ── Core: API cache + retry (tasks 4.1 / 4.2) ────────────────────────────────────
        services.AddSingleton<IApiCache, ApiCache>();
        services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();

        // ── Platform: secret storage (task 9.1, Req 22.1) ────────────────────────────────
        services.AddSingleton<ICredentialStore, DpapiCredentialStore>();

        // ── Playback controller + JS bridge (task 8.2 / 8.3) ─────────────────────────────
        // The real WebView2 playback controller (KasetWin.Platform). One instance backs BOTH
        // IPlaybackController (control surface for PlayerService) and IJsBridge (observer events).
        // No factory is passed: the App host (PlaybackWebViewHost, task 8.3) owns the XAML
        // WebView2 element and calls AttachAsync once its CoreWebView2 is ready (Req 1.1).
        services.AddSingleton<WebView2PlaybackController>(static sp => new WebView2PlaybackController(
            coreWebViewFactory: null,
            logger: sp.GetService<ILogger<WebView2PlaybackController>>()));
        services.AddSingleton<IPlaybackController>(static sp => sp.GetRequiredService<WebView2PlaybackController>());
        services.AddSingleton<IJsBridge>(static sp => sp.GetRequiredService<WebView2PlaybackController>());

        // App-layer host that owns the single hidden WebView2 element for its whole lifetime
        // (background audio, Req 1.4). Resolved on the UI thread by the shell (MainWindow), which
        // mounts host.Element into its visual tree and calls InitializeAsync().
        // Shared WebView2 environment (extensions enabled + shared session profile) and the
        // user-managed extensions loader (uBlock etc., ADR 0014 equivalent).
        services.AddSingleton<WebViewEnvironmentProvider>();
        services.AddSingleton<ExtensionsService>();
        services.AddSingleton<PlaybackWebViewHost>();

        // ── App: floating video window controller (task 19.1, Req 26.2–26.4) ─────────────
        // Reparents the single hidden playback element between the shell's hidden mount and a
        // floating VideoWindow — moving (never recreating) the WebView2 so playback continues. The
        // shell registers its hidden mount via AttachHomeMount; the PlayerBar toggles pop-out and
        // gates it on VideoAvailability (Req 26.1).
        services.AddSingleton<VideoWindowController>();

        // ── Platform: cookie source (task 9.1 / 9.3, Req 3.3) ────────────────────────────
        // WebView2CookieSource reads cookies from a CoreWebView2. The provider prefers the core
        // published by the interactive LoginDialog (CoreWebView2Accessor, task 9.3) so that during
        // sign-in cookies are read from the page the user is actually authenticating on; otherwise
        // it falls back to the playback controller's core (the YouTube Music player WebView, task
        // 8.3). Because every in-app WebView2 shares one user-data folder (cookie store), both
        // cores observe the same session. It returns null until a core exists, in which case the
        // source yields an empty (signed-out) snapshot instead of throwing, and public endpoints
        // still work.
        services.AddSingleton<CoreWebView2Accessor>();
        services.AddSingleton<Func<CoreWebView2?>>(static sp =>
        {
            var accessor = sp.GetRequiredService<CoreWebView2Accessor>();
            var controller = sp.GetRequiredService<WebView2PlaybackController>();
            return () => accessor.Current ?? controller.CoreWebView2;
        });
        services.AddSingleton<ICookieSource, WebView2CookieSource>();

        // ── Core: YouTube Music client (task 7.1) ────────────────────────────────────────
        // Owns a browser-shaped HttpClient via CreateConfiguredHttpClient (no IHttpClientFactory
        // dependency needed for a single long-lived singleton client).
        services.AddSingleton<IYTMusicClient>(static sp => new YTMusicClient(
            YTMusicClient.CreateConfiguredHttpClient(),
            sp.GetRequiredService<ICookieSource>(),
            sp.GetRequiredService<IApiCache>(),
            sp.GetRequiredService<IRetryPolicy>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<YTMusicClient>>()));

        // ── Core: auth state machine (task 9.2) ──────────────────────────────────────────
        services.AddSingleton<IAuthService, AuthService>();

        // ── App: interactive Google sign-in flow (task 9.3, Req 4.2/4.3) ─────────────────
        // The shell resolves ILoginFlow and calls ShowAsync(XamlRoot) when a sign-in is needed
        // (IAuthService.State == LoggedOut or NeedsReauth). Presents the LoginDialog which hosts a
        // visible WebView2, observes the session, and auto-closes once LoggedIn is detected.
        services.AddSingleton<ILoginFlow, LoginFlow>();

        // ── Core: queue source of truth (task 10.1) ──────────────────────────────────────
        services.AddSingleton<IQueueService, QueueService>();

        // ── Core: lyrics (task 6.2) ──────────────────────────────────────────────────────
        // LyricsService consumes every registered ILyricsProvider (IEnumerable<ILyricsProvider>).
        services.AddSingleton<ILyricsProvider>(static sp => new LRCLibProvider(
            new HttpClient(),
            sp.GetService<ILogger<LRCLibProvider>>()));
        services.AddSingleton<ILyricsService, LyricsService>();

        // ── Core: player (task 11.1) ─────────────────────────────────────────────────────
        // Infinite-mix coordinator (task 18.1, Req 25) shares the queue and drives continuation
        // top-ups via the client's GetMixContinuationAsync; injected into the player.
        services.AddSingleton(static sp => new InfiniteMixCoordinator(
            sp.GetRequiredService<IQueueService>(),
            (token, ct) => sp.GetRequiredService<IYTMusicClient>().GetMixContinuationAsync(token, ct)));
        services.AddSingleton<IPlayerService>(static sp => new PlayerService(
            sp.GetRequiredService<IQueueService>(),
            sp.GetRequiredService<IPlaybackController>(),
            sp.GetRequiredService<IJsBridge>(),
            sp.GetRequiredService<InfiniteMixCoordinator>()));

        // ── Core: full YouTube mode — parallel client + parsers (task 25.1, Req 32) ──────
        // Parallel to IYTMusicClient (ADR-0020): own browser-shaped HttpClient, the YouTube origin
        // (https://www.youtube.com) for SAPISIDHASH + headers, and the WEB client context. Shares
        // the cookie source, API cache (yt:-prefixed keys), and retry policy with the music client.
        services.AddSingleton<IYouTubeClient>(static sp => new YouTubeClient(
            YouTubeClient.CreateConfiguredHttpClient(),
            sp.GetRequiredService<ICookieSource>(),
            sp.GetRequiredService<IApiCache>(),
            sp.GetRequiredService<IRetryPolicy>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<YouTubeClient>>()));

        // ── Core: YouTube video player + PlaybackArbiter (task 25.2/25.3, Req 32.2/32.3) ─
        // The video player tracks the playing video and drives the real watch WebView via the
        // IYouTubeWatchController seam. YouTubeWatchController (KasetWin.Platform) loads
        // www.youtube.com/watch?v={id} in a dedicated WebView2 owned by the App-layer
        // YouTubeWatchWebViewHost (mounted on the watch page); it replaces NullYouTubeWatchController.
        // The watch controller's PlaybackStateObserved event is wired into the player so in-page
        // play/pause is reflected into the arbitrated audio source. The arbiter guarantees a single
        // active audio source: starting the video pauses music and vice-versa (Req 32.3). Resolving
        // it at startup begins arbitration; pausing the YouTube source really pauses the WebView2.
        services.AddSingleton<YouTubeWatchController>(static sp => new YouTubeWatchController(
            sp.GetService<ILogger<YouTubeWatchController>>()));
        services.AddSingleton<IYouTubeWatchController>(static sp => sp.GetRequiredService<YouTubeWatchController>());
        services.AddSingleton<YouTubePlayerService>(static sp =>
        {
            var controller = sp.GetRequiredService<YouTubeWatchController>();
            var player = new YouTubePlayerService(controller);
            // Reflect observed in-page play/pause into the arbitrated source (Req 32.3).
            controller.PlaybackStateObserved += (_, isPlaying) => player.ReportPlaying(isPlaying);
            return player;
        });
        services.AddSingleton(static sp => new PlaybackArbiter(
            new MusicAudioSource(sp.GetRequiredService<IPlayerService>()),
            sp.GetRequiredService<YouTubePlayerService>(),
            sp.GetService<ILogger<PlaybackArbiter>>()));

        // ── Core: request coalescing (task 13.x) ─────────────────────────────────────────
        // Shared single-flight used by the image cache (and any future deduped async work).
        services.AddSingleton<ISingleFlight, SingleFlight>();

        // ── Settings persistence (task 13.1, Req 18.x) ───────────────────────────────────
        // Prefer the packaged on-disk store (ApplicationData.LocalSettings). When the process runs
        // unpackaged ApplicationData.Current throws, so fall back to the volatile in-memory store so
        // the app still launches (settings simply won't persist across runs).
        services.AddSingleton<ISettingsStore>(static _ =>
        {
            try
            {
                return new LocalSettingsStore();
            }
            catch
            {
                return new InMemorySettingsStore();
            }
        });
        services.AddSingleton<ISettingsService, SettingsService>();

        // ── Favorites / pinned items (task 22.1, Req 29) ─────────────────────────────────
        // Persists the ordered favorites list through the same ISettingsStore as preferences; the
        // Home surface shows the Favorites shelf when the list is non-empty (Req 29.4).
        services.AddSingleton<IFavoritesService, FavoritesService>();

        // ── Podcasts: per-episode progress persistence (task 20.1, Req 27.3) ─────────────
        // Persists episode playback progress + played state through the same ISettingsStore as
        // preferences; the Podcasts page records progress when an episode is played.
        services.AddSingleton<IEpisodeProgressStore, EpisodeProgressStore>();

        // ── Imaging: decoder → cache → accent color (task 13.3, Req 16.2/16.3) ───────────
        services.AddSingleton<IImageDecoder, WinRTImageDecoder>();
        services.AddSingleton<IImageCache>(static sp => new ImageCache(
            new HttpClient(),
            sp.GetRequiredService<IImageDecoder>(),
            options: null,
            singleFlight: sp.GetRequiredService<ISingleFlight>(),
            logger: sp.GetService<ILogger<ImageCache>>()));
        services.AddSingleton<IColorExtractor, ColorExtractor>();

        // ── Platform: connectivity (task 13.4, Req 35.2) ─────────────────────────────────
        services.AddSingleton<INetworkMonitor, NetworkMonitor>();

        // ── Platform: system Now Playing / SMTC (task 12.1, Req 10) ──────────────────────
        services.AddSingleton<INowPlayingController, SmtcController>();

        // ── App: shell navigation (task 14.2, Req 16.1) ──────────────────────────────────
        services.AddSingleton<INavigationService, NavigationService>();

        // ── App: track-change toasts (task 28.1, Req 35.1) ───────────────────────────────
        // Backed by the Windows App SDK AppNotificationManager; observes IPlayerService and shows a
        // now-playing toast (title = song title, body = artist) on each track change. Registered in
        // App (not Platform) because the toast APIs ship with the Windows App SDK. The shell starts
        // it once the window is live (MainWindow.StartBackgroundControllers).
        services.AddSingleton<INotificationService, ToastNotificationService>();

        // ── ViewModels (transient, resolved from App.Services) ───────────────────────────
        // TODO (task 14.x): register ViewModels here as the UI pages land, e.g.
        // services.AddTransient<HomeViewModel>();
    }

    /// <summary>Daily-rolling log file under the per-user local app data folder.</summary>
    private static string DefaultLogFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Kaset", "logs", "kaset-.log");
    }
}
