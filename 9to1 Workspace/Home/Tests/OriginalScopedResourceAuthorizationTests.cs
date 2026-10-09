using System.Runtime.ExceptionServices;
using Haven.Application;
using Xunit;
namespace HavenOS.Home.Tests;

public sealed class OriginalScopedResourceAuthorizationTests
{
    private static readonly AuthenticatedResourceActor Actor = new("controlled-resource-actor", "controlled-profile", null, null, "current-control");
    private static readonly ResourceScope Scope = new("controlled.resource", "actual-control", "r1", ResourceAccess.Read);
    [Fact]
    public async Task Raw_actor_faulted_OCE_and_sibling_keep_full_fault_status_and_skip_resolver()
    {
        var cancellation = new OperationCanceledException("raw actor fault"); var sibling = new IOException("raw actor sibling");
        var raw = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([cancellation, sibling]);
        var resolver = new Resolver(); var authority = new ResourceAuthorizationService(new ScopedActor(() => raw.Task), [resolver]); var originals = new List<Task>();
        Task<AuthenticatedResourceActor?>? actual = null; Exception? primary = null;
        try
        {
            actual = authority.AuthorizeForActorWithinOriginalSourceAsync(Actor, "control.read", [Scope], body => body(), originals.Add, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(originals, value => ReferenceEquals(value, raw.Task)); Assert.Equal(0, resolver.Reads);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, cancellation)); Assert.Contains(Leaves(error), value => ReferenceEquals(value, sibling));
        }
        catch (Exception cause) { primary = cause; }
        await Join(actual, originals, primary, [cancellation, sibling]);
    }
    [Fact]
    public async Task Post_factory_scope_fault_still_joins_the_same_held_actor_Task()
    {
        var raw = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = false; var injected = false; var cause = new IOException("exact post-acquisition scope fault");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new Resolver(); var authority = new ResourceAuthorizationService(new ScopedActor(() => { acquired = true; return raw.Task; }), [resolver]);
        var originals = new List<Task>(); Task<AuthenticatedResourceActor?>? actual = null; Exception? primary = null;
        try
        {
            actual = authority.AuthorizeForActorWithinOriginalSourceAsync(Actor, "control.read", [Scope], body =>
            {
                body(); if (acquired && !injected) { injected = true; entered.TrySetResult(); throw cause; }
            }, originals.Add, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.False(actual.IsCompleted); Assert.Contains(originals, value => ReferenceEquals(value, raw.Task));
            raw.TrySetResult(Actor);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.Equal(0, resolver.Reads);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, cause));
        }
        catch (Exception error) { primary = error; }
        finally { raw.TrySetResult(Actor); }
        await Join(actual, originals, primary, [cause]);
    }
    [Fact]
    public async Task Unknown_deep_actor_refuses_before_any_unscoped_actor_factory()
    {
        var actor = new OrdinaryActor(); var authority = new ResourceAuthorizationService(actor, [new Resolver()]); var originals = new List<Task>();
        Task<AuthenticatedResourceActor?>? actual = null; Exception? primary = null; var known = new List<Exception>();
        try
        {
            actual = authority.AuthorizeForActorWithinOriginalSourceAsync(Actor, "control.read", [Scope], body => body(), originals.Add, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            var refusal = Assert.Single(Leaves(error).Distinct<Exception>(ReferenceEqualityComparer.Instance)); known.Add(refusal);
            Assert.IsType<InvalidOperationException>(refusal); Assert.Equal(0, actor.Reads); Assert.True(actual.IsFaulted);
        }
        catch (Exception error) { primary = error; }
        await Join(actual, originals, primary, known);
    }
    private static async Task Join(Task? actual, List<Task> originals, Exception? primary, IReadOnlyCollection<Exception> known)
    {
        var errors = new List<Exception>();
        foreach (var raw in (actual is null ? originals : originals.Append(actual)).Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await raw; } catch (Exception cause) { errors.Add(raw.Exception ?? cause); }
        var unexpected = errors.SelectMany(Leaves).Distinct<Exception>(ReferenceEqualityComparer.Instance)
            .Where(cause => !known.Any(value => ReferenceEquals(value, cause))).ToArray();
        if (unexpected.Length != 0) throw new AggregateException("Actual scoped resource originals failed.", primary is null ? unexpected : new[] { primary }.Concat(unexpected));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException { InnerExceptions.Count: > 0 } compound ? compound.InnerExceptions.SelectMany(Leaves) : [cause];
    private sealed class ScopedActor(Func<Task<AuthenticatedResourceActor?>> original) : IOriginalScopedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => throw new InvalidOperationException("Unscoped factory forbidden");
        public Task<AuthenticatedResourceActor?> GetCurrentWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => original();
    }
    private sealed class OrdinaryActor : IAuthenticatedResourceActorSource
    { public int Reads; public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { Reads++; return ValueTask.FromResult<AuthenticatedResourceActor?>(Actor); } }
    private sealed class Resolver : IOriginalScopedCanonicalResourceAccessResolver
    {
        public int Reads; public string ResourceKind => Scope.Kind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) => throw new InvalidOperationException("Unscoped resolver forbidden");
        public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, Action<Action> caller, Action<Task> retain, CancellationToken token)
        { Reads++; return Task.FromResult(new ResourceAccessDecision(true, "control", actor.ActorId, scope.Revision, actor.OrganisationId)); }
    }
}
