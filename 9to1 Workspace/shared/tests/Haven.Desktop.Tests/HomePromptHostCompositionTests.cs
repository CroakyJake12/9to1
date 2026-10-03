using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Compiled resource and actual normal App registration checks.
/// Headless unavailable-window refusal is not acceptance of classic native window activation.</summary>
public sealed class HomePromptHostCompositionTests
{
    [Fact]
    public async Task Actual_native_assembly_embeds_the_exact_reviewed_Home_approval_Cui_document()
    {
        var assembly = typeof(HavenOS.Home.NativeUI.HomeApprovalCuiSurface).Assembly;
        using var source = assembly.GetManifestResourceStream("HavenOS.Home.NativeUI.Resources.Cui.HomeApprovals.cui")
            ?? throw new InvalidDataException("The actual Home approval CUI resource is missing.");
        var digest = await SHA256.HashDataAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal("98000a602764c3eda03386f49327c067f9b7c20f987260b68eeb21d1902ab266",
            Convert.ToHexString(digest).ToLowerInvariant());
    }

    [AvaloniaFact]
    public async Task Actual_normal_App_presenter_without_a_window_retains_the_real_pending_request_and_cannot_begin_execution()
    {
        var token = TestContext.Current.CancellationToken;
        var app = Assert.IsType<App>(Avalonia.Application.Current);
        Assert.False(app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime or ISingleViewApplicationLifetime,
            "This case requires the actual headless no-window lifetime; it does not substitute a native shell.");
        var services = App.Services ?? throw new InvalidOperationException("Actual normal App services were not initialized.");
        var presenter = Assert.IsType<NativeHomeApprovalPromptPresenter>(services.GetRequiredService<IHomeApprovalPromptPresenter>());
        var permissions = services.GetRequiredService<HomePermissionTrustService>();
        Assert.Same(services.GetRequiredService<HomeAppAiServices>().Permissions, permissions);
        var actor = await services.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token)
            ?? throw new UnauthorizedAccessException("The actual local OS-bound Home profile is unavailable.");
        var target = new HomeObjectReference("home.profile-model-routes", actor.ProfileId);
        var pending = await permissions.AuthorizeAsync(new(null,
            new(actor.ActorId, "Actual normal host test", actor.ProfileId, actor.AuthenticationRevision, true),
            "actual-normal-host-no-window-" + Guid.NewGuid().ToString("N"),
            new(HomeModelPickerFeatureProvider.AppId, "models.routes.update", [target]),
            new([target.ObjectType], 1, [target], false, "Review this exact local model-route request")), token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.False(await presenter.ShowPendingRequestAsync(pending.RequestId, token));
        var observed = await new HomeApprovalPromptFlow(permissions, presenter).ReviewPendingAsync(pending.RequestId, token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, observed.State);
        Assert.Equal("HOME_PERMISSION_REQUIRED", observed.Code);
        Assert.False(observed.IsAllowed);
        Assert.Null(observed.TrustLevel);
        var snapshot = await permissions.GetSnapshotAsync(cancellationToken: token);
        Assert.Contains(snapshot.PendingRequests, request => request.RequestId == pending.RequestId);
        Assert.DoesNotContain(snapshot.RecentAuditEvents, item => item.RequestId == pending.RequestId &&
            item.Kind is HomePermissionAuditKind.ApprovalPromptShown or HomePermissionAuditKind.DecisionMade or HomePermissionAuditKind.ExecutionStarted);
        Assert.False((await permissions.BeginExecutionAsync(pending.RequestId, token)).IsAllowed);
    }
}
