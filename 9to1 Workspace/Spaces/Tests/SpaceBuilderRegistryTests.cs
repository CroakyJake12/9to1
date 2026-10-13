using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceBuilderRegistryTests
{
    [Fact]
    public async Task Basic_and_advanced_patch_one_space_and_preserve_unexposed_configuration_after_reopen()
    {
        using var fixture = new Fixture();
        var registry = fixture.Open();
        var parent = await registry.CreateAsync("Parent");
        var created = await registry.CreateAsync("Builder", "Original", parent.Id);
        var node = new SpaceLayoutNode(Guid.NewGuid(), "Logic", "Retained legacy layout");
        var source = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.File,
            "files", "canonical-file", "revision-1", SpaceContextPermission.Read, SpaceContextIndexState.Ready,
            false, DateTimeOffset.UtcNow);
        var configured = await registry.UpdateAsync(created with
        {
            ExamplePairs = [new("Question", "Answer")],
            LayoutDocument = new([node], []),
            Files = [new("/existing/source.txt", "Source", SpaceFilePermission.ReadOnly, DateTimeOffset.UtcNow)],
            ContextReferences = [source], Shares = [new("owner-id", SpaceShareRole.Editor, DateTimeOffset.UtcNow)],
            GeneratedSurface = new("retained-template", "{}"),
            ModelName = "legacy-model", ThinkingMode = SpaceThinkingMode.Deep
        }, created.Revision);
        var details = new SpaceBuilderBasicPatch("Renamed", "Description", "book", "Exact\ncustom instructions");
        var advanced = await registry.ApplyBuilderAdvancedAsync(configured.Id, configured.Revision,
            new(details, new("configured-model", ["text"], "local"), new(SpaceWebAccessPolicy.Disabled, true),
                new([SpaceMemoryScope.Conversation, SpaceMemoryScope.Space], SpaceMemoryScope.Space),
                [new("skill:canonical", SpaceCapabilityKind.Skill, SpaceCapabilityOverride.Disabled, "1")],
                new(Sources: true, Skills: false)));
        var basic = await fixture.Open().ApplyBuilderBasicAsync(advanced.Id, advanced.Revision,
            details with { Name = "Final name", Instructions = "Later instruction" });
        var reopened = (await fixture.Open().GetAsync(configured.Id))!;

        Assert.Equal(configured.Id, reopened.Id);
        Assert.Equal(configured.Revision + 2, reopened.Revision);
        Assert.Equal(JsonSerializer.Serialize(basic), JsonSerializer.Serialize(reopened));
        Assert.Equal("Final name", reopened.Name);
        Assert.Equal("Later instruction", reopened.Instructions);
        Assert.Equal(JsonSerializer.Serialize(advanced.ModelPolicy), JsonSerializer.Serialize(reopened.ModelPolicy));
        Assert.Equal(JsonSerializer.Serialize(advanced.CapabilityBindings), JsonSerializer.Serialize(reopened.CapabilityBindings));
        Assert.Equal(JsonSerializer.Serialize(advanced.MemoryPolicy), JsonSerializer.Serialize(reopened.MemoryPolicy));
        Assert.Equal(JsonSerializer.Serialize(advanced.Inheritance), JsonSerializer.Serialize(reopened.Inheritance));
        var preserved = reopened with
        {
            Name = configured.Name, Description = configured.Description, IconKey = configured.IconKey,
            Instructions = configured.Instructions, ModelPolicy = configured.ModelPolicy,
            ContextPolicy = configured.ContextPolicy, MemoryPolicy = configured.MemoryPolicy,
            CapabilityBindings = configured.CapabilityBindings, Inheritance = configured.Inheritance,
            Revision = configured.Revision, UpdatedAt = configured.UpdatedAt
        };
        Assert.Equal(JsonSerializer.Serialize(configured), JsonSerializer.Serialize(preserved));
    }

    [Fact]
    public async Task Stale_builder_save_cannot_overwrite_another_registry_edit()
    {
        using var fixture = new Fixture();
        var first = fixture.Open();
        var original = await first.CreateAsync("Concurrent");
        var firstPatch = new SpaceBuilderBasicPatch("First saved name", "", "book", "");
        var saved = await fixture.Open().ApplyBuilderBasicAsync(original.Id, original.Revision, firstPatch);
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() =>
            first.ApplyBuilderBasicAsync(original.Id, original.Revision, firstPatch with { Name = "Stale name" }));
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(await fixture.Open().GetAsync(original.Id)));
    }

    [Fact]
    public async Task Invalid_and_protected_changes_preserve_stored_record()
    {
        using var fixture = new Fixture();
        var registry = fixture.Open();
        var builtin = (await registry.GetAsync(SpaceRegistry.ChatSpaceId))!;
        var patch = new SpaceBuilderBasicPatch("Changed", "", "book", "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyBuilderBasicAsync(builtin.Id, builtin.Revision, patch));
        Assert.Equal(JsonSerializer.Serialize(builtin), JsonSerializer.Serialize(await fixture.Open().GetAsync(builtin.Id)));
        var custom = await registry.CreateAsync("Custom");
        await Assert.ThrowsAsync<ArgumentException>(() => registry.ApplyBuilderAdvancedAsync(custom.Id, custom.Revision,
            new(patch, new(null), new(), new([SpaceMemoryScope.Conversation], SpaceMemoryScope.Team), [], new())));
        Assert.Equal(JsonSerializer.Serialize(custom), JsonSerializer.Serialize(await fixture.Open().GetAsync(custom.Id)));
        var archived = await registry.SetArchivedAsync(custom.Id, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyBuilderBasicAsync(archived.Id, archived.Revision, patch));
        Assert.Equal(JsonSerializer.Serialize(archived), JsonSerializer.Serialize(await fixture.Open().GetAsync(custom.Id)));
    }

    private sealed class Fixture : IDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "spaces-builder-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(DataDirectory);
        public SpaceRegistry Open() => new(new VersionedAtomicSettingsStore(this));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
