using Avalonia.Threading;
using Haven.Desktop.Controls;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class HomePage
{
    /// <summary>Shows the same Home decision surface for an exact server request. Closing is not approval.</summary>
    public async Task ReviewRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var services = App.Services ?? throw new InvalidOperationException("Home services are unavailable.");
        CloseHomeManager();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _inlineHomeLifetime = lifetime;
        using var review = new HomeApprovalCuiSurface(services.GetRequiredService<HomeCoreRuntime>(),
            services.GetRequiredService<HomeLocalProfileIdentity>(), services.GetRequiredService<HomePermissionTrustService>());
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await review.InitializeAsync(lifetime.Token);
            if (!await review.FocusRequestAsync(requestId, lifetime.Token))
                throw new InvalidOperationException("The exact Home request is no longer available.");
            lifetime.Token.ThrowIfCancellationRequested();
            _inlineApprovalClosed = closed;
            HomeDashboardContent.IsVisible = false;
            HomeOwnedSurfacePanel.IsVisible = true;
            HomeOwnedSurfaceTitle.Text = "Home permissions";
            HomeOwnedSurfaceHost.Content = review;
            CloseHomeOwnedSurfaceButton.Content = "Back to Home";
            if (!await review.AcknowledgeDisplayedRequestAsync(lifetime.Token))
                throw new InvalidOperationException("The exact native Home prompt could not be displayed.");
            if (!ReferenceEquals(App.Services, services))
                throw new UnauthorizedAccessException("The original Home prompt host changed.");
            var permissions = services.GetRequiredService<HomePermissionTrustService>();
            while (!closed.Task.IsCompleted)
            {
                await Task.WhenAny(closed.Task, Task.Delay(150, lifetime.Token));
                lifetime.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(App.Services, services))
                    throw new UnauthorizedAccessException("The original Home prompt host changed.");
                if ((await permissions.GetAuthorizationAsync(requestId, lifetime.Token)).State != HomePermissionRequestState.PendingApproval)
                    break;
            }
            lifetime.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            if (ReferenceEquals(_inlineApprovalClosed, closed)) _inlineApprovalClosed = null;
            if (ReferenceEquals(_inlineHomeLifetime, lifetime))
            {
                _inlineHomeLifetime = null;
                HomeOwnedSurfaceHost.Content = null;
                HomeOwnedSurfacePanel.IsVisible = false;
                HomeDashboardContent.IsVisible = true;
            }
        }
    }
}
