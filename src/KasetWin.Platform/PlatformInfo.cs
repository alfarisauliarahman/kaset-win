using KasetWin.Core;

namespace KasetWin.Platform;

/// <summary>
/// Placeholder so the freshly scaffolded <c>KasetWin.Platform</c> adapter assembly
/// compiles and verifies its reference to <c>KasetWin.Core</c>. Real adapters
/// (WebView2PlaybackController, SmtcController, DpapiCredentialStore, ...) are
/// implemented in subsequent tasks.
/// </summary>
public static class PlatformInfo
{
    /// <summary>Logical layer name.</summary>
    public const string Layer = "Platform";

    /// <summary>The Core layer this adapter builds on top of.</summary>
    public static string CoreLayer => CoreInfo.Layer;
}
