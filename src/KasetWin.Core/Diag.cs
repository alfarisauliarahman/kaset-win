namespace KasetWin.Core;

/// <summary>
/// TEMPORARY diagnostic sink. The App wires <see cref="Log"/> to append to a file; Core call sites
/// use <see cref="Write"/> so the Core stays WinUI-free. Remove once the data issues are diagnosed.
/// </summary>
public static class Diag
{
    /// <summary>Set by the App to route diagnostic lines to a log file.</summary>
    public static System.Action<string>? Log { get; set; }

    /// <summary>Emits a diagnostic line (no-op when no sink is attached).</summary>
    public static void Write(string message) => Log?.Invoke(message);
}
