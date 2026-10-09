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

    [Fact]
    public void SaveToPreservesUnrelatedStagingSiblingAndFreshReopenUsesActualFileBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "canvas-save-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "notes.rnote");
        var unrelated = path + ".tmp";
        try
        {
            File.WriteAllText(unrelated, "unrelated user staging");
            _session.SavedPayload = [11, 22, 33, 44];
            _controller.MarkDirty();
            _controller.SaveTo(path);
            Assert.Equal("unrelated user staging", File.ReadAllText(unrelated));
            Assert.Equal(_session.SavedPayload, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp").Where(value => value != unrelated));
            byte[]? received = null;
            var replacement = new StubCanvasSession();
            using var reopened = new CanvasController(() => new StubCanvasSession(), bytes =>
            {
                received = bytes;
                replacement.SavedPayload = bytes;
                return replacement;
            });
            reopened.OpenDocument(path, File.ReadAllBytes(path));
            Assert.Equal(_session.SavedPayload, received);
            Assert.Equal(path, reopened.DocumentPath);
            Assert.False(reopened.IsDirty);
            replacement.SavedPayload = [55, 66, 77];
            reopened.MarkDirty();
            Assert.True(reopened.Save());
            Assert.Equal(replacement.SavedPayload, File.ReadAllBytes(path));
            Assert.Equal("unrelated user staging", File.ReadAllText(unrelated));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SavePromotionFailureKeepsAcknowledgedIdentityAndCleansOnlyItsOwnedStage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "canvas-save-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var originalPath = Path.Combine(directory, "original.rnote");
        var target = Path.Combine(directory, "directory.rnote");
        try
        {
            _controller.SaveTo(originalPath);
            var acknowledged = File.ReadAllBytes(originalPath);
            Directory.CreateDirectory(target);
            var sentinel = Path.Combine(target, "unrelated.txt");
            File.WriteAllText(sentinel, "preserve this directory");
            _session.SavedPayload = [91, 92, 93];
            _controller.MarkDirty();
            var failure = Assert.ThrowsAny<Exception>(() => _controller.SaveTo(target));
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Assert.Equal(originalPath, _controller.DocumentPath);
            Assert.Equal("original", _controller.DocumentName);
            Assert.True(_controller.IsDirty);
            Assert.StartsWith("Save failed:", _controller.StatusText);
            Assert.Equal(acknowledged, File.ReadAllBytes(originalPath));
            Assert.Equal("preserve this directory", File.ReadAllText(sentinel));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FailedReplacementRenderClosesCandidateAndPreservesTheCurrentEditedDocument()
    {
        var original = new StubCanvasSession();
        var replacement = new StubCanvasSession { ReturnEmptyFrame = true };
        using var controller = new CanvasController(() => original, _ => replacement);
        controller.MarkDirty();
        Assert.Throws<InvalidOperationException>(() => controller.OpenDocument("replacement.rnote", [1, 2, 3]));
        Assert.True(replacement.IsDisposed);
        Assert.False(original.IsDisposed);
        Assert.Same(original, controller.Session);
        Assert.True(controller.IsDirty);
        Assert.Null(controller.DocumentPath);
        Assert.Equal("Untitled", controller.DocumentName);
    }

    [Fact]
    public void NonfiniteReplacementRenderBoundsAreRejectedBeforeCurrentRetirement()
    {
        var original = new StubCanvasSession();
        var replacement = new ControlledReplacementSession
        {
            Frame = new(new CanvasDocumentBounds(0, 0, double.NaN, 100), "<svg></svg>")
        };
        using var controller = new CanvasController(() => original, _ => replacement);
        Assert.Throws<InvalidDataException>(() => controller.OpenDocument("invalid.rnote", [1, 2, 3]));
        Assert.True(replacement.DisposeAttempted);
        Assert.False(original.IsDisposed);
        Assert.Same(original, controller.Session);
    }

    [Fact]
    public void FailedReplacementStyleSetupClosesCandidateBeforeRetiringTheCurrentDocument()
    {
        var original = new StubCanvasSession();
        var cause = new IOException("actual replacement setup failure");
        var replacement = new ControlledReplacementSession { SetupFailure = cause };
        using var controller = new CanvasController(() => original, _ => replacement);
        Assert.Same(cause, Assert.Throws<IOException>(() => controller.OpenDocument("replacement.rnote", [1, 2, 3])));
        Assert.True(replacement.DisposeAttempted);
        Assert.False(original.IsDisposed);
        Assert.Same(original, controller.Session);
        Assert.Null(controller.DocumentPath);
    }

    [Fact]
    public void ReplacementRenderAndCleanupFaultsRemainSeparateOriginalCauses()
    {
        var original = new StubCanvasSession();
        var render = new InvalidDataException("actual replacement render failure");
        var cleanup = new IOException("actual replacement cleanup failure");
        var replacement = new ControlledReplacementSession { RenderFailure = render, DisposeFailure = cleanup };
        using var controller = new CanvasController(() => original, _ => replacement);
        var failure = Assert.Throws<AggregateException>(() => controller.OpenDocument("replacement.rnote", [1, 2, 3]));
        Assert.Collection(failure.InnerExceptions, first => Assert.Same(render, first), second => Assert.Same(cleanup, second));
        Assert.True(replacement.DisposeAttempted);
        Assert.False(original.IsDisposed);
        Assert.Same(original, controller.Session);
    }

    [Fact]
    public void FailedNewDocumentSetupDisposesCandidateAndKeepsTheCurrentEditedDocument()
    {
        var original = new StubCanvasSession();
        var failure = new IOException("new session setup failure");
        var replacement = new ControlledReplacementSession { SetupFailure = failure };
        var creations = 0;
        using var controller = new CanvasController(() => creations++ == 0 ? original : replacement);
        controller.MarkDirty();
        Assert.Same(failure, Assert.Throws<IOException>(() => controller.NewDocument()));
        Assert.True(replacement.DisposeAttempted);
        Assert.False(original.IsDisposed);
        Assert.Same(original, controller.Session);
        Assert.True(controller.IsDirty);
    }

    [Fact]
    public void FailedPreviousSessionRetirementClosesThePreparedReplacementWithoutPublishingItsIdentity()
    {
        var retirement = new IOException("actual previous session retirement failure");
        var original = new ControlledReplacementSession { DisposeFailure = retirement };
        var replacement = new StubCanvasSession();
        using var controller = new CanvasController(() => original, _ => replacement);
        try
        {
            Assert.Same(retirement, Assert.Throws<IOException>(() => controller.OpenDocument("replacement.rnote", [1, 2, 3])));
            Assert.True(original.DisposeAttempted);
            Assert.True(replacement.IsDisposed);
            Assert.Same(original, controller.Session);
            Assert.Null(controller.DocumentPath);
        }
        finally { original.DisposeFailure = null; }
    }

    [Fact]
    public void SavedFileAcknowledgementSurvivesAnOriginalStatusObserverFault()
    {
        var directory = Path.Combine(Path.GetTempPath(), "canvas-save-observer-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "acknowledged.rnote");
        var observer = new IOException("actual save observer failure");
        Action handler = () => throw observer;
        try
        {
            _controller.MarkDirty();
            _controller.StateChanged += handler;
            Assert.Same(observer, Assert.Throws<IOException>(() => _controller.SaveTo(path)));
            Assert.Equal(_session.SavedPayload, File.ReadAllBytes(path));
            Assert.Equal(path, _controller.DocumentPath);
            Assert.False(_controller.IsDirty);
            Assert.StartsWith("Saved ", _controller.StatusText);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            _controller.StateChanged -= handler;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveFailureAndObserverFaultBothRemainVisibleAfterOwnedStagingCleanup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "canvas-save-faults-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(directory, "directory.rnote");
        Directory.CreateDirectory(target);
        var observer = new InvalidOperationException("actual save observer fault");
        Action handler = () => throw observer;
        try
        {
            _controller.MarkDirty();
            _controller.StateChanged += handler;
            var failure = Assert.Throws<AggregateException>(() => _controller.SaveTo(target));
            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.True(failure.InnerExceptions[0] is IOException or UnauthorizedAccessException);
            Assert.Same(observer, failure.InnerExceptions[1]);
            Assert.True(_controller.IsDirty);
            Assert.Null(_controller.DocumentPath);
            Assert.StartsWith("Save failed:", _controller.StatusText);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            _controller.StateChanged -= handler;
            Directory.Delete(directory, recursive: true);
        }
    }

    // Fault controls preserve actual cause objects. The physical save controls above
    // certify controller/file behavior; these managed sessions do not certify Rnote ABI or GUI.
    private sealed class ControlledReplacementSession : ICanvasSession
    {
        private readonly StubCanvasSession _inner = new();
        public Exception? RenderFailure; public Exception? SetupFailure; public Exception? DisposeFailure;
        public bool DisposeAttempted;
        public CanvasSvgFrame Frame = new(new CanvasDocumentBounds(0, 0, 100, 100), "<svg></svg>");
        public bool CanUndo => _inner.CanUndo; public bool CanRedo => _inner.CanRedo;
        public void SetTool(CanvasTool tool) => _inner.SetTool(tool);
        public void SetShape(CanvasShape shape) => _inner.SetShape(shape);
        public void SetPenStyle(CanvasTool tool, CanvasRgba color, double width) => _inner.SetPenStyle(tool, color, width);
        public void SetEraser(double width, CanvasEraserStyle style)
        { if (SetupFailure is not null) throw SetupFailure; _inner.SetEraser(width, style); }
        public void SetViewportSize(double width, double height) => _inner.SetViewportSize(width, height);
        public void ZoomTo(double zoom) => _inner.ZoomTo(zoom);
        public void PanBy(double x, double y) => _inner.PanBy(x, y);
        public void BeginStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0) => _inner.BeginStroke(x, y, pressure, tiltX, tiltY);
        public void UpdateStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0) => _inner.UpdateStroke(x, y, pressure, tiltX, tiltY);
        public void EndStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0) => _inner.EndStroke(x, y, pressure, tiltX, tiltY);
        public bool Undo() => _inner.Undo(); public bool Redo() => _inner.Redo();
        public CanvasSvgFrame RenderSvg() { if (RenderFailure is not null) throw RenderFailure; return Frame; }
        public byte[] SaveRnote() => _inner.SaveRnote();
        public void Dispose() { DisposeAttempted = true; if (DisposeFailure is not null) throw DisposeFailure; _inner.Dispose(); }
    }
}
