using System.Text.Json;
using Haven.Application.NodeGraph;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeVersionedNodeGraphRepositoryTests
{
    [Fact]
    public async Task Shared_graph_CAS_preserves_active_revision_through_draft_edits_reopen_and_owner_conflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-graph-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var schema = new NodeGraphSchemaRegistry([new("start", 1, "forms", "forms.read", JsonSerializer.SerializeToElement(new { type = "object" }), [])],
                [new("forms", new HashSet<string> { "start" }, new HashSet<string> { "forms.read" })]);
            var path = Path.Combine(root, "home.json");
            var first = new HomeVersionedNodeGraphRepository(new FileHomeCoreStateStore(path), schema);
            var second = new HomeVersionedNodeGraphRepository(new FileHomeCoreStateStore(path), schema);
            var draft = new GraphDocument(1, Guid.NewGuid(), 1, "forms", GraphRevisionState.Draft,
                [new(Guid.NewGuid(), "start", 1, "forms", "forms.read", null, JsonSerializer.SerializeToElement(new { }), [])], []);
            Assert.True(await first.TrySaveDraftAsync("forms", "canonical-form", draft, 0));
            Assert.True(await first.TryActivateAsync(draft.GraphId, 1));
            Assert.True(await second.TrySaveDraftAsync("forms", "canonical-form", draft with { Revision = 2 }, 1));
            var record = (await first.GetAsync(draft.GraphId))!;
            Assert.Equal(2, record.Draft.Revision); Assert.Equal(1, record.Active!.Revision);
            Assert.Equal(GraphRevisionState.Active, record.Active.State);
            Assert.False(await first.TryActivateAsync(draft.GraphId, 1));
            Assert.False(await first.TrySaveDraftAsync("forms", "foreign-form", draft with { Revision = 3 }, 2));
            Assert.False(await first.TrySaveDraftAsync("forms", "canonical-form", draft with { Revision = 2 }, 1));
            Assert.True(await second.TryActivateAsync(draft.GraphId, 2));
            var activated = (await first.GetAsync(draft.GraphId))!;
            Assert.Equal(2, activated.Active!.Revision);
            Assert.Equal(new long[] { 1, 2 }, activated.ActivatedRevisions.Select(item => item.Revision));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
