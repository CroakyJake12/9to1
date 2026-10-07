using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class HomeDeveloperWorkspaceExecutionConsentTests
{
    [Fact]
    public Task Fresh_scoped_pending_review_observes_permission_without_repeating_native_binding_reads_each_second()
        => RunScopedReviewControl(async rig =>
        {
            rig.Bindings.OriginalRead = Task.FromResult(rig.Bindings.Decision(true));
            var actual = rig.Acquire();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest? request = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var snapshot = await rig.Own(rig.Permissions.GetSnapshotAsync());
                request = snapshot.PendingRequests.SingleOrDefault();
                if (request is not null) break;
                await rig.Own(Task.Delay(10, CancellationToken.None));
            }
            Assert.NotNull(request);
            Assert.Equal(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.PendingApproval, request!.State);
            Assert.Equal(1, rig.Bindings.Revalidations);
            await rig.Own(Task.Delay(TimeSpan.FromMilliseconds(2200), CancellationToken.None));
            Assert.False(actual.IsCompleted); Assert.Equal(1, rig.Bindings.Revalidations);
            Assert.True((await rig.Own(rig.Permissions.DecideAsync(request.RequestId,
                HavenOS.Home.PermissionsTrustNotifications.HomeApprovalChoice.Accept))).Succeeded);
            var consent = await actual.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(rig.Source.IsIssuedOriginalConsent(consent, rig.Tools.Preparation!));
            Assert.Equal(2, rig.Bindings.Revalidations);
            Assert.Equal(0, rig.Bindings.OrdinaryCalls);
            // This is an actual Home component acceptance over synthetic READ/binding/
            // preparation; no Entry, Files/native process or user trust is claimed.
        });

    [Fact]
    public Task Retired_consent_joins_acquired_allowed_resolver_before_refusing_new_Home_factories()
        => RunScopedReviewControl(async rig =>
        {
            var held = new TaskCompletionSource<ResourceAccessDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Bindings.OriginalRead = held.Task; rig.Bindings.AfterRetain = () => retained.TrySetResult();
            var actual = rig.Acquire(); Task? close = null;
            try
            {
                await retained.Task.WaitAsync(TimeSpan.FromSeconds(10));
                rig.Source.RequestOriginalExecutionRetirement();
                close = rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync());
                Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted);
            }
            finally { held.TrySetResult(rig.Bindings.Decision(true)); }
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual.WaitAsync(TimeSpan.FromSeconds(10)));
            var expected = Assert.Single(ScopedReviewLeaves(error).Distinct<Exception>(ReferenceEqualityComparer.Instance));
            Assert.IsType<ObjectDisposedException>(expected); rig.Expected.Add(expected);
            Assert.True(held.Task.IsCompletedSuccessfully); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Equal(0, rig.Bindings.OrdinaryCalls);
            Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests);
            close ??= rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync());
            var cleanup = await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.All(ScopedReviewLeaves(cleanup), cause => Assert.Same(expected, cause));
        });

    // Actual Home actor/store/resource/Broker/consent drivers; the private binding,
    // preparation and resolver outcome are controlled and issue no Files/native grant.
    [Fact]
    public Task Scoped_execution_review_retains_held_resolver_and_guards_restored_callback_before_denial()
        => RunScopedReviewControl(async rig =>
        {
            var prior = ExecutionContext.Capture(); Assert.NotNull(prior);
            var held = new TaskCompletionSource<ResourceAccessDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var guarded = 0;
            rig.Bindings.OriginalRead = held.Task;
            rig.Bindings.AfterRead = () => ExecutionContext.Run(prior!, _ =>
            {
                guarded++;
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalExecutionsAsync(); });
            }, null);
            rig.Bindings.AfterRetain = () => retained.TrySetResult();
            var actual = rig.Acquire();
            try
            {
                await retained.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(actual.IsCompleted); Assert.False(held.Task.IsCompleted);
            }
            finally { held.TrySetResult(rig.Bindings.Decision(false)); }
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual.WaitAsync(TimeSpan.FromSeconds(10)));
            var expected = Assert.Single(ScopedReviewLeaves(error).Distinct<Exception>(ReferenceEqualityComparer.Instance));
            Assert.IsType<UnauthorizedAccessException>(expected); rig.Expected.Add(expected);
            Assert.Equal(1, guarded); Assert.True(held.Task.IsCompletedSuccessfully);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Equal(0, rig.Bindings.OrdinaryCalls);
            Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests);
        });

    [Fact]
    public Task Scoped_execution_review_preserves_faulted_raw_OCE_and_sibling_through_independent_owner_close()
        => RunScopedReviewControl(async rig =>
        {
            var first = new OperationCanceledException("actual faulted controlled resolver OCE");
            var second = new IOException("actual independent controlled resolver sibling");
            var held = new TaskCompletionSource<ResourceAccessDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Bindings.OriginalRead = held.Task; rig.Bindings.AfterRetain = () => retained.TrySetResult();
            var actual = rig.Acquire(); Task? close = null;
            try
            {
                await retained.Task.WaitAsync(TimeSpan.FromSeconds(10));
                close = rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync());
                Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted);
            }
            finally { held.TrySetException([first, second]); rig.Expected.Add(first); rig.Expected.Add(second); }
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains(first, ScopedReviewLeaves(error)); Assert.Contains(second, ScopedReviewLeaves(error));
            Assert.All(ScopedReviewLeaves(error), cause => Assert.True(ReferenceEquals(cause, first) || ReferenceEquals(cause, second)));
            Assert.True(held.Task.IsFaulted); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            close ??= rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync());
            var cleanup = await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains(first, ScopedReviewLeaves(cleanup)); Assert.Contains(second, ScopedReviewLeaves(cleanup));
            Assert.All(ScopedReviewLeaves(cleanup), cause => Assert.True(ReferenceEquals(cause, first) || ReferenceEquals(cause, second)));
            Assert.Same(close, rig.Source.CloseAndDrainOriginalExecutionsAsync());
            Assert.Equal(0, rig.Bindings.OrdinaryCalls);
        });

    [Fact]
    public Task Scoped_binding_cannot_fall_back_to_registered_ordinary_resource_owner()
        => RunScopedReviewControl(async rig =>
        {
            var actual = rig.Acquire();
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            var expected = Assert.Single(ScopedReviewLeaves(error).Distinct<Exception>(ReferenceEqualityComparer.Instance));
            Assert.IsType<InvalidOperationException>(expected);
            Assert.Contains("canonical resource owner lacks its scoped original producer", expected.Message, StringComparison.Ordinal);
            rig.Expected.Add(expected); Assert.Equal(0, rig.Bindings.OrdinaryCalls);
            Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests);
        }, registerScopedResolver: false);

    private static IEnumerable<Exception> ScopedReviewLeaves(Exception cause)
        => cause is AggregateException { InnerExceptions.Count: > 0 } group
            ? group.InnerExceptions.SelectMany(ScopedReviewLeaves) : [cause];

    private static async Task RunScopedReviewControl(Func<ScopedReviewRig, Task> body, bool registerScopedResolver = true)
    {
        ScopedReviewRig? rig = null; Exception? primary = null; var errors = new List<Exception>(); Task? close = null;
        try { rig = new(registerScopedResolver); await rig.Prepare(); await body(rig); }
        catch (Exception cause) { primary = cause; }
        if (rig is not null)
        {
            try { close = rig.Source.CloseAndDrainOriginalExecutionsAsync(); } catch (Exception cause) { Add(cause); }
            foreach (var actual in rig.Originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await actual; } catch (Exception cause) { Add((Exception?)actual.Exception ?? cause); }
            if (close is not null) try { await close; } catch (Exception cause) { Add((Exception?)close.Exception ?? cause); }
            try { Directory.Delete(rig.Root, true); } catch (Exception cause) { Add(cause); }
        }
        if (primary is not null) errors.Insert(0, primary);
        if (errors.Count != 0) throw new AggregateException("Actual scoped execution review body/independent source drain failed.", errors);
        void Add(Exception cause)
        {
            foreach (var leaf in ScopedReviewLeaves(cause))
                if (rig?.Expected.Contains(leaf) != true && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }

    private sealed class ScopedReviewRig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-execution-scoped-review-" + Guid.NewGuid().ToString("N"));
        internal readonly HomeLocalProfileIdentity Profiles;
        internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeDeveloperWorkspaceExecutionConsentSource Source;
        internal readonly BindingSource Inner = new(); internal readonly ToolSource Tools = new();
        internal readonly ScopedReviewBindings Bindings;
        internal readonly List<Task> Originals = [];
        internal readonly HashSet<Exception> Expected = new(ReferenceEqualityComparer.Instance);
        internal ScopedReviewRig(bool registeredScopedResolver)
        {
            try
            {
                Directory.CreateDirectory(Root); var store = new FileHomeCoreStateStore(Path.Combine(Root, "home.json"));
                Profiles = new(store, new Principal()); var policy = new HomeDeveloperWorkspaceExecutionActionPolicySource();
                Permissions = new(store, policy.TryGet); Bindings = new(Inner);
                var resolver = registeredScopedResolver ? (ICanonicalResourceAccessResolver)Bindings : Inner;
                var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Profiles, [resolver]), Permissions);
                Source = new(store, Profiles, broker, Permissions, () => Bindings, () => Tools);
            }
            catch (Exception primary)
            {
                try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
                catch (Exception cleanup) { throw new AggregateException("Actual scoped-review fixture acquisition/cleanup failed.", primary, cleanup); }
                throw;
            }
        }
        internal T Own<T>(T raw) where T : Task { Originals.Add(raw); return raw; }
        internal async Task Prepare()
        {
            var actor = await Own(Profiles.GetCurrentAsync(CancellationToken.None).AsTask()); Assert.NotNull(actor);
            Inner.Binding = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Path.GetFullPath(Root), actor!);
            Tools.Prepare(Inner.Binding.CanonicalRoot);
        }
        internal Task<IWorkspaceOriginalProcessStartConsent> Acquire() => Own(Source.AcquireOriginalAsync(
            Inner.Binding!, Tools.Preparation!, Tools.Current!, body => body(), CancellationToken.None));
    }

    private sealed class ScopedReviewBindings(BindingSource inner) : IDeveloperWorkspaceOriginalExecutionBindingSource,
        IDeveloperWorkspaceOriginalExecutionScopedBindingSource, IDeveloperWorkspaceOriginalExecutionCommitBindingSource,
        IDeveloperWorkspaceOriginalExecutionPinCustodySource, IOriginalScopedCanonicalResourceAccessResolver
    {
        internal Task<ResourceAccessDecision>? OriginalRead; internal Action? AfterRead, AfterRetain; internal int OrdinaryCalls, Revalidations;
        public string ResourceKind => HomeDeveloperWorkspaceExecutionConsentSource.ExecuteAction;
        internal ResourceAccessDecision Decision(bool allowed) => new(allowed, "controlled scoped original execution owner",
            inner.Binding!.OriginalActor.ActorId, inner.GetOriginalExecutionScopes(inner.Binding)[0].Revision, inner.Binding.OriginalActor.OrganisationId);
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken token)
        { OrdinaryCalls++; throw new InvalidOperationException("The actual scoped binding cannot use its ordinary resolver fallback."); }
        public async Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string actionId,
            ResourceScope scope, Action<Action> caller, Action<Task> retain, CancellationToken token)
        {
            Task<ResourceAccessDecision>? raw = null; Exception? invocation = null; ResourceAccessDecision result = null!;
            try { caller(() => { raw = OriginalRead ?? Task.FromResult(Decision(false)); retain(raw); AfterRetain?.Invoke(); }); }
            catch (Exception cause) { invocation = cause; }
            Exception? rawFailure = null;
            if (raw is not null) try { result = await raw; } catch (Exception cause) { rawFailure = (Exception?)raw.Exception ?? cause; }
            if (invocation is not null || rawFailure is not null)
                throw new AggregateException("Controlled scoped resolver callback/raw source failed.",
                    new[] { invocation, rawFailure }.Where(cause => cause is not null).Cast<Exception>());
            if (raw is null) throw new InvalidOperationException("No controlled original resolver task was acquired.");
            caller(() => AfterRead?.Invoke()); return result;
        }
        public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding binding) => inner.IsIssuedOriginalBinding(binding);
        public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, AuthenticatedResourceActor actor, CancellationToken token) => inner.RevalidateOriginalAsync(binding, actor, token);
        public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding binding) => inner.GetOriginalExecutionScopes(binding);
        public void DemandExternalOriginalExecutionBindingJoin() => inner.DemandExternalOriginalExecutionBindingJoin();
        public Task RevalidateOriginalWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, CancellationToken token)
        { Revalidations++; return inner.RevalidateOriginalWithinSourceAsync(binding, actor, scope, retain, token); }
        public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, Action<Action> scope, Action<Task> retain, CancellationToken token)
            => inner.AcquireOriginalExecutionPinWithinSourceAsync(binding, scope, retain, token);
        public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, CancellationToken token) => inner.AcquireOriginalExecutionPinAsync(binding, token);
        public bool IsIssuedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => inner.IsIssuedOriginalExecutionPin(binding, pin);
        public bool IsOwnedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => inner.IsOwnedOriginalExecutionPin(binding, pin);
    }
}
