using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

[CollectionDefinition("Assistants native UI", DisableParallelization = true)]
public sealed class AssistantsNativeUiCollection { }

[Collection("Assistants native UI")]
public sealed class AssistantsNativeCuiSurfaceLifecycleTests
{
    [Fact]
    public async Task Actual_cui_hosts_initialize_and_same_original_close_detaches_the_native_tree()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = BridgeSource.Create();
            bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness());
            try
            {
                var initialization = surface.InitializeAsync();
                Assert.Same(initialization, surface.OriginalInitialization);
                await initialization;
                await Dispatcher.UIThread.InvokeAsync(() => { }).GetTask();
                Assert.NotNull(surface.SceneHost);
                Assert.Same(surface.SceneHost, surface.Content);
                Assert.Same(controller, surface.OriginalController);
                Assert.True(surface.IsOriginalClosePrepared);
                var close = surface.CloseAndDrainAsync();
                await close;
                Assert.Same(close, surface.OriginalClose);
                Assert.Same(close, surface.CloseAndDrainAsync());
                Assert.Null(surface.Content);
                Assert.True(bridge.Source.RetirementRequested);
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Partial_owner_is_captured_before_native_publication_and_cannot_join_its_constructor()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = BridgeSource.Create();
            bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            AssistantsNativeCuiSurface? captured = null;
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness(), captureOriginalOwner: actual =>
            {
                captured = actual;
                Assert.Null(actual.SceneHost);
                Assert.Null(actual.Content);
                Assert.Throws<InvalidOperationException>(() => { _ = actual.CloseAndDrainAsync(); });
                Assert.Null(actual.OriginalClose);
            });
            try
            {
                Assert.Same(surface, captured);
                await surface.InitializeAsync();
                await surface.CloseAndDrainAsync();
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Restored_context_source_callback_cannot_join_the_same_native_original()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = BridgeSource.Create();
            bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness());
            var foreignContext = ExecutionContext.Capture()!;
            var observed = false;
            bridge.Source.BeforeList = () => ExecutionContext.Run(foreignContext, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = surface.CloseAndDrainAsync(); });
                observed = true;
                Assert.Null(surface.OriginalClose);
            }, null);
            try
            {
                await surface.InitializeAsync();
                Assert.True(observed);
                await surface.CloseAndDrainAsync();
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Held_actual_source_keeps_close_pending_and_preserves_all_fault_siblings()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = BridgeSource.Create();
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness());
            var canceledLikeFault = new OperationCanceledException("Actual faulted bridge payload.");
            var sibling = new IOException("Actual bridge sibling.");
            try
            {
                var initialization = surface.InitializeAsync();
                await bridge.Source.ListStarted.Task;
                Assert.False(surface.IsOriginalClosePrepared);
                var tree = surface.Content;
                var close = surface.CloseAndDrainAsync();
                Assert.False(close.IsCompleted);
                Assert.Same(tree, surface.Content);
                bridge.Source.List.TrySetException([canceledLikeFault, sibling]);
                var failure = await Record.ExceptionAsync(() => initialization);
                Assert.True(initialization.IsFaulted);
                Assert.True(ContainsSame(failure!, canceledLikeFault));
                Assert.True(ContainsSame(failure!, sibling));
                var closeFailure = await Record.ExceptionAsync(() => close);
                Assert.True(close.IsFaulted);
                Assert.True(ContainsSame(closeFailure!, canceledLikeFault));
                Assert.True(ContainsSame(closeFailure!, sibling));
                Assert.Same(close, surface.CloseAndDrainAsync());
                Assert.Same(tree, surface.Content);
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_empty_or_nested_source_group_stays_opaque_and_close_stays_failed(bool nested)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var empty = new AggregateException("Foreign empty source cause.");
            var actual = nested ? new AggregateException("Foreign group.", empty, new IOException("Nested cause.")) : empty;
            var bridge = BridgeSource.Create();
            bridge.Source.List.TrySetException(actual);
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness());
            try
            {
                var initialization = surface.InitializeAsync();
                Assert.Same(actual, await Record.ExceptionAsync(() => initialization));
                var tree = surface.Content;
                var close = surface.CloseAndDrainAsync();
                Assert.Same(actual, await Record.ExceptionAsync(() => close));
                Assert.True(close.IsFaulted);
                Assert.Same(close, surface.CloseAndDrainAsync());
                Assert.Same(tree, surface.Content);
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task A_known_initialization_refusal_cannot_report_a_ready_product_or_destroy_its_tree()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var refusal = new AssistantCommandRefusedException("The owning setup is unavailable.");
            var bridge = BridgeSource.Create(); bridge.Source.List.TrySetException(refusal);
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness());
            try
            {
                var initialization = surface.InitializeAsync();
                Assert.Same(refusal, await Record.ExceptionAsync(() => initialization));
                Assert.True(initialization.IsFaulted);
                Assert.False(surface.IsOriginalClosePrepared);
                var tree = surface.Content;
                Assert.Same(refusal, await Record.ExceptionAsync(() => surface.CloseAndDrainAsync()));
                Assert.Same(tree, surface.Content);
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Same_issuer_acknowledged_refusal_keeps_draft_then_corrected_save_can_close()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var bridge = BridgeSource.Create(); bridge.Source.List.TrySetResult(new([], []));
            var controller = new AssistantsWorkspaceController(bridge.Bridge);
            var surface = new AssistantsNativeCuiSurface(controller, new ReadyReadiness());
            var refusal = new AssistantCommandRefusedException("The first actual configuration submission was refused.");
            var attempts = 0;
            bridge.Source.CreateDefinition = configuration => ++attempts == 1
                ? Task.FromException<AssistantDefinitionSnapshot>(refusal)
                : Task.FromResult(new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"),
                    1, ConfiguredIdentityKind.Assistant, configuration, []));
            try
            {
                await surface.InitializeAsync();
                await surface.Bindings.DispatchAsync("assistants.create.start", null);
                Assert.True(surface.Bindings.TrySetValue("DraftName", "Original unsaved name"));
                await surface.Bindings.DispatchAsync("assistants.configuration.save", null);
                Assert.Equal("Original unsaved name", surface.Bindings.Draft!.Configuration.Name);
                Assert.True(surface.Bindings.Draft.IsDirty);
                Assert.True(surface.HasUnsavedChanges);
                Assert.Null(surface.OriginalClose);
                Assert.True(surface.Bindings.TrySetValue("DraftName", "Corrected name"));
                await surface.Bindings.DispatchAsync("assistants.configuration.save", null);
                Assert.Equal(2, attempts);
                Assert.False(surface.Bindings.Draft!.IsDirty);
                Assert.Equal("Corrected name", controller.Snapshot.SelectedAssistant!.Configuration.Name);
                await Dispatcher.UIThread.InvokeAsync(() => { }).GetTask();
                Assert.True(await surface.PrepareToCloseAsync());
                var close = surface.CloseAndDrainAsync(); await close;
                Assert.True(close.IsCompletedSuccessfully);
                Assert.Null(surface.Content);
            }
            finally { await SettleAsync(surface, controller, bridge.Source); }
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    private static bool ContainsSame(Exception current, Exception actual) => ReferenceEquals(current, actual) ||
        current is AggregateException group && group.InnerExceptions.Any(child => ContainsSame(child, actual));

    private static async Task SettleAsync(AssistantsNativeCuiSurface surface,
        AssistantsWorkspaceController controller, BridgeSource bridge)
    {
        bridge.List.TrySetResult(new([], [])); // Release the SAME raw gate even after an early assertion.
        surface.RequestRetirement();
        if (surface.OriginalInitialization is { } initialization) await Record.ExceptionAsync(() => initialization);
        await Record.ExceptionAsync(() => surface.CloseAndDrainAsync());
        await Record.ExceptionAsync(() => controller.CloseAndDrainAsync());
    }

    private sealed class ReadyReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "TestFixture", "Test fixture only."));
    }

    // A test-only source issuer. It creates no Den, Task, Space or permission authority.
    public class BridgeSource : DispatchProxy
    {
        public readonly TaskCompletionSource<AssistantCatalogueObservation> List = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ListStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? BeforeList;
        public Func<AssistantConfiguration, Task<AssistantDefinitionSnapshot>>? CreateDefinition;
        public bool RetirementRequested;
        public static (IAssistantCanonicalBridge Bridge, BridgeSource Source) Create()
        {
            var actual = DispatchProxy.Create<IAssistantCanonicalBridge, BridgeSource>();
            return (actual, (BridgeSource)(object)actual);
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IAssistantCanonicalBridge.ListAsync):
                    ListStarted.TrySetResult(); BeforeList?.Invoke(); return List.Task;
                case nameof(IAssistantCanonicalBridge.CreateAsync):
                    return (CreateDefinition ?? throw new NotSupportedException("No fixture definition source."))(
                        (AssistantConfiguration)args![1]!);
                case nameof(IAssistantCanonicalBridge.RequestRetirement): RetirementRequested = true; return null;
                case nameof(IAssistantCanonicalBridge.DemandExternalOriginalRetirementJoin): return null;
                case nameof(IAssistantCanonicalBridge.CloseAndDrainAsync): return Task.CompletedTask;
                default: throw new NotSupportedException("The native lifecycle fixture has no product execution owner.");
            }
        }
    }
}

public sealed class AssistantsTestApplication : Avalonia.Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<AssistantsTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
