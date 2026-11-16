using CakeOS.Cui.Language;
using HavenOS.AIStudio;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioCuiBindingsTests
{
    private static AIStudioApi Api()
    {
        var path = Path.Combine(Path.GetTempPath(), "studio-cui-tests", Guid.NewGuid().ToString("N"));
        return new(new JsonStudioProjectStore(path), new UnavailableStudioRuntimeAdapter(), new UnavailableCanonicalAgentBuilderAdapter(),
            new JsonStudioResourceStore(path), new UnavailableStudioPermissionSimulationAdapter(), new UnavailableStudioContextInspectorAdapter(), new UnavailableStudioReplayAdapter());
    }

    private static T Value<T>(StudioCuiBindings binding, string path)
    { Assert.True(binding.TryGetValue(path, out var value)); return Assert.IsAssignableFrom<T>(value); }

    [Fact]
    public void EmbeddedCuiParsesAndHasTheExistingNavigationAndEngineActions()
    {
        using var stream = typeof(StudioCuiBindings).Assembly.GetManifestResourceStream("HavenOS.AIStudio.Studio.cui");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var parser = new CuiRichParser();
        parser.Parse(source, "Studio.cui");
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
        foreach (var action in new[] { "Import", "SaveProject", "ValidateProject", "PreviewProject", "InstallTool", "ProjectHistory",
            "DuplicateProject", "ExportProject", "RecoverProject", "CreateAgent", "RunPlayground", "CreateResource", "SaveResource",
            "ResourceHistory", "RecoverResource", "RunEvaluation", "RunTestSuite", "EngineRun", "EnginePause", "EngineRecover", "EngineRefresh", "EngineMore" })
            Assert.Contains($"action=\"{action}\"", source);
        Assert.DoesNotContain("Fluent", source);
    }

    [Fact]
    public async Task ExistingProjectCreateEditHistoryDuplicateExportAndRecoveryRemainConnected()
    {
        var api = Api(); var files = new Dialogs();
        await using var binding = new StudioCuiBindings(api, new(null), files, action => action());
        await binding.DispatchAsync("CreateHarness", null);
        Assert.True(Value<bool>(binding, "ShowProjectEditor"));
        Assert.False(Value<bool>(binding, "ShowSearch"));
        Assert.True(binding.TrySetValue("Name", "Edited harness"));
        await binding.DispatchAsync("SaveProject", null);
        var project = Assert.Single((await api.ListProjectsAsync()).Value!.Items);
        Assert.Equal("Edited harness", project.Name);
        Assert.True(project.Version > 1);
        await binding.DispatchAsync("ProjectHistory", null);
        Assert.True(Value<bool>(binding, "HasNotice"));
        await binding.DispatchAsync("ExportProject", null);
        Assert.NotEmpty(files.Exported!);
        await binding.DispatchAsync("DuplicateProject", null);
        Assert.Equal(2, (await api.ListProjectsAsync()).Value!.Items.Count);
        await binding.DispatchAsync("RecoverProject", null);
        Assert.Single((await api.ListProjectsAsync()).Value!.Items);
        Assert.Equal(2, (await api.ListProjectsAsync(includeDeleted: true)).Value!.Items.Count);
        files.Imported = files.Exported;
        await binding.DispatchAsync("Import", null);
        Assert.Equal(2, (await api.ListProjectsAsync()).Value!.Items.Count);
    }

    [Fact]
    public async Task ResourceCreateOpenEditHistoryAndRecoveryPreserveVersionedStore()
    {
        var api = Api();
        await using var binding = new StudioCuiBindings(api, new(null), new Dialogs(), action => action());
        await binding.DispatchAsync("Navigate", "Schemas");
        Assert.True(binding.TrySetValue("Name", "First schema"));
        await binding.DispatchAsync("CreateResource", null);
        var row = Assert.Single(Value<IReadOnlyList<StudioCuiRow>>(binding, "Rows"));
        await binding.DispatchAsync("OpenRow", row);
        Assert.True(Value<bool>(binding, "ShowResourceEditor"));
        Assert.True(binding.TrySetValue("Name", "Revised schema"));
        await binding.DispatchAsync("SaveResource", null);
        var saved = Assert.Single((await api.ListResourcesAsync()).Value!);
        Assert.Equal("Revised schema", saved.Name);
        Assert.Equal(2, saved.Version);
        await binding.DispatchAsync("ResourceHistory", null);
        Assert.True(Value<bool>(binding, "HasNotice"));
        await binding.DispatchAsync("RecoverResource", null);
        Assert.Empty((await api.ListResourcesAsync()).Value!);
    }

    [Fact]
    public async Task UnregisteredRuntimeAndSelfCheckRemainTruthfullyUnavailable()
    {
        await using var binding = new StudioCuiBindings(Api(), new(null), new Dialogs(), action => action());
        await binding.DispatchAsync("Navigate", "Engine self-check");
        Assert.True(Value<bool>(binding, "ShowEngine"));
        Assert.False(Value<bool>(binding, "CanEngineRun"));
        Assert.Contains("software test only", Value<string>(binding, "EngineTitle"));
        await binding.DispatchAsync("Navigate", "Playground");
        Assert.True(binding.TrySetValue("Input", "Software test request; runtime is unavailable."));
        await binding.DispatchAsync("RunPlayground", null);
        Assert.Equal("RuntimeUnavailable", Value<string>(binding, "NoticeTitle"));
        await binding.DispatchAsync("Navigate", "Agents");
        await binding.DispatchAsync("CreateAgent", null);
        Assert.Equal("CapabilityUnavailable", Value<string>(binding, "NoticeTitle"));
    }

    [Fact]
    public async Task AllResourceAndTemplateDestinationsRemainReachable()
    {
        await using var binding = new StudioCuiBindings(Api(), new(null), new Dialogs(), action => action());
        foreach (var page in new[] { "Evaluations", "Test Suites", "Schemas", "Model Routers", "Generative UI", "Run Profiles" })
        { await binding.DispatchAsync("Navigate", page); Assert.True(Value<bool>(binding, "ShowResourceCreate")); }
        await binding.DispatchAsync("Navigate", "Templates");
        var rows = Value<IReadOnlyList<StudioCuiRow>>(binding, "Rows");
        Assert.Equal(StudioTemplateCatalog.All.Count, rows.Count);
        await binding.DispatchAsync("OpenRow", rows[0]);
        Assert.True(Value<bool>(binding, "ShowProjectEditor"));
    }

    private sealed class Dialogs : IStudioFileDialogs
    {
        public byte[]? Imported { get; set; }
        public byte[]? Exported { get; private set; }
        public Task<byte[]?> ImportAsync(CancellationToken cancellationToken) => Task.FromResult(Imported);
        public Task<bool> ExportAsync(string name, byte[] bytes, CancellationToken cancellationToken)
        { Exported = bytes.ToArray(); return Task.FromResult(true); }
    }

    [Fact]
    public async Task ExportReplacesSeekableContentsWithoutTrailingOldBytes()
    {
        using var output = new MemoryStream();
        output.Write(new byte[128]);
        output.Position = 57;
        await StudioProjectExport.WriteAsync(output, new byte[] { 12, 34 });
        Assert.Equal(new byte[] { 12, 34 }, output.ToArray());
    }
}
