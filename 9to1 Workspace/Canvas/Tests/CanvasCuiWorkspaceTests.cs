using CakeOS.Cui;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasCuiWorkspaceTests
{
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
        var control = controls.Children.Single(component => component.AuthoredId == controlId);
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
}
