using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

// Actual registry/CAS/Builder/CUI controls. The native Home/Notes issuer and approval
// window have separate integration requirements; the display token below grants no IO.
public sealed class SpaceBuilderCanonicalSourceTests
{
    [Fact]
    public async Task Canonical_source_and_budget_save_on_same_space_without_losing_unexposed_configuration()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Sources", cancellationToken: token);
        space = await registry.UpdateAsync(space with { ModelPolicy = new("keep-model", ["Text"], "keep-provider"),
            LayoutDocument = new([new(Guid.NewGuid(), "Keep", "Authored logic")], []) }, space.Revision, token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        var reference = Reference();
        Assert.True(editor.AttachContextReference(reference, space.Revision));
        Assert.True(editor.SetField(SpaceBuilderField.SourceOnly, "true"));
        Assert.True(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "1"));
        Assert.Empty((await registry.GetAsync(space.Id, token))!.ContextReferences ?? []);
        Assert.True(await editor.SaveAsync(token));
        var reopened = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        var saved = Assert.Single(reopened.DraftConfiguration!.ContextReferences);
        Assert.Equal(reference, saved); Assert.Equal(space.Id, reopened.Id);
        Assert.Equal(SpaceContextPermission.Read, saved.Permission); Assert.Equal(SpaceContextIndexState.NotRequired, saved.IndexState);
        Assert.Equal(JsonSerializer.Serialize(space.ModelPolicy), JsonSerializer.Serialize(reopened.ModelPolicy));
        Assert.Equal(JsonSerializer.Serialize(space.LayoutDocument), JsonSerializer.Serialize(reopened.LayoutDocument));
        Assert.True(reopened.DraftConfiguration.ContextPolicy!.SourceOnly); Assert.Equal(1, reopened.DraftConfiguration.ContextPolicy.MaximumRetrievedItems);
    }

    [Fact]
    public async Task Files_reference_keeps_issued_identity_revision_and_digest_in_draft_without_changing_active_policy()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Managed Files reference", cancellationToken: token);
        var storeId = Guid.NewGuid(); var parentFolderId = Guid.NewGuid(); var fileId = Guid.NewGuid(); var revisionId = Guid.NewGuid();
        var digest = "sha256:" + new string('a', 64);
        var reference = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.File, "files",
            storeId.ToString("D") + "/" + fileId.ToString("D"), revisionId.ToString("D"), SpaceContextPermission.Read,
            SpaceContextIndexState.NotRequired, true, DateTimeOffset.UtcNow, fileId)
        { ContentSha256 = digest, ParentFolderId = parentFolderId };
        var editor = new SpaceBuilderController(registry, space.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.AttachContextReference(reference, space.Revision));
        Assert.Empty((await registry.GetAsync(space.Id, token))!.ContextReferences ?? []);
        Assert.True(await editor.SaveAsync(token));

        var reopened = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        var saved = Assert.Single(reopened.DraftConfiguration!.ContextReferences);
        Assert.Equal(reference, saved);
        Assert.Equal(digest, saved.ContentSha256);
        Assert.Equal(space.Id, reopened.Id);
        Assert.Empty(reopened.ContextReferences ?? []);
        Assert.Equal(space.ActiveConfigurationRevision, reopened.ActiveConfigurationRevision);
        var invalid = SpaceConfigurationDocument.From(reopened) with
        { ContextReferences = [reference with { ContentSha256 = "sha256:not-a-current-digest" }] };
        await Assert.ThrowsAsync<ArgumentException>(() => files.OpenRegistry().SaveDraftAsync(space.Id, invalid,
            reopened.Revision, reopened.DraftConfigurationRevision, reopened.ActiveConfigurationRevision, token));
        var unchanged = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        Assert.Equal(digest, Assert.Single(unchanged.DraftConfiguration!.ContextReferences).ContentSha256);
    }

    [Fact]
    public async Task Context_source_priority_survives_reload_and_controls_the_real_item_limit()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("Ordered sources", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id);
        Assert.True(await editor.LoadAsync(token));
        var first = Reference();
        var second = Reference() with { ContextId = Guid.NewGuid(), CanonicalEntityId = Guid.NewGuid().ToString("D") };
        Assert.True(editor.AttachContextReference(first, space.Revision));
        Assert.True(editor.AttachContextReference(second, space.Revision));
        Assert.True(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "1"));
        var draftRevision = editor.State.DraftRevision!.Value;
        var bindings = new SpaceBuilderCuiBindings(() => editor.State, (_, _) => { }, (_, _) => { }, (_, _) => { },
            () => [], () => [], (command, parameter, _) =>
            {
                if (parameter is SpaceBuilderContextOrderRequest request)
                    editor.MoveContextReference(request.SpaceId, request.DraftRevision, request.ContextId,
                        request.ExpectedIndex, command == "space-builder.source.move-up" ? -1 : 1);
                return ValueTask.CompletedTask;
            }, sourceOptions: () => []);
        Assert.True(bindings.TryGetValue("builder.contextReferences", out var projected));
        var rows = Assert.IsAssignableFrom<object[]>(projected);
        var moveUp = Assert.IsType<SpaceBuilderContextReferenceRow>(rows[1]);
        Assert.Equal(2, moveUp.order);
        Assert.True(moveUp.canMoveUp);
        Assert.False(moveUp.canMoveDown);
        await bindings.DispatchAsync("space-builder.source.move-up", moveUp.moveUp, token);
        Assert.Equal(second.ContextId, editor.State.Draft.ContextReferences[0].ContextId);
        await bindings.DispatchAsync("space-builder.source.move-up", moveUp.moveUp, token);
        Assert.Equal(second.ContextId, editor.State.Draft.ContextReferences[0].ContextId); // Stale index cannot reorder another current source.
        Assert.Empty((await registry.GetAsync(space.Id, token))!.ContextReferences ?? []); // Active policy is untouched before explicit activation.
        Assert.True(await editor.SaveAsync(token));

        var current = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        Assert.Equal(space.ActiveConfigurationRevision, current.ActiveConfigurationRevision);
        Assert.Empty(current.ContextReferences ?? []);
        var reopened = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await reopened.LoadAsync(token));
        Assert.Equal(new[] { second.ContextId, first.ContextId }, reopened.State.Draft.ContextReferences.Select(reference => reference.ContextId));
        var collection = new OriginalSpaceContextCollection(space.Id, "ordered source test",
            reopened.State.Draft.ContextReferences, 1, new OriginalSpaceContextBudget(4_096, 1));
        Assert.Equal(second.ContextId, Assert.Single(collection.RequestedSources).ContextId);
    }

    [Fact]
    public async Task Removing_legacy_reference_does_not_delete_or_read_the_original_file()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var path = Path.Combine(files.DataDirectory, "keep-original.txt"); await File.WriteAllTextAsync(path, "PRESERVE FILE BYTES", token);
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Migration", cancellationToken: token);
        space = await registry.AddFileAsync(space.Id, path, cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.DetachLegacyFileReference(path));
        Assert.Single((await registry.GetAsync(space.Id, token))!.Files); // Still only a draft edit.
        Assert.True(editor.AttachContextReference(Reference(), space.Revision)); Assert.True(await editor.SaveAsync(token));
        var saved = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        Assert.Single(saved.Files); // Active files remain until draft activation.
        Assert.Empty(saved.DraftConfiguration!.Files);
        Assert.True(File.Exists(path)); Assert.Equal("PRESERVE FILE BYTES", await File.ReadAllTextAsync(path, token));
    }

    [Fact]
    public async Task Conflicting_save_preserves_current_store_and_keeps_selected_source_in_draft()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("CAS", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        var reference = Reference(); Assert.True(editor.AttachContextReference(reference, space.Revision));
        await registry.UpdateAsync(space with { Description = "Other genuine update" }, space.Revision, token);
        Assert.False(await editor.SaveAsync(token)); Assert.True(editor.State.HasConflict);
        Assert.Equal(reference, Assert.Single(editor.State.Draft.ContextReferences));
        var current = (await files.OpenRegistry().GetAsync(space.Id, token))!;
        Assert.Equal("Other genuine update", current.Description); Assert.Empty(current.ContextReferences ?? []);
    }

    [Fact]
    public async Task Stale_choice_revision_and_invalid_budget_cannot_mutate_the_draft()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Stale choice", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        Assert.False(editor.AttachContextReference(Reference(), space.Revision + 1)); Assert.Empty(editor.State.Draft.ContextReferences);
        Assert.True(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "0"));
        Assert.False(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "-1"));
        Assert.False(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "1001"));
        Assert.False(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "all"));
        Assert.Equal(0, editor.State.Draft.ContextPolicy.MaximumRetrievedItems);
        Assert.True(editor.SetField(SpaceBuilderField.MaximumRetrievedItems, "")); Assert.Null(editor.State.Draft.ContextPolicy.MaximumRetrievedItems);
    }

    [Fact]
    public async Task Readding_same_notebook_updates_its_pin_without_duplicate_context_identity()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Version pin", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        var reference = Reference(); Assert.True(editor.AttachContextReference(reference, space.Revision));
        Assert.True(editor.AttachContextReference(reference with { ContextId = Guid.NewGuid(), RevisionToken = "8" }, space.Revision));
        var actual = Assert.Single(editor.State.Draft.ContextReferences);
        Assert.Equal(reference.ContextId, actual.ContextId); Assert.Equal("8", actual.RevisionToken);
        Assert.True(editor.DetachContextReference(actual.ContextId)); Assert.Empty(editor.State.Draft.ContextReferences);
        Assert.False(editor.DetachContextReference(Guid.NewGuid()));
        Assert.True(await editor.DiscardChangesAsync(token)); Assert.Empty(editor.State.Draft.ContextReferences);
    }

    [Fact]
    public async Task Context_inclusion_toggle_changes_only_automatic_use_not_source_identity_or_permission()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Context inclusion", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        var original = Reference(); Assert.True(editor.AttachContextReference(original, space.Revision));
        var bindings = new SpaceBuilderCuiBindings(() => editor.State,
            (field, value) => editor.SetField(field, value),
            (category, value) => editor.SetCategoryState(category, value),
            (kind, id) => editor.DetachCapability(kind, id),
            () => [], () => [], (_, _, _) => ValueTask.CompletedTask,
            setContextIncluded: (contextId, included) => editor.SetContextReferenceIncluded(contextId, included));
        Assert.True(bindings.TryGetValue("builder.contextReferences", out var rows));
        var row = Assert.IsType<SpaceBuilderContextReferenceRow>(Assert.Single(Assert.IsAssignableFrom<object[]>(rows)));
        Assert.True(row.included);

        Assert.True(bindings.TrySetItemValue(row, "included", false));
        var edited = Assert.Single(editor.State.Draft.ContextReferences);
        Assert.Equal(original.ContextId, edited.ContextId);
        Assert.Equal(original.Kind, edited.Kind);
        Assert.Equal(original.OwnerAppId, edited.OwnerAppId);
        Assert.Equal(original.CanonicalEntityId, edited.CanonicalEntityId);
        Assert.Equal(original.RevisionToken, edited.RevisionToken);
        Assert.Equal(original.Permission, edited.Permission);
        Assert.False(edited.IncludedAutomatically);
        Assert.False(bindings.TrySetItemValue(row, "included", true)); // Stale item is not admitted after draft refresh.

        Assert.True(await editor.SaveAsync(token));
        var reopened = new SpaceBuilderController(files.OpenRegistry(), space.Id);
        Assert.True(await reopened.LoadAsync(token));
        var persisted = Assert.Single(reopened.State.Draft.ContextReferences);
        Assert.Equal(original.ContextId, persisted.ContextId);
        Assert.Equal(original.OwnerAppId, persisted.OwnerAppId);
        Assert.Equal(original.CanonicalEntityId, persisted.CanonicalEntityId);
        Assert.Equal(original.RevisionToken, persisted.RevisionToken);
        Assert.Equal(SpaceContextPermission.Read, persisted.Permission);
        Assert.False(persisted.IncludedAutomatically);
    }

    [Fact]
    public async Task Named_CUI_source_action_forwards_exact_original_token_and_reference_remove_parameters()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Actual CUI", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        var reference = Reference(); Assert.True(editor.AttachContextReference(reference, space.Revision));
        var issued = new DisplayOnlyToken();
        var option = new SpaceBuilderBoardsSourceOption(reference.CanonicalEntityId, "Original notebook", reference.RevisionToken!, issued);
        var fileIssued = new DisplayOnlyFilesToken();
        var fileOption = new SpaceBuilderFilesSourceOption(Guid.NewGuid().ToString("D"), "Original file", "Files / Reports",
            Guid.NewGuid().ToString("D"), fileIssued);
        var calls = new List<(string Command, object? Parameter)>();
        var bindings = new SpaceBuilderCuiBindings(() => editor.State, (field, value) => editor.SetField(field, value),
            (category, value) => editor.SetCategoryState(category, value), (kind, id) => editor.DetachCapability(kind, id),
            () => [], () => [], (command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; },
            () => true, () => false, () => [option], () => true,
            filesOptions: () => [fileOption], canBrowseFiles: () => true);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader(); loader.SetBindingContext(bindings);
            var (tree, diagnostics) = loader.TryLoad(SpaceBuilderCuiDocument.Load()); Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
            var window = new Window { Content = root, Width = 1100, Height = 1000 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                await bindings.DispatchAsync("space-builder.view.advanced", null, token);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                // The authored Builder is vertically scrollable. Realized repeat rows below the
                // initial viewport are absent from the visual tree until their section is brought
                // into view, so exercise the actual row action after scrolling the rendered page.
                foreach (var scroll in root.GetVisualDescendants().OfType<ScrollViewer>())
                    scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                bool HasCuiId(Button button, string id) =>
                    button.Name == id || AutomationProperties.GetAutomationId(button) == id ||
                    CuiRuntimeIdentity.GetStableId(button)?.EndsWith("/id:" + id, StringComparison.Ordinal) == true;
                Button Find(string id) => root.GetVisualDescendants().OfType<Button>().Single(button => HasCuiId(button, id));
                var options = Assert.IsAssignableFrom<IReadOnlyList<SpaceBuilderBoardsSourceOption>>(GetBinding("builder.boardOptions"));
                Assert.Single(options);
                var matchingAdds = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => HasCuiId(button, "space-builder-attach-boards-source")).ToArray();
                Assert.True(matchingAdds.Length == 1,
                    $"Expected the single Boards source action in Advanced; found {matchingAdds.Length}. " +
                    $"Buttons=[{string.Join(", ", root.GetVisualDescendants().OfType<Button>().Select(button =>
                        $"{button.Name ?? "<unnamed>"}/{AutomationProperties.GetAutomationId(button) ?? "<no-id>"}/" +
                        $"{CuiRuntimeIdentity.GetStableId(button) ?? "<no-stable-id>"}"))}]");
                var add = matchingAdds[0]; Assert.True(add.IsEnabled);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var fileOptions = Assert.IsAssignableFrom<IReadOnlyList<SpaceBuilderFilesSourceOption>>(GetBinding("builder.filesOptions"));
                Assert.Same(fileOption, Assert.Single(fileOptions));
                var fileAdd = Find("space-builder-attach-files-source"); Assert.True(fileAdd.IsEnabled);
                fileAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var remove = Find("space-builder-remove-context-source");
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Equal("space-builder.source.attach", calls[0].Command); Assert.Same(issued, calls[0].Parameter);
                Assert.Equal("space-builder.files.attach", calls[1].Command); Assert.Same(fileIssued, calls[1].Parameter);
                Assert.Equal(("space-builder.source.remove", (object?)reference.ContextId.ToString("D")), calls[2]);
            }
            finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;

            object? GetBinding(string path)
            {
                Assert.True(bindings.TryGetValue(path, out var value));
                return value;
            }
        }, token);
    }

    private sealed class DisplayOnlyToken : ISpaceBuilderOriginalBoardsSourceChoice { }
    private sealed class DisplayOnlyFilesToken : ISpaceBuilderOriginalFilesSourceChoice { }
    private static SpaceContextReference Reference() => new(Guid.NewGuid(), SpaceContextReferenceKind.BoardPage,
        "boards", Guid.NewGuid().ToString("D"), "7", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired,
        true, DateTimeOffset.UtcNow);
}
