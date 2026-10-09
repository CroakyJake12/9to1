using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.Memory;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

[Collection("Assistants native UI")]
public sealed class AssistantsNativeMemoryLifecycleTests
{
    [Fact]
    public async Task Same_memory_close_joins_before_borrowed_Core_and_reentrant_retirement_cannot_self_join()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = AssistantsNativeCuiSurfaceLifecycleTests.BridgeSource.Create(); bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge); var memory = new HeldMemory(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new FixtureReadiness(), memoryManagement: memory);
            var window = new Window { Content = surface }; window.Show(); var failures = new List<Exception>();
            var selfJoinDenied = false; var foreignContext = ExecutionContext.Capture()!;
            memory.BeforeRetirement = () => ExecutionContext.Run(foreignContext, _ =>
            { Assert.Throws<InvalidOperationException>(() => { _ = surface.CloseAndDrainAsync(); }); selfJoinDenied = true; }, null);
            try
            {
                await surface.InitializeAsync(); Assert.Same(memory, surface.OriginalMemoryManagementController);
                var close = surface.CloseAndDrainAsync(); await memory.CloseStarted.Task;
                Assert.True(selfJoinDenied); Assert.False(close.IsCompleted); Assert.False(bridge.Source.RetirementRequested);
                Assert.Same(memory.Close.Task, memory.OriginalClose); Assert.NotNull(surface.Content);
                memory.Close.SetResult(); await close;
                Assert.True(bridge.Source.RetirementRequested); Assert.Null(surface.Content);
                Assert.Same(close, surface.CloseAndDrainAsync());
            }
            catch (Exception failure) { failures.Add(failure); }
            finally
            {
                memory.BeforeRetirement = null; memory.Close.TrySetResult();
                try { await surface.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
                try { await controller.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
                if (surface.OriginalClose?.IsCompletedSuccessfully == true && controller.OriginalClose?.IsCompletedSuccessfully == true) window.Close();
            }
            if (failures.Count != 0) throw new AggregateException("Actual native memory fixture sources retained.", failures);
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }
    private sealed class FixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "Fixture", "UI ownership fixture only."));
    }
    private sealed class HeldMemory(IAssistantCanonicalBridge bridge) : IAssistantMemoryManagementController
    {
        internal readonly TaskCompletionSource Close = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CloseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? BeforeRetirement;
        public Task? OriginalClose { get; private set; }
        public bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge actualBridge) => ReferenceEquals(bridge, actualBridge);
        public void DemandExternalOriginalRetirementJoin() { }
        public void RequestRetirement() => BeforeRetirement?.Invoke();
        public Task CloseAndDrainAsync() { CloseStarted.TrySetResult(); return OriginalClose ??= Close.Task; }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        public Task<AssistantMemoryView> ReadAsync(AssistantConversationBinding actual, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantMemoryWritePreview> PrepareAsync(AssistantMemoryView sameView, string title, string summary,
            Guid operationId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantMemorySaveResult> CommitAsync(AssistantMemoryWritePreview samePreview, CancellationToken token = default) => throw new NotSupportedException();
    }
}
