using System.Text;
using HavenOS.AIStudio;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioProjectApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-studio-tests", Guid.NewGuid().ToString("N"));
    private AIStudioApi _api = null!;

    public Task InitializeAsync()
    {
        _api = CreateApi();
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

        var reopened = await CreateApi().OpenProjectAsync(created.Value.ProjectId);
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

    [Fact]
    public async Task EvaluationSuitesRequireStableIdentityProvenanceAndTargetRevision()
    {
        var suite = new EvaluationSuite(Guid.NewGuid(), "Quality gate", "not-registered", "Harness", 4,
            [new EvaluationCase(Guid.NewGuid(), "case", "input", "{}", null, null, [], [], null, null, null, "test-file", 4)],
            [new EvaluationGrader(Guid.NewGuid(), EvaluationGraderType.RequiredActions, "{}", true)], null, null, DateTimeOffset.UtcNow, 1);
        var created = await _api.CreateEvaluationSuiteAsync(suite.Name, suite);
        Assert.True(created.IsSuccess);
        Assert.Equal(StudioResourceKind.EvaluationSuite, created.Value!.Kind);
        Assert.Equal(created.Value.ResourceId, (await CreateApi().OpenResourceAsync(created.Value.ResourceId)).Value!.ResourceId);

        var invalid = suite with { Cases = [suite.Cases[0] with { SourceProvenance = "", TargetRevision = 3 }] };
        var rejected = await _api.CreateEvaluationSuiteAsync("Invalid", invalid);
        Assert.Equal("InvalidDefinition", rejected.Error!.Code);
    }

    [Fact]
    public async Task FailedEvaluationTargetIsPersistedWithExactCaseAndStructuredFailure()
    {
        var suite = new EvaluationSuite(Guid.NewGuid(), "Unavailable target", "missing-id", "Harness", 1,
            [new EvaluationCase(Guid.NewGuid(), "failure reproduction", "try", "{}", null, null, [], [], null, null, null, "suite.json", 1)],
            [new EvaluationGrader(Guid.NewGuid(), EvaluationGraderType.ExactOutput, "{}", true)], null, null, DateTimeOffset.UtcNow, 1);
        var resource = await _api.CreateEvaluationSuiteAsync(suite.Name, suite);
        var run = await _api.RunEvaluationAsync(resource.Value!.ResourceId);
        Assert.True(run.IsSuccess);
        Assert.Equal("Failed", run.Value!.Results.Single().Status);
        Assert.Equal("EvaluationTargetUnavailable", run.Value.Results.Single().StructuredErrorCode);
        Assert.Single((await _api.ListEvaluationRunsAsync(suite.EvalSuiteId)).Value!);
    }

    [Fact]
    public async Task TestSuiteRetainsReproducibleFailedCaseAndAssertionDefinition()
    {
        var test = new TestSuite(Guid.NewGuid(), "Runtime boundary", "missing-id", "Harness", 7,
            [new StudioTestCase(Guid.NewGuid(), "missing target", "input", "{}",
                [new TestAssertion("a1", "outputEquals", "$", "\"expected\"")], 7, "test-contract")], null, 1);
        var resource = await _api.CreateTestSuiteAsync(test.Name, test);
        var run = await _api.RunTestSuiteAsync(resource.Value!.ResourceId);
        Assert.True(run.IsSuccess);
        Assert.Equal("Failed", run.Value!.Results.Single().Status);
        Assert.Equal("EvaluationTargetUnavailable", run.Value.Results.Single().StructuredErrorCode);
        Assert.Contains("test-contract", run.Value.Results.Single().ReproductionJson);
        Assert.Single((await _api.ListTestRunsAsync(test.TestSuiteId)).Value!);
    }

    [Fact]
    public async Task ReusableResourcesValidateStableIdentityAndExplicitSafetyAllowLists()
    {
        var noIdentity = await _api.CreateSchemaAsync("Contract", "{\"version\":1}");
        Assert.Equal("InvalidDefinition", noIdentity.Error!.Code);
        var schema = await _api.CreateSchemaAsync("Contract", "{\"schemaId\":\"person.v1\",\"version\":1,\"fields\":[]}");
        Assert.True(schema.IsSuccess);
        var genUi = await _api.CreateGenerativeUiAsync("Cards", "{\"components\":[],\"actions\":[]}");
        Assert.True(genUi.IsSuccess);
        var unsafeUi = await _api.CreateGenerativeUiAsync("Unbounded", "{\"components\":[]}");
        Assert.Equal("InvalidDefinition", unsafeUi.Error!.Code);
    }

    [Fact]
    public async Task PermissionAndContextDiagnosticsDoNotInventRuntimeTruth()
    {
        var permission = await _api.SimulatePermissionsAsync("{\"capability\":\"file.write\"}");
        Assert.Equal("CapabilityUnavailable", permission.Error!.Code);
        var context = await _api.InspectContextAsync(Guid.NewGuid());
        Assert.Equal("ContextUnavailable", context.Error!.Code);
        var replay = await _api.RestartReplayAsync(Guid.NewGuid(), "node-1", "{}");
        Assert.Equal("ReplayPointUnavailable", replay.Error!.Code);
    }

    [Fact]
    public async Task DependencyInspectorReportsMissingStableReferences()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Harness");
        var changed = await _api.SaveProjectAsync(created.Value!.ProjectId, 1, created.Value.DefinitionJson,
            [new StudioDependency("Plugin", "plugin:missing", "1.2.*")]);
        var dependencies = await _api.InspectDependenciesAsync(changed.Value!.ProjectId);
        Assert.True(dependencies.IsSuccess);
        Assert.Equal("missing", dependencies.Value!.Nodes.Single(item => item.StableId == "plugin:missing").State);
    }

    [Fact]
    public async Task ProjectPinAndProjectFilesAreVersionedAndRejectEscapingPaths()
    {
        var created = await _api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Pinned");
        var pinned = await _api.SetProjectPinnedAsync(created.Value!.ProjectId, 1, true);
        Assert.True(pinned.IsSuccess);
        Assert.Single((await _api.ListProjectsAsync(pinnedOnly: true)).Value!.Items);
        Assert.Equal("InvalidProjectPath", (await _api.SetProjectFileAsync(pinned.Value!.ProjectId, 2, "../outside.txt", "x")).Error!.Code);
        var file = await _api.SetProjectFileAsync(pinned.Value.ProjectId, 2, "src/main.txt", "safe");
        Assert.True(file.IsSuccess);
        Assert.Equal("safe", file.Value!.ProjectFiles!["src/main.txt"]);
    }

    [Fact]
    public async Task ResourceRenameUpdateHistoryDeleteAndRecoveryAreVersioned()
    {
        var created = await _api.CreateSchemaAsync("Person", "{\"schemaId\":\"person.v1\",\"type\":\"object\"}");
        var renamed = await _api.RenameResourceAsync(created.Value!.ResourceId, 1, "Person schema");
        Assert.True(renamed.IsSuccess);
        var changed = await _api.UpdateResourceAsync(renamed.Value!.ResourceId, 2, "{\"schemaId\":\"person.v2\",\"type\":\"object\"}");
        Assert.True(changed.IsSuccess);
        Assert.Equal(2, (await _api.ResourceHistoryAsync(changed.Value!.ResourceId)).Value!.Count);
        var deleted = await _api.DeleteResourceAsync(changed.Value.ResourceId, 3);
        Assert.True(deleted.IsSuccess);
        Assert.Empty((await _api.ListResourcesAsync(StudioResourceKind.Schema)).Value!);
        var recovered = await _api.RecoverResourceAsync(changed.Value.ResourceId);
        Assert.True(recovered.IsSuccess);
        Assert.Equal("Person schema", recovered.Value!.Name);
    }

    [Fact]
    public async Task InvalidResourceSaveDoesNotPartiallyRenameOrAdvanceRevision()
    {
        var created = await _api.CreateSchemaAsync("Person", "{\"schemaId\":\"person.v1\",\"type\":\"object\"}");
        var rejected = await _api.SaveResourceAsync(created.Value!.ResourceId, 1, "Changed", "not-json");
        Assert.Equal("InvalidDefinition", rejected.Error!.Code);
        var reopened = await _api.OpenResourceAsync(created.Value.ResourceId);
        Assert.Equal(1, reopened.Value!.Version);
        Assert.Equal("Person", reopened.Value.Name);
    }

    private AIStudioApi CreateApi() => new(new JsonStudioProjectStore(_root), new UnavailableStudioRuntimeAdapter(),
        new UnavailableCanonicalAgentBuilderAdapter(), new JsonStudioResourceStore(_root),
        new UnavailableStudioPermissionSimulationAdapter(), new UnavailableStudioContextInspectorAdapter(),
        new UnavailableStudioReplayAdapter());
}
