using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using HomePermissionRequestState = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState;
using HomeObjectReference = HavenOS.Home.PermissionsTrustNotifications.HomeObjectReference;

namespace Haven.Desktop.Tests;

/// <summary>Actual native notification and canonical audit refusal controls. These headless cases
/// issue NO successful presentation receipt and establish no installed/native-pixel authority.
/// The existing two approval facts and the separate original host fixture remain unchanged.</summary>
public sealed class HomeApprovalWarningOrderingTests
{
    [AvaloniaFact]
    public Task Missing_checked_native_presenter_keeps_extended_warning_audit_and_trust_unavailable() =>
        RunOriginalRefusalAsync(retireAtFirstWarningNotification: false);

    [AvaloniaFact]
    public Task First_native_warning_visibility_retirement_cannot_record_an_earlier_ghost_warning_audit() =>
        RunOriginalRefusalAsync(retireAtFirstWarningNotification: true);

    private static async Task RunOriginalRefusalAsync(bool retireAtFirstWarningNotification)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-warning-order-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>();
        var expected = new List<Exception>();
        var createdRoot = false;
        HomeCoreRuntime? runtime = null;
        HomeApprovalCuiSurface? surface = null;
        CuiSceneHost? host = null;
        Window? window = null;
        StackPanel? warningParent = null;
        TextBlock? status = null;
        Task? originalInitialization = null;
        Task<bool>? originalFocus = null;
        Task? originalSurfaceClose = null;
        Task? originalHostClose = null;
        Task? originalRuntimeClose = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? warningChanges = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? statusChanges = null;
        Exception? callbackFailure = null;
        var firstWarning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawWarning = false;
        var nativeWindowClosed = false;
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            Directory.CreateDirectory(root);
            createdRoot = true;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(cancellationToken);
            Assert.NotNull(actor);
            var permissions = new HomePermissionTrustService(store,
                (app, action) => app == "write" && action == "write.file.save"
                    ? new(HomePermissionRisk.High, false, true, true) : null);
            var target = new HomeObjectReference("files.item", Guid.NewGuid().ToString("N"));
            var request = await permissions.AuthorizeAsync(new(null,
                new(actor.ActorId, "Actual warning fixture", actor.ProfileId, actor.AuthenticationRevision, true),
                "actual-warning-order", new("write", "write.file.save", [target]),
                new(["files.item"], 1, [target], false, "Save the reviewed document")), cancellationToken);
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            runtime = new HomeCoreRuntime([new HomeCoreStateService(store),
                new HomePermissionsCoreService(permissions, profiles)]);
            surface = new HomeApprovalCuiSurface(runtime, profiles, permissions);
            Assert.Null(surface.OriginalWarningPresentationSource);
            originalInitialization = surface.InitializeAsync(cancellationToken);
            await originalInitialization;
            originalFocus = surface.FocusRequestAsync(request.RequestId, cancellationToken);
            Assert.True(await originalFocus);
            host = Assert.IsType<CuiSceneHost>(surface.Content);
            var warning = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Name == "approval-trust-warning");
            Assert.Equal("Always Trust can allow this caller to read, modify or destroy user data within the displayed action and scope without asking again. Review the caller and affected objects carefully. You can go back without granting trust, and revoke granted trust below.", warning.Text);
            warningParent = warning.GetVisualAncestors().OfType<StackPanel>().First();
            status = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Name == "approval-status");
            warningChanges = (_, args) =>
            {
                if (sawWarning || args.Property.Name != "IsVisible" ||
                    !warningParent.IsVisible) return;
                sawWarning = true;
                try
                {
                    if (retireAtFirstWarningNotification)
                    {
                        // Request only from the actual accepted action's synchronous native notification.
                        // The fixture joins this SAME close outside the action/load dependency.
                        originalSurfaceClose = surface.CloseAndDrainAsync();
                        originalHostClose = host.OriginalClose;
                    }
                    firstWarning.TrySetResult();
                }
                catch (Exception error)
                {
                    callbackFailure = error;
                    firstWarning.TrySetException(error);
                    throw;
                }
            };
            statusChanges = (_, args) =>
            {
                if (args.Property.Name == "Text" && status.Text ==
                    "The native warning presentation could not be verified. Extended trust remains unavailable.")
                    unavailable.TrySetResult();
            };
            warningParent.PropertyChanged += warningChanges;
            status.PropertyChanged += statusChanges;
            window = new Window { Content = surface, Width = 900, Height = 700 };
            window.Show();
            var openWarning = Assert.Single(surface.GetVisualDescendants().OfType<Button>(),
                control => control.Name == "approval-warning");
            openWarning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // These are actual native notification/status tasks, never delay or displayed-pixel proof.
            await firstWarning.Task.WaitAsync(cancellationToken);
            if (!retireAtFirstWarningNotification)
                await unavailable.Task.WaitAsync(cancellationToken);
            Assert.True(sawWarning);
            Assert.Null(callbackFailure);
            originalSurfaceClose ??= surface.CloseAndDrainAsync();
            Assert.Same(originalSurfaceClose, surface.CloseAndDrainAsync());
            originalHostClose ??= host.OriginalClose;
            Assert.NotNull(originalHostClose);
            var closeFailure = await Record.ExceptionAsync(() => originalSurfaceClose!);
            if (retireAtFirstWarningNotification)
            {
                Assert.NotNull(closeFailure);
                var actualCauses = LeafCauses(closeFailure).ToArray();
                Assert.NotEmpty(actualCauses);
                Assert.All(actualCauses, cause => Assert.IsAssignableFrom<OperationCanceledException>(cause));
                // Admit only these exact observed retirement causes after the real native notification.
                expected.AddRange(actualCauses);
                var sameCloseFailure = await Record.ExceptionAsync(() => surface.CloseAndDrainAsync());
                Assert.Same(closeFailure, sameCloseFailure);
            }
            else Assert.Null(closeFailure);
            Assert.True(originalHostClose.IsCompleted);
            var snapshot = await permissions.GetSnapshotAsync(cancellationToken: cancellationToken);
            var stillPending = Assert.Single(snapshot.PendingRequests, item => item.RequestId == request.RequestId);
            Assert.False(stillPending.AlwaysTrustWarningShown);
            Assert.Empty(snapshot.Grants);
            Assert.DoesNotContain(snapshot.RecentAuditEvents,
                item => item.ResultCode == "HOME_ALWAYS_TRUST_WARNING_SHOWN");
            Assert.False((await permissions.GetAuthorizationAsync(request.RequestId, cancellationToken)).IsAllowed);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (surface is not null)
            {
                try { originalSurfaceClose ??= surface.CloseAndDrainAsync(); }
                catch (Exception error) { Add(errors, error); }
            }
            await CollectAsync(originalInitialization, [], errors);
            await CollectAsync(originalFocus, [], errors);
            await CollectAsync(originalSurfaceClose, expected, errors);
            if (host is not null)
            {
                try { originalHostClose ??= host.OriginalClose; }
                catch (Exception error) { Add(errors, error); }
            }
            await CollectAsync(originalHostClose, expected, errors);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Attempt(errors, () => { if (warningParent is not null && warningChanges is not null) warningParent.PropertyChanged -= warningChanges; });
                    Attempt(errors, () => { if (status is not null && statusChanges is not null) status.PropertyChanged -= statusChanges; });
                    if (window is not null && originalSurfaceClose?.IsCompleted == true)
                        Attempt(errors, () => { window.Close(); nativeWindowClosed = true; });
                });
            }
            catch (Exception error) { Add(errors, error); }
            if (callbackFailure is not null) Add(errors, callbackFailure);
            if (runtime is not null && (surface is null || originalSurfaceClose?.IsCompleted == true) &&
                (window is null || nativeWindowClosed))
            {
                try { originalRuntimeClose = runtime.DisposeAsync().AsTask(); }
                catch (Exception error) { Add(errors, error); }
            }
            await CollectAsync(originalRuntimeClose, [], errors);
            if (createdRoot && (surface is null || originalSurfaceClose?.IsCompleted == true) &&
                (window is null || nativeWindowClosed) &&
                (runtime is null || originalRuntimeClose?.IsCompleted == true))
            {
                try { Directory.Delete(root, true); }
                catch (Exception error) { Add(errors, error); }
            }
            else if (createdRoot)
                Add(errors, new InvalidOperationException("Retain every original native/runtime drain before deleting warning fixture state."));
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original warning refusal fixture and independent cleanup failed.", errors);
    }

    private static async Task CollectAsync(Task? original, IReadOnlyList<Exception> expected, List<Exception> errors)
    {
        if (original is null) return;
        try { await original.ConfigureAwait(false); }
        catch (Exception error)
        {
            foreach (var cause in LeafCauses(error))
                if (!expected.Any(known => ReferenceEquals(known, cause))) Add(errors, cause);
        }
    }

    private static IEnumerable<Exception> LeafCauses(Exception error)
    {
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Count != 0)
        {
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in LeafCauses(inner)) yield return cause;
        }
        else yield return error;
    }

    private static void Attempt(List<Exception> errors, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception error) { Add(errors, error); }
    }

    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error);
    }
}
