using Haven.Application;
using Haven.Application.Go;
using Xunit;

namespace Haven.Core.Tests;

// Controlled dispatch boundary only: these providers do not represent an installed owner or a resource grant.
public sealed class GoOriginalActorInvocationTests
{
    private static readonly AuthenticatedResourceActor Original = new("actor", "profile", null, null, "revision");
    private static readonly GoResult Result = new("owner", new("app", "item", "id", "1"), "Item", "Items", [new("open", "Open")]);

    [Fact]
    public async Task CurrentOnlyProviderCannotReceiveOriginalSessionInvocation()
    {
        var provider = new CurrentOnly();
        var service = new GoService([provider]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.InvokeForActorAsync(Result, "open", Original));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ExplicitOwnerReceivesExactOriginalActorAndReference()
    {
        var provider = new OriginalOwner();
        await new GoService([provider]).InvokeForActorAsync(Result, "open", Original);
        Assert.Same(Original, provider.Actor);
        Assert.Same(Result.Reference, provider.Reference);
        Assert.Equal("open", provider.Action);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ScopeAndDisplayedActionDenyBeforeOwnerPort()
    {
        var provider = new OriginalOwner();
        var service = new GoService([provider]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.InvokeForActorAsync(Result, "remove", Original));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.InvokeForActorAsync(Result, "open", Original,
            new GoScope(ActionIds: new HashSet<string> { "other" })));
        Assert.Null(provider.Actor);
    }

    [Fact]
    public async Task LegacyInvocationRemainsExplicitlyCurrentOnly()
    {
        var provider = new CurrentOnly();
        await new GoService([provider]).InvokeAsync(Result, "open");
        Assert.Equal(1, provider.Calls);
    }

    private class CurrentOnly : IGoProvider
    {
        public string ProviderId => "owner";
        public int Calls { get; private set; }
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        { await Task.CompletedTask; yield break; }
        public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken cancellationToken)
        { Calls++; return Task.CompletedTask; }
    }
    private sealed class OriginalOwner : CurrentOnly, IGoOriginalActorInvocation
    {
        public AuthenticatedResourceActor? Actor { get; private set; }
        public GoCanonicalReference? Reference { get; private set; }
        public string? Action { get; private set; }
        public Task InvokeForActorAsync(GoCanonicalReference reference, string actionId,
            AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
        { Actor = expectedActor; Reference = reference; Action = actionId; return Task.CompletedTask; }
    }
}
