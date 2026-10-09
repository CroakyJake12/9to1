#if !ANDROID
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using ActualHomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [Fact]
    public void Automation_library_actual_Microsoft_DI_outer_and_factory_scope_are_the_same_configured_pair_only()
    {
        IServiceProvider? factoryScope = null;
        var registrations = new ServiceCollection();
        registrations.AddSingleton<AutomationProviderProbe>(actual => { factoryScope = actual; return new(); });
        using var configured = registrations.BuildServiceProvider();
        _ = configured.GetRequiredService<AutomationProviderProbe>();
        Assert.NotNull(factoryScope); Assert.NotSame(configured, factoryScope);
        Assert.Same(configured.GetRequiredService<IServiceProvider>(), factoryScope);
        Assert.True(App.HasOriginalAutomationLibraryProviderTuple(factoryScope, configured));
        Assert.True(App.HasOriginalAutomationLibraryProviderTuple(configured, configured));
        using var foreign = registrations.BuildServiceProvider();
        Assert.False(App.HasOriginalAutomationLibraryProviderTuple(foreign, configured));
        Assert.False(App.HasOriginalAutomationLibraryProviderTuple(foreign.GetRequiredService<IServiceProvider>(), configured));
    }
    private sealed class AutomationProviderProbe { }

    [AvaloniaTheory(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    [InlineData(false)]
    [InlineData(true)]
    public Task Saved_library_actual_review_and_disable_buttons_require_individual_Home_accept_and_preserve_all_other_row_fields(bool recover) =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var definitions = await SeedSavedAutomationLibrary(rig, repository, 2);
            await WithActualAutomationChanges(rig, Assert.IsType<CanonicalAutomationLibraryOriginalReadOwner>(source), async graph =>
            {
                await WithActualChangeLibrary(rig, graph, async (window, page) =>
                {
                    var row = SavedLibraryRows(page).Single(value => value.Name == definitions[1].Name);
                    var before = await graph.SelectedRaw(definitions[1].Id);
                    var selection = page.GetVisualDescendants().OfType<Button>().Single(value =>
                        Label(value) == "View saved settings" && IsRowButton(value, row.Name));
                    selection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await rig.Keep(page.Bindings.OriginalCommand!);
                    await PumpSavedLibrary(window);
                    var change = page.GetVisualDescendants().OfType<Button>().Single(value => Label(value) ==
                        (recover ? "Review saved settings" : "Disable saved automation"));
                    Assert.True(change.IsEnabled); change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var actual = rig.Keep(page.Bindings.OriginalCommand ?? throw new InvalidOperationException("No actual CUI change action was dispatched."));
                    var request = await graph.WaitReview(actual);
                    Assert.Equal(recover ? HomeCanonicalAutomationDefinitionWriteSource.RecoveryAction :
                        HomeCanonicalAutomationDefinitionWriteSource.DisableAction, request.Scope.ActionName);
                    Assert.True(request.Policy.RequiresPerActionApproval); Assert.False(actual.IsCompleted);
                    Assert.Equal(before, await graph.SelectedRaw(definitions[1].Id));
                    Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                        cancellationToken: rig.Token))).Succeeded);
                    await actual; await PumpSavedLibrary(window);
                    Assert.Contains(recover ? "marked for attention" : "is disabled", BindingValue<string>(page, "ChangeStatus"));
                    var after = await graph.SelectedRaw(definitions[1].Id);
                    foreach (var pair in before.Where(value => value.Key != "is_enabled")) Assert.Equal(pair.Value, after[pair.Key]);
                    Assert.False((await rig.Keep(repository.GetAllAsync(rig.Token))).Single(value => value.Id == definitions[1].Id).IsEnabled);
                    Assert.Equal(definitions[0], (await rig.Keep(repository.GetAllAsync(rig.Token))).Single(value => value.Id == definitions[0].Id));
                    Assert.NotNull(page.Bindings.OriginalChangeObservation);
                    Assert.True(page.Bindings.OriginalChangeObservation!.OriginalClose!.IsCompletedSuccessfully);
                });
            });
        });

    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Saved_library_view_close_detaches_pending_delivery_before_commands_join_then_same_actual_process_approval_commits() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary(); var definitions = await SeedSavedAutomationLibrary(rig, repository, 2);
            await WithActualAutomationChanges(rig, Assert.IsType<CanonicalAutomationLibraryOriginalReadOwner>(source), async graph =>
            {
                await WithActualChangeLibrary(rig, graph, async (window, page) =>
                {
                    var row = SavedLibraryRows(page).Single(value => value.Name == definitions[1].Name);
                    await rig.Keep(page.Bindings.DispatchAsync("select", row.Target, rig.Token).AsTask());
                    await PumpSavedLibrary(window);
                    var button = page.GetVisualDescendants().OfType<Button>().Single(value => Label(value) == "Disable saved automation");
                    Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var viewCommand = rig.Keep(page.Bindings.OriginalCommand!); var request = await graph.WaitReview(viewCommand);
                    var clock = Stopwatch.StartNew();
                    while (page.Bindings.OriginalChangeObservation is null && clock.Elapsed < TimeSpan.FromSeconds(15))
                    { if (viewCommand.IsCompleted) { await viewCommand; throw new InvalidOperationException("The actual delivery did not publish."); } await PumpSavedLibrary(window); await Task.Delay(10, rig.Token); }
                    var observation = page.Bindings.OriginalChangeObservation ?? throw new InvalidOperationException("No actual retained delivery.");
                    Assert.True(graph.Writer.IsIssuedOriginalChangeObservation(observation));
                    // Only this owning fixture acquires the SAME cached business task.
                    // The view obtains an observation, never this pending raw driver.
                    var business = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(observation.OriginalIntent,
                        rig.Scope, rig.Retain, rig.Token));
                    Assert.False(business.IsCompleted); Assert.False(viewCommand.IsCompleted);
                    var close = rig.Keep(page.CloseAndDrainAsync()); Assert.Same(close, page.OriginalClose);
                    await close.WaitAsync(TimeSpan.FromSeconds(15), rig.Token); await viewCommand;
                    Assert.Null(page.Content); Assert.False(business.IsCompleted);
                    Assert.Contains((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests,
                        value => value.RequestId == request.RequestId);
                    Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                        cancellationToken: rig.Token))).Succeeded);
                    var acknowledgment = await business;
                    Assert.Equal(observation.OriginalIntent.OperationId, acknowledgment.OperationId);
                    Assert.False(acknowledgment.Definition.IsEnabled); Assert.True(close.IsCompletedSuccessfully);
                });
            });
        });

    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Saved_library_recorded_legacy_run_returns_source_issued_unavailable_preparation_without_review_or_view_close_fault() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary(); var definitions = await SeedSavedAutomationLibrary(rig, repository, 2);
            Assert.True(await rig.Keep(repository.TryAcquireLeaseAsync(definitions[1].Id, "actual-legacy-run",
                DateTimeOffset.UtcNow.AddMinutes(30), rig.Token)));
            await WithActualAutomationChanges(rig, Assert.IsType<CanonicalAutomationLibraryOriginalReadOwner>(source), async graph =>
            {
                await WithActualChangeLibrary(rig, graph, async (window, page) =>
                {
                    var row = SavedLibraryRows(page).Single(value => value.Name == definitions[1].Name);
                    await rig.Keep(page.Bindings.DispatchAsync("select", row.Target, rig.Token).AsTask());
                    var before = await graph.SelectedRaw(definitions[1].Id);
                    await rig.Keep(page.Bindings.DispatchAsync("disable", null, rig.Token).AsTask()); await PumpSavedLibrary(window);
                    Assert.Contains("Review is unavailable", BindingValue<string>(page, "ChangeStatus"));
                    Assert.Null(page.Bindings.OriginalChangeObservation);
                    Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
                    Assert.Equal(before, await graph.SelectedRaw(definitions[1].Id));
                });
            });
        });

    private static async Task WithActualAutomationChanges(Rig rig, CanonicalAutomationLibraryOriginalReadOwner library,
        Func<ActualLibraryChanges, Task> body)
    {
        var graph = new ActualLibraryChanges(rig, library); var failures = new List<Exception>();
        try { await body(graph); } catch (Exception cause) { failures.Add(cause); }
        foreach (var acquire in new Func<Task>[] { graph.Writer.CloseAndDrainOriginalAsync, graph.Writes.CloseAndDrainOriginalAsync })
        {
            Task? close = null;
            try { close = rig.Keep(acquire()); await close; } catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual library Home/write originals failed; fixtures retained.", failures);
    }
    private static async Task WithActualChangeLibrary(Rig rig, ActualLibraryChanges graph,
        Func<Window, OriginalAutomationLibraryDesktopPage, Task> body)
    {
        using var app = new CancellationTokenSource(); using var native = new CancellationTokenSource();
        var window = new Window { Width = 1100, Height = 1000 }; window.Show();
        OriginalAutomationLibraryDesktopPage? page = null; var failures = new List<Exception>(); var healthy = false;
        try
        {
            page = OriginalAutomationLibraryDesktopPage.BindOriginal(graph.Library, rig.Home.Profiles, window,
                app.Token, native.Token, actual => page = actual, graph.Writer);
            window.Content = page; await rig.Keep(page.InitializeAsync(rig.Token)); await PumpSavedLibrary(window);
            await body(window, page);
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            if (page is not null)
            {
                Task? close = null;
                try { close = rig.Keep(page.CloseAndDrainAsync()); Assert.Same(close, page.OriginalClose); await close; healthy = true; }
                catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
            }
            else healthy = true;
            if (healthy) window.Close();
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual library rendering/body/close failed; unknown originals retained.", failures);
    }
    private sealed class ActualLibraryChanges
    {
        private readonly Rig _rig;
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Library;
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Writer;
        internal readonly HomeCanonicalAutomationDefinitionWriteSource Writes;
        internal readonly HomePermissionTrustService Permissions;
        internal ActualLibraryChanges(Rig rig, CanonicalAutomationLibraryOriginalReadOwner library)
        {
            _rig = rig; Library = library; Writer = new(library);
            var policies = new HomeCanonicalAutomationDefinitionActionPolicySource();
            Permissions = new(rig.Home.StateStore, policies.TryGet);
            HomeCanonicalAutomationDefinitionWriteSource? actual = null;
            var resolver = new HomeCanonicalAutomationDefinitionResourceResolver(() => actual ?? throw new InvalidOperationException("The actual Home review source is unavailable."));
            var resources = new ResourceAuthorizationService(rig.Home.Profiles, [resolver]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            Writes = actual = new(rig.Home.StateStore, rig.Home.Profiles, resources, broker, Permissions, Writer);
            Writer.BindOriginalHomeWriteSource(Writes);
            Assert.True(Writes.HasOriginalComposition(rig.Home.StateStore, rig.Home.Profiles, Writer));
            Assert.True(resolver.IsBoundToOriginalOwner(Writes)); Assert.Same(rig.Store, Writer.OriginalStore);
        }
        internal async Task<ActualHomePermissionRequest> WaitReview(Task sameView)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                var pending = (await _rig.Keep(Permissions.GetSnapshotAsync(cancellationToken: _rig.Token))).PendingRequests
                    .Where(value => HomeCanonicalAutomationDefinitionWriteSource.IsSupportedAction(value.Scope.ActionName)).ToArray();
                if (pending.Length != 0) return Assert.Single(pending);
                if (sameView.IsCompleted) { await sameView; throw new InvalidOperationException("The actual view completed before Home review."); }
                await Task.Delay(10, _rig.Token);
            }
            throw new TimeoutException("No individual actual automation Home review.");
        }
        internal async Task<IReadOnlyDictionary<string, string?>> SelectedRaw(Guid id)
        {
            ICanonicalAutomationLibraryOriginalReadSource source = Library;
            var observed = await _rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(_rig.Actor, new(Limit: 32),
                _rig.Scope, _rig.Retain, _rig.Token));
            return Assert.Single(observed.Definitions, value => value.Value.Id == id).RetainedProtectedDescriptors;
        }
    }
}
#endif
