using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;
using Xunit;

namespace Haven.Core.Tests;

public sealed class OriginalActorDiscoveryTests
{
    private static readonly AuthenticatedResourceActor Original = new("actor-a", "profile-a", null, null, "revision-a");
    private static readonly ResourceScope Scope = new("controlled.item", "item", "1", ResourceAccess.Read);

    [Fact]
    public async Task Resource_actor_replacement_before_first_read_never_calls_owner()
    {
        var actors = new Actors { Current = Original with { AuthenticationRevision = "revision-b" } };
        var resolver = new Resolver();
        var resources = new ResourceAuthorizationService(actors, [resolver]);
        Assert.Null(await resources.AuthorizeForActorAsync(Original, "read", [Scope]));
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Resource_actor_replacement_during_owner_read_denies_returned_authority()
    {
        var actors = new Actors { Current = Original };
        var resolver = new Resolver { OnEvaluate = () => actors.Current = Original with { ProfileId = "profile-b" } };
        var resources = new ResourceAuthorizationService(actors, [resolver]);
        Assert.Null(await resources.AuthorizeForActorAsync(Original, "read", [Scope]));
        Assert.Equal(1, resolver.Calls);
        Assert.Equal(Original, resolver.ObservedActor);
    }

    [Fact]
    public async Task Original_query_missing_port_reports_unavailable_without_legacy_query()
    {
        var provider = new LegacyOwner();
        var updates = new List<GoUpdate>();
        await foreach (var update in new GoService([provider]).QueryForActorAsync(new("query"), Original)) updates.Add(update);
        var completion = Assert.Single(updates);
        Assert.True(completion.Complete);
        Assert.NotNull(completion.Failure);
        Assert.Null(completion.Result);
        Assert.Equal(0, provider.LegacyCalls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GoService([provider])
            .ResolveForActorAsync("controlled", new("owner", "kind", "id"), Original));
        Assert.Equal(0, provider.LegacyCalls);
    }

    [Fact]
    public async Task Original_query_and_resolution_forward_exact_actor_only_to_optional_owner()
    {
        var provider = new OriginalOwner();
        var service = new GoService([provider]);
        var updates = new List<GoUpdate>();
        await foreach (var update in service.QueryForActorAsync(new("query"), Original)) updates.Add(update);
        Assert.Equal(2, updates.Count);
        Assert.Null(updates[1].Failure);
        var resolved = await service.ResolveForActorAsync("controlled", new("owner", "kind", "id"), Original);
        Assert.NotNull(resolved);
        Assert.Equal(provider.Result.Reference, resolved.Reference);
        Assert.Equal(provider.Result.Label, resolved.Label);
        Assert.Equal(2, provider.OriginalCalls);
        Assert.Same(Original, provider.ObservedActor);
        Assert.Equal(0, provider.LegacyCalls);
    }

    [Fact]
    public async Task Lying_scope_count_is_bounded_before_any_original_owner_call()
    {
        var provider = new OriginalOwner();
        var values = new LyingSet();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var update in new GoService([provider]).QueryForActorAsync(
                new("query", Scope: new(ProviderIds: values)), Original)) { }
        });
        Assert.Equal(257, values.Consumed);
        Assert.Equal(0, provider.OriginalCalls);
        Assert.Equal(0, provider.LegacyCalls);
    }
    private sealed class LyingSet : IReadOnlySet<string>
    {
        public int Count => 1;
        public int Consumed;
        public IEnumerator<string> GetEnumerator()
        { for (var index = 0; index < 1_000_000; index++) { Consumed++; yield return "provider-" + index; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Contains(string value) => false;
        public bool IsProperSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsProperSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool Overlaps(IEnumerable<string> other) => throw new NotSupportedException();
        public bool SetEquals(IEnumerable<string> other) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Lying_action_count_is_bounded_before_result_publication_or_invocation(int path)
    {
        var actions = new LyingActions();
        var provider = new OriginalOwner();
        provider.Result = provider.Result with { Actions = actions };
        var service = new GoService([provider]);
        if (path == 0)
        {
            var updates = new List<GoUpdate>();
            await foreach (var update in service.QueryForActorAsync(new("query"), Original)) updates.Add(update);
            var completion = Assert.Single(updates);
            Assert.Null(completion.Result); Assert.NotNull(completion.Failure);
        }
        else if (path == 1)
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ResolveForActorAsync("controlled", new("owner", "kind", "id"), Original));
        else
            await Assert.ThrowsAsync<InvalidDataException>(async () => await service.InvokeForActorAsync(provider.Result, "action-0", Original));
        Assert.Equal(65, actions.Consumed);
        Assert.Equal(0, provider.InvokeCalls);
    }
    private sealed class LyingActions : IReadOnlyList<GoAction>
    {
        public int Count => 1;
        public int Consumed;
        public GoAction this[int index] => throw new NotSupportedException();
        public IEnumerator<GoAction> GetEnumerator()
        { for (var index = 0; index < 1_000_000; index++) { Consumed++; yield return new("action-" + index, "Action"); } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Controlled boundary owners, not installed-app authorization or a native launch proof.
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }
    private sealed class Resolver : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "controlled.item";
        public int Calls;
        public Action? OnEvaluate;
        public AuthenticatedResourceActor? ObservedActor;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
            ResourceScope scope, CancellationToken ct)
        {
            Calls++; ObservedActor = actor; OnEvaluate?.Invoke();
            return ValueTask.FromResult(new ResourceAccessDecision(true, "controlled", actor.ActorId, scope.Revision, actor.OrganisationId));
        }
    }
    private class LegacyOwner : IGoProvider, IGoCanonicalResolver
    {
        public string ProviderId => "controlled";
        public int LegacyCalls;
        public virtual IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken ct)
        { LegacyCalls++; throw new InvalidOperationException("Legacy query must not run."); }
        public Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
        { LegacyCalls++; throw new InvalidOperationException("Legacy resolution must not run."); }
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class OriginalOwner : LegacyOwner, IGoOriginalActorQuery, IGoOriginalActorCanonicalResolver, IGoOriginalActorInvocation
    {
        public int OriginalCalls;
        public int InvokeCalls;
        public AuthenticatedResourceActor? ObservedActor;
        public GoResult Result { get; set; } = new("controlled", new("owner", "kind", "id", "1"), "Item", "Items", []);
        public async IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expectedActor,
            [EnumeratorCancellation] CancellationToken ct)
        { OriginalCalls++; ObservedActor = expectedActor; await Task.CompletedTask; ct.ThrowIfCancellationRequested(); yield return Result; }
        public Task<GoResult?> ResolveForActorAsync(GoCanonicalLocator locator, AuthenticatedResourceActor expectedActor, CancellationToken ct)
        { OriginalCalls++; ObservedActor = expectedActor; return Task.FromResult<GoResult?>(Result); }
        public Task InvokeForActorAsync(GoCanonicalReference reference, string action, AuthenticatedResourceActor expectedActor, CancellationToken ct)
        { InvokeCalls++; return Task.CompletedTask; }
    }
}
