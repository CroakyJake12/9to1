using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesSourceManagementTests
{
    [Fact]
    public async Task Added_local_sources_are_visible_and_removal_keeps_original_files_and_other_references()
    {
        using var files = new SpaceDetailsTestFiles();
        var firstPath = Path.Combine(files.DataDirectory, "first.txt");
        var secondPath = Path.Combine(files.DataDirectory, "second.txt");
        await File.WriteAllTextAsync(firstPath, "Keep original first");
        await File.WriteAllTextAsync(secondPath, "Keep original second");
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Sources");
        var controller = new SpacesHomeController(registry);
        await controller.SelectSpaceAsync(space.Id);
        await controller.AddContextAsync(space.Id, firstPath);
        await controller.AddContextAsync(space.Id, secondPath);

        var sources = controller.State.Selection!.FileSources;
        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => Assert.Contains("access checked when used", source.AccessLabel));
        await controller.RemoveFileSourceAsync(sources.Single(source => source.Path == firstPath));

        var reopened = new SpacesHomeController(files.OpenRegistry());
        await reopened.SelectSpaceAsync(space.Id);
        Assert.Equal(secondPath, Assert.Single(reopened.State.Selection!.FileSources).Path);
        Assert.Equal("Keep original first", await File.ReadAllTextAsync(firstPath));
        Assert.Equal("Keep original second", await File.ReadAllTextAsync(secondPath));
    }

    [Fact]
    public async Task Old_source_row_cannot_remove_source_after_selection_changes()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var first = await registry.CreateAsync("First");
        var second = await registry.CreateAsync("Second");
        await registry.AddFileAsync(first.Id, "/tmp/first.txt");
        var controller = new SpacesHomeController(registry);
        await controller.SelectSpaceAsync(first.Id);
        var stale = Assert.Single(controller.State.Selection!.FileSources);
        await controller.SelectSpaceAsync(second.Id);

        await controller.RemoveFileSourceAsync(stale);

        Assert.Single((await registry.GetAsync(first.Id))!.Files);
        Assert.Empty((await registry.GetAsync(second.Id))!.Files);
        Assert.Equal(second.Id, controller.State.Selection!.Id);
    }

    [Fact]
    public async Task Changed_space_refuses_stale_source_removal_without_losing_new_sources()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Sources");
        await registry.AddFileAsync(space.Id, "/tmp/first.txt");
        var controller = new SpacesHomeController(registry);
        await controller.SelectSpaceAsync(space.Id);
        var stale = Assert.Single(controller.State.Selection!.FileSources);
        await registry.AddFileAsync(space.Id, "/tmp/second.txt");

        await controller.RemoveFileSourceAsync(stale);

        Assert.Equal(2, (await registry.GetAsync(space.Id))!.Files.Count);
        Assert.Contains("Could not remove source", controller.State.StatusMessage);
    }

    [Fact]
    public async Task Source_add_and_remove_check_the_expected_revision_inside_the_registry()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Sources");
        var current = await registry.AddFileAsync(space.Id, "/tmp/first.txt");
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() =>
            registry.AddFileAsync(space.Id, "/tmp/second.txt", SpaceFilePermission.ReadOnly, space.Revision));
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() =>
            registry.RemoveFileAsync(space.Id, "/tmp/first.txt", space.Revision));
        Assert.Equal(current.Files, (await registry.GetAsync(space.Id))!.Files);
    }
    [Fact]
    public async Task Typed_source_inspection_and_removal_preserve_other_sources_and_permissions()
    {
        using var files = new SpaceDetailsTestFiles();
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Typed sources");
        var first = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.WriteArtifact,
            "write", "artifact:first", "revision-7", SpaceContextPermission.Unknown,
            SpaceContextIndexState.Stale, true, DateTimeOffset.UtcNow);
        var second = first with { ContextId = Guid.NewGuid(), CanonicalEntityId = "artifact:second", Permission = SpaceContextPermission.Denied };
        await registry.UpdateAsync(space with { ContextReferences = [first, second] }, space.Revision);
        var controller = new SpacesHomeController(registry);
        await controller.SelectSpaceAsync(space.Id);
        var row = controller.State.Selection!.ContextReferences.Single(source => source.ContextId == first.ContextId);
        Assert.Equal("Source: write", row.OwnerLabel);
        Assert.Equal("Revision: revision-7", row.RevisionLabel);
        Assert.Equal("Included automatically", row.InclusionLabel);
        Assert.Equal("Unknown", row.PermissionLabel);
        Assert.Equal("Stale", row.IndexStateLabel);

        await controller.RemoveContextReferenceAsync(row);

        var remaining = Assert.Single((await files.OpenRegistry().GetAsync(space.Id))!.ContextReferences!);
        Assert.Equal(second, remaining);
    }
    [Fact]
    public async Task Local_source_identity_follows_platform_path_case_rules()
    {
        using var files = new SpaceDetailsTestFiles();
        var upper = Path.Combine(files.DataDirectory, "Notes.txt");
        var lower = Path.Combine(files.DataDirectory, "notes.txt");
        await File.WriteAllTextAsync(upper, "upper source");
        await File.WriteAllTextAsync(lower, "lower source");
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Case-sensitive sources");
        await registry.AddFileAsync(space.Id, upper);
        var added = await registry.AddFileAsync(space.Id, lower);
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, added.Files.Count);
        var removed = await registry.RemoveFileAsync(space.Id, upper, added.Revision);
        if (OperatingSystem.IsWindows()) Assert.Empty(removed.Files);
        else
        {
            Assert.Equal(lower, Assert.Single(removed.Files).Path);
            Assert.Equal("upper source", await File.ReadAllTextAsync(upper));
        }
        Assert.Equal("lower source", await File.ReadAllTextAsync(lower));
    }
}
