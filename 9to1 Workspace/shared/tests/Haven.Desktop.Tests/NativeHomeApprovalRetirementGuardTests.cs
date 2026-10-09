using System.Runtime.ExceptionServices;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;
using HomeObjectReference = HavenOS.Home.PermissionsTrustNotifications.HomeObjectReference;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using HomePermissionRequestState = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState;

namespace Haven.Desktop.Tests;

// Headless source-ownership controls only; no presented-frame or installed authority proof.
public sealed class NativeHomeApprovalRetirementGuardTests
{
    [AvaloniaFact]
    public async Task Live_original_owner_preflight_refuses_before_seal_then_external_request_seals_new_opens()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-native-home-guard48-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var permissions = new HomePermissionTrustService(store, (_, _) => null);
        var runtime = new HomeCoreRuntime([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, profiles)]);
        var owner = new HomeNativeApprovalWindowOwner(runtime, profiles, permissions);
        var errors = new List<Exception>();
        Task? ownerClose = null, runtimeClose = null;
        try
        {
            using (CloudflareOriginalExecutionGuard.EnterOriginal(owner))
            {
                Assert.Throws<InvalidOperationException>(owner.DemandExternalOriginalRetirementJoin);
                Assert.Throws<InvalidOperationException>(owner.RequestRetirement);
                Assert.Null(owner.OriginalClose);
            }
            owner.DemandExternalOriginalRetirementJoin();
            owner.RequestRetirement();
            Assert.Null(owner.OriginalClose); // Request seals admission; it cannot stand in for the held drain.
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.OpenOriginalAsync(); });
            ownerClose = owner.CloseAndDrainAsync(); Assert.Same(ownerClose, owner.CloseAndDrainAsync()); await ownerClose;
            Assert.True(owner.OriginalRetirementCapturedAndSettled);
            Assert.Null(owner.OriginalWindow); Assert.Null(owner.OriginalSurface);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            try { ownerClose ??= owner.CloseAndDrainAsync(); } catch (Exception error) { Add(errors, error); }
            if (ownerClose is not null)
                try { await ownerClose; } catch (Exception error) { Add(errors, error); }
            // Keep Home alive when its native borrower has not reached the original drain.
            if (ownerClose?.IsCompleted == true)
            {
                try { runtimeClose = runtime.DisposeAsync().AsTask(); } catch (Exception error) { Add(errors, error); }
                if (runtimeClose is not null)
                    try { await runtimeClose; } catch (Exception error) { Add(errors, error); }
            }
            if (ownerClose?.IsCompleted == true && runtimeClose?.IsCompleted == true)
                try { Directory.Delete(root, true); } catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Native review guard and independent cleanup failed.", errors);
    }

    [AvaloniaFact]
    public async Task Actual_refocus_read_model_notification_and_window_close_callbacks_cannot_join_parent_under_restored_context()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-native-home-callback48-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>();
        var beforeOriginal = ExecutionContext.Capture() ?? throw new InvalidOperationException("Capture the genuine pre-original context.");
        var principals = new ObservingPrincipal(new OperatingSystemPrincipalSource());
        HomeCoreRuntime? runtime = null;
        HomeNativeApprovalWindowOwner? owner = null;
        Task? start = null, open = null, focus = null, ownerClose = null, runtimeClose = null;
        CuiViewModel? model = null; Window? window = null;
        PropertyChangedEventHandler? notified = null; EventHandler<WindowClosingEventArgs>? closing = null;
        var created = false; var readCallbacks = 0; var modelCallbacks = 0; var closeCallbacks = 0;
        try
        {
            Directory.CreateDirectory(root); created = true;
            var token = TestContext.Current.CancellationToken;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, principals);
            var actor = await profiles.GetCurrentAsync(token); Assert.NotNull(actor);
            var permissions = new HomePermissionTrustService(store, (app, action) => app == "write" && action == "write.file.save"
                ? new(HomePermissionRisk.Elevated, true, false, true) : null);
            async Task<string> Prepare(string id)
            {
                var target = new HomeObjectReference("files.item", Guid.NewGuid().ToString("N"));
                var request = await permissions.AuthorizeAsync(new(null,
                    new(actor.ActorId, "Actual local test caller", actor.ProfileId, actor.AuthenticationRevision, true),
                    id, new("write", "write.file.save", [target]),
                    new(["files.item"], 1, [target], false, "Review " + id)), token);
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                return request.RequestId;
            }
            var first = await Prepare("callback-first"); var second = await Prepare("callback-second");
            runtime = new HomeCoreRuntime([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, profiles)]);
            start = runtime.StartAsync(token); await start;
            owner = new HomeNativeApprovalWindowOwner(runtime, profiles, permissions);
            open = owner.OpenOriginalAsync(first, token); await open;
            window = Assert.IsType<Window>(owner.OriginalWindow);
            var surface = Assert.IsType<HomeApprovalCuiSurface>(owner.OriginalSurface);
            model = Assert.IsType<CuiViewModel>(typeof(HomeApprovalCuiSurface)
                .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(surface));
            void RefuseBeforeEffect()
            {
                Assert.Throws<InvalidOperationException>(owner.DemandExternalOriginalRetirementJoin);
                Assert.Throws<InvalidOperationException>(owner.RequestRetirement);
                Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
                Assert.Null(owner.OriginalClose);
            }
            principals.Callback = () => ExecutionContext.Run(beforeOriginal.CreateCopy(), _ =>
            {
                readCallbacks++; RefuseBeforeEffect();
            }, null);
            notified = (_, _) => ExecutionContext.Run(beforeOriginal.CreateCopy(), _ =>
            {
                modelCallbacks++; RefuseBeforeEffect();
            }, null);
            model.PropertyChanged += notified;
            focus = owner.OpenOriginalAsync(second, token); await focus;
            Assert.Equal(1, readCallbacks); Assert.True(modelCallbacks > 0);
            Assert.Same(window, owner.OriginalWindow); Assert.Same(surface, owner.OriginalSurface);
            model.PropertyChanged -= notified; notified = null;
            // The owner invokes this SAME real Window.Close after surface drain. Restoring
            // the earlier context cannot turn its synchronous native callback into an external join.
            closing = (_, _) => ExecutionContext.Run(beforeOriginal.CreateCopy(), _ =>
            {
                closeCallbacks++;
                var published = Assert.IsAssignableFrom<Task>(owner.OriginalClose);
                Assert.Throws<InvalidOperationException>(owner.DemandExternalOriginalRetirementJoin);
                Assert.Throws<InvalidOperationException>(owner.RequestRetirement);
                Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
                Assert.Same(published, owner.OriginalClose);
            }, null);
            window.Closing += closing;
            ownerClose = owner.CloseAndDrainAsync(); await ownerClose;
            Assert.Equal(1, closeCallbacks);
            Assert.True(owner.OriginalRetirementCapturedAndSettled);
            Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).Grants);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            principals.Callback = null;
            if (model is not null && notified is not null) model.PropertyChanged -= notified;
            if (window is not null && closing is not null) window.Closing -= closing;
            if (owner is not null)
                try { ownerClose ??= owner.CloseAndDrainAsync(); } catch (Exception error) { Add(errors, error); }
            foreach (var original in new[] { start, open, focus, ownerClose }.Where(task => task is not null))
                try { await original!; } catch (Exception error) { Add(errors, error); }
            if (runtime is not null && (owner is null || ownerClose?.IsCompleted == true))
            {
                try { runtimeClose = runtime.DisposeAsync().AsTask(); } catch (Exception error) { Add(errors, error); }
                if (runtimeClose is not null)
                    try { await runtimeClose; } catch (Exception error) { Add(errors, error); }
            }
            if (created && (owner is null || ownerClose?.IsCompleted == true) && (runtime is null || runtimeClose?.IsCompleted == true))
                try { Directory.Delete(root, true); } catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Native original callbacks and independent cleanup failed.", errors);
    }

    private sealed class ObservingPrincipal(ITrustedHostPrincipalSource actual) : ITrustedHostPrincipalSource
    {
        internal Action? Callback;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var original = actual.GetPrincipalAsync(token);
            Interlocked.Exchange(ref Callback, null)?.Invoke();
            return original; // The real principal source and original ValueTask remain unchanged.
        }
    }

    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error);
    }
}
