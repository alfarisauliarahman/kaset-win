using Microsoft.Extensions.Logging;

namespace KasetWin.Core.Diagnostics;

/// <summary>
/// Kategori logger terstruktur yang dipakai di seluruh aplikasi (Req 21.1).
/// Nama kategori muncul sebagai <c>SourceContext</c> pada keluaran log sehingga
/// peristiwa dapat difilter per-subsistem.
/// </summary>
public static class LogCategory
{
    /// <summary>Pemutaran / kontrol player (PlayerService, WebView2 bridge).</summary>
    public const string Player = "Kaset.Player";

    /// <summary>Autentikasi dan siklus sesi (AuthService).</summary>
    public const string Auth = "Kaset.Auth";

    /// <summary>Panggilan InnerTube / YTMusicClient.</summary>
    public const string Api = "Kaset.Api";

    /// <summary>WebView2 host / JS bridge.</summary>
    public const string WebView = "Kaset.WebView";

    /// <summary>Lapisan jaringan / HttpClient.</summary>
    public const string Network = "Kaset.Network";

    /// <summary>Notifikasi & kontrol media sistem (SMTC).</summary>
    public const string Notification = "Kaset.Notification";

    /// <summary>Seluruh nama kategori yang dikenal.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Player, Auth, Api, WebView, Network, Notification,
    };

    /// <summary>
    /// Membuat <see cref="ILogger"/> berkategori dari sebuah <see cref="ILoggerFactory"/>.
    /// </summary>
    public static ILogger CreateLogger(ILoggerFactory factory, string category)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateLogger(category);
    }
}
