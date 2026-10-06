using Haven.Application;
namespace Haven.Core.Tests;
public sealed partial class TaskRunPermissionAuthorityTests
{
    [Fact] public async Task Issued_route_configuration_membership_survives_actual_lease_close_without_reopening_it()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var admission = new TaskRunAttemptAdmission(task, lease.AttemptId, lease);
        var source = (ITaskRunOriginalIssuedRouteConfigurationSource)rig.Authority;
        var original = source.TryObserveOriginalIssuedRouteConfiguration(admission); Assert.NotNull(original);
        try { Assert.True(source.IsIssuedOriginalRouteConfiguration(original!, admission)); Assert.Equal(candidate.ProviderId, original!.ProviderId); }
        finally { await lease.DisposeAsync(); }
        Assert.True(source.IsIssuedOriginalRouteConfiguration(original!, admission));
        Assert.Null(source.TryObserveOriginalIssuedRouteConfiguration(admission));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.ValidateOriginalToolAsync(admission, "read_file", default));
    }
    [Fact] public async Task Copied_admission_or_another_actual_issuer_cannot_validate_historical_configuration()
    {
        var rig = new Rig(); var foreign = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var admission = new TaskRunAttemptAdmission(task, lease.AttemptId, lease);
        var source = (ITaskRunOriginalIssuedRouteConfigurationSource)rig.Authority;
        try
        {
            var original = source.TryObserveOriginalIssuedRouteConfiguration(admission); Assert.NotNull(original);
            Assert.False(source.IsIssuedOriginalRouteConfiguration(original!, admission with { }));
            Assert.False(((ITaskRunOriginalIssuedRouteConfigurationSource)foreign.Authority).IsIssuedOriginalRouteConfiguration(original!, admission));
            Assert.Null(source.TryObserveOriginalIssuedRouteConfiguration(admission with { AttemptId = Guid.NewGuid() }));
        }
        finally { await lease.DisposeAsync(); }
    }
}
