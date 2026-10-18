using Haven.Application;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceBuilderControllerTests
{
    [Fact]
    public async Task Load_save_and_discard_round_trip_the_canonical_record()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var controller = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await controller.LoadAsync(token));
        Assert.True(controller.SetField(SpaceBuilderField.Name, "Updated"));
        Assert.True(controller.SetField(SpaceBuilderField.Description, "New purpose"));
        Assert.True(controller.SetCategoryState(SpaceBuilderCategory.Skills, SpaceBuilderCategoryState.Enabled));
        Assert.True(controller.State.CanSave);
        Assert.True(await controller.SaveAsync(token));
        var saved = await files.OpenRegistry().GetAsync(space.Id, token);
        Assert.Equal("Updated", saved!.Name);
        Assert.Equal("New purpose", saved.Description);
        Assert.False(controller.State.HasChanges);
        // Discard re-reads the canonical record without deleting the draft conflict path.
        Assert.True(controller.SetField(SpaceBuilderField.Name, "Unsaved"));
        Assert.True(controller.State.HasChanges);
        Assert.True(await controller.DiscardChangesAsync(token));
        Assert.Equal("Updated", controller.State.Draft.Name);
        Assert.False(controller.State.HasChanges);
    }

    [Fact]
    public async Task Conflict_is_reported_without_overwriting_the_draft()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var first = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        var second = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await first.LoadAsync(token));
        Assert.True(await second.LoadAsync(token));
        Assert.True(first.SetField(SpaceBuilderField.Name, "First saved"));
        Assert.True(await first.SaveAsync(token));
        Assert.True(second.SetField(SpaceBuilderField.Name, "Second draft"));
        Assert.False(await second.SaveAsync(token));
        Assert.True(second.State.HasConflict);
        Assert.Equal("Second draft", second.State.Draft.Name);
    }
}
