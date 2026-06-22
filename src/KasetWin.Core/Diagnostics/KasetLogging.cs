using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace KasetWin.Core.Diagnostics;

/// <summary>
/// Tingkat verbositas log yang didukung (Req 21.2), dipetakan ke level Serilog.
/// </summary>
public enum KasetLogLevel
{
    /// <summary>Diagnostik rinci untuk pengembangan.</summary>
    Debug,

    /// <summary>Peristiwa operasional umum.</summary>
    Info,

    /// <summary>Kondisi tak terduga yang tidak menggagalkan operasi.</summary>
    Warning,

    /// <summary>Kegagalan operasi.</summary>
    Error,
}

/// <summary>
/// Opsi konfigurasi untuk bootstrap logging.
/// </summary>
public sealed class KasetLoggingOptions
{
    /// <summary>Level minimum yang ditulis. Default <see cref="KasetLogLevel.Info"/>.</summary>
    public KasetLogLevel MinimumLevel { get; set; } = KasetLogLevel.Info;

    /// <summary>
    /// Path file log dengan rolling harian. Jika null, sink file dilewati
    /// (mis. untuk pengujian headless yang hanya butuh sink debug).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>Apakah menulis ke sink Debug (<c>System.Diagnostics.Debug</c>). Default true.</summary>
    public bool WriteToDebug { get; set; } = true;
}

/// <summary>
/// Bootstrap logging terstruktur Kaset (Req 21): membangun pipeline
/// <see cref="Serilog"/> dengan redaksi rahasia wajib (<see cref="RedactingEnricher"/>)
/// dan mengekspos-nya melalui <see cref="ILoggerFactory"/> /
/// <see cref="ILoggingBuilder"/> agar dipakai App (Generic Host) maupun CLI.
///
/// Helper ini berada di Core (bebas WinUI) sehingga dapat diuji headless.
/// </summary>
public static class KasetLogging
{
    /// <summary>
    /// Membangun <see cref="Serilog.Core.Logger"/> dengan enricher redaksi dan sink
    /// file + debug sesuai <paramref name="options"/>.
    /// </summary>
    public static Serilog.Core.Logger BuildSerilogLogger(KasetLoggingOptions? options = null)
    {
        options ??= new KasetLoggingOptions();

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilogLevel(options.MinimumLevel))
            .Enrich.FromLogContext()
            // Redaksi wajib: tidak ada cookie/token/SAPISID/Authorization yang lolos ke sink.
            .Enrich.With(new RedactingEnricher());

        if (options.WriteToDebug)
        {
            configuration.WriteTo.Debug(
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }

        if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            configuration.WriteTo.File(
                path: options.FilePath,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }

        return configuration.CreateLogger();
    }

    /// <summary>
    /// Membangun <see cref="ILoggerFactory"/> siap pakai yang didukung Serilog.
    /// Cocok untuk CLI / pengujian yang tidak memakai Generic Host.
    /// </summary>
    public static ILoggerFactory BuildLoggerFactory(KasetLoggingOptions? options = null)
    {
        var logger = BuildSerilogLogger(options);
        // dispose: true → menutup factory akan mem-flush & menutup logger Serilog.
        return new SerilogLoggerFactory(logger, dispose: true);
    }

    /// <summary>
    /// Mendaftarkan logging Kaset (Serilog + redaksi) pada <see cref="ILoggingBuilder"/>
    /// host. Membersihkan provider bawaan agar Serilog menjadi pipeline tunggal.
    /// </summary>
    public static ILoggingBuilder AddKasetLogging(this ILoggingBuilder builder, KasetLoggingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var logger = BuildSerilogLogger(options);
        builder.ClearProviders();
        builder.AddSerilog(logger, dispose: true);
        return builder;
    }

    private static LogEventLevel ToSerilogLevel(KasetLogLevel level) => level switch
    {
        KasetLogLevel.Debug => LogEventLevel.Debug,
        KasetLogLevel.Info => LogEventLevel.Information,
        KasetLogLevel.Warning => LogEventLevel.Warning,
        KasetLogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information,
    };
}
