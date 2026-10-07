using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Real maintained Linux handles/tasks; the injected parent refuses joins and issues
// no execution binding, metadata store, Home consent or complete-setup authority.
public sealed partial class DeveloperOriginalDirectoryObservationTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Directory_leaf_close_preserves_physical_self_join_refusal_without_joining_live_saved_root_parent()
    {
        var parent = new LiveSavedRootJoinRefusal();
        await using var rig = new Rig(parent); var token = CancellationToken.None;
        var capture = await rig.Capture(token); var intent = rig.Intent(capture);
        var permission = rig.Setups.Issue(intent); var step = intent.Steps[1];
        var prepared = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, step, token);
        var before = File.ReadAllBytes(rig.File); var oldContext = ExecutionContext.Capture();
        Assert.NotNull(oldContext);
        Entry? entry = null; Task<IDeveloperProjectOriginalDirectoryObservation>? actual = null;
        Exception? physicalRefusal = null; var errors = new List<Exception>();
        try
        {
            var actualEntry = await permission.EnterOriginalStepAsync(step, token); entry = actualEntry;
            actualEntry.Check = () => ExecutionContext.Run(oldContext, _ =>
            {
                physicalRefusal = Assert.Throws<InvalidOperationException>((Action)(() => { _ = prepared.CloseAndDrainAsync(); }));
            }, null);
            actual = actualEntry.RunOriginalStep(step, () => prepared.ObserveOriginalDirectoryAsync(actualEntry, token), token);
            var observation = await actual;
            Assert.IsType<InvalidOperationException>(physicalRefusal);
            actualEntry.Check = null; await actualEntry.DisposeAsync(); entry = null;
            parent.Live = true; var parentCalls = parent.JoinDemands;
            var close = prepared.CloseAndDrainAsync(); Assert.Same(close, prepared.CloseAndDrainAsync()); await close;
            Assert.Equal(parentCalls, parent.JoinDemands);
            Assert.True(rig.Directories.IsIssuedOriginalDirectoryOutcome(prepared, actual, observation));
            Assert.Equal(before, File.ReadAllBytes(rig.File)); Assert.Equal(1, permission.EntryStarts);
            Assert.Same(parent.Refusal, Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = rig.Source.CloseAndDrainOriginalCapturesAsync(); })));
            Assert.Equal(parentCalls + 1, parent.JoinDemands);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            parent.Live = false;
            if (actual is not null) try { await actual; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) { entry.Check = null; try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); } }
            try { await prepared.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await rig.Source.CloseAndDrainOriginalCapturesAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Directory leaf, genuine self-join and independent cleanup controls failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Directory_registration_leaf_refuses_live_own_original_and_drains_held_metadata_without_joining_parent()
    {
        var parent = new LiveSavedRootJoinRefusal();
        await using var rig = new Rig(parent); var token = CancellationToken.None;
        var original = await OriginalRegistration(rig, token);
        var before = File.ReadAllBytes(rig.File); var handle = OriginalRegistrationHandle(original.Preparation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new object(); Entry? entry = null; Task<object>? driver = null, metadata = null;
        Task? close = null; Exception? ambientRefusal = null; var errors = new List<Exception>();
        async Task<object> OriginalMetadata()
        {
            await Task.Yield();
            ambientRefusal = Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = original.Preparation.CloseAndDrainAsync(); }));
            entered.TrySetResult(); return await held.Task;
        }
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry,
                () => metadata = OriginalMetadata(), token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.IsType<InvalidOperationException>(ambientRefusal); Assert.NotNull(metadata);
            parent.Live = true; var parentCalls = parent.JoinDemands;
            close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync());
            Assert.Equal(parentCalls, parent.JoinDemands); Assert.False(close.IsCompleted); Assert.False(driver.IsCompleted);
            Assert.False(handle.IsClosed); held.TrySetResult(result);
            Assert.Same(result, await driver); await close; Assert.True(handle.IsClosed);
            Assert.True(original.Source.IsIssuedOriginalDirectoryRegistrationOutcome(original.Preparation, driver, metadata!, result));
            Assert.Equal(before, File.ReadAllBytes(rig.File)); Assert.Equal(parentCalls, parent.JoinDemands);
            await actualEntry.DisposeAsync(); entry = null;
            Assert.Same(parent.Refusal, Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = rig.Source.CloseAndDrainOriginalCapturesAsync(); })));
            Assert.Equal(parentCalls + 1, parent.JoinDemands);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            parent.Live = false; held.TrySetResult(result);
            if (driver is not null) try { await driver; } catch (Exception error) { errors.Add(error); }
            if (close is not null) try { await close; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            try { await rig.Source.CloseAndDrainOriginalCapturesAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Directory registration leaf, original metadata and independent cleanup controls failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task File_registration_leaf_preserves_restored_and_async_self_join_refusals_and_drains_without_parent_join()
    {
        var parent = new LiveSavedRootJoinRefusal();
        await using var rig = new Rig(parent); var token = CancellationToken.None;
        var original = await OriginalFileRegistration(rig, token);
        var before = File.ReadAllBytes(rig.File); var handle = OriginalFileRegistrationHandle(original.Preparation);
        var oldContext = ExecutionContext.Capture(); Assert.NotNull(oldContext);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new object(); Entry? entry = null; Task<object>? driver = null, metadata = null; Task? close = null;
        Exception? physicalRefusal = null, ambientRefusal = null; var errors = new List<Exception>();
        async Task<object> OriginalMetadata(FileStream stream)
        {
            Assert.Same(handle, stream.SafeFileHandle);
            original.Preparation.RunOriginalFileSourceScope(() => ExecutionContext.Run(oldContext, _ =>
            {
                physicalRefusal = Assert.Throws<InvalidOperationException>((Action)(() =>
                { _ = original.Preparation.CloseAndDrainAsync(); }));
            }, null));
            await Task.Yield();
            ambientRefusal = Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = original.Preparation.CloseAndDrainAsync(); }));
            entered.TrySetResult(); return await held.Task;
        }
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalFileRegistrationAsync(actualEntry,
                stream => metadata = OriginalMetadata(stream), token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.IsType<InvalidOperationException>(physicalRefusal); Assert.IsType<InvalidOperationException>(ambientRefusal);
            Assert.NotNull(metadata); parent.Live = true; var parentCalls = parent.JoinDemands;
            close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync());
            Assert.Equal(parentCalls, parent.JoinDemands); Assert.False(close.IsCompleted); Assert.False(driver.IsCompleted);
            Assert.False(handle.IsClosed); held.TrySetResult(result);
            Assert.Same(result, await driver); await close; Assert.True(handle.IsClosed);
            Assert.True(original.Source.IsIssuedOriginalFileRegistrationOutcome(original.Preparation, driver, metadata!, result));
            Assert.Equal(before, File.ReadAllBytes(rig.File)); Assert.Equal(parentCalls, parent.JoinDemands);
            await actualEntry.DisposeAsync(); entry = null;
            Assert.Same(parent.Refusal, Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = rig.Source.CloseAndDrainOriginalCapturesAsync(); })));
            Assert.Equal(parentCalls + 1, parent.JoinDemands);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            parent.Live = false; held.TrySetResult(result);
            if (driver is not null) try { await driver; } catch (Exception error) { errors.Add(error); }
            if (close is not null) try { await close; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            try { await rig.Source.CloseAndDrainOriginalCapturesAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("File leaf, original metadata and independent cleanup controls failed.", errors);
    }

    private sealed class LiveSavedRootJoinRefusal : IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource
    {
        internal bool Live;
        internal int JoinDemands;
        internal readonly InvalidOperationException Refusal = new("Controlled live saved-root parent join refusal; no binding is issued.");
        public void DemandExternalOriginalExecutionBindingJoin()
        { JoinDemands++; if (Live) throw Refusal; }
        public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding sameBinding) => false;
        public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
            AuthenticatedResourceActor sameActor, CancellationToken cancellationToken)
            => throw new NotSupportedException("No saved-root authority is issued by this leaf guard fixture.");
        public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
            => throw new NotSupportedException("No execution scope is issued by this leaf guard fixture.");
        public IDeveloperWorkspaceOriginalExecutionDescriptorEvidence GetOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
            => throw new NotSupportedException("No descriptor evidence is issued by this leaf guard fixture.");
        public bool IsIssuedOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
            IDeveloperWorkspaceOriginalExecutionDescriptorEvidence sameEvidence) => false;
    }
}
