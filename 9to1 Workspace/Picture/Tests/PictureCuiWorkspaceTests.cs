using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureCuiWorkspaceTests
{
    [Fact]
    public async Task Embedded_scene_dispatch_captures_document_file_and_revision_before_focus_changes()
    {
        var scene = PictureCuiWorkspace.LoadDocument();
        Assert.Equal("picture-shell", Assert.Single(scene.Components).AuthoredId);
        Assert.Contains(scene.Components[0].Children, component => component.Type == "PictureRasterSurface");
        PictureWorkspaceCommand? captured = null;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new PictureCuiWorkspace(async (command, _) => { captured = command; await gate.Task; }, _ => true);
        var first = PictureDocument.Create(20, 30, "files-target", "files-revision").Rotate();
        var backingId = Guid.NewGuid();
        workspace.Refresh(first, "Ready", "Raster editor", backingId);
        var invocation = workspace.DispatchAsync("9to1.Picture.Save", null, TestContext.Current.CancellationToken);
        workspace.Refresh(PictureDocument.Create(5, 5), "Ready", "Raster editor");
        gate.SetResult();
        await invocation;
        Assert.Equal(first.DocumentId, captured!.DocumentId);
        Assert.Equal(first.Revision, captured.BaseRevision);
        Assert.Equal(first.FileId, captured.FileId);
        Assert.Equal(first.FileId, captured.SourceFileId);
        Assert.Equal(backingId, captured.BackingFileId);
        Assert.False(workspace.IsActionAvailable("caller.resource.delete"));
        Assert.Throws<ArgumentException>(() => workspace.DispatchAsync("9to1.Picture.Save", "/untrusted/path", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Host_read_only_state_cannot_dispatch_edit_or_expose_source_path()
    {
        var workspace = new PictureCuiWorkspace((_, _) => throw new InvalidOperationException("Must not dispatch"), kind => kind == PictureWorkspaceCommandKind.Open);
        workspace.Refresh(PictureDocument.Create(10, 10), "Read only", "Raster editor");
        Assert.True(workspace.IsActionAvailable("9to1.Picture.Open"));
        Assert.False(workspace.IsActionAvailable("9to1.Picture.Rotate"));
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync("9to1.Picture.Rotate", null, TestContext.Current.CancellationToken));
        Assert.False(workspace.TryGetValue("SourcePath", out _));
    }

    [Fact]
    public void Raw_source_identity_does_not_enable_editable_save_without_a_backing_artifact()
    {
        var workspace = new PictureCuiWorkspace((_, _) => throw new InvalidOperationException("Must not save the raw source"), _ => true);
        workspace.Refresh(PictureDocument.Create(2, 2, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()), "Source open", "Raster editor");
        Assert.False(workspace.IsActionAvailable("9to1.Picture.Save"));
        Assert.False(workspace.IsActionAvailable("9to1.Picture.Export"));
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync("9to1.Picture.Save", null, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Readonly_animation_navigation_and_live_command_bindings_follow_owning_host_capabilities()
    {
        var available = true;
        PictureWorkspaceCommand? dispatched = null;
        var workspace = new PictureCuiWorkspace((command, _) => { dispatched = command; return ValueTask.CompletedTask; },
            command => command == PictureWorkspaceCommandKind.NextFrame && available);
        var document = PictureDocument.Create(2, 1);
        workspace.Refresh(document, "Read only", "Animation preview", Guid.NewGuid(), 80_000);
        Assert.True(workspace.TryGetValue("CanAdvanceFrames", out var canAdvance));
        Assert.Equal(true, canAdvance);
        Assert.True(workspace.TryGetValue("CanSave", out var canSave));
        Assert.Equal(false, canSave);
        Assert.True(workspace.TryGetValue("AnimationSummary", out var timing));
        Assert.Equal("Paused · Frame delay: 80 ms", timing);
        Assert.True(workspace.TryGetValue("CanPlay", out var canPlay));
        Assert.Equal(false, canPlay);
        Assert.True(workspace.TryGetValue("CanPause", out var canPause));
        Assert.Equal(false, canPause);
        await workspace.DispatchAsync("9to1.Picture.Animation.NextFrame", null, TestContext.Current.CancellationToken);
        Assert.Equal(PictureWorkspaceCommandKind.NextFrame, dispatched!.Kind);
        Assert.Equal(document.DocumentId, dispatched.DocumentId);
        Assert.Equal(document.Revision, dispatched.BaseRevision);
        var notifications = 0;
        workspace.PropertyChanged += (_, _) => notifications++;
        available = false;
        workspace.RefreshAvailability();
        Assert.Equal(1, notifications);
        workspace.TryGetValue("CanAdvanceFrames", out canAdvance);
        Assert.Equal(false, canAdvance);
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync("9to1.Picture.Animation.NextFrame", null, TestContext.Current.CancellationToken));
    }

}
