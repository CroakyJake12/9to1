using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.Migration;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

[Collection("Assistants native UI")]
public sealed class AssistantsNativeLegacyMigrationLifecycleTests
{
    [Fact]
    public async Task Actual_missing_source_scene_is_visible_and_refuses_migration_without_a_dummy_owner()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = AssistantsNativeCuiSurfaceLifecycleTests.BridgeSource.Create(); bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new FixtureReadiness());
            var window = new Window { Width = 900, Height = 760, Content = surface }; window.Show();
            Exception? bodyFailure = null; Task? initialization = null; Task? command = null;
            try
            {
                initialization = surface.InitializeAsync(); await initialization;
                command = surface.Bindings.DispatchAsync("assistants.legacy.open", null).AsTask(); await command;
                window.UpdateLayout();
                Assert.Null(surface.OriginalMigrationController);
                Assert.Contains(surface.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Review existing definitions");
                Assert.True(surface.MigrationBindings.TryGetValue("MigrationStatus", out var status));
                Assert.Contains("through Home", Assert.IsType<string>(status));
                Assert.False(surface.MigrationBindings.IsActionAvailable("assistants.legacy.confirm"));
                Assert.False(surface.MigrationBindings.IsActionAvailable("assistants.legacy.undo"));
                await surface.MigrationBindings.DispatchAsync("assistants.legacy.back", null);
                Assert.True(surface.IsOriginalClosePrepared);
            }
            catch (Exception failure) { bodyFailure = failure; }
            finally { await FinishAsync(surface, controller, window, initialization, command, bodyFailure); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Same_migration_close_joins_before_borrowed_core_bridge_and_retirement_callbacks_cannot_self_join()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = AssistantsNativeCuiSurfaceLifecycleTests.BridgeSource.Create(); bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var migration = new HeldMigrationSource(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new FixtureReadiness(), migration: migration);
            var window = new Window { Content = surface }; window.Show();
            Exception? bodyFailure = null; Task? initialization = null; Task? close = null;
            var foreignContext = ExecutionContext.Capture()!;
            var selfJoinDenied = false;
            migration.BeforeRetirement = () => ExecutionContext.Run(foreignContext, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = surface.CloseAndDrainAsync(); }); selfJoinDenied = true;
            }, null);
            try
            {
                initialization = surface.InitializeAsync(); await initialization;
                close = surface.CloseAndDrainAsync();
                await migration.CloseStarted.Task;
                Assert.Same(migration.Close.Task, migration.OriginalClose); Assert.False(close.IsCompleted);
                Assert.True(selfJoinDenied); Assert.False(bridge.Source.RetirementRequested);
                Assert.NotNull(surface.Content);
                migration.Close.TrySetResult(); await close;
                Assert.True(bridge.Source.RetirementRequested); Assert.Null(surface.Content);
                Assert.Same(close, surface.OriginalClose); Assert.Same(close, surface.CloseAndDrainAsync());
            }
            catch (Exception failure) { bodyFailure = failure; }
            finally
            {
                migration.Close.TrySetResult(); migration.BeforeRetirement = null;
                await FinishAsync(surface, controller, window, initialization, close, bodyFailure);
            }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Earlier_held_list_terminal_cannot_clear_or_publish_error_into_the_newer_original_review(bool declined)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = AssistantsNativeCuiSurfaceLifecycleTests.BridgeSource.Create(); bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var migration = new HeldListingMigrationSource(bridge.Bridge); migration.Close.TrySetResult();
            var surface = new AssistantsNativeCuiSurface(controller, new FixtureReadiness(), migration: migration);
            var window = new Window { Width = 900, Height = 760, Content = surface }; window.Show();
            Exception? bodyFailure = null; Task? initialization = null, first = null, second = null;
            try
            {
                initialization = surface.InitializeAsync(); await initialization;
                first = surface.Bindings.DispatchAsync("assistants.legacy.open", null).AsTask();
                await migration.FirstStarted.Task;
                second = surface.Bindings.DispatchAsync("assistants.legacy.open", null).AsTask();
                await migration.SecondStarted.Task;
                surface.Bindings.SetConversationStatus("Current conversation status remains owned by its presentation.");
                if (declined) migration.First.TrySetException(new AssistantCommandRefusedException("Earlier source declined before effects."));
                else migration.First.TrySetResult(new([], null));
                await first;
                Assert.False(second.IsCompleted);
                Assert.True(surface.Bindings.TryGetValue("ConversationStatus", out var conversationStatus));
                Assert.Equal("Current conversation status remains owned by its presentation.", conversationStatus);
                Assert.False(surface.MigrationBindings.IsActionAvailable("assistants.legacy.list"));
                Assert.True(surface.MigrationBindings.TryGetValue("CanMigrationList", out var canList)); Assert.Equal(false, canList);
                Assert.True(surface.MigrationBindings.TryGetValue("HasMigrationError", out var hasError)); Assert.Equal(false, hasError);
                var latest = new LegacyAgentMigrationItem(Guid.NewGuid(), "Latest held source", "", true, false, LegacyAgentMigrationState.Unselected);
                migration.Second.TrySetResult(new([latest], null)); await second;
                Assert.True(surface.MigrationBindings.IsActionAvailable("assistants.legacy.list"));
                Assert.True(surface.MigrationBindings.TryGetValue("MigrationRows", out var rows));
                Assert.Equal("Latest held source", Assert.Single(Assert.IsAssignableFrom<IEnumerable<AssistantsLegacyMigrationCuiBindings.LegacyRow>>(rows)).Name);
                await surface.MigrationBindings.DispatchAsync("assistants.legacy.back", null);
            }
            catch (Exception failure) { bodyFailure = failure; }
            finally
            {
                migration.First.TrySetResult(new([], null)); migration.Second.TrySetResult(new([], null));
                await FinishAsync(surface, controller, window, initialization,
                    Task.WhenAll(first ?? Task.CompletedTask, second ?? Task.CompletedTask), bodyFailure);
            }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    private static async Task FinishAsync(AssistantsNativeCuiSurface surface, AssistantsWorkspaceController controller,
        Window window, Task? initialization, Task? accepted, Exception? bodyFailure)
    {
        var causes = new List<Exception>();
        void Add(Exception actual) { if (!causes.Any(cause => ReferenceEquals(cause, actual))) causes.Add(actual); }
        async Task Join(Task actual)
        {
            try { await actual; }
            catch (Exception caught) { foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [caught]) Add(cause); }
        }
        if (bodyFailure is not null) Add(bodyFailure);
        if (initialization is not null) await Join(initialization);
        if (accepted is not null) await Join(accepted);
        Task? actualClose = null;
        try { actualClose = surface.CloseAndDrainAsync(); } catch (Exception failure) { Add(failure); }
        if (actualClose is not null) await Join(actualClose);
        // Do not release the actual fixture window or borrowed bridge on failed/unknown migration/native drain.
        if (actualClose?.IsCompletedSuccessfully == true)
        {
            Task? controllerClose = null;
            try { controllerClose = controller.CloseAndDrainAsync(); } catch (Exception failure) { Add(failure); }
            if (controllerClose is not null) await Join(controllerClose);
            if (controllerClose?.IsCompletedSuccessfully == true)
                try { window.Close(); } catch (Exception failure) { Add(failure); }
        }
        else if (causes.Count == 0) Add(new InvalidOperationException("The actual fixture close remains unresolved."));
        if (causes.Count == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Count > 1) throw new AggregateException("Actual migration UI fixture body and source causes retained.", causes);
    }

    private sealed class FixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "TestFixture", "Test-only CUI ownership control; no Den or installation authority."));
    }

    private class HeldMigrationSource : ILegacyAgentMigrationController
    {
        private readonly IAssistantCanonicalBridge _bridge;
        internal HeldMigrationSource(IAssistantCanonicalBridge actualBridge) => _bridge = actualBridge;
        public bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge actualBridge) => ReferenceEquals(_bridge, actualBridge);
        internal readonly TaskCompletionSource Close = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CloseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? BeforeRetirement;
        public Task? OriginalClose { get; private set; }
        public void RequestRetirement() => BeforeRetirement?.Invoke();
        public void DemandExternalOriginalRetirementJoin() { }
        public Task CloseAndDrainAsync() { CloseStarted.TrySetResult(); return OriginalClose ??= Close.Task; }
        public virtual bool IsAcknowledgedOriginalCommandRefusal(Task actual) => false;
        public virtual Task<LegacyAgentMigrationPage> ListPageAsync(string? cursor = null, int maximum = 30, CancellationToken token = default) => throw new NotSupportedException();
        public Task<LegacyAgentMigrationPreview> PreviewAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<LegacyAgentLinkPage> ReadPreservedLinksAsync(LegacyAgentMigrationPreview preview, string kind, string? cursor = null, int maximum = 30, CancellationToken token = default) => throw new NotSupportedException();
        public Task<LegacyAgentMigrationResult> ClassifyAsync(LegacyAgentMigrationPreview preview, ConfiguredIdentityKind kind, AssistantConfiguration configuration, Guid operationId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<LegacyAgentRecoveryPreview> ReadRecoveryAsync(AssistantIdentity identity, CancellationToken token = default) => throw new NotSupportedException();
        public Task<LegacyAgentRecoveryResult> UndoAsync(LegacyAgentRecoveryPreview preview, Guid operationId, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class HeldListingMigrationSource(IAssistantCanonicalBridge actualBridge) : HeldMigrationSource(actualBridge)
    {
        private int _calls;
        internal readonly TaskCompletionSource<LegacyAgentMigrationPage> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<LegacyAgentMigrationPage> Second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsAcknowledgedOriginalCommandRefusal(Task actual) => ReferenceEquals(actual, First.Task) && actual.IsFaulted;
        public override Task<LegacyAgentMigrationPage> ListPageAsync(string? cursor = null, int maximum = 30, CancellationToken token = default)
        {
            switch (Interlocked.Increment(ref _calls))
            {
                case 1: FirstStarted.TrySetResult(); return First.Task;
                case 2: SecondStarted.TrySetResult(); return Second.Task;
                default: throw new InvalidOperationException("The fixture did not admit a third list request.");
            }
        }
    }

}
