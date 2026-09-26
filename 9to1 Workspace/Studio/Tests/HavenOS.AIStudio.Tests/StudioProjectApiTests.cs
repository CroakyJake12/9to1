using System.Text;
using HavenOS.AIStudio;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioProjectApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-studio-tests", Guid.NewGuid().ToString("N"));
    private AIStudioApi _api = null!;

    public Task InitializeAsync()
    {
        _api = new AIStudioApi(new JsonStudioProjectStore(_root), new UnavailableStudioRuntimeAdapter(),
            new UnavailableCanonicalAgentBuilderAdapter());
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateHarnessUsesFunctionalVersionedTemplateAndPersistsAcrossStoreInstances()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Support helper");
        Assert.True(created.IsSuccess);
        Assert.NotEqual(Guid.Empty, created.Value!.ProjectId);
        Assert.Equal(1, created.Value.Version);
        Assert.Equal("harness.assistant.v1", created.Value.TemplateId);
        Assert.False(string.IsNullOrWhiteSpace(created.Value.ReadDefinition<HarnessDefinition>()!.Prompt));

        var reopened = await new AIStudioApi(new JsonStudioProjectStore(_root), new UnavailableStudioRuntimeAdapter(),
            new UnavailableCanonicalAgentBuilderAdapter()).OpenProjectAsync(created.Value.ProjectId);
        Assert.True(reopened.IsSuccess);
        Assert.Equal("Support helper", reopened.Value!.Name);
    }

    [Fact]
    public async Task ProjectEditsCreateRevisionsAndRejectStaleWriters()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Harness");
        var first = await _api.RenameProjectAsync(created.Value!.ProjectId, 1, "Renamed");
        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.Value!.Version);

        var stale = await _api.SaveProjectAsync(first.Value.ProjectId, 1, first.Value.DefinitionJson);
        Assert.Equal("RevisionConflict", stale.Error!.Code);

        var history = await _api.ProjectHistoryAsync(first.Value.ProjectId);
        Assert.Single(history.Value!);
        Assert.Equal(1, history.Value![0].Version);
    }

    [Fact]
    public async Task DeleteIsRecoverableAndHiddenFromNormalProjectQueries()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Harness");
        var deleted = await _api.DeleteProjectAsync(created.Value!.ProjectId, 1);
        Assert.True(deleted.IsSuccess);
        Assert.Equal("ProjectNotFound", (await _api.OpenProjectAsync(created.Value.ProjectId)).Error!.Code);
        Assert.Empty((await _api.ListProjectsAsync()).Value!.Items);
        Assert.Single((await _api.ListProjectsAsync(includeDeleted: true)).Value!.Items);

        var recovered = await _api.RecoverProjectAsync(created.Value.ProjectId);
        Assert.True(recovered.IsSuccess);
        Assert.False(recovered.Value!.IsDeleted);
    }

    [Fact]
    public async Task TypeSpecificToolTemplatesRejectMismatchesAndValidateStructure()
    {
        var mismatch = await _api.CreateToolAsync(StudioToolType.Plugin, "tool.skill.v1", "Bad pairing");
        Assert.Equal("TemplateTypeMismatch", mismatch.Error!.Code);

        var created = await _api.CreateToolAsync(StudioToolType.Skill, "tool.skill.v1", "Field guide");
        Assert.True(created.IsSuccess);
        var report = await _api.ValidateProjectAsync(created.Value!.ProjectId);
        Assert.True(report.IsSuccess);
        Assert.True(report.Value!.IsValid);
    }

    [Fact]
    public async Task InvalidDefinitionIsRejectedAndDoesNotChangeVersion()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Harness");
        var result = await _api.SaveProjectAsync(created.Value!.ProjectId, 1, "{broken");
        Assert.Equal("InvalidDefinition", result.Error!.Code);
        Assert.Equal(1, (await _api.OpenProjectAsync(created.Value.ProjectId)).Value!.Version);
    }

    [Fact]
    public async Task ExportImportCreatesIndependentIdentityAndPreservesDefinition()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.research.v1", "Research");
        var exported = await _api.ExportProjectAsync(created.Value!.ProjectId);
        Assert.True(exported.IsSuccess);
        var imported = await _api.ImportProjectAsync(exported.Value!);
        Assert.True(imported.IsSuccess);
        Assert.NotEqual(created.Value.ProjectId, imported.Value!.ProjectId);
        Assert.Equal(created.Value.DefinitionJson, imported.Value.DefinitionJson);
    }

    [Fact]
    public async Task CorruptStoreReturnsStructuredErrorWithoutOverwritingSource()
    {
        Directory.CreateDirectory(_root);
        var storePath = Path.Combine(_root, "projects.v1.json");
        const string corrupt = "{not-json";
        await File.WriteAllTextAsync(storePath, corrupt, Encoding.UTF8);

        var listed = await _api.ListProjectsAsync();
        Assert.Equal("StoreSchemaUnsupported", listed.Error!.Code);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(storePath, Encoding.UTF8));
    }

    [Fact]
    public async Task RuntimeAndDenActionsFailHonestlyWhenCanonicalAdaptersAreAbsent()
    {
        var harness = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Harness");
        Assert.Equal("RuntimeUnavailable", (await _api.TestProjectAsync(harness.Value!.ProjectId)).Error!.Code);
        Assert.Equal("RuntimeUnavailable", (await _api.RunPlaygroundAsync(
            new PlaygroundConfiguration(null, null, [], [], new Dictionary<string, string>(), null, ""), "input")).Error!.Code);
        Assert.Equal("CapabilityUnavailable", (await _api.CreateAgentAsync(
            new CanonicalAgentDefinition(Guid.Empty, "Agent", "", "", "{}", 0, true))).Error!.Code);
    }

    [Fact]
    public async Task CursorAndPageSizeAreValidatedAndPagesDoNotRepeatItems()
    {
        for (var index = 0; index < 3; index++) await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", $"Harness {index}");
        Assert.Equal("InvalidPageSize", (await _api.ListProjectsAsync(0)).Error!.Code);
        Assert.Equal("InvalidCursor", (await _api.ListProjectsAsync(cursor: "not-a-cursor")).Error!.Code);
        var first = await _api.ListProjectsAsync(2);
        var second = await _api.ListProjectsAsync(2, first.Value!.NextCursor);
        Assert.Equal(2, first.Value.Items.Count);
        Assert.Single(second.Value!.Items);
        Assert.Empty(first.Value.Items.Select(item => item.ProjectId).Intersect(second.Value.Items.Select(item => item.ProjectId)));
    }
}
