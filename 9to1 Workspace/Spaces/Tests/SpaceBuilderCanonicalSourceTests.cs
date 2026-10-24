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
        var saved = Assert.Single(reopened.ContextReferences!);
        Assert.Equal(reference, saved); Assert.Equal(space.Id, reopened.Id);
        Assert.Equal(SpaceContextPermission.Read, saved.Permission); Assert.Equal(SpaceContextIndexState.NotRequired, saved.IndexState);
        Assert.Equal(JsonSerializer.Serialize(space.ModelPolicy), JsonSerializer.Serialize(reopened.ModelPolicy));
        Assert.Equal(JsonSerializer.Serialize(space.LayoutDocument), JsonSerializer.Serialize(reopened.LayoutDocument));
        Assert.True(reopened.ContextPolicy!.SourceOnly); Assert.Equal(1, reopened.ContextPolicy.MaximumRetrievedItems);
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
        Assert.Empty((await files.OpenRegistry().GetAsync(space.Id, token))!.Files);
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
    public async Task Named_CUI_source_action_forwards_exact_original_token_and_reference_remove_parameters()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        var registry = files.OpenRegistry(); var space = await registry.CreateAsync("Actual CUI", cancellationToken: token);
        var editor = new SpaceBuilderController(registry, space.Id); Assert.True(await editor.LoadAsync(token));
        var reference = Reference(); Assert.True(editor.AttachContextReference(reference, space.Revision));
        var issued = new DisplayOnlyToken();
        var option = new SpaceBuilderBoardsSourceOption(reference.CanonicalEntityId, "Original notebook", reference.RevisionToken!, issued);
        var calls = new List<(string Command, object? Parameter)>();
        var bindings = new SpaceBuilderCuiBindings(() => editor.State, (field, value) => editor.SetField(field, value),
            (category, value) => editor.SetCategoryState(category, value), (kind, id) => editor.DetachCapability(kind, id),
            () => [], () => [], (command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; },
            () => true, () => false, () => [option], () => true);
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
                Button Find(string id) => root.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.Name == id || AutomationProperties.GetAutomationId(button) == id);
                var add = Find("space-builder-attach-boards-source"); Assert.True(add.IsEnabled);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var remove = Find("space-builder-remove-context-source");
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Equal("space-builder.source.attach", calls[0].Command); Assert.Same(issued, calls[0].Parameter);
                Assert.Equal(("space-builder.source.remove", (object?)reference.ContextId.ToString("D")), calls[1]);
            }
            finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, token);
    }

    private sealed class DisplayOnlyToken : ISpaceBuilderOriginalBoardsSourceChoice { }
    private static SpaceContextReference Reference() => new(Guid.NewGuid(), SpaceContextReferenceKind.BoardPage,
        "boards", Guid.NewGuid().ToString("D"), "7", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired,
        true, DateTimeOffset.UtcNow);
}
