namespace KasetWin.App.Hosting;

/// <summary>
/// Where Kaset's Discord Rich Presence gets its application identity.
/// </summary>
/// <remarks>
/// <para>
/// A Discord <b>Application ID</b> is the public identifier of a Discord application. It decides the
/// name shown in "Listening to …" and which uploaded artwork the presence can reference. It is
/// <b>not</b> a secret: it is transmitted in every presence payload and is visible to anyone who
/// looks. The secret that must never be committed is the application's <i>Client Secret</i>, which
/// is only needed for OAuth flows — local IPC presence (<c>SET_ACTIVITY</c>) does not use it, and
/// Kaset never touches it.
/// </para>
/// <para>
/// Because it is public and identical for everyone, the project ships <b>one</b> shared id here.
/// Requiring each user to register their own application would mean every install needed a trip to
/// the Discord Developer Portal before the feature did anything — which is the same as the feature
/// not existing for most people. The per-user override below exists only for someone who wants their
/// presence to display a different application name.
/// </para>
/// </remarks>
internal static class DiscordRichPresenceOptions
{
    /// <summary>
    /// The project's shared Discord Application ID, used unless the user sets an override.
    /// </summary>
    /// <remarks>
    /// <b>Empty until the project's Discord application is created</b> (Developer Portal → New
    /// Application → name it "Kaset" → copy the Application ID → paste it here). While this is
    /// empty the feature stays unavailable unless a user supplies their own id, and the Settings
    /// card says so rather than silently doing nothing.
    /// </remarks>
    internal const string DefaultApplicationId = "";

    /// <summary>Whether a shared id has been baked in.</summary>
    internal static bool HasDefaultApplicationId => !string.IsNullOrWhiteSpace(DefaultApplicationId);

    /// <summary>
    /// Resolves the id to connect with: the user's override when set, otherwise the shared default.
    /// Empty means the feature cannot run.
    /// </summary>
    internal static string Resolve(string? userOverride) =>
        string.IsNullOrWhiteSpace(userOverride) ? DefaultApplicationId : userOverride.Trim();
}
