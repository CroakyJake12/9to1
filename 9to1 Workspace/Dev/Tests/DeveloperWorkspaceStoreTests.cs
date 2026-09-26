using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperWorkspaceStoreTests : IDisposable
{
    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), "nine-to-one-dev-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Workspace_RoundTripsStableIdentityAndMultiRootState()
    {
        var store = new FileDeveloperWorkspaceStore(_stateRoot);
        var firstRoot = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()));
        var secondRoot = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Environment.CurrentDirectory));
        var workspace = DeveloperWorkspace.Create([firstRoot, secondRoot]);

        var created = await store.CreateAsync(workspace);
        var loaded = await store.GetAsync(workspace.WorkspaceId);

        Assert.True(created.Succeeded);
        Assert.True(loaded.Succeeded);
        Assert.Equal(workspace.WorkspaceId, loaded.Value!.WorkspaceId);
        Assert.Equal([firstRoot, secondRoot], loaded.Value.Roots);
        Assert.Equal(1, loaded.Value.Revision);
    }

    [Fact]
    public async Task SaveAsync_IncrementsRevisionAndRejectsStaleWriter()
    {
        var store = new FileDeveloperWorkspaceStore(_stateRoot);
        var workspace = DeveloperWorkspace.Create([new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()))]);
        var created = await store.CreateAsync(workspace);
        Assert.True(created.Succeeded);

        var firstSave = await store.SaveAsync(created.Value!, expectedRevision: 1);
        var staleSave = await store.SaveAsync(created.Value!, expectedRevision: 1);
        var current = await store.GetAsync(workspace.WorkspaceId);

        Assert.True(firstSave.Succeeded);
        Assert.Equal(2, firstSave.Value!.Revision);
        Assert.False(staleSave.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, staleSave.Error!.Code);
        Assert.Equal(2, current.Value!.Revision);
    }

    [Fact]
    public async Task GetAsync_RejectsUnknownSchemaWithoutReinterpretingIt()
    {
        var workspaceId = Guid.NewGuid();
        var directory = Path.Combine(_stateRoot, "workspaces");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, workspaceId.ToString("N") + ".json"),
            "{\"schemaVersion\":999,\"workspace\":{\"workspaceId\":\"" + workspaceId + "\"}}");

        var loaded = await new FileDeveloperWorkspaceStore(_stateRoot).GetAsync(workspaceId);

        Assert.False(loaded.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.UnsupportedSchemaVersion, loaded.Error!.Code);
    }

    [Fact]
    public async Task GetAsync_ReportsMissingSchemaAsMalformedStoredData()
    {
        var workspaceId = Guid.NewGuid();
        var directory = Path.Combine(_stateRoot, "workspaces");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, workspaceId.ToString("N") + ".json"),
            "{\"workspace\":{\"workspaceId\":\"" + workspaceId + "\"}}");

        var loaded = await new FileDeveloperWorkspaceStore(_stateRoot).GetAsync(workspaceId);

        Assert.False(loaded.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, loaded.Error!.Code);
    }

    [Fact]
    public async Task CreateAsync_RejectsWorkspaceWithoutRoots()
    {
        var workspace = DeveloperWorkspace.Create([new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()))])
            with { Roots = Array.Empty<DeveloperWorkspaceRoot>() };

        var result = await new FileDeveloperWorkspaceStore(_stateRoot).CreateAsync(workspace);

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, result.Error!.Code);
        Assert.False(Directory.Exists(Path.Combine(_stateRoot, "workspaces")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateRoot)) Directory.Delete(_stateRoot, recursive: true);
    }
}
