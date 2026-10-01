using Avalonia.Threading;
using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    /// <summary>Navigate to the current profile's actual Home request review. Returning grants no authority.</summary>
    public async Task ReviewHomeRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        var page = _homePage ??= CreateHomePage();
        AddOrSelectTab("home", "Home", page, false, HavenSurface.Home);
        await page.ActivateAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await page.ReviewRequestAsync(requestId, cancellationToken);
    }
}
