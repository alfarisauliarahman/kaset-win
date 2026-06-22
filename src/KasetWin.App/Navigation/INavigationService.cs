using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Navigation;

/// <summary>
/// Abstraction over the shell's <see cref="Frame"/> so ViewModels and the shell can navigate to
/// detail pages (Playlist/Album/Artist/…) without taking a direct dependency on the concrete
/// <see cref="Frame"/> (Task 14.2, Req 16.1). Implemented by <see cref="NavigationService"/> and
/// registered as a singleton in DI (see <c>AppHost</c> registration note in
/// <see cref="NavigationService"/>).
/// </summary>
/// <remarks>
/// The shell (MainWindow, Task 14.1) calls <see cref="Initialize"/> once with the content
/// <see cref="Frame"/>; everything else navigates through this service. Navigation parameters use
/// stable identities (<c>videoId</c>/<c>browseId</c>) so a target page can re-fetch its surface via
/// <c>ViewModelBase.LoadAsync(key, …)</c>.
/// </remarks>
public interface INavigationService
{
    /// <summary>True when there is an entry to navigate back to.</summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Binds the service to the shell's content <see cref="Frame"/>. Call once from the shell
    /// before any navigation occurs (typically right after the NavigationView/Frame is created in
    /// Task 14.1).
    /// </summary>
    /// <param name="frame">The frame that hosts page content.</param>
    void Initialize(Frame frame);

    /// <summary>
    /// Navigates to the page type <typeparamref name="TPage"/>, optionally passing
    /// <paramref name="parameter"/> (use a stable id such as a <c>browseId</c>/<c>videoId</c>).
    /// </summary>
    /// <typeparam name="TPage">The destination page type.</typeparam>
    /// <param name="parameter">Optional navigation parameter forwarded to the page.</param>
    /// <returns><see langword="true"/> when navigation was initiated.</returns>
    bool NavigateTo<TPage>(object? parameter = null)
        where TPage : Page;

    /// <summary>
    /// Navigates to <paramref name="pageType"/> (non-generic overload for cases where the page type
    /// is only known at runtime, e.g. resolved from a <c>browseId</c> prefix classification).
    /// </summary>
    /// <param name="pageType">The destination page <see cref="Type"/>; must derive from <see cref="Page"/>.</param>
    /// <param name="parameter">Optional navigation parameter forwarded to the page.</param>
    /// <returns><see langword="true"/> when navigation was initiated.</returns>
    bool NavigateTo(Type pageType, object? parameter = null);

    /// <summary>Navigates back one entry when possible.</summary>
    /// <returns><see langword="true"/> when a back navigation occurred.</returns>
    bool GoBack();
}
