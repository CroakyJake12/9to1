using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Fresh_command_binding_and_pin_use_the_same_actual_current_document_and_native_root()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        FilesDeveloperOriginalCurrentProjectSelection? selectionOwner = null;
        IDeveloperOriginalCurrentProjectNativeSource? native = null; CommandReads? reads = null;
        FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? command = null;
        IDeveloperWorkspaceOriginalExecutionBinding? binding = null; IDeveloperWorkspaceOriginalExecutionCommitPin? pin = null;
        Task? pinClose = null; var raw = new List<Task>(); var errors = new List<Exception>();
        try
        {
            var prepared = await CreateCommandReads(rig, token, raw);
            (selectionOwner, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
            var actualReads = reads; var tools = new CommandDenyTools();
            bridge = new(reads, native, () => throw new NotSupportedException("No Home execution consent is issued by this kernel control."), tools);
            var sameBridge = bridge; var previous = ExecutionContext.Capture()!;
            void Scope(Action body)
            {
                ExecutionContext.Run(previous.CreateCopy(), _ => Assert.Throws<InvalidOperationException>((Action)(() =>
                    { _ = sameBridge.CloseAndDrainOriginalExecutionAsync(); })), null);
                body();
            }
            command = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference, prepared.Context,
                Execute, Scope, Retain, token);
            var observation = await command.JoinOriginalAsync();
            Assert.False(observation.Succeeded); // Component-only kernel control, never an action/model/CAS grant.
            Assert.NotNull(binding); Assert.NotNull(pin);
            Assert.True(bridge.IsIssuedOriginalBinding(binding));
            Assert.True(bridge.IsIssuedOriginalExecutionPin(binding, pin));
            Assert.True(bridge.IsOwnedOriginalExecutionPin(binding, pin));
            Assert.True(bridge.IsBoundToOriginalToolOwner(tools));
            Assert.False(await bridge.IsTrustedAsync(binding.WorkspaceId, token));
            Assert.Equal(prepared.Project.Reference.RootId, binding.RootId);
            Assert.Equal(prepared.Project.Root.Location, binding.CanonicalRoot);
            Assert.Equal(ResourceAccess.Execute, Assert.Single(bridge.GetOriginalExecutionScopes(binding)).Access);
            Assert.Equal(0, tools.Preparations);
            pin.DemandOriginalExecutionBinding();
            var file = Path.Combine(rig.WorkspaceStore.OriginalWorkspaceMetadataDirectory,
                prepared.Project.Reference.WorkspaceId.ToString("N") + ".json");
            var before = File.ReadAllBytes(file);
            try { File.WriteAllText(file, "{}"); Assert.Throws<IOException>((Action)pin.DemandOriginalExecutionBinding); }
            finally { File.WriteAllBytes(file, before); }
            pinClose = pin.DisposeAsync().AsTask(); await pinClose;
            Assert.False(bridge.IsIssuedOriginalExecutionPin(binding, pin));
            Assert.True(bridge.IsOwnedOriginalExecutionPin(binding, pin));
            Assert.All(CurrentProjectHandles(native), handle => Assert.True(handle.IsClosed));
            Assert.All(actualReads.Captures, result => Assert.True(native.IsClosedOriginalRead(actualReads.Actual.OriginalSelection,
                result.OriginalCaptureTask, result.OriginalRead, result.Close!)));
            Assert.Contains(raw, task => ReferenceEquals(task, command.OriginalCommandTask));

            async Task<DeveloperOperationResult<DeveloperActionObservation>> Execute()
            {
                binding = await sameBridge.ResolveOriginalBindingAsync(prepared.Project, Scope, Retain, token);
                pin = await sameBridge.AcquireOriginalExecutionPinWithinSourceAsync(binding, Scope, Retain, token);
                return CommandControlResult();
            }
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            if (command is not null) try { await command.JoinOriginalAsync(); } catch (Exception error) { Keep(command.OriginalCommandTask, error); }
            if (pin is not null && pinClose is null) try { pinClose = pin.DisposeAsync().AsTask(); } catch (Exception error) { Keep(null, error); }
            if (pinClose is not null) try { await pinClose; } catch (Exception error) { Keep(pinClose, error); }
            await CloseCommandOwners(bridge, reads, native, selectionOwner, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Retain(Task actual) { lock (raw) raw.Add(actual); }
        void Keep(Task? task, Exception error)
        { foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error)) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    }

    [LinuxDirectoryFact]
    public async Task Actual_command_Task_remains_reachable_when_parent_scope_faults_after_its_acquisition()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        var held = new TaskCompletionSource<DeveloperOperationResult<DeveloperActionObservation>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refusal = new IOException("same command parent refused after actual Task capture");
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        CommandReads? reads = null; FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? invocation = null;
        Task<DeveloperOperationResult<DeveloperActionObservation>>? joined = null;
        var raw = new List<Task>(); var errors = new List<Exception>(); var calls = 0;
        try
        {
            var prepared = await CreateCommandReads(rig, token, raw);
            (selected, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
            bridge = new(reads, native, () => throw new NotSupportedException(), new CommandDenyTools());
            invocation = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference, prepared.Context,
                () => { calls++; return held.Task; }, body => { body(); throw refusal; }, Retain, token);
            Assert.Same(held.Task, invocation.OriginalCommandTask);
            Assert.Same(refusal, Assert.Single(invocation.OriginalPublicationCauses));
            Assert.Contains(raw, task => ReferenceEquals(task, held.Task));
            joined = invocation.JoinOriginalAsync();
            Assert.False(joined.IsCompleted); Assert.Equal(1, calls);
            held.TrySetResult(CommandControlResult());
            var observed = await Assert.ThrowsAsync<AggregateException>(() => joined);
            Assert.Same(refusal, Assert.Single(CurrentProjectLeaves(observed)));
            Assert.True(joined.IsFaulted); Assert.False(joined.IsCanceled);
            Assert.True(held.Task.IsCompletedSuccessfully);
            Assert.Empty(reads.Captures);
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            held.TrySetResult(CommandControlResult());
            try { await held.Task; } catch (Exception error) { Keep(held.Task, error); }
            if (joined is null && invocation is not null) try { joined = invocation.JoinOriginalAsync(); } catch (Exception error) { Keep(null, error); }
            if (joined is not null) try { await joined; } catch (Exception error) { Keep(joined, error); }
            await CloseCommandOwners(bridge, reads, native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Retain(Task actual) { lock (raw) raw.Add(actual); }
        void Keep(Task? task, Exception error)
        { foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error)) if (!ReferenceEquals(cause, refusal) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    }

    [LinuxDirectoryFact]
    public async Task Closed_READ_or_another_Task_conversation_cannot_admit_the_actual_command_factory()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        CommandReads? reads = null; FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
        var raw = new List<Task>(); var errors = new List<Exception>(); var calls = 0;
        try
        {
            var prepared = await CreateCommandReads(rig, token, raw);
            (selected, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
            bridge = new(reads, native, () => throw new NotSupportedException(), new CommandDenyTools());
            Assert.Throws<AggregateException>((Action)(() =>
            {
                _ = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference,
                    prepared.Context with { ContextId = Guid.NewGuid() }, Factory, body => body(), Retain, token);
            }));
            Assert.Equal(0, calls); Assert.Empty(reads.Captures);
            var sameRead = reads.Actual; sameRead.RequestOriginalRetirement();
            var readClose = sameRead.CloseAndDrainOriginalAsync(); Retain(readClose); await readClose;
            Assert.True(reads.IsOwnedOriginalCommandRead(sameRead)); Assert.False(reads.IsIssuedOriginalCommandRead(sameRead));
            Assert.Throws<AggregateException>((Action)(() =>
            {
                _ = bridge.CaptureOriginalCommandWithinSource(sameRead, prepared.Project.Reference, prepared.Context,
                    Factory, body => body(), Retain, token);
            }));
            Assert.Equal(0, calls); Assert.Empty(reads.Captures);
            Assert.False(await bridge.IsTrustedAsync(prepared.Project.Reference.WorkspaceId, token));
            Assert.Empty(CurrentProjectHandles(native));
            Task<DeveloperOperationResult<DeveloperActionObservation>> Factory() { calls++; return Task.FromResult(CommandControlResult()); }
        }
        catch (Exception error) { Keep(null, error); }
        finally { await CloseCommandOwners(bridge, reads, native, selected, rig, raw, Keep); }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Retain(Task actual) { lock (raw) raw.Add(actual); }
        void Keep(Task? task, Exception error)
        { foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error)) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    }

    private static DeveloperOperationResult<DeveloperActionObservation> CommandControlResult() =>
        DeveloperOperationResult<DeveloperActionObservation>.Failure(DeveloperOperationErrorCode.CapabilityUnavailable,
            "Kernel ordering control has no canonical action or Home execution consent.", "component-only");
    private sealed record CommandFixture(FilesDeveloperOriginalCurrentProjectSelection SelectionOwner,
        IDeveloperOriginalCurrentProjectNativeSource Native, CommandReads Reads, DeveloperResolvedProject Project,
        DeveloperCanonicalActionContext Context);
    [LinuxDirectoryFact]
    public async Task Held_command_READ_validation_cannot_start_new_native_capture_after_execution_bridge_retirement()
    {
        await Check(false); await Check(true);
        async Task Check(bool retireWithinParent)
        {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        CommandReads? reads = null; FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? command = null;
        Task<IDeveloperWorkspaceOriginalExecutionBinding>? bindingTask = null; Task? close = null;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new List<Task>(); var errors = new List<Exception>(); Exception? expected = null; var retireAtCallback = false;
        try
        {
            var prepared = await CreateCommandReads(rig, token, raw);
            (selected, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
            var actualReads = reads; var capturesBefore = reads.Captures.Count;
            reads.Validation = (scope, retain, cancellation) =>
            {
                FilesOriginalSourceCallbackScope.Invoke(() =>
                { cancellation.ThrowIfCancellationRequested(); retain(held.Task); enrolled.TrySetResult(); }, scope);
                return held.Task;
            };
            bridge = new(reads, native, () => throw new NotSupportedException("No execute consent in this retirement control."), new CommandDenyTools());
            var sameBridge = bridge;
            command = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference, prepared.Context,
                Execute, Scope, Retain, token);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Contains(raw, task => ReferenceEquals(task, held.Task));
            Assert.NotNull(bindingTask); Assert.False(bindingTask.IsCompleted);
            if (!retireWithinParent)
            { bridge.RequestOriginalExecutionRetirement(); close = bridge.CloseAndDrainOriginalExecutionAsync(); Assert.False(close.IsCompleted); }
            else retireAtCallback = true;
            held.TrySetResult();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => bindingTask);
            expected = Assert.Single(CurrentProjectLeaves(failure)); Assert.IsType<ObjectDisposedException>(expected);
            Assert.True(bindingTask.IsFaulted); Assert.False(bindingTask.IsCanceled);
            Assert.True(held.Task.IsCompletedSuccessfully);
            Assert.Equal(capturesBefore, actualReads.Captures.Count);
            var commandFailure = await Assert.ThrowsAsync<AggregateException>(() => command.JoinOriginalAsync());
            Assert.Same(expected, Assert.Single(CurrentProjectLeaves(commandFailure)));
            close ??= bridge.CloseAndDrainOriginalExecutionAsync();
            var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.Same(expected, Assert.Single(CurrentProjectLeaves(closeFailure)));
            Assert.All(CurrentProjectHandles(native), handle => Assert.True(handle.IsClosed));
            async Task<DeveloperOperationResult<DeveloperActionObservation>> Execute()
            {
                bindingTask = sameBridge.ResolveOriginalBindingAsync(prepared.Project, Scope, Retain, token);
                Retain(bindingTask); await bindingTask; return CommandControlResult();
            }
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            held.TrySetResult();
            if (reads is not null) reads.Validation = null;
            try { await held.Task; } catch (Exception error) { Keep(held.Task, error); }
            if (bindingTask is not null) try { await bindingTask; } catch (Exception error) { Keep(bindingTask, error); }
            if (command is not null) try { await command.JoinOriginalAsync(); } catch (Exception error) { Keep(command.OriginalCommandTask, error); }
            if (close is not null) try { await close; } catch (Exception error) { Keep(close, error); }
            await CloseCommandOwners(bridge, reads, native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Scope(Action body)
        { if (retireAtCallback) bridge!.RequestOriginalExecutionRetirement(); body(); }
        void Retain(Task actual) { lock (raw) raw.Add(actual); }
        void Keep(Task? task, Exception error)
        { foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error)) if (!ReferenceEquals(cause, expected) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
        }
    }

    [LinuxDirectoryFact]
    public async Task Fresh_execution_resolver_recognizes_only_same_private_current_binding_actor_and_scope()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        CommandReads? reads = null; FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? command = null;
        IDeveloperWorkspaceOriginalExecutionBinding? binding = null;
        var raw = new List<Task>(); var errors = new List<Exception>(); var resolverCalls = 0;
        try
        {
            var prepared = await CreateCommandReads(rig, token, raw);
            (selected, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
            bridge = new(reads, native, () => throw new NotSupportedException("This resolver component grants no execute consent."), new CommandDenyTools());
            var sameBridge = bridge;
            var resolver = new FilesDeveloperOriginalCurrentProjectExecutionResolver(() => { resolverCalls++; return sameBridge; });
            Assert.Equal(0, resolverCalls); Assert.Equal("dev.workspace.execute", resolver.ResourceKind);
            Assert.IsAssignableFrom<IOriginalScopedCanonicalResourceAccessResolver>(bridge);
            command = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference, prepared.Context,
                Execute, body => body(), Retain, token);
            await command.JoinOriginalAsync(); Assert.NotNull(binding);
            var scope = Assert.Single(bridge.GetOriginalExecutionScopes(binding));
            var actual = resolver.EvaluateWithinOriginalSourceAsync(binding.OriginalActor, "dev.workspace.execute", scope, body => body(), Retain, token);
            Retain(actual); var decision = await actual;
            Assert.True(decision.Allowed); Assert.Equal(scope.Revision, decision.ResourceRevision);
            Assert.Equal(binding.OriginalActor.ActorId, decision.ActorId);
            var changed = resolver.EvaluateWithinOriginalSourceAsync(binding.OriginalActor, "dev.workspace.execute",
                scope with { Revision = scope.Revision + ":copied" }, body => body(), Retain, token); Retain(changed);
            Assert.False((await changed).Allowed);
            var actorChanged = resolver.EvaluateWithinOriginalSourceAsync(binding.OriginalActor with { AuthenticationRevision = "copied" },
                "dev.workspace.execute", scope, body => body(), Retain, token); Retain(actorChanged);
            Assert.False((await actorChanged).Allowed);
            var wrongAction = resolver.EvaluateWithinOriginalSourceAsync(binding.OriginalActor, "dev.project.current.read", scope,
                body => body(), Retain, token); Retain(wrongAction);
            Assert.False((await wrongAction).Allowed); Assert.Equal(4, resolverCalls);
            Assert.All(reads.Captures, value => Assert.True(native.IsClosedOriginalRead(reads.Actual.OriginalSelection,
                value.OriginalCaptureTask, value.OriginalRead, value.Close!)));
            Assert.All(CurrentProjectHandles(native), handle => Assert.True(handle.IsClosed));
            async Task<DeveloperOperationResult<DeveloperActionObservation>> Execute()
            { binding = await sameBridge.ResolveOriginalBindingAsync(prepared.Project, body => body(), Retain, token); return CommandControlResult(); }
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            if (command is not null) try { await command.JoinOriginalAsync(); } catch (Exception error) { Keep(command.OriginalCommandTask, error); }
            await CloseCommandOwners(bridge, reads, native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Retain(Task actual) { lock (raw) raw.Add(actual); }
        void Keep(Task? task, Exception error)
        { foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error)) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    }

    [LinuxDirectoryFact]
    public async Task Genuine_canceled_raw_capture_cannot_cancel_a_synchronous_post_enrollment_callback_failure()
    {
        await Check(false); await Check(true);
        async Task Check(bool failRetainer)
        {
            var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var actualCanceled = Task.FromCanceled<IDeveloperOriginalProjectCommandNativeRead>(canceled.Token);
            var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refusal = new OperationCanceledException("same synchronous callback refused after actual canceled capture enrollment");
            FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
            CommandReads? reads = null; FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
            FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? command = null;
            Task<IDeveloperWorkspaceOriginalExecutionBinding>? bindingTask = null; Task? close = null;
            var raw = new List<Task>(); var errors = new List<Exception>(); var failures = 0; var captures = 0;
            try
            {
                var prepared = await CreateCommandReads(rig, token, raw);
                (selected, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
                reads.CaptureOverride = (sameRead, _, _, _) =>
                { Assert.Same(reads.Actual, sameRead); Interlocked.Increment(ref captures); return actualCanceled; };
                var tools = new CommandDenyTools();
                bridge = new(reads, native, () => throw new NotSupportedException("No Home execute consent in this source-status control."), tools);
                var sameBridge = bridge;
                command = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference, prepared.Context,
                    Execute, Scope, Retain, token);
                await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                Assert.NotNull(bindingTask);
                var bindingFailure = await Assert.ThrowsAsync<AggregateException>(() => bindingTask);
                CheckCauses(bindingFailure);
                Assert.True(actualCanceled.IsCanceled); Assert.False(actualCanceled.IsFaulted);
                Assert.True(bindingTask.IsFaulted); Assert.False(bindingTask.IsCanceled);
                Assert.Contains(raw, task => ReferenceEquals(task, actualCanceled));
                Assert.Equal(1, captures); Assert.Equal(1, failures);
                Assert.Empty(reads.Captures); Assert.Equal(0, tools.Preparations);
                var commandFailure = await Assert.ThrowsAsync<AggregateException>(() => command.JoinOriginalAsync());
                CheckCauses(commandFailure);
                Assert.True(command.OriginalCommandTask.IsFaulted); Assert.False(command.OriginalCommandTask.IsCanceled);
                close = bridge.CloseAndDrainOriginalExecutionAsync();
                var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
                CheckCauses(closeFailure);
                Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
                Assert.All(CurrentProjectHandles(native), handle => Assert.True(handle.IsClosed));
                async Task<DeveloperOperationResult<DeveloperActionObservation>> Execute()
                {
                    bindingTask = sameBridge.ResolveOriginalBindingAsync(prepared.Project, Scope, Retain, token);
                    Retain(bindingTask); await bindingTask; return CommandControlResult();
                }
            }
            catch (Exception error) { Keep(null, error); }
            finally
            {
                if (reads is not null) reads.CaptureOverride = null;
                try { await actualCanceled; } catch (Exception error) { Keep(actualCanceled, error); }
                if (bindingTask is not null) try { await bindingTask; } catch (Exception error) { Keep(bindingTask, error); }
                if (command is not null) try { await command.JoinOriginalAsync(); } catch (Exception error) { Keep(command.OriginalCommandTask, error); }
                if (close is not null) try { await close; } catch (Exception error) { Keep(close, error); }
                await CloseCommandOwners(bridge, reads, native, selected, rig, raw, Keep);
            }
            if (errors.Count != 0) throw new AggregateException(errors);
            void Scope(Action body)
            {
                body();
                if (!failRetainer && enrolled.Task.IsCompletedSuccessfully && Interlocked.CompareExchange(ref failures, 1, 0) == 0)
                    throw refusal;
            }
            void Retain(Task actual)
            {
                lock (raw) raw.Add(actual);
                if (!ReferenceEquals(actual, actualCanceled)) return;
                enrolled.TrySetResult();
                if (failRetainer && Interlocked.CompareExchange(ref failures, 1, 0) == 0) throw refusal;
            }
            bool Expected(Exception cause) => ReferenceEquals(cause, refusal) ||
                cause is TaskCanceledException cancellation && ReferenceEquals(cancellation.Task, actualCanceled) && actualCanceled.IsCanceled;
            void CheckCauses(Exception error)
            {
                var causes = CurrentProjectLeaves(error).ToArray();
                Assert.Contains(causes, cause => ReferenceEquals(cause, refusal));
                Assert.Contains(causes, cause => cause is TaskCanceledException cancellation && ReferenceEquals(cancellation.Task, actualCanceled));
                Assert.All(causes, cause => Assert.True(Expected(cause), "An unknown direct source or cleanup cause must not be accepted."));
            }
            void Keep(Task? actual, Exception error)
            {
                KeepCauses(error);
                if (actual?.Exception is { } group) KeepCauses(group);
                void KeepCauses(Exception observed)
                {
                    foreach (var cause in CurrentProjectLeaves(observed))
                        if (!Expected(cause) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
                }
            }
        }
    }

    [LinuxDirectoryFact]
    public async Task Canceled_actual_execution_decision_keeps_the_same_outer_publication_callback_failure()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refusal = new OperationCanceledException("same resolver publication retainer refused after actual decision capture");
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        CommandReads? reads = null; FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge = null;
        FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? command = null;
        IDeveloperWorkspaceOriginalExecutionBinding? binding = null;
        Task<ResourceAccessDecision>? actualDecision = null; Task<ResourceAccessDecision>? resolution = null; Task? close = null;
        var raw = new List<Task>(); var errors = new List<Exception>(); var armed = false; var publications = 0;
        var productiveCanceled = new HashSet<Task>(ReferenceEqualityComparer.Instance) { held.Task };
        try
        {
            var prepared = await CreateCommandReads(rig, token, raw);
            (selected, native, reads) = (prepared.SelectionOwner, prepared.Native, prepared.Reads);
            bridge = new(reads, native, () => throw new NotSupportedException("No execute consent in this resolver-status control."), new CommandDenyTools());
            var sameBridge = bridge;
            command = bridge.CaptureOriginalCommandWithinSource(reads.Actual, prepared.Project.Reference, prepared.Context,
                Execute, body => body(), Retain, token);
            await command.JoinOriginalAsync(); Assert.NotNull(binding);
            var scope = Assert.Single(bridge.GetOriginalExecutionScopes(binding));
            var capturesBefore = reads.Captures.Count;
            reads.Validation = (sourceScope, retain, cancellation) =>
            {
                FilesOriginalSourceCallbackScope.Invoke(() => { cancellation.ThrowIfCancellationRequested(); retain(held.Task); }, sourceScope);
                return held.Task;
            };
            var resolver = new FilesDeveloperOriginalCurrentProjectExecutionResolver(() => sameBridge);
            armed = true;
            resolution = resolver.EvaluateWithinOriginalSourceAsync(binding.OriginalActor, "dev.workspace.execute", scope,
                body => body(), Retain, token);
            await refused.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.NotNull(actualDecision); Assert.False(actualDecision.IsCompleted); Assert.False(resolution.IsCompleted);
            Assert.Contains(raw, actual => ReferenceEquals(actual, actualDecision));
            Assert.Contains(raw, actual => ReferenceEquals(actual, held.Task));
            held.TrySetCanceled(canceled.Token);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => resolution);
            // Freeze the actual canceled productive cohort before requesting any close.
            // Later cleanup Tasks must never enlarge the accepted negative outcome.
            lock (raw)
                foreach (var actual in raw.Where(actual => actual.IsCanceled)) productiveCanceled.Add(actual);
            productiveCanceled.Add(actualDecision);
            var causes = CurrentProjectLeaves(failure).ToArray();
            Assert.Contains(causes, cause => ReferenceEquals(cause, refusal));
            Assert.Contains(causes, cause => cause is TaskCanceledException cancellation && KnownCanceled(cancellation.Task));
            Assert.All(causes, cause => Assert.True(Expected(cause), "Only exact known canceled original Tasks and the SAME publication failure are expected."));
            Assert.True(actualDecision.IsCanceled); Assert.False(actualDecision.IsFaulted);
            Assert.True(held.Task.IsCanceled); Assert.False(held.Task.IsFaulted);
            Assert.True(resolution.IsFaulted); Assert.False(resolution.IsCanceled);
            Assert.Equal(2, publications); Assert.Equal(capturesBefore, reads.Captures.Count);
            close = bridge.CloseAndDrainOriginalExecutionAsync();
            var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.All(CurrentProjectLeaves(closeFailure), cause => Assert.True(
                cause is TaskCanceledException cancellation && KnownCanceled(cancellation.Task), "Whole bridge close must retain the actual canceled source cohort."));
            Assert.All(CurrentProjectHandles(native), handle => Assert.True(handle.IsClosed));
            async Task<DeveloperOperationResult<DeveloperActionObservation>> Execute()
            {
                binding = await sameBridge.ResolveOriginalBindingAsync(prepared.Project, body => body(), Retain, token);
                return CommandControlResult();
            }
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            armed = false; held.TrySetCanceled(canceled.Token);
            if (reads is not null) reads.Validation = null;
            try { await held.Task; } catch (Exception error) { Keep(held.Task, error); }
            if (actualDecision is not null) try { await actualDecision; } catch (Exception error) { Keep(actualDecision, error); }
            if (resolution is not null) try { await resolution; } catch (Exception error) { Keep(resolution, error); }
            if (command is not null) try { await command.JoinOriginalAsync(); } catch (Exception error) { Keep(command.OriginalCommandTask, error); }
            if (close is not null) try { await close; } catch (Exception error) { Keep(close, error); }
            await CloseCommandOwners(bridge, reads, native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Retain(Task actual)
        {
            lock (raw)
            {
                raw.Add(actual);
                if (ReferenceEquals(actual, held.Task)) enrolled.TrySetResult();
                if (!armed || actual is not Task<ResourceAccessDecision> decision) return;
                actualDecision ??= decision;
                if (ReferenceEquals(actual, actualDecision) && ++publications == 2)
                { refused.TrySetResult(); throw refusal; }
            }
        }
        bool KnownCanceled(Task? actual)
        {
            if (actual?.IsCanceled != true) return false;
            return productiveCanceled.Contains(actual);
        }
        bool Expected(Exception cause) => ReferenceEquals(cause, refusal) ||
            cause is TaskCanceledException cancellation && KnownCanceled(cancellation.Task);
        void Keep(Task? actual, Exception error)
        {
            KeepCauses(error);
            if (actual?.Exception is { } group) KeepCauses(group);
            void KeepCauses(Exception observed)
            {
                foreach (var cause in CurrentProjectLeaves(observed))
                    if (!Expected(cause) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
            }
        }
    }

    private static async Task<CommandFixture> CreateCommandReads(Rig rig, CancellationToken token, List<Task> raw)
    {
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var container = CurrentProjectContainer(rig.SourcePath); var conversation = CurrentProjectConversation(container);
        var selectionOwner = new FilesDeveloperOriginalCurrentProjectSelection(rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(),
            rig.WorkspaceStore, new CurrentProjectContainers(container));
        IDeveloperOriginalCurrentProjectNativeSource? native = null;
        var errors = new List<Exception>();
        try
        {
            var reference = Reference(intent);
            var selection = await selectionOwner.SelectOriginalWithinSourceAsync(conversation, container,
                JsonSerializer.Serialize(reference), null, null, body => body(), Retain, token);
            var initial = new CurrentProjectReads(selection);
            native = new WorkspaceToolService().CreateOriginalCurrentProjectNativeSource(() => selectionOwner, initial, rig.WorkspaceStore);
            var stored = await rig.WorkspaceStore.GetAsync(intent.WorkspaceId, token);
            if (!stored.Succeeded || stored.Value is null) throw new InvalidOperationException("The genuine saved document fixture is unavailable.");
            var workspace = stored.Value;
            var project = new DeveloperResolvedProject(reference, workspace,
                workspace.Projects.Single(row => row.ProjectId == reference.ProjectId),
                workspace.Roots.Single(row => row.RootId == reference.RootId), null);
            var reads = new CommandReads(selection, selectionOwner.GetOriginalDescriptor(selection), initial.Actual, native);
            return new(selectionOwner, native, reads, project,
                new(Guid.NewGuid(), Guid.NewGuid(), conversation.Id, Guid.NewGuid(), 1, Guid.NewGuid()));
        }
        catch (Exception error) { errors.Add(error); }
        // Protect the earliest actual selection/native owner acquisition even when setup
        // of this real component rig fails before the caller receives its product.
        native?.RequestOriginalRetirement(); selectionOwner.RequestOriginalCurrentProjectRetirement();
        var closes = new List<Task>();
        if (native is not null) try { closes.Add(native.CloseAndDrainOriginalAsync()); } catch (Exception error) { errors.Add(error); }
        try { closes.Add(selectionOwner.CloseAndDrainOriginalCurrentProjectsAsync()); } catch (Exception error) { errors.Add(error); }
        foreach (var close in closes) try { await close; } catch (Exception error) { errors.AddRange(CurrentProjectLeaves(close.Exception ?? error)); }
        throw new AggregateException(errors);
        void Retain(Task task) { lock (raw) raw.Add(task); }
    }
    private static async Task CloseCommandOwners(FilesDeveloperOriginalCurrentProjectExecutionBridge? bridge,
        CommandReads? reads, IDeveloperOriginalCurrentProjectNativeSource? native,
        FilesDeveloperOriginalCurrentProjectSelection? selected, Rig rig, List<Task> raw, Action<Task?, Exception> keep)
    {
        bridge?.RequestOriginalExecutionRetirement(); reads?.Actual.RequestOriginalRetirement();
        var closes = new List<Task>();
        if (bridge is not null) try { closes.Add(bridge.CloseAndDrainOriginalExecutionAsync()); } catch (Exception error) { keep(null, error); }
        if (reads is not null) try { closes.Add(reads.Actual.CloseAndDrainOriginalAsync()); } catch (Exception error) { keep(null, error); }
        foreach (var close in closes) try { await close; } catch (Exception error) { keep(close, error); }
        await CloseCurrentProjectOwners(native, selected, rig, raw, keep);
    }

    // Private synthetic Home command READ only for component/kernel ordering. It uses
    // the actual maintained Linux native owner; it issues no Home review, Task/action,
    // model, process dispatch or restored capsule permission.
    private sealed class CommandReads : IDeveloperOriginalProjectCommandReadSource
    {
        internal readonly CommandRead Actual;
        internal readonly List<CommandNative> Captures = [];
        internal Func<Action<Action>, Action<Task>, CancellationToken, Task>? Validation;
        internal Func<IDeveloperOriginalProjectCommandRead, Action<Action>, Action<Task>, CancellationToken,
            Task<IDeveloperOriginalProjectCommandNativeRead>>? CaptureOverride;
        private readonly IDeveloperOriginalCurrentProjectNativeSource _native;
        internal CommandReads(IDeveloperOriginalCurrentProjectSelection selection,
            IDeveloperOriginalCurrentProjectDescriptor descriptor, IDeveloperProjectOriginalReadAdmission admission,
            IDeveloperOriginalCurrentProjectNativeSource native)
        { _native = native; Actual = new(this, selection, descriptor, admission); }
        public bool IsIssuedOriginalCommandRead(IDeveloperOriginalProjectCommandRead value) => ReferenceEquals(value, Actual) && !Actual.Sealed;
        public bool IsOwnedOriginalCommandRead(IDeveloperOriginalProjectCommandRead value) => ReferenceEquals(value, Actual);
        public Task<IDeveloperOriginalProjectCommandRead> AcquireOriginalCommandReadWithinSourceAsync(Conversation conversation,
            ContainerDefinition container, string reference, Action<Action> scope, Action<Task> retain, CancellationToken token)
            => throw new NotSupportedException("This synthetic control never acquires Home manual approval.");
        public Task ValidateOriginalCommandReadWithinSourceAsync(IDeveloperOriginalProjectCommandRead value,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            FilesOriginalSourceCallbackScope.Invoke(() => { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalCommandRead(value)) throw new UnauthorizedAccessException(); }, scope);
            if (Validation is not null) return Validation(scope, retain, token);
            var actual = Task.CompletedTask; retain(actual); return actual;
        }
        public Task<IDeveloperOriginalProjectCommandNativeRead> CaptureOriginalCommandNativeReadWithinSourceAsync(
            IDeveloperOriginalProjectCommandRead value, Action<Action> scope, Action<Task> retain, CancellationToken token)
            => CaptureOverride is { } source ? source(value, scope, retain, token) : CaptureOrdinaryAsync(value, scope, retain, token);
        private async Task<IDeveloperOriginalProjectCommandNativeRead> CaptureOrdinaryAsync(
            IDeveloperOriginalProjectCommandRead value, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<IDeveloperOriginalCurrentProjectNativeRead>? actual = null; IDeveloperOriginalCurrentProjectNativeRead? read = null;
            var errors = new List<Exception>(); CommandNative? result = null;
            try
            {
                FilesOriginalSourceCallbackScope.Invoke(() =>
                {
                    if (!IsIssuedOriginalCommandRead(value)) throw new UnauthorizedAccessException();
                    actual = _native.CaptureOriginalWithinSourceAsync(Actual.OriginalSelection, Actual.OriginalReadAdmission, scope, retain, token);
                    retain(actual);
                }, scope);
            }
            catch (Exception error) { errors.AddRange(CurrentProjectLeaves(error)); }
            if (actual is not null)
                try { read = await actual; result = new(Actual, actual, read); Captures.Add(result); }
                catch (Exception error) { errors.AddRange(CurrentProjectLeaves(actual.Exception ?? error)); }
            if (errors.Count != 0)
            {
                if (result is not null)
                {
                    try { result.Close = result.OriginalRead.DisposeAsync().AsTask(); } catch (Exception error) { errors.Add(error); }
                    if (result.Close is not null) try { await result.Close; } catch (Exception error) { errors.AddRange(CurrentProjectLeaves(result.Close.Exception ?? error)); }
                }
                throw new AggregateException(errors);
            }
            return result ?? throw new UnauthorizedAccessException();
        }
        public bool IsIssuedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead value, IDeveloperOriginalProjectCommandNativeRead result)
            => IsIssuedOriginalCommandRead(value) && IsOwnedOriginalCommandNativeRead(value, result) &&
                _native.IsIssuedOriginalRead(Actual.OriginalSelection, result.OriginalCaptureTask, result.OriginalRead);
        public bool IsOwnedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead value, IDeveloperOriginalProjectCommandNativeRead result)
            => ReferenceEquals(value, Actual) && result is CommandNative native && Captures.Contains(native);
        public Task ValidateOriginalCommandNativeReadWithinSourceAsync(IDeveloperOriginalProjectCommandRead value,
            IDeveloperOriginalProjectCommandNativeRead result, Action<Action> scope, Action<Task> retain, CancellationToken token)
            => _native.ValidateOriginalReadWithinSourceAsync(Actual.OriginalSelection, result.OriginalCaptureTask, result.OriginalRead, scope, retain, token);
        public bool IsClosedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead value,
            IDeveloperOriginalProjectCommandNativeRead result, Task close)
        {
            if (result is CommandNative native && IsOwnedOriginalCommandNativeRead(value, result))
            { native.Close = close; return _native.IsClosedOriginalRead(Actual.OriginalSelection, result.OriginalCaptureTask, result.OriginalRead, close); }
            return false;
        }
        internal sealed class CommandRead(CommandReads owner, IDeveloperOriginalCurrentProjectSelection selection,
            IDeveloperOriginalCurrentProjectDescriptor descriptor, IDeveloperProjectOriginalReadAdmission admission) : IDeveloperOriginalProjectCommandRead
        {
            internal bool Sealed; private Task? _close;
            public IDeveloperOriginalCurrentProjectSelection OriginalSelection => selection;
            public IDeveloperProjectOriginalReadAdmission OriginalReadAdmission => admission;
            public IDeveloperOriginalCurrentProjectDescriptor OriginalDescriptor => descriptor;
            public TaskRunColdProjectIdentity OriginalIdentity
            {
                get
                {
                    var first = owner.Captures.First();
                    return new(descriptor.WorkspaceId, descriptor.ProjectId, descriptor.RootId,
                        descriptor.WorkspaceRevision, descriptor.ProjectRevision, descriptor.RepositoryBindingId,
                        descriptor.RegisteredProjectRoot, descriptor.ExactProjectReferenceJson,
                        descriptor.OriginalContainer.Id, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                            JsonSerializer.SerializeToUtf8Bytes(descriptor.OriginalContainer, new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant(),
                        descriptor.OriginalContainer.Instructions, first.OriginalRead.OriginalWorkspaceDocumentSha256,
                        first.OriginalRead.OriginalRegisteredRootFingerprint, descriptor.OriginalActor);
                }
            }
            public Task OriginalPreparation => Task.CompletedTask;
            public void RequestOriginalRetirement() => Sealed = true;
            public void DemandExternalOriginalJoin() => owner._native.DemandExternalOriginalJoin();
            public Task CloseAndDrainOriginalAsync()
            { DemandExternalOriginalJoin(); Sealed = true; return _close ??= Close(); }
            private async Task Close()
            {
                var closes = new List<Task>(); var errors = new List<Exception>();
                foreach (var capture in owner.Captures)
                    try { capture.Close ??= capture.OriginalRead.DisposeAsync().AsTask(); closes.Add(capture.Close); }
                    catch (Exception error) { errors.Add(error); }
                foreach (var close in closes) try { await close; } catch (Exception error) { errors.AddRange(CurrentProjectLeaves(close.Exception ?? error)); }
                if (errors.Count != 0) throw new AggregateException(errors);
            }
            public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
        }
        internal sealed record CommandNative(IDeveloperOriginalProjectCommandRead OriginalCommandRead,
            Task<IDeveloperOriginalCurrentProjectNativeRead> OriginalCaptureTask, IDeveloperOriginalCurrentProjectNativeRead OriginalRead)
            : IDeveloperOriginalProjectCommandNativeRead
        { internal Task? Close; }
    }
    private sealed class CommandDenyTools : ITaskRunToolActionOwner
    {
        internal int Preparations;
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string name) => false;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt,
            TaskExecutionSnapshot snapshot, Guid action, OllamaToolCall call, ToolRuntimeKind runtime,
            PermissionMode mode, string? root, CancellationToken token) { Preparations++; throw new NotSupportedException(); }
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation,
            Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot snapshot, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token) => throw new NotSupportedException();
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot snapshot, CancellationToken token) => throw new NotSupportedException();
    }
}
