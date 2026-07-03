using System.Diagnostics;

namespace KasetWin.Core.Diagnostics;

/// <summary>
/// THROWAWAY DIAGNOSTIC — remove before shipping. Writes timestamped, thread-tagged stage markers
/// to <c>%TEMP%\kaset_trace.txt</c> to instrument the sign-in → playback (Bug A) and
/// YouTube → History (Bug B) code paths per the repo "MEASURE BEFORE YOU FIX" rule.
/// </summary>
/// <remarks>
/// <para>
/// The app is a packaged/sandboxed MSIX, so this writes to <see cref="Path.GetTempPath"/> (the
/// per-app container temp) and opens/appends/closes on every line so the file is effectively
/// flushed after each entry. Access is serialized by a process-wide lock.
/// </para>
/// <para>
/// 🚨 Callers MUST only pass non-secret stage markers, counts, durations, and thread ids. NEVER
/// pass cookie/token/SAPISID values or any header content. All writes are best-effort and never
/// throw (a failed diagnostic must not perturb the path being measured).
/// </para>
/// </remarks>
public static class KasetTrace
{
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static readonly string FilePath =
        Path.Combine(Path.GetTempPath(), "kaset_trace.txt");

    /// <summary>Writes a single stage marker line. Best-effort; never throws.</summary>
    /// <param name="stage">Non-secret stage marker (e.g. <c>"BugB:RequestAsync.cookie.start"</c>).</param>
    /// <param name="detail">Optional non-secret detail (counts/durations/flags). Never secrets.</param>
    public static void Log(string stage, string? detail = null)
    {
        try
        {
            var line =
                $"{DateTime.Now:HH:mm:ss.fff} +{Clock.ElapsedMilliseconds,7}ms " +
                $"T{Environment.CurrentManagedThreadId,-3} {stage}" +
                (string.IsNullOrEmpty(detail) ? string.Empty : $" | {detail}");

            lock (Gate)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never perturb the measured path.
        }
    }

    /// <summary>
    /// Returns elapsed milliseconds since a captured start timestamp, for logging durations of an
    /// awaited boundary without logging any payload. Use with <see cref="Now"/>.
    /// </summary>
    public static long Now() => Clock.ElapsedMilliseconds;

    /// <summary>Convenience: logs <paramref name="stage"/> with a <c>dur=…ms</c> detail.</summary>
    public static void LogSince(string stage, long startMs, string? extra = null)
    {
        var dur = Clock.ElapsedMilliseconds - startMs;
        Log(stage, extra is null ? $"dur={dur}ms" : $"dur={dur}ms {extra}");
    }
}
