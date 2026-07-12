using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KasetWin.App.Views;

/// <summary>
/// Temporary stand-in shown by the shell (Task 14.1) when navigating to a section whose real page
/// is still being built in a parallel task (Home/Explore/Search/Library/Settings, Tasks 14.3–14.10).
/// It displays the pending section title passed as the navigation parameter so the
/// <see cref="NavigationView"/> + <see cref="Frame"/> wiring can be exercised without a build break.
/// </summary>
/// <remarks>
/// TODO (Tasks 14.3–14.10): once the concrete pages land (e.g. <c>KasetWin.App.Views.HomePage</c>),
/// the shell resolves them by type and this placeholder is no longer navigated to for that section.
/// </remarks>
public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        this.InitializeComponent();
        ComingSoonText.Text = Localization.UiStrings.ComingSoon;
    }

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string title && !string.IsNullOrWhiteSpace(title))
        {
            TitleText.Text = title;
        }
    }
}
