using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;
using Haven.Core.Forms;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Tests;

public sealed class FormGraphReferenceTests
{
    [Fact]
    public async Task Published_graph_pin_survives_later_activation_and_restart_and_foreign_owner_is_rejected()
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var publications = new FormPublicationService(settings, settings, authority, new StructuralValidator());
        var schema = new NodeGraphSchemaRegistry([new("forms.start", 1, "forms", "forms.read",
            JsonSerializer.SerializeToElement(new { type = "object" }), [])],
            [new(FormGraphReferenceService.StateProfile, new HashSet<string> { "forms.start" }, new HashSet<string> { "forms.read" })]);
        var graphPath = Path.Combine(paths.DataDirectory, "home.json");
        var graphs = new HomeVersionedNodeGraphRepository(new FileHomeCoreStateStore(graphPath), schema);
        var project = FormProjectEditor.Create("Pinned logic", FormModeKind.Form, DateTimeOffset.UtcNow);
        var startID = Guid.NewGuid();
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, FormGraphReferenceService.StateProfile, GraphRevisionState.Draft,
            [new(startID, "forms.start", 1, "forms", "forms.read", null, JsonSerializer.SerializeToElement(new { }), [])], []);
        Assert.True(await graphs.TrySaveDraftAsync("forms", project.FormID.ToString("D"), graph, 0));
        Assert.True(await graphs.TryActivateAsync(graph.GraphId, 1));
        project = project with { ModeDefinition = new(project.ModeDefinition.ModeID, FormModeKind.Custom, graph.GraphId, startID) };
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        var service = new FormGraphReferenceService(publications, graphs, schema);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ValidatePinnedAsync(project));
        var pinned = await service.PinDraftAsync(project.FormID, created.Publication!.Revision);
        Assert.True(pinned.Success);
        var published = await publications.PublishAsync(project.FormID, pinned.Publication!.Revision);
        var retained = FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(Assert.Single(published.Publication!.Versions).Project.GetRawText()));
        Assert.Equal(1, retained.ModeDefinition.StateGraphRevision);
        Assert.True(await graphs.TrySaveDraftAsync("forms", project.FormID.ToString("D"), graph with { Revision = 2 }, 1));
        Assert.True(await graphs.TryActivateAsync(graph.GraphId, 2));
        var reopened = new HomeVersionedNodeGraphRepository(new FileHomeCoreStateStore(graphPath), schema);
        var restarted = new FormGraphReferenceService(publications, reopened, schema);
        await restarted.ValidatePinnedAsync(retained);
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.ValidatePinnedAsync(retained with { FormID = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.ValidatePinnedAsync(retained with
            { ModeDefinition = retained.ModeDefinition with { StateGraphRevision = 99 } }));
        authority.Allowed = false;
        Assert.Equal("PermissionDenied", (await restarted.PinDraftAsync(project.FormID, published.Publication.Revision)).Code);
        authority.Allowed = true;
        Assert.Equal("RevisionConflict", (await restarted.PinDraftAsync(project.FormID, 1)).Code);
    }
    private sealed class StructuralValidator : IFormProjectPublicationValidator
    {
        public void Validate(Guid formID, JsonElement canonicalProject)
        {
            var project = FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(canonicalProject.GetRawText()));
            Assert.Equal(formID, project.FormID); // Fixture checks storage/pinning, not custom-mode execution.
        }
    }
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => Allowed));

        public bool Allowed { get; set; } = true;
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken) => ValueTask.FromResult(Allowed);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-graph-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
    private sealed class FixtureAdmission(Func<bool> allowed) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) => ValueTask.FromResult(allowed());
    }

}
