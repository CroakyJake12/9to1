using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;

namespace Haven.Desktop.Tests;

/// <summary>Supplemental actual embedded-host notification/lifetime control. Source-only;
/// the real local fixture owner is not an installed Home or permission receipt.</summary>
public sealed class HomeApprovalOriginalHostLifetimeTests
{
    [AvaloniaFact]
    public async Task Actual_background_retirement_publishes_host_stop_before_callback_returns_and_drains_before_surface_detach()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-approval-host-close-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>();
        var expectedCauses = new List<Exception>();
        var createdRoot = false;
        HomeCoreRuntime? runtime = null;
        HomeApprovalCuiSurface? surface = null;
        CuiSceneHost? host = null;
        Task? originalInitialization = null;
        Task? originalSurfaceClose = null;
        Task? originalHostCloseAtCallback = null;
        Task? originalRuntimeClose = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? surfaceChanges = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? hostChanges = null;
        var retiredAtBackground = false;
        var hostStopPublishedBeforeCallbackReturned = false;
        Exception? callbackFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            createdRoot = true;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(store, (_, _) => null);
            runtime = new HomeCoreRuntime([new HomeCoreStateService(store),
                new HomePermissionsCoreService(permissions, profiles)]);
            surface = new HomeApprovalCuiSurface(runtime, profiles, permissions);
            hostChanges = (_, args) =>
            {
                if (retiredAtBackground || args.Property.Name != "Background") return;
                retiredAtBackground = true;
                try
                {
                    // Actual synchronous native notification: request only, never self-join this load.
                    originalSurfaceClose = surface.CloseAndDrainAsync();
                    originalHostCloseAtCallback = host!.OriginalClose;
                    hostStopPublishedBeforeCallbackReturned = originalHostCloseAtCallback is not null;
                }
                catch (Exception error)
                {
                    callbackFailure = error;
                    throw;
                }
            };
            surfaceChanges = (_, args) =>
            {
                if (args.Property.Name != "Content" || host is not null ||
                    surface.Content is not CuiSceneHost actualHost) return;
                host = actualHost;
                actualHost.PropertyChanged += hostChanges;
            };
            surface.PropertyChanged += surfaceChanges;
            originalInitialization = surface.InitializeAsync(TestContext.Current.CancellationToken);
            var initializationFailure = await Record.ExceptionAsync(() => originalInitialization!);

            Assert.True(retiredAtBackground);
            Assert.True(hostStopPublishedBeforeCallbackReturned);
            Assert.Null(callbackFailure);
            Assert.NotNull(host);
            Assert.NotNull(originalSurfaceClose);
            Assert.NotNull(originalHostCloseAtCallback);
            var originalBodyCancellation = Assert.IsAssignableFrom<OperationCanceledException>(initializationFailure);
            expectedCauses.Add(originalBodyCancellation); // Only after actual notification and exact cause observation.
            Assert.Same(originalSurfaceClose, surface.CloseAndDrainAsync());
            Assert.Same(originalHostCloseAtCallback, host.OriginalClose);
            Assert.Same(originalHostCloseAtCallback, host.CloseOriginalAsync());

            var closeFailure = await Record.ExceptionAsync(() => originalSurfaceClose!);
            Assert.NotNull(closeFailure);
            Assert.All(LeafCauses(closeFailure), cause => Assert.Same(originalBodyCancellation, cause));
            Assert.True(originalHostCloseAtCallback.IsCompleted);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Assert.Null(surface.Content);
                Assert.Null(host.Availability);
                Assert.Empty(host.Diagnostics);
                Assert.Empty(host.Resources.MergedDictionaries);
            });
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            // Request stop before joining any dependent load; retain every returned original immediately.
            if (surface is not null)
            {
                try { originalSurfaceClose ??= surface.CloseAndDrainAsync(); }
                catch (Exception error) { Add(errors, error); }
            }
            await CollectAsync(originalInitialization, expectedCauses, errors);
            await CollectAsync(originalSurfaceClose, expectedCauses, errors);
            if (host is not null)
            {
                try { originalHostCloseAtCallback ??= host.OriginalClose; }
                catch (Exception error) { Add(errors, error); }
            }
            await CollectAsync(originalHostCloseAtCallback, expectedCauses, errors);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Attempt(errors, () => { if (surface is not null && surfaceChanges is not null) surface.PropertyChanged -= surfaceChanges; });
                    Attempt(errors, () => { if (host is not null && hostChanges is not null) host.PropertyChanged -= hostChanges; });
                });
            }
            catch (Exception error) { Add(errors, error); }
            if (callbackFailure is not null && !expectedCauses.Any(cause => ReferenceEquals(cause, callbackFailure)))
                Add(errors, callbackFailure);
            if (runtime is not null)
            {
                try { originalRuntimeClose = runtime.DisposeAsync().AsTask(); }
                catch (Exception error) { Add(errors, error); }
            }
            await CollectAsync(originalRuntimeClose, [], errors);
            if (createdRoot && (runtime is null || originalRuntimeClose?.IsCompleted == true) &&
                (surface is null || originalSurfaceClose?.IsCompleted == true))
            {
                try { Directory.Delete(root, true); }
                catch (Exception error) { Add(errors, error); }
            }
            else if (createdRoot)
                Add(errors, new InvalidOperationException("Retain native/runtime close evidence before deleting the original fixture state."));
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original native Home approval fixture and independent cleanup failed.", errors);
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
