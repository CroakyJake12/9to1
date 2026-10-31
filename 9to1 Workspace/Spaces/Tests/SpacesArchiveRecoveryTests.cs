using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpacesArchiveRecoveryTests
{
    [Fact]
    public async Task Restore_retains_identity_configuration_and_current_selection_across_fresh_registry()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var space = await registry.CreateAsync("Research archive", "Keep these instructions");
        await registry.SetArchivedAsync(space.Id, true);
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var selected = controller.State.Selection!.Id;
        var archived = Assert.Single(controller.State.ArchivedSpaces);
        Assert.DoesNotContain(controller.State.CustomSpaces, item => item.Id == space.Id);
        Assert.DoesNotContain(controller.State.MoveParentOptions, item => item.Id == space.Id);

        await controller.RestoreSpaceAsync(archived);

        Assert.Empty(controller.State.ArchivedSpaces);
        Assert.Equal(space.Id, Assert.Single(controller.State.CustomSpaces).Id);
        Assert.Equal(selected, controller.State.Selection!.Id);
        var reopened = await new SpaceRegistry(store).GetAsync(space.Id);
        Assert.NotNull(reopened);
        Assert.False(reopened.IsArchived);
        Assert.Equal(space.CreatedAt, reopened.CreatedAt);
        Assert.Equal(space.Instructions, reopened.Instructions);
        Assert.Equal(space.Name, reopened.Name);
    }

    [Fact]
    public async Task Stale_restore_refuses_newer_archive_and_preserves_it()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Archive");
        await registry.SetArchivedAsync(space.Id, true);
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var archived = Assert.Single(controller.State.ArchivedSpaces);
        await registry.RenameAsync(space.Id, "Changed elsewhere");

        await controller.RestoreSpaceAsync(archived);

        Assert.True((await registry.GetAsync(space.Id))!.IsArchived);
        Assert.Contains("Could not restore", controller.State.StatusMessage);
        Assert.Equal("Changed elsewhere", (await registry.GetAsync(space.Id))!.Name);
    }

    [Fact]
    public async Task Archived_child_requires_explicit_parent_restore_and_retains_hierarchy()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var parent = await registry.CreateAsync("Parent");
        var child = await registry.CreateAsync("Child");
        await registry.MoveAsync(child.Id, parent.Id);
        await registry.SetArchivedAsync(child.Id, true);
        await registry.SetArchivedAsync(parent.Id, true);
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();

        await controller.RestoreSpaceAsync(controller.State.ArchivedSpaces.Single(item => item.Id == child.Id));
        Assert.Contains("Restore the parent", controller.State.StatusMessage);
        Assert.True((await registry.GetAsync(child.Id))!.IsArchived);
        Assert.True((await registry.GetAsync(parent.Id))!.IsArchived);

        await controller.RestoreSpaceAsync(controller.State.ArchivedSpaces.Single(item => item.Id == parent.Id));
        await controller.RestoreSpaceAsync(controller.State.ArchivedSpaces.Single(item => item.Id == child.Id));
        Assert.Empty(controller.State.ArchivedSpaces);
        Assert.Equal(parent.Id, (await registry.GetAsync(child.Id))!.ParentSpaceId);
    }

    [Fact]
    public async Task Reload_retires_old_restore_row_and_search_includes_archived_names()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Research archive");
        await registry.SetArchivedAsync(space.Id, true);
        var controller = new SpacesHomeController(registry);
        await controller.LoadAsync();
        var old = Assert.Single(controller.State.ArchivedSpaces);
        await controller.LoadAsync();
        await controller.RestoreSpaceAsync(old);
        Assert.True((await registry.GetAsync(space.Id))!.IsArchived);
        Assert.Contains("Refresh Spaces", controller.State.StatusMessage);
        controller.SetQuery("Research");
        Assert.Single(controller.State.ArchivedSpaces);
        controller.SetQuery("Missing");
        Assert.Empty(controller.State.ArchivedSpaces);
        controller.SetQuery("");
        Assert.Single(controller.State.ArchivedSpaces);
    }

    [Fact]
    public async Task Archive_checks_expected_revision_before_changing_state()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Current");
        await registry.RenameAsync(space.Id, "Updated");
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() =>
            registry.SetArchivedAsync(space.Id, true, space.Revision));
        Assert.False((await registry.GetAsync(space.Id))!.IsArchived);
    }
}
