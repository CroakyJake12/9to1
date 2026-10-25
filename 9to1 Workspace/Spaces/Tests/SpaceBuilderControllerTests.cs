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
        Assert.Equal("Original", saved!.Name);
        Assert.Equal("Updated", saved.DraftConfiguration!.Name);
        Assert.Equal("New purpose", saved.DraftConfiguration.Description);
        Assert.Equal(Haven.Application.SpaceCapabilityCategoryState.Enabled, saved.DraftConfiguration.CapabilityCategoryPolicy!.Skills);
        // Hidden policy must survive a Basic save.
        Assert.NotNull(saved.CapabilityBindings);
        Assert.False(controller.State.HasChanges);
        // Active revision must NOT advance on an ordinary (unvalidated) save.
        Assert.Equal(space.Revision, saved.Revision);
        Assert.Equal(space.ActiveConfigurationRevision, saved.ActiveConfigurationRevision);
        Assert.True(controller.State.CanActivate);
        Assert.True(await controller.ActivateAsync(token));
        saved = await files.OpenRegistry().GetAsync(space.Id, token);
        Assert.Equal("Updated", saved!.Name);
        Assert.Equal("New purpose", saved.Description);
        Assert.Equal(Haven.Application.SpaceCapabilityCategoryState.Enabled, saved.CapabilityCategoryPolicy!.Skills);
        Assert.Null(saved.DraftConfiguration);
        Assert.Equal(space.ActiveConfigurationRevision + 1, saved.ActiveConfigurationRevision);
        Assert.Equal(space.Revision + 1, saved.Revision);
        Assert.Equal("Updated", controller.State.Draft.Name);
        // Discard re-reads the canonical record without deleting the draft conflict path.
        Assert.True(controller.SetField(SpaceBuilderField.Name, "Unsaved"));
        Assert.True(controller.State.HasChanges);
        Assert.True(await controller.DiscardChangesAsync(token));
        Assert.Equal("Updated", controller.State.Draft.Name);
        Assert.False(controller.State.HasChanges);
    }

    [Fact]
    public async Task Registered_user_message_graph_activates_only_after_shared_compile_and_keeps_space_identity()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Executable graph", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id);
        Assert.True(await editor.LoadAsync(token));
        var layout = UserMessageGraph();
        Assert.True(editor.SetLayoutDocument(layout));
        Assert.True(await editor.SaveAsync(token));
        Assert.True(editor.State.CanActivate);
        Assert.True(await editor.ActivateAsync(token));

        var active = (await registry.GetAsync(space.Id, token))!;
        Assert.Null(active.DraftConfiguration);
        Assert.Equal(space.Id, active.Id);
        Assert.Equal(space.Revision + 1, active.Revision);
        Assert.Equal(space.ActiveConfigurationRevision + 1, active.ActiveConfigurationRevision);
        Assert.Equal(layout.GraphId, active.LayoutDocument!.GraphId);
        Assert.Equal(SpaceLayoutRevisionState.Active, active.LayoutDocument.State);
        var compiled = SpaceUserMessageGraph.Compile(active.Id, active.Revision, active.LayoutDocument);
        Assert.Equal(active.Revision, compiled.SpaceRevision);
        Assert.Equal(layout.GraphId, compiled.GraphId);

        var rejectedSpace = await registry.CreateAsync("Unsupported graph", cancellationToken: token);
        var rejected = new SpaceBuilderController(registry, rejectedSpace.Id);
        Assert.True(await rejected.LoadAsync(token));
        var unsupportedBase = UserMessageGraph();
        var unsupportedLayout = unsupportedBase with
        {
            Nodes = unsupportedBase.Nodes.Select(node => node with { TypeId = "spaces.user-message.unknown" }).ToArray()
        };
        Assert.True(rejected.SetLayoutDocument(unsupportedLayout));
        Assert.True(await rejected.SaveAsync(token));
        Assert.False(rejected.State.CanActivate);
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => registry.ActivateDraftAsync(rejectedSpace.Id,
            rejected.State.SpaceRevision!.Value, rejected.State.DraftRevision!.Value,
            rejected.State.ActiveRevision!.Value, token));
        var preserved = (await registry.GetAsync(rejectedSpace.Id, token))!;
        Assert.Equal(rejectedSpace.ActiveConfigurationRevision, preserved.ActiveConfigurationRevision);
        Assert.Null(preserved.LayoutDocument);
        Assert.NotNull(preserved.DraftConfiguration);
    }

    private static SpaceLayoutDocument UserMessageGraph()
    {
        var source = Guid.NewGuid(); var truncate = Guid.NewGuid(); var output = Guid.NewGuid();
        static SpaceLayoutPort Port(string id, SpaceLayoutPortDirection direction) => new(id, id, direction, "string", false);
        return new SpaceLayoutDocument(
            [
                new(source, "Space User Message", "Source")
                {
                    TypeId = SpaceUserMessageGraph.SourceType,
                    SchemaVersion = 1,
                    Ports = [Port("out", SpaceLayoutPortDirection.Output)]
                },
                new(truncate, "Space User Message", "Truncate")
                {
                    TypeId = SpaceUserMessageGraph.TruncateType,
                    SchemaVersion = 1,
                    Ports = [Port("in", SpaceLayoutPortDirection.Input), Port("out", SpaceLayoutPortDirection.Output)],
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["maximumTextElements"] = "256" }
                },
                new(output, "Space User Message", "Output")
                {
                    TypeId = SpaceUserMessageGraph.OutputType,
                    SchemaVersion = 1,
                    Ports = [Port("in", SpaceLayoutPortDirection.Input)]
                }
            ],
            [
                new(Guid.NewGuid(), source, "out", truncate, "in"),
                new(Guid.NewGuid(), truncate, "out", output, "in")
            ])
        {
            GraphId = Guid.NewGuid(),
            SchemaVersion = SpaceLayoutDocument.CurrentSchemaVersion,
            Revision = 1,
            State = SpaceLayoutRevisionState.Draft,
            ConnectionProfileId = SpaceUserMessageGraph.ProfileId
        };
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
    public async Task Draft_save_preserves_active_revision_and_stale_active_cas_cannot_publish_it()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Active version", cancellationToken: token);
        var controller = new SpaceBuilderController(registry, space.Id);
        Assert.True(await controller.LoadAsync(token));
        Assert.True(controller.SetField(SpaceBuilderField.Name, "Draft version"));
        Assert.True(await controller.SaveAsync(token));
        var savedDraft = (await registry.GetAsync(space.Id, token))!;
        Assert.Equal(space.Revision, savedDraft.Revision);
        Assert.Equal(space.ActiveConfigurationRevision, savedDraft.ActiveConfigurationRevision);
        Assert.Equal("Active version", savedDraft.Name);
        Assert.Equal("Draft version", savedDraft.DraftConfiguration!.Name);

        var independentlyChanged = await registry.UpdateAsync(savedDraft with { Description = "Live update" },
            savedDraft.Revision, token);
        Assert.Equal("Draft version", independentlyChanged.DraftConfiguration!.Name);
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() => registry.ActivateDraftAsync(space.Id,
            savedDraft.Revision, savedDraft.DraftConfigurationRevision, savedDraft.ActiveConfigurationRevision, token));
        var afterConflict = (await registry.GetAsync(space.Id, token))!;
        Assert.Equal("Active version", afterConflict.Name);
        Assert.Equal("Live update", afterConflict.Description);
        Assert.Equal("Draft version", afterConflict.DraftConfiguration!.Name);
    }

    [Fact]
    public async Task Draft_validation_reserves_names_used_by_other_saved_drafts()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var firstSpace = await registry.CreateAsync("First", cancellationToken: token);
        var secondSpace = await registry.CreateAsync("Second", cancellationToken: token);
        var first = new SpaceBuilderController(registry, firstSpace.Id);
        var second = new SpaceBuilderController(registry, secondSpace.Id);
        Assert.True(await first.LoadAsync(token));
        Assert.True(first.SetField(SpaceBuilderField.Name, "Reserved by draft"));
        Assert.True(await first.SaveAsync(token));

        Assert.True(await second.LoadAsync(token));
        Assert.True(second.SetField(SpaceBuilderField.Name, "Reserved by draft"));
        var issues = await second.ValidateDraftAsync(token);
        Assert.Contains(issues, issue => issue.Contains("saved draft", StringComparison.OrdinalIgnoreCase));
        Assert.False(await second.SaveAsync(token));
        Assert.Contains("active configuration was preserved", second.State.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Second", (await registry.GetAsync(secondSpace.Id, token))!.Name);
        Assert.Null((await registry.GetAsync(secondSpace.Id, token))!.DraftConfiguration);
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

    [Fact]
    public async Task Basic_advanced_and_expert_edit_one_space_and_preserve_the_graph_draft()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("One Space", cancellationToken: token);
        var graphId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var originalGraph = new SpaceLayoutDocument(
            [new SpaceLayoutNode(nodeId, "Logic", "Existing rule")
            {
                TypeId = "spaces.logic.identity",
                SchemaVersion = 2,
                Metadata = new Dictionary<string, string> { ["source"] = "author" }
            }], [])
        {
            GraphId = graphId,
            SchemaVersion = SpaceLayoutDocument.CurrentSchemaVersion,
            Revision = 4,
            Groups = [new SpaceLayoutGroup(Guid.NewGuid(), "Existing group", [nodeId])],
            Comments = [new SpaceLayoutComment(Guid.NewGuid(), "Keep this note", 12, 18)]
        };
        space = await files.OpenRegistry().UpdateAsync(space with { LayoutDocument = originalGraph }, space.Revision, token);

        var editor = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await editor.LoadAsync(token));
        var originalRevision = editor.State.DraftRevision;
        editor.SelectEditorView(SpaceBuilderEditorView.Advanced);
        Assert.Equal(space.Id, editor.State.SpaceId);
        Assert.Equal(originalRevision, editor.State.DraftRevision);
        editor.SetField(SpaceBuilderField.Description, "Changed in Advanced");
        editor.SelectExpertSection(SpaceBuilderExpertSection.ModelLogic);
        var changedGraph = originalGraph with
        {
            Nodes = [.. originalGraph.Nodes, new SpaceLayoutNode(Guid.NewGuid(), "Condition", "New condition")]
        };
        Assert.True(editor.SetLayoutDocument(changedGraph));
        Assert.Equal(graphId, editor.State.Draft.LayoutDocument!.GraphId);
        Assert.Equal(SpaceBuilderEditorView.Expert, editor.State.EditorView);
        Assert.Equal(originalRevision, editor.State.DraftRevision);
        Assert.True(await editor.SaveAsync(token));

        var reopened = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await reopened.LoadAsync(token));
        var persisted = reopened.State.Draft.LayoutDocument!;
        Assert.Equal(space.Id, reopened.State.SpaceId);
        Assert.Equal(graphId, persisted.GraphId);
        Assert.Equal(5, persisted.Revision);
        Assert.Equal(2, persisted.Nodes.Count);
        Assert.Equal("spaces.logic.identity", persisted.Nodes[0].TypeId);
        Assert.Equal("author", persisted.Nodes[0].Metadata["source"]);
        var persistedGroup = Assert.Single(persisted.Groups);
        Assert.Equal("Existing group", persistedGroup.Title);
        Assert.Equal(new[] { nodeId }, persistedGroup.NodeIds);
        Assert.Equal(originalGraph.Comments.Single(), persisted.Comments.Single());
        Assert.Equal("Changed in Advanced", reopened.State.Draft.Description);
    }

    [Fact]
    public async Task Preview_and_validation_use_the_draft_without_changing_the_saved_space()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Preview target", cancellationToken: token);
        var editor = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.SetField(SpaceBuilderField.Description, "Draft description"));
        var originalRevision = editor.State.DraftRevision;
        var originalActiveRevision = editor.State.ActiveRevision;

        var preview = editor.PreviewDraft();
        Assert.NotNull(preview);
        Assert.Equal(space.Id, preview!.SpaceId);
        Assert.Equal(originalRevision, preview.DraftRevision);
        Assert.Equal("Draft description", preview.Description);
        Assert.Contains("no conversation", preview.Notice);
        var valid = await editor.ValidateDraftAsync(token);
        Assert.Single(valid);
        Assert.StartsWith("Draft structure is valid", valid[0]);
        var persisted = await files.OpenRegistry().GetAsync(space.Id, token);
        Assert.Equal(space.Revision, persisted!.Revision);
        Assert.Equal(space.Description, persisted.Description);
        Assert.Equal(originalActiveRevision, editor.State.ActiveRevision);

        var nodeId = Guid.NewGuid();
        Assert.True(editor.SetLayoutDocument(new SpaceLayoutDocument(
            [new SpaceLayoutNode(nodeId, "Logic", "Broken")],
            [new SpaceLayoutEdge(Guid.NewGuid(), nodeId, "missing-output", Guid.NewGuid(), "missing-input")])));
        var invalid = await editor.ValidateDraftAsync(token);
        Assert.Contains(invalid, issue => issue.StartsWith("Graph validation failed", StringComparison.Ordinal));
        Assert.Null(editor.State.Preview); // every later edit clears stale preview output
    }

    [Fact]
    public async Task Owner_validation_checks_unsaved_projection_without_saving_or_publishing()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Active name", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.SetField(SpaceBuilderField.Name, "Unsaved candidate"));
        var validator = new InspectingDraftValidator(registry);

        var issues = await editor.ValidateDraftAsync(validator, body => body(), _ => { }, token);

        Assert.Single(issues);
        Assert.StartsWith("Draft structure and current", issues[0]);
        Assert.Equal("Unsaved candidate", validator.ValidatedDraft!.Name);
        var after = (await registry.GetAsync(space.Id, token))!;
        Assert.Equal(space.Revision, after.Revision);
        Assert.Equal(space.ActiveConfigurationRevision, after.ActiveConfigurationRevision);
        Assert.Equal(space.DraftConfigurationRevision, after.DraftConfigurationRevision);
        Assert.Null(after.DraftConfiguration);
        Assert.Equal("Active name", after.Name);
        Assert.Equal("Unsaved candidate", editor.State.Draft.Name);
    }

    [Fact]
    public async Task Owner_validation_result_is_discarded_if_draft_changes_while_check_is_running()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Active name", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.SetField(SpaceBuilderField.Name, "First unsaved candidate"));
        var validator = new InspectingDraftValidator(registry, hold: true);

        var validationTask = editor.ValidateDraftAsync(validator, body => body(), _ => { }, token);
        await validator.Started.Task.WaitAsync(token);
        Assert.True(editor.SetField(SpaceBuilderField.Description, "Changed during validation"));
        validator.Release.TrySetResult();
        var issues = await validationTask;

        Assert.Contains(issues, issue => issue.Contains("changed while validation was running", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("First unsaved candidate", validator.ValidatedDraft!.Name);
        Assert.Equal("Changed during validation", editor.State.Draft.Description);
        Assert.Contains("changed while validation was running", editor.State.ValidationIssues.Single(), StringComparison.OrdinalIgnoreCase);
        var after = (await registry.GetAsync(space.Id, token))!;
        Assert.Equal(space.Revision, after.Revision);
        Assert.Null(after.DraftConfiguration);
        Assert.Equal("Active name", after.Name);
    }

    [Fact]
    public async Task Parent_policy_change_after_validation_keeps_the_saved_draft_and_active_configuration()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var parent = await registry.CreateAsync("Parent", cancellationToken: token);
        var child = await registry.CreateAsync("Child", null, parent.Id, token);
        var draft = SpaceConfigurationDocument.From(child) with { Name = "Child draft" };
        var savedDraft = await registry.SaveDraftAsync(child.Id, draft, child.Revision,
            child.DraftConfigurationRevision, child.ActiveConfigurationRevision, token);
        var validator = new ParentRacingActivationValidator(registry, parent.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ActivateDraftAsync(savedDraft.Id,
            savedDraft.Revision, savedDraft.DraftConfigurationRevision, savedDraft.ActiveConfigurationRevision,
            validator, body => body(), _ => { }, token));

        var unchangedChild = (await registry.GetAsync(child.Id, token))!;
        var changedParent = (await registry.GetAsync(parent.Id, token))!;
        Assert.Equal("Child", unchangedChild.Name);
        Assert.Equal("Child draft", unchangedChild.DraftConfiguration!.Name);
        var validatedParent = Assert.Single(validator.ValidatedAncestry, revision => revision.SpaceId == parent.Id);
        Assert.NotEqual(validatedParent.SpaceRevision, changedParent.Revision);
    }

    private sealed class ParentRacingActivationValidator(SpaceRegistry registry, Guid parentId)
        : ISpaceDraftActivationValidator
    {
        public IReadOnlyList<SpaceActivationAncestryRevision> ValidatedAncestry { get; private set; } = [];

        public async Task<SpaceDraftActivationValidation> ValidateAsync(SpaceDefinition current,
            SpaceConfigurationDocument draft, Action<Action> acquireOriginal, Action<Task> retainOriginal,
            CancellationToken cancellationToken = default)
        {
            var resolved = await new SpacePolicyResolver(registry).ResolveDraftAsync(current, draft,
                acquireOriginal, retainOriginal, cancellationToken);
            ValidatedAncestry = resolved.Ancestry;
            await registry.RenameAsync(parentId, "Parent changed after validation", cancellationToken);
            return new(current.Id, current.Revision, current.DraftConfigurationRevision,
                current.ActiveConfigurationRevision, ValidatedAncestry);
        }
    }

    private sealed class InspectingDraftValidator(SpaceRegistry registry, bool hold = false)
        : ISpaceDraftActivationValidator
    {
        public SpaceConfigurationDocument? ValidatedDraft { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SpaceDraftActivationValidation> ValidateAsync(SpaceDefinition current,
            SpaceConfigurationDocument draft, Action<Action> acquireOriginal, Action<Task> retainOriginal,
            CancellationToken cancellationToken = default)
        {
            ValidatedDraft = draft;
            Started.TrySetResult();
            if (hold) await Release.Task.WaitAsync(cancellationToken);
            var resolved = await new SpacePolicyResolver(registry).ResolveDraftAsync(current, draft,
                acquireOriginal, retainOriginal, cancellationToken);
            return new(current.Id, current.Revision, current.DraftConfigurationRevision,
                current.ActiveConfigurationRevision, resolved.Ancestry);
        }
    }

}
