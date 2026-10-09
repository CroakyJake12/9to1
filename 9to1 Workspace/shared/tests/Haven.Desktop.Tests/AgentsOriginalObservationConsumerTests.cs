using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Catalog;
using Xunit;

namespace Haven.Desktop.Tests;

// Uses the existing genuine controlled source-owner path, not a UI-created
// lease, copied approval/actor, provider readiness or installed-account proof.
public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Agent_page_detaches_real_retry_observation_while_original_provider_disposal_remains_pending()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (runtime, definition) = CreateAgentCaller(rig, rows);
        Assert.IsAssignableFrom<IAgentRunOriginalObservationSource>(runtime);
        var paused = await runtime.RunAsync(definition.Id, "Original Agent observation retirement", default);
        var binding = Assert.IsType<AgentRunCanonicalBinding>(paused.CanonicalTask);
        await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, default);
        Assert.True(runtime.HasOriginalUnstartedRetrySource(paused.Id));
        rig.Client.RunApprovedOwnedFrame = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.NextDisposeEntered = entered;
        rig.Client.OriginalDispose = release.Task;
        var terminalHistory = new TaskCompletionSource<AgentRun>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CaptureTerminal(AgentRun run)
        { if (run.Id == paused.Id && run.Status == AgentRunStatus.Completed) terminalHistory.TrySetResult(run); }
        runtime.RunChanged += CaptureTerminal;
        var viewModel = CreateObservationViewModel(rig, definition);
        await viewModel.RefreshCommand.ExecuteAsync();
        var page = new AgentsPage(viewModel, runtime);
        var scene = page.HavenScene;
        Task<AgentRun?>? actual = null;
        try
        {
            await scene.RefreshRunsAsync();
            actual = scene.RetryLatestAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Drain the real queued native input/presentation callbacks before
            // sealing; source detachment cannot stand in for their custody.
            await Dispatcher.UIThread.InvokeAsync(() => { }).GetTask();
            Assert.False(actual.IsCompleted);
            Assert.False(release.Task.IsCompleted);
            Assert.Equal(1, rig.Client.Dispatches);
            var recorded = Assert.IsType<AgentRun>(await rows.GetAsync(paused.Id, default));
            Assert.NotEqual(AgentRunStatus.Completed, recorded.Status);
            Assert.Equal(binding.TaskId, recorded.CanonicalTask!.TaskId);
            Assert.Equal(binding.ExecutionId, recorded.CanonicalTask.ExecutionId);
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(close.IsCompletedSuccessfully);
            Assert.Null(await actual.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Null(page.Scene.Root);
            Assert.False(release.Task.IsCompleted); // SAME actual provider cleanup remains pending.
            Assert.False(release.Task.IsCanceled);
            Assert.False(terminalHistory.Task.IsCompleted);
            Assert.NotEqual(AgentRunStatus.Cancelled, (await rows.GetAsync(paused.Id, default))!.Status);
            release.TrySetResult();
            var acknowledgedHistory = await terminalHistory.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(paused.Id, acknowledgedHistory.Id);
            Assert.Equal(binding.TaskId, acknowledgedHistory.CanonicalTask!.TaskId);
            Assert.Equal(binding.ExecutionId, acknowledgedHistory.CanonicalTask.ExecutionId);
            Assert.Equal(AgentRunStatus.Completed, (await rows.GetAsync(paused.Id, default))!.Status);
            Assert.Equal(1, rig.Client.Dispatches);
            // This is the real terminal history/event acknowledgement. Whole
            // business-original ledger completion is separately issuer-owned.
        }
        finally
        {
            release.TrySetResult();
            if (actual is not null) _ = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5)));
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            runtime.RunChanged -= CaptureTerminal;
        }
    }

    [Fact]
    public async Task Actual_capture_retains_genuine_lease_returned_after_its_acquisition_callback_seals_parent()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var originalRead = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (runtime, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return originalRead.Task; }));
        var source = Assert.IsAssignableFrom<IAgentRunOriginalObservationSource>(runtime);
        var actualLease = source.StartObservedOriginalRun(definition.Id, "Source-owned pending observation capture", CancellationToken.None);
        var actualWait = actualLease.WaitOriginalObservationAsync();
        AgentsOriginalObservationCapture? capture = null;
        var parent = new Haven.Desktop.Services.DesktopOriginalWorkLifetime(() =>
        {
            capture?.RequestRetirement();
            return capture?.OriginalClose ?? Task.CompletedTask; // Only this actual test owner has no other resources.
        }, () => Task.CompletedTask);
        var calls = 0;
        var ownJoinRefused = false;
        AgentRunObservationResult? observed = null;
        Task? actual = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            actual = parent.RunAsync(async original =>
            {
                // This is the real consumer component with a genuine SAME-service
                // resource. The private test callback controls only return timing;
                // it cannot construct a lease or create permission/producer authority.
                capture = new AgentsOriginalObservationCapture(source, original, () =>
                {
                    ++calls;
                    parent.RequestRetirement();
                    ownJoinRefused = Record.Exception(() => { _ = parent.CloseAndDrainAsync(); }) is InvalidOperationException;
                    return actualLease;
                }, () => parent.IsRetiring);
                var actualObservation = original.AwaitAsync(capture.ActualObservation);
                capture.BeginOriginalAcquisition();
                try { observed = await actualObservation; }
                finally
                {
                    capture.RequestRetirement();
                    await original.AwaitAsync(capture.CloseAndDrainAsync());
                }
            });
            await actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var close = parent.CloseAndDrainAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.Equal(1, calls);
            Assert.True(ownJoinRefused);
            Assert.Null(observed);
            Assert.NotNull(capture);
            Assert.True(capture.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(source.IsIssuedOriginalObservation(actualLease));
            Assert.Equal(AgentRunObservationDisposition.ObservationDetached,
                (await actualWait).Disposition);
            Assert.False(originalRead.Task.IsCompleted);
            Assert.False(originalRead.Task.IsCanceled);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            var model = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
                Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
            originalRead.TrySetResult(new[] { model });
            if (actual is not null) _ = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
            _ = await Record.ExceptionAsync(() => parent.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
            _ = await Record.ExceptionAsync(() => actualLease.DetachAndDrainOriginalObservationAsync().WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
    }

    [Fact]
    public async Task Actual_source_issuance_survives_detach_ack_and_does_not_complete_pending_model_read()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var originalRead = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (runtime, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return originalRead.Task; }));
        var source = Assert.IsAssignableFrom<IAgentRunOriginalObservationSource>(runtime);
        var actualLease = source.StartObservedOriginalRun(definition.Id, "Original still-owned metadata read", CancellationToken.None);
        var actualWait = actualLease.WaitOriginalObservationAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(actualWait.IsCompleted);
            Assert.True(source.IsIssuedOriginalObservation(actualLease));
            actualLease.RequestOriginalObservationRetirement();
            var actualDetach = actualLease.DetachAndDrainOriginalObservationAsync();
            Assert.Same(actualDetach, actualLease.DetachAndDrainOriginalObservationAsync());
            await actualDetach.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var result = await actualWait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(AgentRunObservationDisposition.ObservationDetached, result.Disposition);
            Assert.Null(result.TerminalRun);
            Assert.True(source.IsIssuedOriginalObservation(actualLease));
            Assert.False(originalRead.Task.IsCompleted);
            Assert.False(originalRead.Task.IsCanceled);
            Assert.Empty(rows.Values);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            var model = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
                Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
            originalRead.TrySetResult(new[] { model });
            _ = await Record.ExceptionAsync(() => actualWait.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
            _ = await Record.ExceptionAsync(() => actualLease.DetachAndDrainOriginalObservationAsync().WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
    }

    [AvaloniaFact]
    public async Task Actual_observed_model_read_direct_siblings_survive_scene_and_independent_page_close()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var first = new IOException("Original Agent observed model read first");
        var sibling = new UnauthorizedAccessException("Original Agent observed model read sibling");
        var originalRead = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (runtime, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return originalRead.Task; }));
        Assert.IsAssignableFrom<IAgentRunOriginalObservationSource>(runtime);
        var viewModel = CreateObservationViewModel(rig, definition);
        await viewModel.RefreshCommand.ExecuteAsync();
        var page = new AgentsPage(viewModel, runtime);
        var scene = page.HavenScene;
        Task<AgentRun?>? actual = null;
        try
        {
            await scene.RefreshRunsAsync();
            scene.RunTaskInput.Text = "Retain both actual raw causes";
            actual = scene.RunAgentAsync(Assert.Single(viewModel.Items));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            originalRead.TrySetException(new Exception[] { first, sibling });
            Assert.NotNull(await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.True(actual.IsFaulted);
            Assert.True(HasObservationCause(actual.Exception!, first));
            Assert.True(HasObservationCause(actual.Exception!, sibling));
            var close = page.CloseAndDrainAsync();
            Assert.NotNull(await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.True(close.IsFaulted);
            Assert.False(close.IsCanceled);
            Assert.True(HasObservationCause(close.Exception!, first));
            Assert.True(HasObservationCause(close.Exception!, sibling));
            Assert.Null(page.Scene.Root); // Independent renderer cleanup is still attempted.
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            originalRead.TrySetException(new Exception[] { first, sibling });
            if (actual is not null) _ = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5)));
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [AvaloniaFact]
    public async Task Nested_actual_source_factories_with_restored_context_refuse_child_scene_and_existing_page_joins()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var rawRead = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (runtime, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() =>
        { readEntered.TrySetResult(); return rawRead.Task; }));
        var source = Assert.IsAssignableFrom<IAgentRunOriginalObservationSource>(runtime);
        var viewModel = CreateObservationViewModel(rig, definition);
        await viewModel.RefreshCommand.ExecuteAsync();
        var page = new AgentsPage(viewModel, runtime);
        var scene = page.HavenScene;
        // Read the actual production scene owner/cohort, not a mirror coordinator
        // or replacement issuer. Only the private factory callback is controlled.
        var owner = ReadActualObservationSceneField<Haven.Desktop.Services.DesktopOriginalWorkLifetime>(scene, "_originalWork");
        var cohort = ReadActualObservationSceneField<List<AgentsOriginalObservationCapture>>(scene, "_observations");
        var cohortGate = ReadActualObservationSceneField<object>(scene, "_observationGate");
        var beforeOwnerContext = ExecutionContext.Capture()!;
        AgentsOriginalObservationCapture? outer = null;
        AgentsOriginalObservationCapture? inner = null;
        AgentRunOriginalObservationLease? outerLease = null;
        AgentRunOriginalObservationLease? innerLease = null;
        Task<AgentRunObservationResult?>? innerObservation = null;
        var refused = false;
        Task? actual = null;
        try
        {
            actual = owner.RunAsync(async original =>
            {
                outer = new AgentsOriginalObservationCapture(source, original, () =>
                {
                    outerLease = source.StartObservedOriginalRun(definition.Id, "Outer actual observed source", CancellationToken.None);
                    inner = new AgentsOriginalObservationCapture(source, original, () =>
                    {
                        innerLease = source.StartObservedOriginalRun(definition.Id, "Inner actual observed source", CancellationToken.None);
                        ExecutionContext.Run(beforeOwnerContext, _ =>
                        {
                            page.RequestRetirement(); // Same actual wrapper/scene closes are published first.
                            Assert.NotNull(page.OriginalClose);
                            Assert.False(page.OriginalClose!.IsCompleted);
                            Assert.NotNull(outer!.OriginalClose);
                            Assert.NotNull(inner!.OriginalClose);
                            // Both exact physical owners remain active here. Checking
                            // only the nested top factory would miss the outer one.
                            Assert.Throws<InvalidOperationException>(() => { _ = outer!.CloseAndDrainAsync(); });
                            Assert.Throws<InvalidOperationException>(() => { _ = inner!.CloseAndDrainAsync(); });
                            Assert.Throws<InvalidOperationException>(() => { _ = scene.CloseAndDrainAsync(); });
                            Assert.Throws<InvalidOperationException>(() => { _ = page.CloseAndDrainAsync(); });
                            refused = true;
                        }, null);
                        return innerLease;
                    }, () => owner.IsRetiring);
                    lock (cohortGate) cohort.Add(inner);
                    innerObservation = original.AwaitAsync(inner.ActualObservation);
                    inner.BeginOriginalAcquisition();
                    return outerLease;
                }, () => owner.IsRetiring);
                lock (cohortGate) cohort.Add(outer);
                var outerObservation = original.AwaitAsync(outer.ActualObservation);
                outer.BeginOriginalAcquisition();
                try
                {
                    Assert.Null(await outerObservation);
                    Assert.NotNull(innerObservation);
                    Assert.Null(await innerObservation!);
                }
                finally
                {
                    var failures = new List<Exception>();
                    foreach (var capture in new[] { outer, inner }.OfType<AgentsOriginalObservationCapture>())
                    {
                        try { capture.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
                        Task? close = null;
                        try { close = capture.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
                        if (close is not null)
                            try { await original.AwaitAsync(close); } catch (Exception error) { failures.Add(error); }
                    }
                    if (failures.Count != 0) throw new AggregateException("Actual controlled acquisition/child retirement failed.", failures);
                }
            });
            await actual.WaitAsync(TimeSpan.FromSeconds(5));
            await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var actualPageClose = page.CloseAndDrainAsync();
            Assert.Same(actualPageClose, page.CloseAndDrainAsync());
            await actualPageClose.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(refused);
            Assert.True(actualPageClose.IsCompletedSuccessfully);
            Assert.Null(page.Scene.Root);
            Assert.NotNull(outerLease);
            Assert.NotNull(innerLease);
            foreach (var lease in new[] { outerLease!, innerLease! })
            {
                Assert.True(source.IsIssuedOriginalObservation(lease));
                var sourceClose = lease.DetachAndDrainOriginalObservationAsync();
                Assert.Same(sourceClose, lease.DetachAndDrainOriginalObservationAsync());
                Assert.True(sourceClose.IsCompletedSuccessfully);
                Assert.Equal(AgentRunObservationDisposition.ObservationDetached, (await lease.WaitOriginalObservationAsync()).Disposition);
            }
            Assert.False(rawRead.Task.IsCompleted);
            Assert.False(rawRead.Task.IsCanceled);
            Assert.Empty(rows.Values);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            var model = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
                Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
            rawRead.TrySetResult(new[] { model });
            if (actual is not null) _ = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(5)));
            foreach (var lease in new[] { outerLease, innerLease }.OfType<AgentRunOriginalObservationLease>())
                _ = await Record.ExceptionAsync(() => lease.DetachAndDrainOriginalObservationAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static T ReadActualObservationSceneField<T>(object scene, string name)
    {
        var field = scene.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The actual Agent scene owning field is missing: " + name);
        return (T)(field.GetValue(scene) ?? throw new InvalidOperationException("No actual Agent scene field value exists: " + name));
    }

    private static CatalogPageViewModel CreateObservationViewModel(Rig rig, AgentDefinition definition)
    {
        var model = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        return new CatalogPageViewModel(CatalogPageKind.Agents, new AgentCatalog(definition), new AgentModelDiscovery(model), true);
    }

    private static bool HasObservationCause(Exception container, Exception expected) =>
        ReferenceEquals(container, expected) || container is AggregateException group &&
        group.InnerExceptions.Any(child => HasObservationCause(child, expected)); // Test inspection only.
}
