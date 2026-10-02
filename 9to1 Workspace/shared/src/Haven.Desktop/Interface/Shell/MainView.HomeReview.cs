using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    /// <summary>Navigate to the current profile's actual Home request review. Returning grants no authority.</summary>
    public Task ReviewHomeRequestAsync(string requestId, CancellationToken cancellationToken = default) =>
        ReviewHomeRequestCoreAsync(requestId, App.Services, null, cancellationToken);

    /// <summary>Original native host navigation retains its captured provider and actor through Home activation.
    /// It is the same actual Home surface, never an approver or reusable approval grant.</summary>
    public Task ReviewHomeRequestForOriginalHostAsync(string requestId, IServiceProvider originalServices,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalServices); ArgumentNullException.ThrowIfNull(originalActor);
        return ReviewHomeRequestCoreAsync(requestId, originalServices, originalActor, cancellationToken);
    }
    private async Task ReviewHomeRequestCoreAsync(string requestId, IServiceProvider? originalServices,
        AuthenticatedResourceActor? originalActor, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var actors = originalActor is null ? null : originalServices?.GetRequiredService<IAuthenticatedResourceActorSource>();
        await RequireOriginalAsync();
        var page = _homePage ??= CreateHomePage();
        AddOrSelectTab("home", "Home", page, false, HavenSurface.Home);
        await page.ActivateAsync(cancellationToken);
        await RequireOriginalAsync();
        // ReviewRequest captures the actual same provider synchronously before its native Home awaits.
        await page.ReviewRequestAsync(requestId, cancellationToken);

        async Task RequireOriginalAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed || originalServices is null || !ReferenceEquals(App.Services, originalServices))
                throw new UnauthorizedAccessException("The original Home navigation host changed.");
            if (originalActor is not null && (actors is null || await actors.GetCurrentAsync(cancellationToken) != originalActor))
                throw new UnauthorizedAccessException("The original Home navigation actor changed.");
            if (IsDisposed || !ReferenceEquals(App.Services, originalServices))
                throw new UnauthorizedAccessException("The original Home navigation host changed.");
        }
    }
}
