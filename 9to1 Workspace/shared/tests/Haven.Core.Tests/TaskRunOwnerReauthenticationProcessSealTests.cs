using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

// Synthetic actual authority/lease controls; the request-only flag is not a join or grant.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Permanent_process_seal_refuses_fresh_start_route_attempt_command_and_pin()
    {
        var rig = new Setup(); var proposed = Proposed();
        var task = proposed with { OwnerBinding = await rig.Authority.AuthorizeStartAsync(proposed, default) };
        var candidate = await rig.Authority.CaptureSelectedRouteAsync(task, rig.Provider.Model, [ToolCapability.Text], []);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var before = rig.Actors.Reads;
        try
        {
            Assert.False(rig.Authority.IsOriginalAdmissionSealed);
            rig.Authority.RequestOriginalAdmissionSeal(); rig.Authority.RequestOriginalAdmissionSeal();
            Assert.True(rig.Authority.IsOriginalAdmissionSealed); Assert.Equal(before, rig.Actors.Reads);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Authority.AuthorizeStartAsync(Proposed(), default));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Authority.CaptureSelectedRouteAsync(task,
                rig.Provider.Model, [ToolCapability.Text], []));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Authority.AuthorizeAttemptAsync(task,
                Guid.NewGuid(), candidate, null, default));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Authority.ValidateTaskCommandAsync(task, "task:steer", default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(default).AsTask());
            Assert.Null(await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default));
            Assert.Equal(before, rig.Actors.Reads); Assert.Equal(task.OwnerBinding, lease.Owner);
        }
        finally { await lease.DisposeAsync(); await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Process_seal_keeps_actual_held_pin_and_close_original_until_real_release()
    {
        var rig = new Setup(); var proposed = Proposed();
        var task = proposed with { OwnerBinding = await rig.Authority.AuthorizeStartAsync(proposed, default) };
        var candidate = await rig.Authority.CaptureSelectedRouteAsync(task, rig.Provider.Model, [ToolCapability.Text], []);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var pin = await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default); Assert.NotNull(pin);
        Task? close = null;
        try
        {
            rig.Authority.RequestOriginalAdmissionSeal(); Assert.True(rig.Authority.IsOriginalAdmissionSealed);
            close = lease.DisposeAsync().AsTask(); Assert.Same(close, lease.DisposeAsync().AsTask());
            Assert.False(close.IsCompleted); // Seal neither releases the original pin nor proves settlement.
            await pin!.DisposeAsync(); pin = null; await close;
            Assert.True(close.IsCompletedSuccessfully); Assert.True(rig.Authority.IsOriginalAdmissionSealed);
            Assert.Null(await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default));
        }
        finally
        {
            if (pin is not null) await pin.DisposeAsync(); close ??= lease.DisposeAsync().AsTask(); await close;
            await rig.Authority.CloseAndDrainOwnerReauthenticationAsync();
        }
    }
}
