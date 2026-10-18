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
        Assert.True(controller.SetCategoryState(SpaceBuilderCategory.Skills, Haven.Application.SpaceCapabilityCategoryState.Enabled));
        Assert.True(controller.State.CanSave);
        Assert.True(await controller.SaveAsync(token));
        var saved = await files.OpenRegistry().GetAsync(space.Id, token);
        Assert.Equal("Updated", saved!.Name);
        Assert.Equal("New purpose", saved.Description);
        Assert.Equal(Haven.Application.SpaceCapabilityCategoryState.Enabled, saved.CapabilityCategoryPolicy!.Skills);
        // Hidden policy must survive a Basic save.
        Assert.NotNull(saved.CapabilityBindings);
        Assert.False(controller.State.HasChanges);
        // Active revision must NOT advance on an ordinary (unvalidated) save.
        Assert.NotEqual(controller.State.ActiveRevision, controller.State.DraftRevision);
        // Discard re-reads the canonical record without deleting the draft conflict path.
        Assert.True(controller.SetField(SpaceBuilderField.Name, "Unsaved"));
        Assert.True(controller.State.HasChanges);
        Assert.True(await controller.DiscardChangesAsync(token));
        Assert.Equal("Updated", controller.State.Draft.Name);
        Assert.False(controller.State.HasChanges);
    }

    [Fact]
    public async Task Category_and_hidden_policy_survive_save_and_reopen()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        // Seed hidden policy on the canonical record.
        space = await files.OpenRegistry().UpdateAsync(space with
        {
            CapabilityBindings = [new("plugin-id", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Disabled, "v1")],
            MemoryPolicy = new([SpaceMemoryScope.Conversation], SpaceMemoryScope.Conversation)
        }, space.Revision, token);
        var first = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await first.LoadAsync(token));
        Assert.True(first.SetCategoryState(SpaceBuilderCategory.Skills, Haven.Application.SpaceCapabilityCategoryState.Enabled));
        Assert.True(await first.SaveAsync(token));

        // A SECOND controller on the SAME canonical record must see the saved category + hidden policy.
        var reopened = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await reopened.LoadAsync(token));
        Assert.Equal(Haven.Application.SpaceCapabilityCategoryState.Enabled, reopened.State.Draft.CategoryPolicy.Skills);
        var record = await files.OpenRegistry().GetAsync(space.Id, token);
        Assert.Single(record!.CapabilityBindings!);
        Assert.Equal(SpaceCapabilityOverride.Disabled, record.CapabilityBindings![0].Override);
        Assert.NotNull(record.MemoryPolicy);
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

    [Fact]
    public async Task Invalid_category_state_is_rejected()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var controller = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await controller.LoadAsync(token));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.SetCategoryState(SpaceBuilderCategory.Skills, (Haven.Application.SpaceCapabilityCategoryState)999));
    }

    [Fact]
    public async Task Invalid_persisted_category_state_fails_validation()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        // Bypass typed validation by constructing a definition with an invalid enum cast.
        var invalid = space with { CapabilityCategoryPolicy = new Haven.Application.SpaceCapabilityCategoryPolicy(
            (Haven.Application.SpaceCapabilityCategoryState)999) };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            files.OpenRegistry().UpdateAsync(invalid, space.Revision, token));
    }


    [Fact]
    public async Task Attach_and_detach_capability_preserves_canonical_identity()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var controller = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await controller.LoadAsync(token));

        var binding = new SpaceCapabilityBinding("pkg-1/skill-1", SpaceCapabilityKind.Skill, SpaceCapabilityOverride.Inherit, "1.2.3");
        Assert.True(controller.AttachCapability(binding));
        Assert.Single(controller.State.Draft.CapabilityBindings);
        Assert.Equal("pkg-1/skill-1", controller.State.Draft.CapabilityBindings[0].CapabilityId);
        Assert.Equal("1.2.3", controller.State.Draft.CapabilityBindings[0].VersionPin);

        // Duplicate attach is idempotent.
        Assert.True(controller.AttachCapability(binding));
        Assert.Single(controller.State.Draft.CapabilityBindings);

        Assert.True(controller.DetachCapability(SpaceCapabilityKind.Skill, "pkg-1/skill-1"));
        Assert.Empty(controller.State.Draft.CapabilityBindings);

        // Detach non-existent is a no-op success.
        Assert.True(controller.DetachCapability(SpaceCapabilityKind.Skill, "nonexistent"));
    }

    [Fact]
    public async Task Capability_bindings_survive_save_and_reopen()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var controller = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await controller.LoadAsync(token));

        var binding = new SpaceCapabilityBinding("plugin-abc", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Disabled, "2.0.0");
        Assert.True(controller.AttachCapability(binding));
        Assert.True(await controller.SaveAsync(token));

        var reopened = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await reopened.LoadAsync(token));
        Assert.Single(reopened.State.Draft.CapabilityBindings);
        Assert.Equal("plugin-abc", reopened.State.Draft.CapabilityBindings[0].CapabilityId);
        Assert.Equal(SpaceCapabilityOverride.Disabled, reopened.State.Draft.CapabilityBindings[0].Override);
        Assert.Equal("2.0.0", reopened.State.Draft.CapabilityBindings[0].VersionPin);
    }

}
