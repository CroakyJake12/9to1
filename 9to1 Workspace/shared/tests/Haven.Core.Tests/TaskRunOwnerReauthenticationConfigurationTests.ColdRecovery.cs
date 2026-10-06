using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Unconfigured_cold_authority_refuses_before_claim_context_reads_and_keeps_original_local_lease()
    {
        var rig = new Setup(); var claim = new UnissuedColdClaim(); var context = new UnissuedColdContext(claim);
        var proposed = Proposed();
        var task = proposed with { OwnerBinding = await rig.Authority.AuthorizeStartAsync(proposed, default) };
        var route = await rig.Authority.CaptureSelectedRouteAsync(task, rig.Provider.Model, [ToolCapability.Text], []);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), route, null, default);
        try
        {
            ITaskRunColdOwnerAuthority cold = rig.Authority;
            Assert.False(cold.HasOriginalColdRecoveryComposition(null!, null!));
            Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = cold.PrepareOriginalColdOwnerAsync(claim, context, task, default); }));
            Assert.Equal(0, claim.Reads); Assert.Equal(0, context.Reads);
            await lease.RevalidateAsync(default); Assert.Same(task.OwnerBinding, lease.Owner);
            var pin = await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default);
            Assert.NotNull(pin); await pin!.DisposeAsync();
        }
        finally { await lease.DisposeAsync(); await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Public_cold_admission_interface_cannot_replace_an_original_private_authority_receipt()
    {
        var rig = new Setup(); var claim = new UnissuedColdClaim(); var context = new UnissuedColdContext(claim);
        var admission = new UnissuedColdAdmission(claim, context);
        try
        {
            Assert.False(rig.Authority.IsIssuedOriginalColdOwner(admission, claim, context));
            Assert.False(rig.Authority.HasOriginalColdRecoveryComposition(null!, null!));
            Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = rig.Authority.ActivateAcknowledgedOriginalColdOwnerAsync(admission, null!, default); }));
            Assert.Equal(0, claim.Reads); Assert.Equal(0, context.Reads); Assert.Equal(0, admission.Reads);
            Assert.Equal(0, rig.Actors.Reads);
        }
        finally { await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Missing_cold_configuration_is_observation_only_and_cannot_issue_or_seal_a_normal_owner()
    {
        var rig = new Setup();
        try
        {
            Assert.False(rig.Authority.HasOriginalColdRecoveryComposition(null!, null!));
            Assert.Equal(0, rig.Actors.Reads);
            Assert.Throws<ArgumentNullException>(() => rig.Authority.ConfigureOriginalColdRecoverySources(null!, null!));
            Assert.Equal(0, rig.Actors.Reads);
            var proposed = Proposed(); var binding = await rig.Authority.AuthorizeStartAsync(proposed, default);
            Assert.Equal(proposed.TaskId, binding.TaskId); Assert.Equal(proposed.ExecutionId, binding.ExecutionId);
            Assert.Equal("revision-one", binding.AuthenticationRevision);
        }
        finally { await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }
    private sealed class UnissuedColdClaim : ITaskRunColdJournalClaim
    {
        internal int Reads;
        public ITaskRunColdJournalEntry OriginalEntry { get { Reads++; throw new NotSupportedException("Unissued metadata must not be read."); } }
        public TaskExecutionSnapshot OriginalExpected { get { Reads++; throw new NotSupportedException(); } }
        public Guid ClaimId { get { Reads++; throw new NotSupportedException(); } }
        public ValueTask DisposeAsync() => throw new NotSupportedException("This interface is not an owned journal claim.");
    }
    private sealed class UnissuedColdContext(UnissuedColdClaim claim) : ITaskRunColdContextLease
    {
        internal int Reads;
        public ITaskRunColdJournalClaim OriginalClaim { get { Reads++; return claim; } }
        public AuthenticatedResourceActor CurrentActor { get { Reads++; throw new NotSupportedException(); } }
        public ValueTask DisposeAsync() => throw new NotSupportedException("No actual context was issued.");
    }
    private sealed class UnissuedColdAdmission(UnissuedColdClaim claim, UnissuedColdContext context) : ITaskRunColdOwnerAdmission
    {
        internal int Reads;
        public ITaskRunColdJournalClaim OriginalClaim { get { Reads++; return claim; } }
        public ITaskRunColdContextLease OriginalContext { get { Reads++; return context; } }
        public TaskExecutionOwnerBinding PreviousOwner => throw new NotSupportedException();
        public TaskExecutionOwnerBinding NextOwner => throw new NotSupportedException();
        public ValueTask RevalidateAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => throw new NotSupportedException("No private admission was issued.");
    }
}
