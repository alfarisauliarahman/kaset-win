using System.Text.RegularExpressions;

namespace KasetWin.Core.Diagnostics;

/// <summary>
/// Redaksi murni (tanpa state) yang menyamarkan nilai sensitif dari teks bebas
/// sebelum ditulis ke log atau diagnostik (Req 21.3, 22.3).
///
/// Kontrak (Property 33): untuk teks apa pun yang memuat pola sensitif —
/// header <c>Authorization</c>, token <c>SAPISIDHASH</c>, nilai cookie, dan token —
/// keluaran tidak lagi memuat nilai aslinya. Nilai dirujuk dengan nama kuncinya,
/// bukan nilainya.
///
/// PENTING: kelas ini TIDAK boleh memuat nilai rahasia nyata di kode/komentar.
/// </summary>
public static partial class Redactor
{
    /// <summary>Penanda pengganti yang ditulis menggantikan nilai sensitif.</summary>
    public const string Placeholder = "REDACTED";

    /// <summary>
    /// Nama kunci/cookie/atribut yang nilainya selalu disensor di mana pun ditemukan,
    /// baik dalam format <c>name=value</c>, <c>name: value</c>, maupun JSON <c>"name":"value"</c>.
    /// </summary>
    private static readonly string[] SensitiveKeys =
    {
        // Cookie autentikasi Google/YouTube.
        "__Secure-3PAPISID", "__Secure-1PAPISID",
        "__Secure-3PSIDTS", "__Secure-1PSIDTS",
        "__Secure-3PSIDCC", "__Secure-1PSIDCC",
        "__Secure-3PSID", "__Secure-1PSID",
        "SAPISID", "APISID", "HSID", "SSID", "SIDCC", "SID",
        "LOGIN_INFO", "VISITOR_INFO1_LIVE", "PREF",
        // Token & kredensial generik.
        "access_token", "refresh_token", "id_token", "token",
        "api_key", "apikey", "authorization", "auth", "password", "secret",
    };

    // Header bernilai sampai akhir baris: Authorization / Cookie / Set-Cookie.
    [GeneratedRegex(@"(?im)\b(Authorization|Cookie|Set-Cookie)(\s*[:=]\s*)(?<value>[^\r\n]+)")]
    private static partial Regex HeaderLineRegex();

    // Token SAPISIDHASH (termasuk varian 1P/3P): "SAPISIDHASH <ts>_<hash>".
    [GeneratedRegex(@"(?i)\b(SAPISID(?:1P|3P)?HASH)(\s+)(?<value>\S+)")]
    private static partial Regex SapisidHashRegex();

    // Token Bearer: "Bearer <token>".
    [GeneratedRegex(@"(?i)\b(Bearer)(\s+)(?<value>[A-Za-z0-9\-._~+/]+=*)")]
    private static partial Regex BearerRegex();

    // name=value / "name": "value" untuk kunci sensitif yang dikenal.
    private static readonly Regex NamedSecretRegex = BuildNamedSecretRegex();

    /// <summary>
    /// Mengembalikan salinan <paramref name="input"/> dengan seluruh nilai sensitif disamarkan.
    /// Aman dipanggil dengan null/empty (dikembalikan apa adanya).
    /// </summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        // 1) Header bernilai-sampai-akhir-baris (Authorization/Cookie/Set-Cookie).
        var result = HeaderLineRegex().Replace(input, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Placeholder}");

        // 2) Token SAPISIDHASH.
        result = SapisidHashRegex().Replace(result, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Placeholder}");

        // 3) Token Bearer.
        result = BearerRegex().Replace(result, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Placeholder}");

        // 4) Pasangan kunci=nilai sensitif yang dikenal (cookie & token).
        result = NamedSecretRegex.Replace(result, m => $"{m.Groups["prefix"].Value}{Placeholder}{m.Groups["suffix"].Value}");

        return result;
    }

    private static Regex BuildNamedSecretRegex()
    {
        // Urutkan dari nama terpanjang agar "__Secure-3PSID" tidak tertangkap sebagian oleh "SID".
        var names = SensitiveKeys
            .OrderByDescending(k => k.Length)
            .Select(Regex.Escape);
        var alternation = string.Join("|", names);

        // prefix : (opsional kutip) nama (opsional kutip) pemisah (opsional kutip-nilai)
        // value  : nilai sampai pemisah cookie/JSON/query
        // suffix : kutip penutup nilai jika ada
        var pattern =
            "(?i)(?<prefix>[\"']?(?:" + alternation + ")[\"']?\\s*[:=]\\s*[\"']?)" +
            "(?<value>[^\"';,&\\s\\r\\n]+)" +
            "(?<suffix>[\"']?)";

        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
