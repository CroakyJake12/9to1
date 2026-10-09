#if !ANDROID
using System.Collections;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    public static bool UsesLinuxNativeCapabilityFixture => OperatingSystem.IsLinux();

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxNativeCapabilityFixture),
        Skip = "The maintained kernel/Home fixture is Linux-only; Windows acceptance is separate.")]
    public Task Rendered_capability_setup_requires_real_Home_approval_then_saves_an_issued_Tool_preference() =>
        RunWithOriginalDen(rig => WithActualCapabilitySetup(rig, async setup =>
        {
            Assert.Empty((await setup.ReadActualRowsAsync()).Definitions);
            await setup.WithShownViewAsync(async view =>
            {
                var definition = await view.CreateSavedAssistantAsync("Fictional capability helper");
                await view.ReviewSetupAsync();
                view.Click("assistant-tools-setup-start");
                var request = await setup.WaitForActualApprovalAsync(view);
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.Empty((await setup.ReadActualRowsAsync()).Definitions);
                Assert.True((await rig.Keep(setup.Permissions.DecideAsync(request.RequestId,
                    HomeApprovalChoice.Accept, cancellationToken: rig.Token))).Succeeded);
                await view.WaitAsync(() => view.StatusContains("setup completed") && view.CanReadActualChooser());
                var actual = Assert.IsType<AssistantDefinitionSnapshot>(view.Controller.Snapshot.SelectedAssistant);
                Assert.Equal(definition.Identity, actual.Identity);
                Assert.Equal(definition.Revision, actual.Revision);
                await view.ChooseAndSaveActualToolAsync(setup);
                actual = Assert.IsType<AssistantDefinitionSnapshot>(view.Controller.Snapshot.SelectedAssistant);
                Assert.Equal(definition.Identity, actual.Identity);
                Assert.True(actual.Revision > definition.Revision);
                Assert.Empty(view.Controller.Snapshot.Conversations); // No Task/conversation/model run was fabricated by setup.
            });
            await setup.WithShownViewAsync(async view =>
            {
                await view.OpenSavedConfigurationAsync();
                var actual = Assert.IsType<AssistantDefinitionSnapshot>(view.Controller.Snapshot.SelectedAssistant);
                Assert.Single(actual.Configuration.ToolIds);
                Assert.Empty(view.Controller.Snapshot.Conversations);
                await view.ReadActualChooserAsync();
                Assert.NotEmpty(view.CurrentRows());
            });
        }));

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxNativeCapabilityFixture),
        Skip = "The maintained kernel/Home fixture is Linux-only; Windows acceptance is separate.")]
    public Task Closing_a_rendered_setup_view_preserves_the_same_pending_Home_request_and_reopened_chooser() =>
        RunWithOriginalDen(rig => WithActualCapabilitySetup(rig, async setup =>
        {
            HomePermissionRequest? pending = null; AssistantIdentity? identity = null;
            await setup.WithShownViewAsync(async view =>
            {
                identity = (await view.CreateSavedAssistantAsync("Fictional pending setup helper")).Identity;
                await view.ReviewSetupAsync(); view.Click("assistant-tools-setup-start");
                pending = await setup.WaitForActualApprovalAsync(view);
                Assert.Empty((await setup.ReadActualRowsAsync()).Definitions);
                Assert.True(await rig.Keep(view.Surface.PrepareToCloseAsync(rig.Token)));
                // The helper now independently joins the actual surface/controller.
                // Their delivery retirement does not cancel the issuer's SQL/Home process.
            });
            var sameRequest = Assert.IsType<HomePermissionRequest>(pending);
            var home = await rig.Keep(setup.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.Contains(home.PendingRequests, value => value.RequestId == sameRequest.RequestId);
            Assert.Null(setup.Creator.OriginalClose);
            Assert.Empty((await setup.ReadActualRowsAsync()).Definitions);
            Assert.True((await rig.Keep(setup.Permissions.DecideAsync(sameRequest.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: rig.Token))).Succeeded);
            await setup.WaitForActualBuiltInsAsync();
            await setup.WithShownViewAsync(async view =>
            {
                await view.OpenSavedConfigurationAsync();
                var actual = Assert.IsType<AssistantDefinitionSnapshot>(view.Controller.Snapshot.SelectedAssistant);
                Assert.Equal(identity, actual.Identity); Assert.Empty(view.Controller.Snapshot.Conversations);
                await view.ReadActualChooserAsync(); Assert.NotEmpty(view.CurrentRows());
                await view.ChooseAndSaveActualToolAsync(setup);
            });
            home = await rig.Keep(setup.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.DoesNotContain(home.PendingRequests, value => value.RequestId == sameRequest.RequestId);
        }));

    private sealed partial class Rig
    {
        // The SAME privately constructed owning path object, not a new path/DB or grant.
        internal IAppPaths CapabilitySetupOriginalPaths => _paths;
    }

    private static async Task WithActualCapabilitySetup(Rig rig, Func<NativeCapabilitySetupGraph, Task> body)
    {
        var actual = new NativeCapabilitySetupGraph(rig); var errors = new List<Exception>();
        try { actual.InitializeOriginalOwners(); await body(actual); } catch (Exception cause) { errors.Add(cause); }
        await actual.JoinOriginalsAsync(errors);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual native capability setup retained at " + rig.Root, errors);
    }

    private sealed class NativeCapabilitySetupGraph
    {
        internal readonly Rig Rig;
        private CanonicalCapabilityCatalogueReadOwner? _catalogue;
        private CanonicalCapabilityCatalogueInitializationOwner? _creator;
        private HomeCapabilityCatalogueInitializationWriteSource? _writes;
        private HomePermissionTrustService? _permissions;
        private AssistantOriginalCapabilityOwner? _capabilities;
        internal CanonicalCapabilityCatalogueReadOwner Catalogue => _catalogue ?? throw new InvalidOperationException("Actual catalogue was not captured.");
        internal CanonicalCapabilityCatalogueInitializationOwner Creator => _creator ?? throw new InvalidOperationException("Actual process creator was not captured.");
        internal HomeCapabilityCatalogueInitializationWriteSource Writes => _writes ?? throw new InvalidOperationException("Actual WRITE source was not captured.");
        internal HomePermissionTrustService Permissions => _permissions ?? throw new InvalidOperationException("Actual Home permission source was not captured.");
        internal AssistantOriginalCapabilityOwner Capabilities => _capabilities ?? throw new InvalidOperationException("Actual Core source was not captured.");
        private readonly List<NativeCapabilitySetupView> _views = [];

        internal NativeCapabilitySetupGraph(Rig rig) => Rig = rig;
        internal void InitializeOriginalOwners()
        {
            var rig = Rig;
            var repository = new CapabilityRepository(rig.Database);
            var registry = new CapabilityRegistryService(repository);
            _catalogue = new(rig.Store, rig.Database, rig.CapabilitySetupOriginalPaths,
                repository, registry, rig.Home.Ownership);
            _creator = new(Catalogue);
            var policy = new HomeCapabilityCatalogueInitializationActionPolicySource();
            _permissions = new(rig.Home.StateStore, policy.TryGet);
            HomeCapabilityCatalogueInitializationWriteSource? sameWrites = null;
            var resources = new ResourceAuthorizationService(rig.Home.Profiles,
                [new HomeCapabilityCatalogueInitializationResourceResolver(() => sameWrites
                    ?? throw new InvalidOperationException("The actual setup WRITE owner was not captured."))]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            _writes = sameWrites = new(rig.Home.StateStore, rig.Home.Profiles, resources, broker, Permissions, Creator);
            Creator.BindOriginalHomeWriteSource(Writes);
            var den = rig.DenFactory ?? throw new InvalidOperationException("Use the SAME initialized genuine Den fixture.");
            _capabilities = new(den, rig.Provider.GetRequiredService<IConversationRepository>(), registry,
                rig.Provider.GetRequiredService<ChatSessionService>(), Catalogue, sameInitialization: Creator);
        }

        internal Task<ICapabilityOriginalRepositoryObservation> ReadActualRowsAsync() => Rig.Keep(
            Catalogue.ReadOriginalCapabilitiesWithinSourceAsync(Rig.Actor, Rig.Scope, Rig.Retain, Rig.Token));

        internal async Task WaitForActualBuiltInsAsync()
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                var actual = await ReadActualRowsAsync();
                Assert.True(Catalogue.IsIssuedOriginalRepositoryObservation(actual));
                if (CapabilityRegistryCatalog.BuiltIns.All(expected => actual.Definitions.Any(row => row.Id == expected.Id))) return;
                await Task.Delay(50, Rig.Token);
            }
            throw new TimeoutException("The actual protected catalogue did not observe the accepted setup pair.");
        }

        internal async Task<HomePermissionRequest> WaitForActualApprovalAsync(NativeCapabilitySetupView view)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                await view.FlushAsync(); view.DemandNoActionFailure();
                var actual = await Rig.Keep(Permissions.GetSnapshotAsync(cancellationToken: Rig.Token));
                var pending = actual.PendingRequests.Where(value => value.Scope.ActionName ==
                    HomeCapabilityCatalogueInitializationWriteSource.WriteAction).ToArray();
                if (pending.Length != 0) return Assert.Single(pending);
                await Task.Delay(10, Rig.Token);
            }
            throw new TimeoutException("The rendered action did not create its actual individual Home request.");
        }

        internal async Task WithShownViewAsync(Func<NativeCapabilitySetupView, Task> body)
        {
            var den = Rig.DenFactory ?? throw new InvalidOperationException("The actual Den was not captured.");
            var business = Rig.OriginalOrdinaryBusiness ?? throw new InvalidOperationException("The actual ordinary host was not captured.");
            var controller = AssistantsWorkspaceFactory.Create(den,
                Rig.Provider.GetRequiredService<IConversationRepository>(),
                Rig.Provider.GetRequiredService<IConversationProductionRepository>(),
                Rig.Provider.GetRequiredService<ChatSessionService>(),
                Rig.Provider.GetRequiredService<TaskExecutionCoordinator>(), business, actualCapabilities: Capabilities);
            var view = new NativeCapabilitySetupView(this, controller); _views.Add(view);
            var errors = new List<Exception>();
            try { await view.InitializeAsync(); await body(view); Assert.True(await Rig.Keep(view.Surface.PrepareToCloseAsync(Rig.Token))); }
            catch (Exception cause) { errors.Add(cause); }
            await view.JoinOriginalsAsync(errors);
            if (errors.Count != 0) throw new AggregateException("Actual rendered setup view originals retained.", errors);
        }

        internal async Task JoinOriginalsAsync(List<Exception> errors)
        {
            foreach (var view in _views) await view.JoinOriginalsAsync(errors);
            try { _creator?.RequestOriginalPendingReviewWithdrawals(); } catch (Exception cause) { errors.Add(cause); }
            var closers = new List<Func<Task>>();
            if (_capabilities is { } capabilities) closers.Add(capabilities.CloseAndDrainAsync);
            if (_creator is { } creator) closers.Add(creator.CloseAndDrainOriginalAsync);
            if (_writes is { } writes) closers.Add(writes.CloseAndDrainOriginalAsync);
            foreach (var acquire in closers)
            {
                Task? raw = null;
                try { raw = Rig.Keep(acquire()); await raw; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
            }
        }
    }

    private sealed class NativeCapabilitySetupView(NativeCapabilitySetupGraph graph, AssistantsWorkspaceController controller)
    {
        internal readonly Window Window = new() { Width = 1000, Height = 850 };
        internal AssistantsWorkspaceController Controller => controller;
        private AssistantsNativeCuiSurface? _surface;
        internal AssistantsNativeCuiSurface Surface => _surface ?? throw new InvalidOperationException("The actual partial surface was not captured.");

        internal async Task InitializeAsync()
        {
            Window.Show();
            _surface = new(controller, new CapabilitySetupFixtureReadiness(),
                captureOriginalOwner: actual => _surface = actual);
            Window.Content = Surface; await graph.Rig.Keep(Surface.InitializeAsync(graph.Rig.Token)); await FlushAsync();
        }
        internal Task FlushAsync() => graph.Rig.Keep(Dispatcher.UIThread.InvokeAsync(Window.UpdateLayout,
            DispatcherPriority.Background).GetTask());
        internal void DemandNoActionFailure() => Assert.All(Window.GetVisualDescendants().OfType<CuiSceneHost>(),
            actual => Assert.Null(actual.LastActionFailure));
        internal void Click(string id)
        {
            DemandNoActionFailure();
            var button = Assert.Single(Window.GetVisualDescendants().OfType<Button>(),
                actual => actual.IsEffectivelyVisible && actual.Name == id);
            Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        internal async Task WaitAsync(Func<bool> settled)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                await FlushAsync(); DemandNoActionFailure(); if (settled()) return;
                Assert.False(Surface.IsRetiring); await Task.Delay(10, graph.Rig.Token);
            }
            throw new TimeoutException("The actual rendered capability action did not settle.");
        }
        internal bool StatusContains(string text) => Surface.Bindings.TryGetValue("CapabilityInitializationStatus", out var actual)
            && actual is string detail && detail.Contains(text, StringComparison.OrdinalIgnoreCase);
        internal async Task<AssistantDefinitionSnapshot> CreateSavedAssistantAsync(string name)
        {
            Click("assistant-create"); await WaitAsync(() => Surface.Bindings.Draft is not null);
            Assert.True(Surface.Bindings.TrySetValue("DraftName", name)); await FlushAsync(); Click("assistant-save");
            await WaitAsync(() => Controller.Snapshot.SelectedAssistant?.Configuration.Name == name
                && Surface.Bindings.Draft?.IsDirty == false);
            return Assert.IsType<AssistantDefinitionSnapshot>(Controller.Snapshot.SelectedAssistant);
        }
        internal async Task ReviewSetupAsync()
        {
            Click("assistant-tools-setup-review");
            await WaitAsync(() => Surface.Bindings.TryGetValue("CanStartCapabilityInitialization", out var actual) && actual is true);
        }
        internal async Task OpenSavedConfigurationAsync()
        {
            Assert.True(Surface.Bindings.TryGetValue("Assistants", out var actual));
            var row = Assert.Single(Assert.IsAssignableFrom<IEnumerable>(actual).Cast<AssistantsCuiBindings.AssistantRow>());
            await graph.Rig.Keep(Surface.Bindings.DispatchAsync("assistants.open", row, graph.Rig.Token).AsTask());
            await graph.Rig.Keep(Surface.Bindings.DispatchAsync("assistants.configuration.open", null, graph.Rig.Token).AsTask());
            await FlushAsync();
        }
        internal object[] CurrentRows()
        {
            Assert.True(Surface.Bindings.TryGetValue("ConfigurationCapabilityRows", out var actual));
            return Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object>().ToArray();
        }
        internal bool CanReadActualChooser() => Surface.Bindings.TryGetValue("CanReadConfigurationCapabilities", out var actual) && actual is true;
        internal async Task ReadActualChooserAsync()
        {
            await WaitAsync(CanReadActualChooser);
            var previous = CurrentRows();
            var button = Assert.Single(Window.GetVisualDescendants().OfType<Button>(), actual => actual.IsEffectivelyVisible
                && actual.Content as string == "Review current Tools and Apps");
            Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => CanReadActualChooser() && CurrentRows() is { Length: > 0 } rows &&
                rows.Any(row => !previous.Any(old => ReferenceEquals(old, row))));
        }
        internal async Task ChooseAndSaveActualToolAsync(NativeCapabilitySetupGraph setup)
        {
            await ReadActualChooserAsync();
            var row = CurrentRows().First(actual => Surface.Bindings.TryGetItemValue(actual, "CanChoose", out var allowed) && allowed is true);
            Assert.True(Surface.Bindings.TryGetItemValue(row, "Name", out var name));
            var metadata = await setup.ReadActualRowsAsync();
            var selected = Assert.Single(metadata.Definitions, actual => actual.Name == Assert.IsType<string>(name));
            Assert.True(selected.IsBuiltIn);
            var button = Window.GetVisualDescendants().OfType<Button>().First(actual => actual.IsEffectivelyVisible
                && actual.IsEnabled && actual.Content as string == "Add this preference");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Surface.Bindings.Draft?.Configuration.ToolIds.Contains(selected.Id.ToString("D")) == true);
            Click("assistant-save");
            await WaitAsync(() => Surface.Bindings.Draft?.IsDirty == false &&
                Controller.Snapshot.SelectedAssistant?.Configuration.ToolIds.Contains(selected.Id.ToString("D")) == true);
        }
        internal async Task JoinOriginalsAsync(List<Exception> errors)
        {
            if (_surface is not null)
            {
                try { _surface.RequestRetirement(); } catch (Exception cause) { errors.Add(cause); }
                Task? raw = null;
                try { raw = graph.Rig.Keep(_surface.CloseAndDrainAsync()); await raw; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
            }
            Task? close = null;
            try { close = graph.Rig.Keep(controller.CloseAndDrainAsync()); await close; }
            catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
            if (_surface?.OriginalClose?.IsCompletedSuccessfully == true && controller.OriginalClose?.IsCompletedSuccessfully == true)
                Window.Close();
        }
    }

    private sealed class CapabilitySetupFixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ActualHeadlessFixture",
                "Local CUI availability only; actual Home/Den/store sources own every permission. No installed qualification."));
    }
}
#endif
