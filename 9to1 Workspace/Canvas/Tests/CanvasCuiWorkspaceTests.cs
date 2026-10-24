using CakeOS.Cui;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasCuiWorkspaceTests
{
    [Fact]
    public void Compact_document_pills_toggle_existing_commands_and_canonical_document_information()
    {
        var dispatchCount = 0;
        CanvasWorkspaceCommand? dispatched = null;
        using var workspace = new CanvasCuiWorkspace((command, _) =>
        {
            dispatchCount++;
            dispatched = command;
            return ValueTask.CompletedTask;
        }, _ => true);
        var artifact = CanvasArtifact.Create("Project notebook", CanvasDocumentMode.Paged);
        workspace.Refresh(artifact, "Opened canonical artifact", documentLocation: new("9to1 Drive", "canvas-folder-id"));

        Assert.True(workspace.TryGetValue("CanToggleCommandSurface", out var canToggleCommands));
        Assert.Equal(true, canToggleCommands);
        Assert.True(workspace.TryGetValue("CanToggleDocumentInfo", out var canToggleInfo));
        Assert.Equal(true, canToggleInfo);
        Assert.True(workspace.TryGetValue("DisplayName", out var name));
        Assert.Equal("Project notebook", name);
        Assert.True(workspace.TryGetValue("CanvasMode", out var mode));
        Assert.Equal(nameof(CanvasDocumentMode.Paged), mode);
        Assert.True(workspace.TryGetValue("PageCount", out var pageCount));
        Assert.Equal(1, pageCount);
        Assert.True(workspace.TryGetValue("ArtifactId", out var artifactId));
        Assert.Equal(artifact.ArtifactId, artifactId);
        Assert.True(workspace.TryGetValue("RevisionId", out var revisionId));
        Assert.Equal(artifact.RevisionId, revisionId);
        Assert.True(workspace.TryGetValue("FilesLocationName", out var locationName));
        Assert.Equal("9to1 Drive", locationName);
        Assert.True(workspace.TryGetValue("ParentFolderId", out var folderId));
        Assert.Equal("canvas-folder-id", folderId);
        Assert.True(workspace.TryGetValue("DocumentInfoAccessibleName", out var accessibleName));
        Assert.Contains("Project notebook", Assert.IsType<string>(accessibleName));

        workspace.DispatchAsync("9to1.Canvas.DocumentMenu.Toggle", null).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("CommandSurfaceVisible", out var commandVisible));
        Assert.Equal(true, commandVisible);
        workspace.DispatchAsync("9to1.Canvas.DocumentInfo.Toggle", null).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("CommandSurfaceVisible", out commandVisible));
        Assert.Equal(false, commandVisible);
        Assert.True(workspace.TryGetValue("DocumentInfoVisible", out var infoVisible));
        Assert.Equal(true, infoVisible);
        Assert.Equal(0, dispatchCount); // Local disclosure actions do not invoke document/Home mutations.

        workspace.DispatchAsync("9to1.Canvas.DocumentMenu.Toggle", null).GetAwaiter().GetResult();
        workspace.DispatchAsync("9to1.Canvas.View.Fit", null).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("CommandSurfaceVisible", out commandVisible));
        Assert.Equal(false, commandVisible);
        Assert.Equal(CanvasWorkspaceCommandKind.FitView, dispatched!.Kind);
        Assert.Equal(1, dispatchCount);

        var shell = WorkspaceShell();
        var controls = shell.Children.Single(component => component.AuthoredId == "canvas-document-controls");
        Assert.Equal(2, controls.Children[0].Children.Count); // Only the compact menu and name pills remain in the header row.
        var commandSurface = Descendants(shell).Single(component => component.AuthoredId == "canvas-command-surface");
        Assert.Equal("CommandSurfaceVisible", Assert.IsType<CuiBindingValue>(commandSurface.Properties["Active"]).Path);
        var documentInfo = Descendants(shell).Single(component => component.AuthoredId == "canvas-document-info");
        Assert.Equal("DocumentInfoVisible", Assert.IsType<CuiBindingValue>(documentInfo.Properties["Active"]).Path);
    }

    [Fact]
    public void Accessible_object_list_projects_canonical_names_alt_order_and_geometry_and_pages_without_mutation()
    {
        var pageId = Guid.NewGuid(); var layerId = Guid.NewGuid();
        var stroke = new CanvasInkStroke
        {
            LayerId = layerId,
            Samples = [new(4, 5), new(9, 12)]
        };
        var objects = Enumerable.Range(0, 51).Select(index => new CanvasObject
        {
            ObjectTypeId = "canvas.shape.rectangle", LayerId = layerId,
            Geometry = new(index, index + 1, 20, 10),
            Accessibility = new() { Name = index == 0 ? "Diagram" : $"Shape {index + 1}",
                AltText = index == 0 ? "A sample diagram" : null, Description = index == 0 ? "Project overview" : null }
        }).ToList();
        var artifact = CanvasArtifact.Create("Accessible content");
        artifact.Pages = [new CanvasPage
        {
            PageId = pageId, Layers = [new CanvasLayer { LayerId = layerId, Name = "Foreground" }],
            LayerOrder = [layerId], Objects = objects, ObjectOrder = objects.Select(value => value.ObjectId).ToList(),
            Strokes = [stroke], StrokeOrder = [stroke.StrokeId]
        }];
        artifact.PageOrder = [pageId];
        var workspace = new CanvasCuiWorkspace((_, _) => throw new InvalidOperationException("Inspection is read-only."), _ => true);
        workspace.Refresh(artifact, "Opened canonical artifact");

        Assert.True(workspace.TryGetValue("AccessibleObjects", out var firstValue));
        var firstPage = Assert.IsAssignableFrom<IReadOnlyList<CanvasAccessibleObjectRow>>(firstValue);
        Assert.Equal(50, firstPage.Count);
        Assert.True(workspace.TryGetValue("AccessiblePageSummary", out var firstPageSummary));
        Assert.Equal("Objects 1–50 of 52", firstPageSummary);
        Assert.True(workspace.TryGetValue("CanToggleAccessibleList", out var canToggle));
        Assert.Equal(true, canToggle);
        Assert.False(workspace.IsActionAvailable("9to1.Canvas.Accessibility.Inspect"));
        workspace.DispatchAsync("9to1.Canvas.Accessibility.ToggleObjects", null).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("AccessibleListVisible", out var visible));
        Assert.Equal(true, visible);
        var first = Assert.Single(firstPage, row => row.Id == objects[0].ObjectId.ToString("D"));
        Assert.Equal("Page 1: Diagram", first.AccessibleName);
        Assert.Equal("canvas.shape.rectangle", first.Type);
        Assert.Contains("alt text: A sample diagram", first.Details);
        Assert.Contains("object order 1", first.Details);
        Assert.Contains("layer order 1", first.Details);
        Assert.Contains("position (0, 1)", first.Details);
        Assert.True(workspace.IsActionAvailable("9to1.Canvas.Accessibility.Inspect"));
        workspace.DispatchAsync("9to1.Canvas.Accessibility.Inspect", first.Id).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("AccessibleInspectionSummary", out var inspected));
        Assert.Contains("A sample diagram", Assert.IsType<string>(inspected));
        workspace.Refresh(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact)), "Transient tool state changed");
        Assert.True(workspace.TryGetValue("AccessibleObjects", out var sameRevisionValue));
        var sameRevisionPage = Assert.IsAssignableFrom<IReadOnlyList<CanvasAccessibleObjectRow>>(sameRevisionValue);
        Assert.Same(first, Assert.Single(sameRevisionPage, row => row.Id == first.Id));
        Assert.True(workspace.IsActionAvailable("9to1.Canvas.Accessibility.NextPage"));

        workspace.DispatchAsync("9to1.Canvas.Accessibility.NextPage", null).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("AccessibleObjects", out var secondValue));
        var secondPage = Assert.IsAssignableFrom<IReadOnlyList<CanvasAccessibleObjectRow>>(secondValue);
        Assert.Equal(2, secondPage.Count);
        Assert.NotSame(firstPage, secondPage);
        Assert.Equal(objects[50].ObjectId.ToString("D"), secondPage[0].Id);
        var strokeRow = Assert.Single(secondPage, row => row.Id == stroke.StrokeId.ToString("D"));
        Assert.Equal("Freehand ink", strokeRow.Type);
        Assert.Contains("stroke order 1", strokeRow.Details);
        Assert.Contains("2 samples", strokeRow.Details);
        workspace.DispatchAsync("9to1.Canvas.Accessibility.Inspect", strokeRow.Id).GetAwaiter().GetResult();
        Assert.True(workspace.TryGetValue("AccessibleInspectionSummary", out inspected));
        Assert.Contains("Ink stroke 1", Assert.IsType<string>(inspected));
        Assert.True(workspace.TryGetValue("AccessiblePageSummary", out var secondPageSummary));
        Assert.Equal("Objects 51–52 of 52", secondPageSummary);
        Assert.True(workspace.IsActionAvailable("9to1.Canvas.Accessibility.PreviousPage"));
        Assert.Throws<InvalidOperationException>(() => workspace.DispatchAsync("9to1.Canvas.Accessibility.Inspect", first.Id));
        workspace.DispatchAsync("9to1.Canvas.Accessibility.PreviousPage", null).GetAwaiter().GetResult();

        var changedArtifact = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact));
        changedArtifact.RevisionId = Guid.NewGuid();
        changedArtifact.Pages[0] = changedArtifact.Pages[0] with
        {
            Objects = [changedArtifact.Pages[0].Objects[0] with { Accessibility = new() { Name = "Updated diagram" } }, .. changedArtifact.Pages[0].Objects.Skip(1)]
        };
        workspace.Refresh(changedArtifact, "New canonical revision");
        Assert.True(workspace.TryGetValue("AccessibleObjects", out var newRevisionValue));
        var newRevisionPage = Assert.IsAssignableFrom<IReadOnlyList<CanvasAccessibleObjectRow>>(newRevisionValue);
        var updatedRow = Assert.Single(newRevisionPage, row => row.Id == objects[0].ObjectId.ToString("D"));
        Assert.Equal("Page 1: Updated diagram", updatedRow.AccessibleName);
        Assert.NotSame(first, updatedRow);

        var shell = WorkspaceShell();
        Assert.Contains(shell.Children, component => component.AuthoredId == "canvas-accessible-objects");
        Assert.Contains(shell.Children, component => component.AuthoredId == "canvas-accessible-objects-toggle");
        workspace.Refresh(null, "No artifact open");
        Assert.True(workspace.TryGetValue("CanToggleAccessibleList", out canToggle));
        Assert.Equal(false, canToggle);
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync("9to1.Canvas.Accessibility.ToggleObjects", null));
        workspace.Dispose();
    }

    [Theory]
    [InlineData("canvas-fit", "CanFitView", "9to1.Canvas.View.Fit", CanvasWorkspaceCommandKind.FitView)]
    [InlineData("canvas-zoom-in", "CanZoomIn", "9to1.Canvas.View.ZoomIn", CanvasWorkspaceCommandKind.ZoomIn)]
    [InlineData("canvas-zoom-out", "CanZoomOut", "9to1.Canvas.View.ZoomOut", CanvasWorkspaceCommandKind.ZoomOut)]
    public async Task View_control_bindings_follow_live_host_availability_and_keep_the_captured_artifact(
        string controlId, string binding, string action, CanvasWorkspaceCommandKind kind)
    {
        var hostAllowsView = true;
        CanvasWorkspaceCommand? captured = null;
        using var workspace = new CanvasCuiWorkspace((command, _) =>
        {
            captured = command;
            return ValueTask.CompletedTask;
        }, command => hostAllowsView && command == kind);
        var artifact = CanvasArtifact.Create("Revision notes");
        workspace.Refresh(artifact, "Opened canonical artifact");

        // Exercise the actual authored control through the canonical evaluator.
        // An unrecognised binding discards its computed out value.
        var shell = WorkspaceShell();
        var controls = shell.Children.Single(component => component.AuthoredId == "canvas-document-controls");
        var control = Descendants(controls).Single(component => component.AuthoredId == controlId);
        var authoredBinding = Assert.IsType<CuiBindingValue>(control.Properties["Interactive"]);
        Assert.Equal(binding, authoredBinding.Path);
        Assert.True(workspace.TryGetValue(binding, out var available));
        Assert.Equal(true, available);
        Assert.Equal(true, CuiBindingEvaluator.Evaluate(authoredBinding, workspace));
        await workspace.DispatchAsync(action, null);
        Assert.NotNull(captured);
        Assert.Equal(kind, captured.Kind);
        Assert.Equal(artifact.ArtifactId, captured.ArtifactId);
        Assert.Equal(artifact.RevisionId, captured.BaseRevisionId);

        // Host revocation must still disable the recognised control and prevent
        // action dispatch; recognising the name supplies no authority.
        hostAllowsView = false;
        workspace.RefreshAvailability();
        Assert.True(workspace.TryGetValue(binding, out available));
        Assert.Equal(false, available);
        Assert.Equal(false, CuiBindingEvaluator.Evaluate(authoredBinding, workspace));
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync(action, null));

        // No open artifact remains unavailable even if the host allows viewing.
        hostAllowsView = true;
        workspace.Refresh(null, "No artifact open");
        Assert.True(workspace.TryGetValue(binding, out available));
        Assert.Equal(false, available);
        Assert.Equal(false, CuiBindingEvaluator.Evaluate(authoredBinding, workspace));
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync(action, null));
        Assert.False(workspace.TryGetValue("UnknownViewBinding", out _));
    }

    [Fact]
    public async Task Embedded_scene_and_dispatch_keep_the_target_captured_before_host_authorization()
    {
        var shell = WorkspaceShell();
        var spatial = Assert.Single(shell.Children, component => component.AuthoredId == "canvas-surface");
        Assert.Equal("Object", spatial.Type);
        Assert.Equal("CanvasSpatialSurface", Assert.IsType<CuiLiteralValue>(spatial.Properties["Type"]).Value);
        CanvasWorkspaceCommand? captured = null;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new CanvasCuiWorkspace(async (command, _) => { captured = command; await gate.Task; }, _ => true);
        var first = CanvasArtifact.Create("First");
        workspace.Refresh(first, "Pending flush");
        var invocation = workspace.DispatchAsync("9to1.Canvas.Artifact.Save", null);
        workspace.Refresh(CanvasArtifact.Create("Second"), "Ready");
        gate.SetResult();
        await invocation;
        Assert.Equal(first.ArtifactId, captured!.ArtifactId);
        Assert.Equal(first.RevisionId, captured.BaseRevisionId);
        Assert.Equal(CanvasWorkspaceCommandKind.Flush, captured.Kind);
        Assert.False(workspace.IsActionAvailable("arbitrary.resource.write"));
        Assert.Throws<ArgumentException>(() => workspace.DispatchAsync("9to1.Canvas.Artifact.Save", "/untrusted/path"));
    }

    [Fact]
    public void Host_read_only_state_disables_mutations_and_unknown_bindings_fail_closed()
    {
        var workspace = new CanvasCuiWorkspace((_, _) => throw new InvalidOperationException("Must not dispatch"), kind => kind == CanvasWorkspaceCommandKind.Open);
        workspace.Refresh(CanvasArtifact.Create(), "Read only");
        Assert.True(workspace.IsActionAvailable("9to1.Canvas.Artifact.Open"));
        Assert.False(workspace.IsActionAvailable("9to1.Canvas.History.Undo"));
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync("9to1.Canvas.History.Undo", null));
        Assert.False(workspace.TryGetValue("ActorId", out _));
        Assert.True(workspace.TryGetValue("CanOpen", out var open));
        Assert.Equal(true, open);
        foreach (var binding in new[] { "CanImport", "CanFlush", "CanUndo", "CanRedo", "CanEditInk", "CanPenTool", "CanEraserTool" })
        {
            Assert.True(workspace.TryGetValue(binding, out var allowed));
            Assert.Equal(false, allowed);
        }
        var notifications = 0;
        workspace.PropertyChanged += (_, _) => notifications++;
        workspace.RefreshAvailability();
        Assert.Equal(1, notifications);
    }

    private static CuiComponent WorkspaceShell()
    {
        // Page is an explicit canonical component. The parser preserves this
        // authored hierarchy rather than flattening its single child.
        var page = Assert.Single(CanvasCuiWorkspace.LoadDocument().Components);
        Assert.Equal("Page", page.Type);
        var shell = Assert.Single(page.Children);
        Assert.Equal("canvas-shell", shell.AuthoredId);
        return shell;
    }

    private static IEnumerable<CuiComponent> Descendants(CuiComponent component)
    {
        yield return component;
        foreach (var child in component.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
