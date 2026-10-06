using System.Collections.Immutable;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Real local Home store/profile/review/Accept/claim/held entry. Destination, capture and
// physical outcome issuers below are synthetic private sources, not Files/kernel evidence.
public sealed partial class HomeDeveloperProjectSetupPermissionTests
{
    [Fact] public Task A_value_equal_public_intent_cannot_acquire_the_private_destination_review() => Run(async rig =>
    {
        var intent = await rig.Prepare(); var actual = rig.Own(rig.Source.AcquireOriginalAsync(intent with { }, rig.Issuer.Capture, default));
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expected.Add(actual); rig.ExpectedClose = true;
        Assert.Contains(Leaves(error), cause => cause is UnauthorizedAccessException);
        Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests);
    });
    [Fact] public Task Pending_high_risk_review_has_no_physical_step_entry_or_effect() => Run(async rig =>
    {
        var intent = await rig.Prepare(); var actual = rig.Own(rig.Source.AcquireOriginalAsync(intent, rig.Issuer.Capture, default));
        try
        {
            var pending = await rig.Pending(); Assert.Equal(HomeDeveloperProjectSetupPermissionSource.SetupAction, pending.Scope.ActionName);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State); Assert.False(actual.IsCompleted); Assert.Equal(0, rig.Effects);
        }
        finally
        {
            rig.Expected.Add(actual); rig.ExpectedClose = true; rig.Source.RequestOriginalSetupRetirement();
            try { await actual; } catch { _ = actual.Exception; }
        }
    });
    [Fact] public Task Accepted_review_issues_one_exact_step_entry_and_only_private_result_validation_after_actual_close() => Run(async rig =>
    {
        var permission = await rig.Accept(); var step = rig.Issuer.Intent!.Steps[0];
        Assert.Throws<UnauthorizedAccessException>(() => { _ = permission.EnterOriginalStepAsync(step with { }, default); });
        var enter = rig.Own(permission.EnterOriginalStepAsync(step, default)); Assert.Same(enter, permission.EnterOriginalStepAsync(step, default));
        var entry = await enter; Assert.True(permission.IsIssuedOriginalStepEntry(step, entry));
        Assert.True(await rig.Own(entry.CheckOriginalStepCommitAsync(step, default).AsTask()));
        var result = new object(); var raw = Task.FromResult<object?>(result);
        var body = rig.Own(entry.RunOriginalStep(step, () => { rig.Effects++; rig.Issuer.Bind(step, raw, result); return raw; }, default));
        Assert.Same(raw, body); Assert.Same(result, await body); Assert.Equal(1, rig.Effects);
        Assert.Throws<UnauthorizedAccessException>(() => { _ = entry.RunOriginalStep(step, () => Task.CompletedTask, default); });
        Assert.Throws<UnauthorizedAccessException>(() => { _ = permission.ValidateOriginalStepResultAsync(step, entry, raw, result, default); });
        var close = rig.Own(entry.DisposeAsync().AsTask()); Assert.Same(close, entry.DisposeAsync().AsTask()); await close;
        Assert.True(close.IsCompletedSuccessfully); Assert.False(permission.IsIssuedOriginalStepEntry(step, entry));
        await rig.Own(permission.ValidateOriginalStepResultAsync(step, entry, raw, result, default)); Assert.Equal(1, rig.Issuer.Outcomes);
        Assert.Throws<UnauthorizedAccessException>(() => { _ = permission.ValidateOriginalStepResultAsync(step, entry, raw, new object(), default); });
        // One folder result is not complete setup: final real journal ACK is absent.
        var sourceClose = rig.Own(rig.Source.CloseAndDrainOriginalSetupsAsync());
        var error = await Assert.ThrowsAsync<AggregateException>(() => sourceClose); rig.Expected.Add(sourceClose);
        Assert.Contains(Leaves(error), cause => cause.Message.Contains("DEV_SETUP_FINAL_JOURNAL_ACK_REQUIRED", StringComparison.Ordinal));
        Assert.True(sourceClose.IsFaulted);
    });
    [Fact] public Task Held_faulted_original_step_OCE_and_sibling_are_joined_before_independent_entry_cleanup() => Run(async rig =>
    {
        var permission = await rig.Accept(); var step = rig.Issuer.Intent!.Steps[0]; var entry = await rig.Own(permission.EnterOriginalStepAsync(step, default));
        var raw = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new OperationCanceledException("the original step Task is faulted"); var second = new IOException("independent original physical cause");
        var body = rig.Own(entry.RunOriginalStep(step, () => raw.Task, default));
        var close = rig.Own(entry.DisposeAsync().AsTask()); Assert.False(close.IsCompleted);
        try { Assert.Same(raw.Task, body); }
        finally { raw.TrySetException([first, second]); rig.Expected.Add(body); rig.Expected.Add(close); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => close); Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error));
        Assert.True(body.IsFaulted); Assert.True(close.IsFaulted);
        Assert.Throws<UnauthorizedAccessException>(() => { _ = permission.ValidateOriginalStepResultAsync(step, entry, body, null, default); });
        Assert.Equal(0, rig.Issuer.Outcomes);
    });
    [Fact] public Task Restored_context_destination_callback_cannot_join_its_encompassing_Home_setup_owner() => Run(async rig =>
    {
        var previous = ExecutionContext.Capture(); Assert.NotNull(previous); var permission = await rig.Accept(); var calls = 0;
        rig.Issuer.OnValidate = () => ExecutionContext.Run(previous!, _ =>
        { calls++; Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalSetupsAsync(); }); }, null);
        await rig.Own(rig.Source.ValidateOriginalAsync(rig.Issuer.Intent!, permission, default)); Assert.True(calls > 0);
        rig.Issuer.OnValidate = null;
    });
    [Fact] public Task Actual_caller_revocation_after_review_prevents_a_new_held_step_entry() => Run(async rig =>
    {
        var permission = await rig.Accept(); var actor = rig.Issuer.Intent!.OriginalActor;
        Assert.True((await rig.Own(rig.Permissions.BlockCallerAsync(actor.ActorId))).Succeeded);
        var enter = rig.Own(permission.EnterOriginalStepAsync(rig.Issuer.Intent.Steps[0], default));
        var error = await Assert.ThrowsAsync<AggregateException>(() => enter); rig.Expected.Add(enter);
        Assert.Contains(Leaves(error), cause => cause is UnauthorizedAccessException); Assert.Equal(0, rig.Effects);
    });
    [Fact] public Task Held_Home_entry_releases_before_joining_an_admitted_public_validation_at_retirement() => Run(async rig =>
    {
        var permission = await rig.Accept(); var step = rig.Issuer.Intent!.Steps[0];
        var entry = await rig.Own(permission.EnterOriginalStepAsync(step, default));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Issuer.OnValidate = () => entered.TrySetResult();
        Task? validation = null;
        try
        {
            // The SAME public validation reaches source validation, then its actual
            // profile read waits for the real Home gate held by this issued entry.
            validation = rig.Own(rig.Source.ValidateOriginalAsync(rig.Issuer.Intent, permission, default));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(validation.IsCompleted); rig.Issuer.OnValidate = null;
            var close = rig.Own(rig.Source.CloseAndDrainOriginalSetupsAsync());
            rig.Expected.Add(validation); rig.Expected.Add(close);
            var error = await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(close.IsFaulted); Assert.True(validation.IsFaulted); Assert.False(validation.IsCanceled);
            Assert.Contains(Leaves(error), cause => cause is ObjectDisposedException);
            Assert.Contains(Leaves(error), cause => cause.Message.Contains("DEV_SETUP_FINAL_JOURNAL_ACK_REQUIRED", StringComparison.Ordinal));
            var entryClose = rig.Own(entry.DisposeAsync().AsTask());
            Assert.Same(entryClose, entry.DisposeAsync().AsTask()); await entryClose;
            Assert.True(entryClose.IsCompletedSuccessfully); Assert.Equal(0, rig.Effects);
        }
        finally
        {
            rig.Issuer.OnValidate = null;
            if (validation is not null) rig.Expected.Add(validation);
            // On the immutable predecessor a timed-out close is genuinely wedged;
            // release its actual entry independently so the failing control drains.
            await rig.Own(entry.DisposeAsync().AsTask());
        }
    });
    private static IEnumerable<Exception> Leaves(Exception value) => value is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [value];
    private static async Task Run(Func<Rig, Task> body, IDeveloperProjectOriginalSetupCompletionSource? completions = null)
    {
        var rig = new Rig(completions); var errors = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { errors.Add(cause); }
        try { await rig.Source.CloseAndDrainOriginalSetupsAsync(); }
        catch (Exception cause)
        {
            // These negatives establish required causes only, not absence of extra
            // unknown cleanup causes or successful whole setup/host close.
            if (!rig.ExpectedClose && !Leaves(cause).Any(item => item.Message.Contains("DEV_SETUP_FINAL_JOURNAL_ACK_REQUIRED", StringComparison.Ordinal))) errors.Add(cause);
        }
        foreach (var original in rig.Originals)
            try { await original; } catch (Exception cause) { if (!rig.Expected.Contains(original)) errors.Add(cause); _ = original.Exception; }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Home setup control or retained cleanup failed.", errors);
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-dev-setup-" + Guid.NewGuid().ToString("N"));
        internal readonly HomeLocalProfileIdentity Profiles; internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeDeveloperProjectSetupPermissionSource Source; internal readonly Issuer Issuer = new();
        internal readonly List<Task> Originals = []; internal readonly HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal bool ExpectedClose; internal int Effects;
        internal Rig(IDeveloperProjectOriginalSetupCompletionSource? completions = null)
        {
            Directory.CreateDirectory(Root); var store = new FileHomeCoreStateStore(Path.Combine(Root, "home.json")); Profiles = new(store, new Principal());
            var policy = new HomeDeveloperProjectSetupActionPolicySource(); Permissions = new(store, policy.TryGet);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Profiles, [Issuer]), Permissions);
            Source = completions is null
                ? new(store, Profiles, broker, Permissions, () => Issuer, () => Issuer, () => Issuer)
                : new(store, Profiles, broker, Permissions, () => Issuer, () => Issuer, () => Issuer, () => completions);
        }
        internal T Own<T>(T original) where T : Task { Originals.Add(original); return original; }
        internal async Task<DeveloperProjectSetupIntent> Prepare()
        {
            var actor = await Own(Profiles.GetCurrentAsync(default).AsTask()); Assert.NotNull(actor); return Issuer.Prepare(actor!);
        }
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> Pending()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var snapshot = await Own(Permissions.GetSnapshotAsync()); if (snapshot.PendingRequests.SingleOrDefault() is { } request) return request;
                await Task.Delay(10);
            }
            throw new TimeoutException("The genuine Home setup review was not published.");
        }
        internal async Task<IDeveloperProjectOriginalSetupPermission> Accept()
        {
            var intent = await Prepare(); var task = Own(Source.AcquireOriginalAsync(intent, Issuer.Capture, default)); var request = await Pending();
            Assert.True((await Own(Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept))).Succeeded);
            var result = await task; ExpectedClose = true; return result;
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => ValueTask.FromResult<string?>("synthetic-device-local-original-setup-principal"); }
    private sealed class Capture : IDeveloperProjectOriginalSourceCapture
    {
        public string OriginalCaptureReference => "synthetic-private-capture";
        public string OriginalCaptureDigest => new('a', 64);
        public ImmutableArray<string> OriginalFolderPaths => [];
        public ImmutableArray<DeveloperProjectCapturedSourceFile> OriginalFiles => [new("readme.txt", 1, new('b', 64), "synthetic-private-file")];
    }
    private sealed class Issuer : IDeveloperProjectOriginalSetupScopeSource, IDeveloperProjectOriginalCaptureAuthority,
        IDeveloperProjectOriginalSetupStepOutcomeSource, ICanonicalResourceAccessResolver
    {
        internal readonly Capture Capture = new(); internal DeveloperProjectSetupIntent? Intent; internal Action? OnValidate; internal int Outcomes;
        private DeveloperProjectSetupStep? _step; private Task? _task; private object? _result;
        private readonly ResourceScope _scope = new("dev.project.destination", "synthetic-private-destination", "synthetic-private-revision", ResourceAccess.Write);
        internal DeveloperProjectSetupIntent Prepare(AuthenticatedResourceActor actor)
        {
            var folder = Guid.NewGuid(); var file = Guid.NewGuid();
            var steps = new[] { DeveloperProjectSetupStepKind.CreateProjectFolder, DeveloperProjectSetupStepKind.CreateProjectDirectory,
                DeveloperProjectSetupStepKind.RegisterProjectFolder, DeveloperProjectSetupStepKind.PublishImmutableSource,
                DeveloperProjectSetupStepKind.PublishMaterializedFile, DeveloperProjectSetupStepKind.PublishFileRevision,
                DeveloperProjectSetupStepKind.RegisterMaterialization, DeveloperProjectSetupStepKind.SaveDevWorkspace }
                .Select(kind => new DeveloperProjectSetupStep(Guid.NewGuid(), kind,
                    kind is DeveloperProjectSetupStepKind.PublishImmutableSource or DeveloperProjectSetupStepKind.PublishMaterializedFile or DeveloperProjectSetupStepKind.PublishFileRevision or DeveloperProjectSetupStepKind.RegisterMaterialization ? file : null, folder)).ToImmutableArray();
            Intent = new(Guid.NewGuid(), actor, Guid.NewGuid(), new('c', 64), Guid.NewGuid(), Guid.NewGuid().ToString("D"),
                Capture.OriginalCaptureReference, Capture.OriginalCaptureDigest, Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), folder, "ReviewedProject", [],
                [new(file, folder, Guid.NewGuid(), "readme.txt", 1, new('b', 64), "synthetic-private-file")], steps);
            Assert.Null(Intent.Validate()); return Intent;
        }
        internal void Bind(DeveloperProjectSetupStep step, Task task, object result) { _step = step; _task = task; _result = result; }
        public string ResourceKind => _scope.Kind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
            => ValueTask.FromResult(new ResourceAccessDecision(Intent?.OriginalActor == actor && scope == _scope && action == HomeDeveloperProjectSetupPermissionSource.SetupAction,
                "synthetic-private-destination", actor.ActorId, scope.Revision, actor.OrganisationId));
        public bool IsIssuedOriginalSetupBinding(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture) => ReferenceEquals(intent, Intent) && ReferenceEquals(capture, Capture);
        public Task RevalidateOriginalSetupAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalSetupBinding(intent, capture) || actor != intent.OriginalActor) throw new UnauthorizedAccessException(); OnValidate?.Invoke(); return Task.CompletedTask; }
        public IReadOnlyList<ResourceScope> GetOriginalSetupScopes(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
            => IsIssuedOriginalSetupBinding(intent, capture) ? [_scope] : throw new UnauthorizedAccessException();
        public void DemandExternalOriginalSetupScopeJoin() { }
        public bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture original) => ReferenceEquals(original, Capture);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture original, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(original) || actor != Intent?.OriginalActor) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public void DemandExternalOriginalCaptureJoin() { }
        public bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task task, object? result)
            => IsIssuedOriginalSetupBinding(intent, capture) && ReferenceEquals(step, _step) && ReferenceEquals(task, _task) && ReferenceEquals(result, _result);
        public Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task task, object? result, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalStepOutcome(intent, capture, step, task, result)) throw new UnauthorizedAccessException(); Outcomes++; return Task.CompletedTask; }
        public DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task task, object? result)
            => IsIssuedOriginalStepOutcome(intent, capture, step, task, result) ? new("synthetic-private-result", new('d', 64)) : throw new UnauthorizedAccessException();
        public void DemandExternalOriginalSetupStepOutcomeJoin() { }
    }
}
