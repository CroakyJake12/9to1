using Haven.Application;

namespace Haven.Core.Tests;

public sealed class SpaceRegistryTests
{
    [Fact]
    public async Task First_load_seeds_built_ins_as_normal_space_records()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);

        var spaces = await registry.GetAllAsync();

        Assert.Equal(8, spaces.Count);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.ChatSpaceId && space.Kind == SpaceKind.Chat && space.Origin == SpaceOrigin.BuiltIn);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.StudySpaceId && space.Kind == SpaceKind.Study && space.IsBuiltIn);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.TasksSpaceId && space.Kind == SpaceKind.Tasks && space.IsBuiltIn);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.ShoppingSpaceId && space.Kind == SpaceKind.Shopping && space.IsBuiltIn);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.ResearchSpaceId && space.Kind == SpaceKind.Research && space.IsBuiltIn);
        var agent = spaces.Single(space => space.Id == SpaceRegistry.AgentSpaceId);
        Assert.True(agent.IsBuiltIn);
        Assert.Equal(SpaceKind.Agent, agent.Kind);
        Assert.False(string.IsNullOrWhiteSpace(agent.Instructions));
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.TranslateSpaceId && space.Kind == SpaceKind.Translate);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.ExperiencesSpaceId && space.Kind == SpaceKind.Experiences);
    }

    [Fact]
    public async Task Lifecycle_create_rename_archive_unarchive_and_delete_is_persisted()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);

        var created = await registry.CreateAsync("  Physics project  ", "Exam prep");
        Assert.Equal("Physics project", created.Name);

        var renamed = await registry.RenameAsync(created.Id, "Physics revision");
        Assert.Equal("Physics revision", renamed.Name);
        Assert.True(renamed.UpdatedAt >= created.UpdatedAt);

        await registry.SetArchivedAsync(created.Id, true);
        Assert.DoesNotContain(await registry.GetAllAsync(), space => space.Id == created.Id);
        Assert.Contains(await registry.GetAllAsync(includeArchived: true), space => space.Id == created.Id && space.IsArchived);

        await registry.DeleteAsync(created.Id);
        Assert.True((await registry.GetAsync(created.Id))!.IsArchived);
        await registry.RestoreAsync(created.Id);
        Assert.False((await registry.GetAsync(created.Id))!.IsArchived);
        await registry.DeleteAsync(created.Id);

        var reloaded = new SpaceRegistry(store);
        Assert.Contains(await reloaded.GetAllAsync(includeArchived: true), space => space.Id == created.Id && space.IsArchived);
    }

    [Fact]
    public async Task Fork_copies_configuration_but_gets_independent_identity()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var source = (await registry.GetAsync(SpaceRegistry.ResearchSpaceId))!;
        var configured = await registry.UpdateAsync(source with
        {
            ModelName = "local-model",
            Instructions = "Be precise",
            ThinkingMode = SpaceThinkingMode.Deep,
            ExamplePairs = [new SpaceExamplePair("Question", "Answer")]
        });

        var fork = await registry.ForkAsync(configured.Id);

        Assert.NotEqual(configured.Id, fork.Id);
        Assert.False(fork.IsBuiltIn);
        Assert.Equal(configured.Id, fork.ForkedFromSpaceId);
        Assert.Equal(configured.ModelName, fork.ModelName);
        Assert.Equal(configured.Instructions, fork.Instructions);
        Assert.Single(fork.ExamplePairs);
    }

    [Fact]
    public async Task Files_store_normalized_path_and_explicit_permission()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var space = await registry.CreateAsync("Files");
        var path = Path.Combine(Path.GetTempPath(), "haven-space", "notes.txt");

        var updated = await registry.AddFileAsync(space.Id, path, SpaceFilePermission.ReadWrite);

        var file = Assert.Single(updated.Files);
        Assert.Equal(Path.GetFullPath(path), file.Path);
        Assert.Equal("notes.txt", file.DisplayName);
        Assert.Equal(SpaceFilePermission.ReadWrite, file.Permission);

        updated = await registry.RemoveFileAsync(space.Id, path);
        Assert.Empty(updated.Files);
    }

    [Fact]
    public async Task Layout_documents_are_snapshotted_and_forked_independently()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Layout Space");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var nodes = new[]
        {
            new SpaceLayoutNode(firstId, "Input", "Prompt")
            {
                X = 20,
                Y = 30,
                Ports = [new SpaceLayoutPort("out", "Out", SpaceLayoutPortDirection.Output)]
            },
            new SpaceLayoutNode(secondId, "Surface", "Checklist")
            {
                X = 320,
                Y = 30,
                Ports = [new SpaceLayoutPort("in", "In", SpaceLayoutPortDirection.Input)]
            }
        };
        var layout = new SpaceLayoutDocument(nodes,
        [
            new SpaceLayoutEdge(Guid.NewGuid(), firstId, "out", secondId, "in")
        ]);

        var updated = await registry.SetLayoutAsync(space.Id, layout);
        nodes[0] = nodes[0] with { Title = "Mutated caller state" };

        Assert.NotNull(updated.LayoutDocument);
        Assert.Equal("Prompt", updated.LayoutDocument!.Nodes[0].Title);
        Assert.Single(updated.LayoutDocument.Edges);

        var fork = await registry.ForkAsync(updated.Id);
        Assert.NotNull(fork.LayoutDocument);
        Assert.NotSame(updated.LayoutDocument, fork.LayoutDocument);
        Assert.Equal(updated.LayoutDocument.Nodes.Select(node => node.Id), fork.LayoutDocument!.Nodes.Select(node => node.Id));
        Assert.Equal(updated.LayoutDocument.Edges.Select(edge => edge.Id), fork.LayoutDocument.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public async Task Deleting_current_custom_space_returns_to_unscoped_chat()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var created = await registry.CreateAsync("Temporary Space");
        await registry.SetCurrentSpaceIdAsync(created.Id);

        await registry.DeleteAsync(created.Id);

        Assert.Null(await registry.GetCurrentSpaceIdAsync());
        Assert.True((await registry.GetAsync(created.Id))!.IsArchived);
    }

    [Fact]
    public async Task Built_in_spaces_cannot_be_deleted()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.GetAllAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DeleteAsync(SpaceRegistry.StudySpaceId));
        Assert.Contains("Built-in Spaces", error.Message);

        var agentError = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DeleteAsync(SpaceRegistry.AgentSpaceId));
        Assert.Contains("Built-in Spaces", agentError.Message);
    }

    [Fact]
    public async Task Built_in_graphs_are_protected_and_custom_graphs_are_validated()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.SetLayoutAsync(SpaceRegistry.StudySpaceId, SpaceLayoutDocument.Empty));
        var space = await registry.CreateAsync("Graph");
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var invalid = new SpaceLayoutDocument(
        [
            new SpaceLayoutNode(source, "Input", "Source")
            {
                Ports = [new SpaceLayoutPort("in", "Input", SpaceLayoutPortDirection.Input)]
            },
            new SpaceLayoutNode(target, "Output", "Target")
            {
                Ports = [new SpaceLayoutPort("out", "Output", SpaceLayoutPortDirection.Output)]
            }
        ],
        [new SpaceLayoutEdge(Guid.NewGuid(), source, "in", target, "out")]);

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetLayoutAsync(space.Id, invalid));
    }

    [Fact]
    public async Task Reorder_and_nesting_keep_stable_ids_and_reject_cycles()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var parent = await registry.CreateAsync("Parent");
        var first = await registry.CreateAsync("First");
        var second = await registry.CreateAsync("Second");
        var firstId = first.Id;

        first = await registry.MoveAsync(first.Id, parent.Id);
        second = await registry.MoveAsync(second.Id, parent.Id, 0);

        Assert.Equal(firstId, first.Id);
        Assert.Equal(parent.Id, first.ParentSpaceId);
        Assert.Equal(1, first.Position);
        Assert.Equal(parent.Id, second.ParentSpaceId);
        Assert.Equal(0, second.Position);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.MoveAsync(parent.Id, first.Id));
    }

    [Fact]
    public async Task Moving_and_archiving_preserve_sibling_positions_and_protect_children()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var first = await registry.CreateAsync("Root first");
        var second = await registry.CreateAsync("Root second");
        var parent = await registry.CreateAsync("Parent");
        var firstBuiltIn = (await registry.GetAsync(SpaceRegistry.ChatSpaceId))!;

        await registry.MoveAsync(second.Id, parent.Id);
        var roots = (await registry.GetAllAsync()).Where(space => !space.IsBuiltIn && space.ParentSpaceId is null)
            .OrderBy(space => space.Position).ToArray();

        Assert.Equal([first.Id, parent.Id], roots.Select(space => space.Id));
        Assert.Equal([0, 1], roots.Select(space => space.Position));
        var reloadedBuiltIn = (await registry.GetAsync(firstBuiltIn.Id))!;
        Assert.Equal(firstBuiltIn.Revision, reloadedBuiltIn.Revision);
        Assert.Equal(firstBuiltIn.Position, reloadedBuiltIn.Position);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetArchivedAsync(parent.Id, true));
    }

    [Fact]
    public async Task Update_uses_expected_revision_and_built_in_identity_is_protected()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Versioned");
        var updated = await registry.UpdateAsync(space with { Description = "Updated" }, space.Revision);
        Assert.Equal("Updated", updated.Description);
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(
            () => registry.UpdateAsync(updated with { Description = "Stale" }, space.Revision));

        var chat = (await registry.GetAsync(SpaceRegistry.ChatSpaceId))!;
        var protectedChat = await registry.UpdateAsync(chat with
        {
            Name = "Renamed Chat",
            Instructions = "Replace protected behaviour",
            Kind = SpaceKind.General
        });
        Assert.Equal("Chat", protectedChat.Name);
        Assert.Equal(SpaceKind.Chat, protectedChat.Kind);
        Assert.NotEqual("Replace protected behaviour", protectedChat.Instructions);
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
