using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class HomeDeveloperProjectSetupPermissionTests
{
    [Fact]
    public async Task Synthetic_journal_receipt_cannot_complete_missing_original_reviewed_steps()
    {
        var completion = new SyntheticCompletion();
        await Run(async rig =>
        {
            await rig.Accept();
            var close = rig.Own(rig.Source.CloseAndDrainOriginalSetupsAsync());
            var error = await Assert.ThrowsAsync<AggregateException>(() => close);
            rig.Expected.Add(close);
            Assert.Contains(Leaves(error), cause => cause.Message.Contains("DEV_SETUP_FINAL_JOURNAL_ACK_REQUIRED", StringComparison.Ordinal));
            Assert.Equal(0, completion.Reads); Assert.Equal(0, completion.Validations);
            var snapshot = await rig.Own(rig.Permissions.GetSnapshotAsync());
            Assert.DoesNotContain(snapshot.RecentAuditEvents, audit => audit.ResultCode == "DEV_SETUP_ORIGINAL_ACKNOWLEDGED");
        }, completion);
    }

    [Fact]
    public async Task Every_synthetic_physical_step_and_private_synthetic_journal_receipt_precede_Home_setup_audit()
    {
        var completion = new SyntheticCompletion();
        await Run(async rig =>
        {
            await CompleteSyntheticSteps(rig);
            var close = rig.Own(rig.Source.CloseAndDrainOriginalSetupsAsync()); await close;
            Assert.True(close.IsCompletedSuccessfully); Assert.Equal(1, completion.Reads); Assert.Equal(1, completion.Validations);
            var snapshot = await rig.Own(rig.Permissions.GetSnapshotAsync());
            Assert.Contains(snapshot.RecentAuditEvents, audit => audit.ResultCode == "DEV_SETUP_ORIGINAL_ACKNOWLEDGED"
                && audit.RequestState == HomePermissionRequestState.Succeeded);
        }, completion);
    }

    [Fact]
    public async Task Actual_faulted_journal_validation_OCE_and_sibling_keep_setup_partial_and_retain_both_causes()
    {
        var first = new OperationCanceledException("original journal validator is faulted");
        var second = new IOException("independent original journal failure");
        var completion = new SyntheticCompletion(); completion.Validation.SetException([first, second]);
        await Run(async rig =>
        {
            await CompleteSyntheticSteps(rig);
            var close = rig.Own(rig.Source.CloseAndDrainOriginalSetupsAsync());
            var error = await Assert.ThrowsAsync<AggregateException>(() => close); rig.Expected.Add(close);
            Assert.True(close.IsFaulted); Assert.True(completion.Validation.Task.IsFaulted);
            Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error));
            var snapshot = await rig.Own(rig.Permissions.GetSnapshotAsync());
            Assert.DoesNotContain(snapshot.RecentAuditEvents, audit => audit.ResultCode == "DEV_SETUP_ORIGINAL_ACKNOWLEDGED");
        }, completion);
    }

    private static async Task CompleteSyntheticSteps(Rig rig)
    {
        // Actual Home review/entry/result/cleanup flow; physical and journal sources here
        // are synthetic and provide no Files/kernel/full-import acceptance evidence.
        var permission = await rig.Accept();
        foreach (var step in rig.Issuer.Intent!.Steps)
        {
            var entry = await rig.Own(permission.EnterOriginalStepAsync(step, default));
            var result = new object(); var raw = Task.FromResult<object?>(result);
            var body = rig.Own(entry.RunOriginalStep(step, () => { rig.Issuer.Bind(step, raw, result); return raw; }, default));
            Assert.Same(raw, body); await body;
            await rig.Own(entry.DisposeAsync().AsTask());
            await rig.Own(permission.ValidateOriginalStepResultAsync(step, entry, body, result, default));
        }
    }

    private sealed class SyntheticCompletion : IDeveloperProjectOriginalSetupCompletionSource
    {
        private sealed class Receipt : IDeveloperProjectOriginalSetupCompletion { }
        private readonly Receipt _original = new();
        private DeveloperProjectSetupIntent? _intent; private IDeveloperProjectOriginalSourceCapture? _capture;
        internal int Reads, Validations;
        internal readonly TaskCompletionSource Validation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IDeveloperProjectOriginalSetupCompletion?> GetOriginalCompletionAsync(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, CancellationToken token)
        { Reads++; _intent = intent; _capture = capture; return Task.FromResult<IDeveloperProjectOriginalSetupCompletion?>(_original); }
        public bool IsIssuedOriginalCompletion(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            IDeveloperProjectOriginalSetupCompletion completion) => ReferenceEquals(intent, _intent) && ReferenceEquals(capture, _capture) && ReferenceEquals(completion, _original);
        public Task ValidateOriginalCompletionAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            IDeveloperProjectOriginalSetupCompletion completion, CancellationToken token)
        { Validations++; if (!IsIssuedOriginalCompletion(intent, capture, completion)) throw new UnauthorizedAccessException();
            Validation.TrySetResult(); return Validation.Task; }
        public void DemandExternalOriginalSetupCompletionJoin() { }
    }
}
