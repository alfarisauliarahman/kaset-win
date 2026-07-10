namespace KasetWin.App.ViewModels;

/// <summary>
/// Ambient holder for the signed-in account's display name and avatar, set by
/// <c>MainWindow</c> when the account loads. Lets item templates (e.g. the Home "Listen again"
/// header) show the account without threading it through every feed ViewModel.
/// </summary>
public static class AccountContext
{
    /// <summary>The signed-in account's display name (upper-cased for the YT-style header), or <c>null</c>.</summary>
    public static string? Name { get; set; }

    /// <summary>The signed-in account's avatar URL, or <c>null</c>.</summary>
    public static Uri? AvatarUrl { get; set; }
}
