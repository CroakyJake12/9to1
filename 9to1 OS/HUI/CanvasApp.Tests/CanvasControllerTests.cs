using Xunit;

namespace CakeOS.Canvas.App.Tests;

public sealed class CanvasControllerTests : IDisposable
{
    private readonly StubCanvasSession _session = new();
    private readonly CanvasController _controller;

    public CanvasControllerTests()
    {
        _controller = new CanvasController(() => _session);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void StartsUntitledWithPenAndDefaultZoom()
    {
        Assert.Equal("Untitled", _controller.DocumentName);
        Assert.Equal("Untitled — Canvas", _controller.WindowTitle);
        Assert.Equal(CanvasTool.Pen, _controller.Tool);
        Assert.Equal(CanvasController.DefaultZoom, _controller.Zoom);
        Assert.False(_controller.IsDirty);
        Assert.Contains("SetTool:Pen", _session.Calls);
    }

    [Fact]
    public void AppliesDefaultStylesToSessionAtStartup()
    {
        Assert.True(_session.Styles.ContainsKey(CanvasTool.Pen));
        Assert.True(_session.Styles.ContainsKey(CanvasTool.Highlighter));
        Assert.True(_session.Styles.ContainsKey(CanvasTool.Shape));
        Assert.Contains(_session.Calls, c => c.StartsWith("SetEraser:"));
    }

    [Fact]
    public void SelectToolRoutesThroughSessionAndRefreshes()
    {
        var changed = 0;
        _controller.StateChanged += () => changed++;
        _controller.SelectTool(CanvasTool.Eraser);
        Assert.Equal(CanvasTool.Eraser, _controller.Tool);
        Assert.Contains("SetTool:Eraser", _session.Calls);
        Assert.True(changed > 0);
    }

    [Fact]
    public void SetColorKeepsWidthAndRoutesToSession()
    {
        _controller.SelectTool(CanvasTool.Pen);
        _controller.SetWidth(7);
        _controller.SetColor(new CanvasRgba(1, 0, 0, 1));
        var style = _session.Styles[CanvasTool.Pen];
        Assert.Equal(1, style.Color.R);
        Assert.Equal(7, style.Width);
        Assert.Equal(7, _controller.CurrentWidth);
    }

    [Fact]
    public void HighlighterForcesTranslucentAlpha()
    {
        _controller.SelectTool(CanvasTool.Highlighter);
        _controller.SetColor(new CanvasRgba(1, 1, 0, 1));
        Assert.Equal(0.5, _session.Styles[CanvasTool.Highlighter].Color.A);
    }

    [Fact]
    public void WidthIsClampedToSupportedRange()
    {
        _controller.SelectTool(CanvasTool.Pen);
        _controller.SetWidth(500);
        Assert.Equal(CanvasController.MaxToolWidth, _controller.CurrentWidth);
        _controller.SetWidth(-3);
        Assert.Equal(CanvasController.MinToolWidth, _controller.CurrentWidth);
    }

    [Fact]
    public void EraserWidthAndStyleRouteToSession()
    {
        _controller.SelectTool(CanvasTool.Eraser);
        _controller.SetWidth(30);
        _controller.SetEraserStyle(CanvasEraserStyle.Split);
        Assert.Equal(30, _session.EraserWidth);
        Assert.Equal(CanvasEraserStyle.Split, _session.EraserStyle);
        Assert.Equal(30, _controller.CurrentWidth);
    }

    [Fact]
    public void ZoomIsClamped()
    {
        _controller.SetZoom(100);
        Assert.Equal(CanvasController.MaxZoom, _controller.Zoom);
        _controller.SetZoom(0);
        Assert.Equal(CanvasController.MinZoom, _controller.Zoom);
    }

    [Fact]
    public void UndoRedoFlowThroughHistoryAndDirty()
    {
        Assert.False(_controller.CanUndo);
        _controller.Undo();
        Assert.False(_controller.IsDirty);
        _session.BeginStroke(0, 0, 0.5);
        _session.EndStroke(10, 10, 0.5);
        Assert.True(_controller.CanUndo);
        _controller.Undo();
        Assert.True(_controller.IsDirty);
        Assert.True(_controller.CanRedo);
        Assert.Contains("Untitled •", _controller.WindowTitle);
        _controller.Redo();
        Assert.False(_controller.CanRedo);
    }

    [Fact]
    public void SaveWithoutPathReturnsFalseKeepsDirty()
    {
        _controller.MarkDirty();
        Assert.False(_controller.Save());
        Assert.True(_controller.IsDirty);
    }

    [Fact]
    public void SaveToWritesAtomicallyAndClearsDirty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "shot.rnote");
        try
        {
            _session.SavedPayload = [9, 8, 7, 6, 5];
            _controller.MarkDirty();
            _controller.SaveTo(path);
            Assert.Equal([9, 8, 7, 6, 5], File.ReadAllBytes(path));
            Assert.False(_controller.IsDirty);
            Assert.Equal("shot", _controller.DocumentName);
            Assert.Equal(path, _controller.DocumentPath);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveFailureKeepsDirtyAndReportsTruthfully()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "shot.rnote");
        try
        {
            _session.ThrowOnSave = true;
            _controller.MarkDirty();
            Assert.Throws<InvalidOperationException>(() => _controller.SaveTo(path));
            Assert.True(_controller.IsDirty);
            Assert.StartsWith("Save failed:", _controller.StatusText);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OpenDocumentSwapsSessionAndNamesDocument()
    {
        var replacement = new StubCanvasSession();
        using var opened = new CanvasController(() => new StubCanvasSession(), _ => replacement);
        var original = opened.Session;
        opened.OpenDocument(Path.Combine("some", "field-notes.rnote"), [1, 2, 3]);
        Assert.Equal("field-notes", opened.DocumentName);
        Assert.False(opened.IsDirty);
        Assert.Same(replacement, opened.Session);
        Assert.NotSame(original, opened.Session);
    }

    [Fact]
    public void NewDocumentResetsIdentityButKeepsToolMemory()
    {
        _controller.SelectTool(CanvasTool.Highlighter);
        _controller.SetWidth(9);
        _controller.MarkDirty();
        _controller.NewDocument();
        Assert.Equal("Untitled", _controller.DocumentName);
        Assert.Null(_controller.DocumentPath);
        Assert.False(_controller.IsDirty);
        Assert.Equal(CanvasTool.Highlighter, _controller.Tool);
        Assert.Equal(9, _controller.CurrentWidth);
    }

    [Fact]
    public void SelectShapeAppliesAndSwitchesToShapeTool()
    {
        _controller.SelectShape(CanvasShape.Arrow);
        Assert.Equal(CanvasShape.Arrow, _controller.Shape);
        Assert.Equal(CanvasTool.Shape, _controller.Tool);
        Assert.Contains("SetShape:Arrow", _session.Calls);
    }
}
