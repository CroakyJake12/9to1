using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeProductivityInsertionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Insertion_passes_exact_captured_new_IDs_and_preserves_uncertain_commit(bool incomplete)
    {
        var owner = new Owner { Incomplete = incomplete };
        var engine = new HomeProductivityEngine(artifactActions: [owner]);
        var item = engine.CreateObject("text.paragraph", Guid.NewGuid(), JsonSerializer.SerializeToElement(new { text = "Insert" }));
        var result = await engine.InsertObjectsAsync(new("write", "doc", 3, [], new HashSet<string> { "text.paragraph" }), [item], "insert-op");
        Assert.Equal(1, owner.Calls);
        Assert.Equal(item.ObjectId, Assert.Single(owner.Objects!).ObjectId);
        Assert.Equal("insert-op", owner.OperationId);
        Assert.Equal(!incomplete, result.Succeeded);
        Assert.Equal(new HomeProductivityArtifactRevision(4), result.ArtifactRevision);
        Assert.Equal(incomplete ? HomeProductivityArtifactOutcome.NeedsRecovery : HomeProductivityArtifactOutcome.Committed, result.Outcome);
    }
    [Fact]
    public async Task Invalid_or_unsupported_insertion_never_calls_owner()
    {
        var owner = new Owner(); var engine = new HomeProductivityEngine(artifactActions: [owner]);
        var item = engine.CreateObject("text.paragraph", Guid.NewGuid(), JsonSerializer.SerializeToElement(new { text = "Insert" }));
        var context = new HomeProductivityContext("write", "doc", 3, [], new HashSet<string>());
        Assert.False((await engine.InsertObjectsAsync(context, [item], "insert")).Succeeded);
        Assert.False((await engine.InsertObjectsAsync(context, [item, item], "insert")).Succeeded);
        Assert.Equal(0, owner.Calls);
    }
    private sealed class Owner : IHomeProductivityArtifactInsertionProvider
    {
        public string AppId => "write";
        public bool Incomplete;
        public int Calls;
        public IReadOnlyList<HomeProductivityObject>? Objects;
        public string? OperationId;
        public ValueTask<HomeProductivityActionResult> InsertAsync(HomeProductivityContext context, IReadOnlyList<HomeProductivityObject> objects, string operationId, CancellationToken cancellationToken)
        {
            Calls++; Objects = objects; OperationId = operationId;
            return ValueTask.FromResult(new HomeProductivityActionResult(true, "Inserted", "Inserted", 4,
                Incomplete ? [] : objects.Select(item => item.ObjectId).ToArray())
                { ArtifactRevision = new(4), Outcome = HomeProductivityArtifactOutcome.Committed });
        }
        public ValueTask<HomeProductivityActionResult> ApplyAsync(HomeProductivityContext context, HomeProductivityAction action,
            Func<HomeProductivityObject, HomeProductivityObject> sharedTransformation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
