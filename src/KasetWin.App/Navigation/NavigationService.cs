using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Navigation;

/// <summary>
/// Default <see cref="INavigationService"/> that wraps the shell's content <see cref="Frame"/>
/// (Task 14.2, Req 16.1).
/// </summary>
/// <remarks>
/// <para>
/// Register as a singleton so the shell and every ViewModel share one navigation target:
/// </para>
/// <code>
/// // In AppHost.ConfigureServices (Task 14.x wiring — do not add here while 14.1 edits AppHost):
/// services.AddSingleton&lt;INavigationService, NavigationService&gt;();
/// </code>
/// <para>
/// The shell (MainWindow, Task 14.1) resolves the service and calls <see cref="Initialize"/> with
/// its content <see cref="Frame"/> once the NavigationView is built, e.g.:
/// </para>
/// <code>
/// var nav = App.Current.Services.GetRequiredService&lt;INavigationService&gt;();
/// nav.Initialize(ContentFrame);
/// // NavigationView.ItemInvoked → nav.NavigateTo&lt;HomePage&gt;();
/// // NavigationView back button   → nav.GoBack();
/// </code>
/// <para>
/// ViewModels then navigate to detail surfaces without referencing <see cref="Frame"/>, passing a
/// stable id as the parameter:
/// </para>
/// <code>
/// _navigation.NavigateTo&lt;PlaylistPage&gt;(playlist.Id);   // browseId
/// _navigation.NavigateTo&lt;ArtistPage&gt;(artist.Id);
/// </code>
/// </remarks>
public sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    /// <inheritdoc />
    public bool CanGoBack => _frame?.CanGoBack ?? false;

    /// <inheritdoc />
    public void Initialize(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
    }

    /// <inheritdoc />
    public bool NavigateTo<TPage>(object? parameter = null)
        where TPage : Page
        => NavigateTo(typeof(TPage), parameter);

    /// <inheritdoc />
    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(pageType);
        var frame = EnsureInitialized();

        // Frame.Navigate is a no-op (returns false) when navigating to the same page+parameter,
        // which we surface to the caller as-is.
        return parameter is null
            ? frame.Navigate(pageType)
            : frame.Navigate(pageType, parameter);
    }

    /// <inheritdoc />
    public bool GoBack()
    {
        var frame = EnsureInitialized();
        if (!frame.CanGoBack)
        {
            return false;
        }

        frame.GoBack();
        return true;
    }

    private Frame EnsureInitialized()
        => _frame ?? throw new InvalidOperationException(
            $"{nameof(NavigationService)} has not been initialized. Call {nameof(Initialize)}(Frame) " +
            "from the shell before navigating.");
}
