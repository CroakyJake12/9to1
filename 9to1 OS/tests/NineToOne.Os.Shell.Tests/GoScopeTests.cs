using System.Runtime.CompilerServices;
using Haven.Application.Go;

namespace NineToOne.Os.Shell.Tests;

public sealed class GoScopeTests
{
    [Fact]
    public async Task DisabledProviderIsNeverQueriedAndEngineFiltersCategoriesOwnersKindsAndActions()
    {
        var local = new Provider("local", [Result("local", "Files", "file", "Files"), Result("local", "Home", "app", "Apps"), Result("local", "Files", "folder", "Files")]);
        var privateProvider = new Provider("private", [Result("private", "Private", "message", "Messages")]);
        var engine = new GoService([local, privateProvider]);
        var scope = new GoScope(new HashSet<string> { "local" }, new HashSet<string> { "Files" }, new HashSet<string> { "file" }, new HashSet<string> { "Open" });
        var results = new List<GoResult>();
        await foreach (var update in engine.QueryAsync(new("", "Files", Scope: scope))) if (update.Result is { } result) results.Add(result);
        var only = Assert.Single(results);
        Assert.Equal("file", only.Reference.Kind); Assert.Equal("Open", Assert.Single(only.Actions).Id);
        Assert.Equal(0, privateProvider.Queries); Assert.Equal(1, local.Queries);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.InvokeAsync(only, "Share", scope));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.InvokeAsync(only, "Open", scope with { ProviderIds = new HashSet<string>() }));
        Assert.Equal(0, local.Invocations);
        await engine.InvokeAsync(only, "Open", scope);
        Assert.Equal(1, local.Invocations); Assert.Same(only.Reference, local.LastReference);
    }
    [Fact]
    public async Task ScopeSnapshotCannotBeExpandedByMutationWhileProviderRuns()
    {
        var owners = new HashSet<string> { "Home" }; var actions = new HashSet<string> { "Open" };
        var provider = new Provider("local", [Result("local", "Files", "file", "Files"), Result("local", "Home", "app", "Apps")])
        { OnQuery = () => { owners.Add("Files"); actions.Add("Share"); } };
        var results = new List<GoResult>();
        await foreach (var update in new GoService([provider]).QueryAsync(new("", Scope: new(Owners: owners, ActionIds: actions))))
            if (update.Result is { } result) results.Add(result);
        var only = Assert.Single(results); Assert.Equal("Home", only.Reference.Owner); Assert.Equal("Open", Assert.Single(only.Actions).Id);
    }
    [Fact]
    public async Task EmptyProviderScopeDoesNotDiscoverAnyProviderMetadata()
    {
        var provider = new Provider("private", []); var updates = new List<GoUpdate>();
        await foreach (var update in new GoService([provider]).QueryAsync(new("", Scope: new(ProviderIds: new HashSet<string>())))) updates.Add(update);
        Assert.Empty(updates); Assert.Equal(0, provider.Queries);
    }
    [Fact]
    public async Task StableResolutionUsesExactOwnerAndSnapshotsScopeBeforeMetadataRead()
    {
        var actions = new HashSet<string> { "Open" };
        var expected = Result("installed", "Home", "app", "Apps");
        var owner = new Resolver(expected) { OnRead = () => actions.Add("Share") };
        var engine = new GoService([owner]); var locator = new GoCanonicalLocator("Home", "app", expected.Reference.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.ResolveAsync("installed", locator, new(ProviderIds: new HashSet<string>())));
        Assert.Equal(0, owner.Reads);
        var actual = await engine.ResolveAsync("installed", locator, new(ActionIds: actions));
        Assert.NotNull(actual); Assert.Same(expected.Reference, actual.Reference); Assert.Equal("Open", Assert.Single(actual.Actions).Id);
        Assert.Equal(1, owner.Reads); Assert.Equal(0, owner.Queries);
        owner.Result = expected with { Reference = expected.Reference with { Id = Guid.NewGuid().ToString("D") } };
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.ResolveAsync("installed", locator));
        Assert.Null(await engine.ResolveAsync("missing", locator));
    }
    [Fact]
    public async Task StableResolutionDeadlineDoesNotWaitForNoncooperativeOwner()
    {
        var expected = Result("installed", "Home", "app", "Apps");
        var hold = new TaskCompletionSource<GoResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Resolver(expected) { Pending = hold.Task };
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GoService([owner], TimeSpan.FromMilliseconds(50)).ResolveAsync("installed",
                new("Home", "app", expected.Reference.Id)));
        }
        finally { hold.TrySetResult(expected); }
    }
    private sealed class Resolver(GoResult result) : IGoCanonicalResolver
    {
        public string ProviderId => "installed"; public int Reads; public int Queries;
        public GoResult Result = result; public Action? OnRead; public Task<GoResult?>? Pending;
        public Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
        { Reads++; OnRead?.Invoke(); return Pending ?? Task.FromResult<GoResult?>(Result); }
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        { Queries++; await Task.Yield(); yield return Result; }
        public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct) => throw new InvalidOperationException();
    }
    private static GoResult Result(string provider, string owner, string kind, string category) => new(provider,
        new(owner, kind, Guid.NewGuid().ToString("D"), "current-revision"), "Canonical entity", category, [new("Open", "Open"), new("Share", "Share")]);
    private sealed class Provider(string id, IReadOnlyList<GoResult> results) : IGoProvider
    {
        public string ProviderId => id; public int Queries; public int Invocations;
        public Action? OnQuery; public GoCanonicalReference? LastReference;
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            Queries++; OnQuery?.Invoke(); await Task.Yield();
            foreach (var result in results) { ct.ThrowIfCancellationRequested(); yield return result; }
        }
        public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
        { Invocations++; LastReference = reference; return Task.CompletedTask; }
    }
}
