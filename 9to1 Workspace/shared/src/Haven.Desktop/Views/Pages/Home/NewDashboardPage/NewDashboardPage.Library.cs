using Avalonia.Controls;
using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private Func<HomeLibraryDestinationPage>? _briefingLibraryFactory;
    private HomeLibraryDestinationPage? _briefingLibraryPage;
    private Task? _briefingLibraryClose;
    private readonly Border _briefingLibraryHost = new();

    /// <summary>The compact cards borrow the actual Library page and its original source,
    /// permission, navigation and close owners. This does not build another artifact index.</summary>
    internal void ConfigureHomeBriefingLibrary(Func<HomeLibraryDestinationPage>? factory)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_briefingLibraryPage is not null && !ReferenceEquals(factory, _briefingLibraryFactory))
            throw new InvalidOperationException("Retain the Dashboard's original Library source factory.");
        _briefingLibraryFactory = factory;
        ConfigureHomeBriefingLibrary(factory is not null);
    }

    private async Task RefreshBriefingLibraryAsync(CancellationToken token)
    {
        if (!ShowsHomeBriefing || !_dashboardActive || _briefingLibraryFactory is null) return;
        token.ThrowIfCancellationRequested();
        if (_briefingLibraryPage is null)
        {
            // Source construction is delayed until the user's saved Dashboard actually
            // requests this briefing. New Tab and custom widget layouts never create it.
            var library = _briefingLibraryPage = _briefingLibraryFactory();
            library.SetDashboardPresentation();
            _briefingLibraryHost.Child = library;
            _dashboardBindings.Set("HasBriefingLibrarySource", true);
        }
        // Deactivation fences publication but does not cancel a child's accepted
        // original under an unrelated caller token. Its real close owns cancellation
        // and settlement; the Dashboard continues to retain this actual activation.
        await AwaitHomeSourceAsync(_briefingLibraryPage.ActivateOriginalAsync(CancellationToken.None));
    }
}
