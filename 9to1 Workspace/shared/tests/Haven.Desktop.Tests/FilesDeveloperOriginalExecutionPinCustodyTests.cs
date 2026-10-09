using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Genuine_pin_cleanup_identity_survives_retirement_without_live_execution_authority()
    {
        var rig = await Rig.Create(true, true);
        Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? acquisition = null;
        IDeveloperWorkspaceOriginalExecutionCommitPin? pin = null;
        Task? close = null; var errors = new List<Exception>();
        try
        {
            var token = TestContext.Current.CancellationToken;
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(
                rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
            var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
            IDeveloperWorkspaceOriginalExecutionPinCustodySource custody = rig.Effector;
            acquisition = rig.Effector.AcquireOriginalExecutionPinAsync(binding, token);
            pin = await acquisition;
            Assert.True(custody.IsOwnedOriginalExecutionPin(binding, pin));
            Assert.True(rig.Effector.IsIssuedOriginalExecutionPin(binding, pin));
            var copy = new CopiedCleanupBinding(binding); var unknown = new UnknownCleanupPin();
            Assert.False(custody.IsOwnedOriginalExecutionPin(copy, pin));
            Assert.False(custody.IsOwnedOriginalExecutionPin(binding, unknown));
            Assert.Equal(0, unknown.DisposeCalls);
            rig.Effector.RequestOriginalFolderSetupRetirement();
            Assert.Same(pin, await acquisition);
            Assert.True(custody.IsOwnedOriginalExecutionPin(binding, pin));
            Assert.False(rig.Effector.IsIssuedOriginalBinding(binding));
            Assert.False(rig.Effector.IsIssuedOriginalExecutionPin(binding, pin));
            Assert.Throws<UnauthorizedAccessException>((Action)(() => pin.DemandOriginalExecutionBinding()));
            close = pin.DisposeAsync().AsTask();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(custody.IsOwnedOriginalExecutionPin(binding, pin));
            Assert.False(rig.Effector.IsIssuedOriginalExecutionPin(binding, pin));
            Assert.Throws<UnauthorizedAccessException>((Action)(() => pin.DemandOriginalExecutionBinding()));
        }
        catch (Exception cause) { Capture(null, cause); }
        finally
        {
            if (pin is not null && close is null)
                try { close = pin.DisposeAsync().AsTask(); } catch (Exception cause) { Capture(null, cause); }
            if (acquisition is not null) try { await acquisition; } catch (Exception cause) { Capture(acquisition, cause); }
            if (close is not null) try { await close; } catch (Exception cause) { Capture(close, cause); }
            Task? rigClose = null;
            try { rigClose = rig.DisposeAsync().AsTask(); } catch (Exception cause) { Capture(null, cause); }
            if (rigClose is not null) try { await rigClose; } catch (Exception cause) { Capture(rigClose, cause); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? actual, Exception cause)
        {
            AddCause(cause);
            if (actual?.Exception is { } group) { AddCause(group); foreach (var direct in group.InnerExceptions) AddCause(direct); }
        }
        void AddCause(Exception cause) { if (!errors.Any(original => ReferenceEquals(original, cause))) errors.Add(cause); }
    }

    private sealed class CopiedCleanupBinding(IDeveloperWorkspaceOriginalExecutionBinding observed)
        : IDeveloperWorkspaceOriginalExecutionBinding
    {
        public Guid WorkspaceId => observed.WorkspaceId;
        public Guid ProjectId => observed.ProjectId;
        public Guid RootId => observed.RootId;
        public long WorkspaceRevision => observed.WorkspaceRevision;
        public string CanonicalRoot => observed.CanonicalRoot;
        public AuthenticatedResourceActor OriginalActor => observed.OriginalActor;
    }
    private sealed class UnknownCleanupPin : IDeveloperWorkspaceOriginalExecutionCommitPin
    {
        public int DisposeCalls;
        public void DemandOriginalExecutionBinding() => throw new UnauthorizedAccessException();
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
