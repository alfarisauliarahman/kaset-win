using Serilog.Core;
using Serilog.Events;

namespace KasetWin.Core.Diagnostics;

/// <summary>
/// Serilog enricher yang menjalankan <see cref="Redactor"/> pada seluruh properti
/// log bertipe string (skalar) sebelum event ditulis ke sink (Req 21.3, 22.3).
///
/// Dengan menyensor nilai properti terstruktur, baik template terender maupun
/// keluaran JSON tidak akan memuat cookie, token, SAPISID, atau header Authorization.
/// </summary>
public sealed class RedactingEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        // Salin nama properti dahulu agar aman memperbarui koleksi saat iterasi.
        foreach (var name in logEvent.Properties.Keys.ToArray())
        {
            if (logEvent.Properties.TryGetValue(name, out var value) &&
                TryRedact(value, out var redacted))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    private static bool TryRedact(LogEventPropertyValue value, out LogEventPropertyValue redacted)
    {
        if (value is ScalarValue { Value: string text })
        {
            var clean = Redactor.Redact(text);
            if (!ReferenceEquals(clean, text) && !string.Equals(clean, text, StringComparison.Ordinal))
            {
                redacted = new ScalarValue(clean);
                return true;
            }
        }

        redacted = value;
        return false;
    }
}
