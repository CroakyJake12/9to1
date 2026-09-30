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
