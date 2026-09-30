using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasCuiWorkspaceTests
{
    [Fact]
    public async Task Embedded_scene_and_dispatch_keep_the_target_captured_before_host_authorization()
    {
        var scene = CanvasCuiWorkspace.LoadDocument();
        Assert.Equal("canvas-shell", Assert.Single(scene.Components).AuthoredId);
        Assert.Contains(scene.Components[0].Children, component => component.Type == "CanvasSpatialSurface");
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
    }
}
