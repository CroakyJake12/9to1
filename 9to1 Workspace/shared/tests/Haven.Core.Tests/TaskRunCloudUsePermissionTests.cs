using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Synthetic actual central engine/actor/raw Task controls. No UI approval is fabricated
/// in production, and no provider, secret, network, Home authority or monetary balance is used.</summary>
public sealed class TaskRunCloudUsePermissionTests
{
    [Theory]
    [InlineData(HavenPermissionPolicy.AlwaysAsk)]
    [InlineData(HavenPermissionPolicy.AskWithHighRisk)]
    [InlineData(HavenPermissionPolicy.AlwaysAllow)]
    public async Task Remote_consequential_use_preserves_actual_Ask_even_under_AlwaysAllow(HavenPermissionPolicy policy)
    {
        var rig = new Rig(); rig.Policy.SetPolicy(policy);
        var error = await Assert.ThrowsAsync<TaskRunCloudPermissionRequiredException>(() => rig.Acquire());
        Assert.Equal(PermissionDecisionKind.Ask, error.OriginalDecision.Kind);
        Assert.Equal(TaskRunCentralCloudUsePermissionSource.ScopeFor(rig.Owner, rig.Candidate), error.OriginalDecision.Scope);
        Assert.Contains("unknown", error.OriginalDecision.Reason);
        Assert.Empty(rig.Policy.Grants);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("run")]
    [InlineData("provider")]
    [InlineData("model-case")]
    [InlineData("actor")]
    [InlineData("authentication-revision")]
    public async Task One_actual_scope_grant_does_not_authorize_another_original_selection_or_actor(string change)
    {
        var rig = new Rig(); var originalScope = rig.Grant();
        var owner = change switch
        {
            "task" => rig.Owner with { TaskId = Guid.NewGuid() },
            "run" => rig.Owner with { ExecutionId = Guid.NewGuid() },
            "actor" => rig.Owner with { ActorId = "another-synthetic-task-actor" },
            "authentication-revision" => rig.Owner with { AuthenticationRevision = "another-real-observation" },
            _ => rig.Owner
        };
        var candidate = change switch
        {
            "provider" => rig.Candidate with { ProviderId = "another-provider" },
            "model-case" => rig.Candidate with { ModelId = "MODEL" },
            _ => rig.Candidate
        };
        rig.Actors.Actor = new(owner.ActorId, owner.ProfileId, owner.AccountId, owner.OrganisationId, owner.AuthenticationRevision);
        var error = await Assert.ThrowsAsync<TaskRunCloudPermissionRequiredException>(() =>
            rig.Source.AcquireOriginalAsync(owner, candidate, default).AsTask());
        Assert.NotEqual(originalScope, error.OriginalDecision.Scope);
        Assert.Equal(PermissionDecisionKind.Ask, error.OriginalDecision.Kind);
        Assert.Single(rig.Policy.Grants);
    }

    [Fact]
    public async Task Revocation_after_async_revalidation_refuses_the_actual_raw_start_at_the_same_writer_gate()
    {
        var rig = new Rig(); var scope = rig.Grant();
        await using var lease = await rig.Acquire();
        await lease.RevalidateAsync(default);
        rig.Policy.Revoke(scope); // Genuine central writer in the admission/actual-start interval.
        var calls = 0;
        Assert.Throws<UnauthorizedAccessException>(() => { _ = lease.RunOriginalInvocation(() => { calls++; return Task.CompletedTask; }); });
        Assert.Equal(0, calls);
        var error = await Assert.ThrowsAsync<TaskRunCloudPermissionRequiredException>(() => lease.RevalidateAsync(default).AsTask());
        Assert.Equal(scope, error.OriginalDecision.Scope);
    }

    [Fact]
    public async Task Actual_raw_task_identity_and_all_direct_original_causes_survive_finite_admission()
    {
        var rig = new Rig(); rig.Grant();
        await using var lease = await rig.Acquire();
        var one = new IOException("Actual raw original one");
        var two = new InvalidOperationException("Actual raw original two");
        var original = Task.WhenAll(Task.FromException(one), Task.FromException(two));
        await lease.RevalidateAsync(default);
        var returned = lease.RunOriginalInvocation(() => original);
        Assert.Same(original, returned);
        try { await returned; } catch (Exception error) { Assert.Same(one, error); }
        Assert.True(returned.IsFaulted);
        var payload = returned.Exception!;
        Assert.Equal(2, payload.InnerExceptions.Count);
        Assert.Same(one, payload.InnerExceptions[0]);
        Assert.Same(two, payload.InnerExceptions[1]);
    }

    [Fact]
    public async Task Actor_retirement_rejects_the_original_lease_despite_its_existing_exact_grant()
    {
        var rig = new Rig(); var scope = rig.Grant();
        await using var lease = await rig.Acquire();
        rig.Actors.Actor = rig.Actors.Actor! with { AuthenticationRevision = "retired" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(default).AsTask());
        Assert.Contains(scope, rig.Policy.Grants);
    }

    [Fact]
    public async Task Later_revocation_cannot_undo_an_already_admitted_actual_task_and_its_owner_still_joins_it()
    {
        var rig = new Rig(); var scope = rig.Grant();
        await using var lease = await rig.Acquire();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? returned = null;
        try
        {
            await lease.RevalidateAsync(default);
            returned = lease.RunOriginalInvocation(() => release.Task);
            Assert.Same(release.Task, returned);
            rig.Policy.Revoke(scope);
            Assert.False(returned.IsCompleted);
            await Assert.ThrowsAsync<TaskRunCloudPermissionRequiredException>(() => lease.RevalidateAsync(default).AsTask());
        }
        finally { release.TrySetResult(); if (returned is not null) await returned; }
        Assert.True(release.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Closed_metadata_lease_cannot_admit_a_new_raw_task()
    {
        var rig = new Rig(); rig.Grant();
        var lease = await rig.Acquire();
        await lease.DisposeAsync();
        var calls = 0;
        Assert.Throws<ObjectDisposedException>(() => { _ = lease.RunOriginalInvocation(() => { calls++; return Task.CompletedTask; }); });
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lease.RevalidateAsync(default).AsTask());
    }

    private sealed class Rig
    {
        public readonly Actors Actors = new();
        public readonly PermissionDecisionEngine Policy = new();
        public readonly TaskRunCentralCloudUsePermissionSource Source;
        public readonly TaskExecutionOwnerBinding Owner;
        public readonly TaskRunRouteCandidate Candidate = new("synthetic-selected-route", 1, "synthetic-provider", "model", null, true, ["Text"]);
        public Rig()
        {
            var actor = Actors.Actor!;
            Owner = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), actor.ActorId, actor.ProfileId, null, null,
                actor.AuthenticationRevision, "synthetic-original-owner");
            Source = new(Actors, Policy);
        }
        public string Grant()
        {
            var scope = TaskRunCentralCloudUsePermissionSource.ScopeFor(Owner, Candidate);
            Policy.Grant(scope); // Only this synthetic owning approval fixture grants.
            return scope;
        }
        public Task<ITaskRunCloudUsePermissionLease> Acquire() => Source.AcquireOriginalAsync(Owner, Candidate, default).AsTask();
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Actor = new("synthetic-task-actor", "synthetic-task-profile", null, null, "one");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Actor); }
    }
}
