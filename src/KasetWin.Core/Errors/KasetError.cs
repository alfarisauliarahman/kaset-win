namespace KasetWin.Core.Errors;

/// <summary>
/// Kategori kegagalan terpadu untuk seluruh aplikasi (Req 20.1).
/// Padanan dari <c>YTMusicError</c> pada implementasi macOS.
/// </summary>
public enum KasetErrorKind
{
    /// <summary>Sesi kedaluwarsa (HTTP 401/403) — memicu re-auth.</summary>
    AuthExpired,

    /// <summary>Belum ada sesi valid saat operasi terautentikasi diminta.</summary>
    NotAuthenticated,

    /// <summary>Kegagalan konektivitas jaringan (Req 20.2). Dapat dicoba ulang.</summary>
    NetworkError,

    /// <summary>Respons gagal di-parse (Req 20.3). Dilempar dari parser.</summary>
    ParseError,

    /// <summary>Error API dengan kode status (selain 401/403). Dapat dicoba ulang.</summary>
    ApiError,

    /// <summary>Kegagalan pemutaran atau DRM tidak tersedia (Req 1.7).</summary>
    PlaybackError,

    /// <summary>Kegagalan yang tidak terklasifikasi.</summary>
    Unknown,
}

/// <summary>
/// Exception terpadu yang merepresentasikan kegagalan Kaset (Req 20).
/// Memuat <see cref="Kind"/> untuk klasifikasi, opsional <see cref="ApiStatusCode"/>
/// untuk <see cref="KasetErrorKind.ApiError"/>, dan <see cref="IsRetryable"/> agar
/// <c>RetryPolicy</c> dapat memutuskan backoff (Req 20.4).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "Nama tipe 'KasetError' ditetapkan oleh desain (padanan YTMusicError macOS).")]
public sealed class KasetError : Exception
{
    /// <summary>Kategori kegagalan.</summary>
    public KasetErrorKind Kind { get; }

    /// <summary>Kode status HTTP terkait, jika ada (relevan untuk <see cref="KasetErrorKind.ApiError"/>).</summary>
    public int? ApiStatusCode { get; }

    /// <summary>
    /// Apakah operasi yang menghasilkan error ini layak dicoba ulang.
    /// Hanya <see cref="KasetErrorKind.NetworkError"/> dan <see cref="KasetErrorKind.ApiError"/> yang retryable.
    /// </summary>
    public bool IsRetryable => Kind is KasetErrorKind.NetworkError or KasetErrorKind.ApiError;

    /// <summary>
    /// Membuat <see cref="KasetError"/> baru.
    /// </summary>
    /// <param name="kind">Kategori kegagalan.</param>
    /// <param name="message">Pesan deskriptif (jangan memuat nilai rahasia).</param>
    /// <param name="inner">Exception penyebab, jika ada.</param>
    /// <param name="statusCode">Kode status HTTP terkait, jika ada.</param>
    public KasetError(KasetErrorKind kind, string message, Exception? inner = null, int? statusCode = null)
        : base(message, inner)
    {
        Kind = kind;
        ApiStatusCode = statusCode;
    }
}
