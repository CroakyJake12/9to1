using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HomeObjectReference = HavenOS.Home.PermissionsTrustNotifications.HomeObjectReference;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using HomePermissionRequestState = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState;

namespace Haven.Desktop.Tests;

/// <summary>Actual local Home/native-window controls, not installed IPC or positive target presentation.
/// Every original approval fixture remains unchanged.</summary>
public sealed class HomeNativeApprovalWindowOwnerTests
{
    [AvaloniaFact]
    public async Task Exact_canonical_request_opens_original_review_and_accept_once_adds_no_extra_trust()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-frontdoor-once-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>();
        HomeCoreRuntime? runtime = null;
        HomeNativeApprovalWindowOwner? owner = null;
        Task? start = null, open = null, refocus = null, close = null, coreClose = null;
        var created = false;
        try
        {
            Directory.CreateDirectory(root); created = true;
            var ct = TestContext.Current.CancellationToken;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(ct);
            Assert.NotNull(actor);
            var permissions = Permissions(store);
            var target = new HomeObjectReference("files.item", Guid.NewGuid().ToString("N"));
            var request = await permissions.AuthorizeAsync(new(null,
                new(actor.ActorId, "Local frontdoor writer", actor.ProfileId, actor.AuthenticationRevision, true),
                "frontdoor-once", new("write", "write.file.save", [target]),
                new(["files.item"], 1, [target], false, "Save the exact reviewed document")), ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            runtime = new HomeCoreRuntime([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, profiles)]);
            start = runtime.StartAsync(ct); await start;
            owner = new HomeNativeApprovalWindowOwner(runtime, profiles, permissions);
            open = owner.OpenOriginalAsync(request.RequestId, ct); await open;
            var window = Assert.IsType<Window>(owner.OriginalWindow);
            var surface = Assert.IsType<HomeApprovalCuiSurface>(owner.OriginalSurface);
            Assert.Same(surface, window.Content);
            Assert.True(window.IsVisible);
            Assert.Null(surface.OriginalWarningPresentationSource);
            var accept = Assert.Single(surface.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Accept once"));
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            // Poll canonical authorization only; this is no delay or render/presentation witness.
            while (!(await permissions.GetAuthorizationAsync(request.RequestId, deadline.Token)).IsAllowed)
                await Task.Yield();
            var snapshot = await permissions.GetSnapshotAsync(cancellationToken: ct);
            Assert.Empty(snapshot.Grants);
            Assert.DoesNotContain(snapshot.PendingRequests, item => item.RequestId == request.RequestId);
            var actualExecution = await permissions.BeginExecutionAsync(request.RequestId, ct);
            Assert.True(actualExecution.IsAllowed);
            Assert.False((await permissions.BeginExecutionAsync(request.RequestId, ct)).IsAllowed);
            var declinedTarget = new HomeObjectReference("files.item", Guid.NewGuid().ToString("N"));
            var declined = await permissions.AuthorizeAsync(new(null,
                new(actor.ActorId, "Local frontdoor writer", actor.ProfileId, actor.AuthenticationRevision, true),
                "frontdoor-decline", new("write", "write.file.save", [declinedTarget]),
                new(["files.item"], 1, [declinedTarget], false, "Save the second exact reviewed document")), ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, declined.State);
            refocus = owner.OpenOriginalAsync(declined.RequestId, ct); await refocus;
            Assert.Same(window, owner.OriginalWindow);
            Assert.Same(surface, owner.OriginalSurface);
            var decline = Assert.Single(surface.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Decline"));
            decline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while ((await permissions.GetAuthorizationAsync(declined.RequestId, deadline.Token)).State ==
                HomePermissionRequestState.PendingApproval) await Task.Yield();
            Assert.Equal(HomePermissionRequestState.Denied,
                (await permissions.GetAuthorizationAsync(declined.RequestId, ct)).State);
            Assert.False((await permissions.BeginExecutionAsync(declined.RequestId, ct)).IsAllowed);
            Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: ct)).Grants);
            window.Close(); // Ordinary review retirement, not Core/process shutdown.
            close = owner.CloseAndDrainAsync();
            Assert.Same(close, owner.CloseAndDrainAsync());
            await close;
            Assert.True(owner.OriginalRetirementCapturedAndSettled);
            Assert.Null(owner.OriginalWindow);
            Assert.Contains(runtime.Current.Services, service => service.IsAvailable);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (owner is not null)
                try { close ??= owner.CloseAndDrainAsync(); } catch (Exception error) { Add(errors, error); }
            if (start is not null) await Collect(start, errors);
            if (open is not null) await Collect(open, errors);
            if (refocus is not null) await Collect(refocus, errors);
            if (close is not null) await Collect(close, errors);
            if (runtime is not null)
            {
                try { coreClose = runtime.DisposeAsync().AsTask(); } catch (Exception error) { Add(errors, error); }
                if (coreClose is not null) await Collect(coreClose, errors);
            }
            try { if (created) Directory.Delete(root, true); } catch (Exception error) { Add(errors, error); }
        }
        Throw(errors);
    }

    [AvaloniaFact]
    public async Task Held_actual_principal_read_keeps_original_review_close_pending_and_denies_new_acquisition()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-frontdoor-held-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>();
        var principals = new HoldingPrincipal(new OperatingSystemPrincipalSource());
        HomeCoreRuntime? runtime = null;
        HomeNativeApprovalWindowOwner? owner = null;
        Task? start = null, open = null, close = null, coreClose = null;
        Exception? expectedOpen = null, expectedClose = null;
        var created = false;
        try
        {
            Directory.CreateDirectory(root); created = true;
            var ct = TestContext.Current.CancellationToken;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, principals);
            Assert.NotNull(await profiles.GetCurrentAsync(ct));
            var permissions = Permissions(store);
            runtime = new HomeCoreRuntime([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, profiles)]);
            start = runtime.StartAsync(ct); await start;
            owner = new HomeNativeApprovalWindowOwner(runtime, profiles, permissions);
            principals.ArmOriginal();
            open = owner.OpenOriginalAsync(cancellationToken: ct);
            await principals.Entered.Task.WaitAsync(ct);
            Assert.False(open.IsCompleted);
            close = owner.CloseAndDrainAsync();
            Assert.Same(close, owner.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(owner.OriginalRetirementCapturedAndSettled);
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.OpenOriginalAsync(); });
            principals.Release.TrySetResult();
            expectedOpen = await Record.ExceptionAsync(() => open);
            Assert.NotNull(expectedOpen);
            expectedClose = await Record.ExceptionAsync(() => close);
            Assert.NotNull(expectedClose);
            Assert.Contains(LeafCauses(expectedClose!), cause => LeafCauses(expectedOpen!).Any(
                original => ReferenceEquals(original, cause)));
            Assert.True(open.IsCompleted);
            Assert.True(close.IsCompleted);
            Assert.True(owner.OriginalRetirementCapturedAndSettled);
            Assert.Null(owner.OriginalWindow);
            Assert.Contains(runtime.Current.Services, service => service.IsAvailable);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            principals.Release.TrySetResult();
            if (owner is not null)
                try { close ??= owner.CloseAndDrainAsync(); } catch (Exception error) { Add(errors, error); }
            if (start is not null) await Collect(start, errors);
            if (open is not null) await Collect(open, errors, expectedOpen);
            if (close is not null) await Collect(close, errors, expectedClose);
            if (runtime is not null)
            {
                try { coreClose = runtime.DisposeAsync().AsTask(); } catch (Exception error) { Add(errors, error); }
                if (coreClose is not null) await Collect(coreClose, errors);
            }
            try { if (created) Directory.Delete(root, true); } catch (Exception error) { Add(errors, error); }
        }
        Throw(errors);
    }

    private static HomePermissionTrustService Permissions(FileHomeCoreStateStore store) =>
        new(store, (app, action) => app == "write" && action == "write.file.save"
            ? new(HomePermissionRisk.Elevated, true, false, true) : null);

    private sealed class HoldingPrincipal(ITrustedHostPrincipalSource original) : ITrustedHostPrincipalSource
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ArmOriginal() => Interlocked.Exchange(ref _armed, 1);
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            var principal = await original.GetPrincipalAsync(cancellationToken);
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult();
                try { return principal; }
                finally { await Release.Task.ConfigureAwait(false); } // Hold only after the real owning observation.
            }
            return principal;
        }
    }

    private static IEnumerable<Exception> LeafCauses(Exception error)
    {
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Count != 0)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in LeafCauses(inner)) yield return cause;
        else yield return error; // Empty compounds remain actual unknown errors.
    }

    private static async Task Collect(Task original, List<Exception> errors, Exception? asserted = null)
    {
        try { await original; }
        catch (Exception observed)
        {
            if (ReferenceEquals(observed, asserted)) return;
            var faults = original.Exception;
            if (faults is null) Add(errors, observed);
            else foreach (var error in faults.InnerExceptions) Add(errors, error);
        }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Actual Home frontdoor fixture and independent cleanup failed.", errors);
    }
}
