using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasAppSurfaceTests
{
    [Fact]
    public void CreateManipulateInkPanUndoRedoKeepsCanonicalCanvasDocument()
    {
        var surface = CanvasAppSurface.Create("Journey");

        Assert.Equal("haven://apps/canvas", CanvasAppSurface.Route);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(surface.Document));
        Assert.Equal(NotesLayoutMode.InfiniteCanvas, surface.Document.LayoutMode);

        var note = surface.AddTextNote("Idea");
        var shape = surface.AddShape("Result");
        Assert.True(surface.MoveObject(note.Id, 120, 140, 10));

        var connector = surface.Connect(note.Id, shape.Id, "leads to");
        Assert.NotNull(connector);

        Assert.True(surface.DrawStroke([
            new CanvasPointerSample(40, 45, 0.4, TimestampMilliseconds: 1000),
            new CanvasPointerSample(70, 80, 0.6, TimestampMilliseconds: 1016),
            new CanvasPointerSample(100, 105, 0.8, TimestampMilliseconds: 1032)
        ]));
        Assert.True(surface.Pan(0, 0, 35, -20));
        surface.SetZoom(1.5);

        var snapshot = surface.Snapshot;
        Assert.Equal(3, snapshot.ObjectCount);
        Assert.Equal(1, snapshot.StrokeCount);
        Assert.Equal(1.5, snapshot.Zoom);
        Assert.Equal(35, snapshot.OffsetX);
        Assert.Equal(-20, snapshot.OffsetY);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));

        Assert.True(surface.Undo());
        Assert.Empty(surface.Board.Strokes);
        Assert.Equal(3, surface.Board.Objects.Count);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));

        Assert.True(surface.Redo());
        Assert.Single(surface.Board.Strokes);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));
    }

    [Fact]
    public void AttachRejectsOrdinaryNotesDocument()
    {
        var document = NotesDocument.Create("Notes");

        var error = Assert.Throws<ArgumentException>(() => new CanvasAppSurface(document));

        Assert.Contains("Haven Canvas document", error.Message);
    }

    [Fact]
    public void Pan_does_not_append_its_endpoint_to_an_in_progress_pen_stroke()
    {
        var surface = CanvasAppSurface.Create();
        surface.Interaction.Tool = CanvasTool.Pen;
        Assert.True(surface.Interaction.Begin(new CanvasPointerSample(10, 20)));

        Assert.True(surface.Pan(0, 0, 30, 40));

        var stroke = Assert.Single(surface.Board.Strokes);
        var point = Assert.Single(stroke.Points);
        Assert.Equal(10, point.X);
        Assert.Equal(20, point.Y);
        Assert.Equal(30, surface.Board.OffsetX);
        Assert.Equal(40, surface.Board.OffsetY);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));
    }

    [Fact]
    public void Invalid_stroke_batch_is_rejected_before_changing_an_active_gesture_or_document()
    {
        var surface = CanvasAppSurface.Create();
        surface.Interaction.Tool = CanvasTool.Pen;
        Assert.True(surface.Interaction.Begin(new CanvasPointerSample(10, 20)));
        var stroke = Assert.Single(surface.Board.Strokes);
        var pointsBefore = stroke.Points.ToArray();

        Assert.Throws<ArgumentException>(() => surface.DrawStroke([
            new CanvasPointerSample(30, 40),
            new CanvasPointerSample(double.NaN, 50)
        ]));

        Assert.Same(stroke, Assert.Single(surface.Board.Strokes));
        Assert.Equal(pointsBefore, stroke.Points);
        Assert.Same(surface.Board, CanvasDocumentModel.GetBoard(surface.Document));
        Assert.True(surface.Interaction.Move(new CanvasPointerSample(15, 25)));
        Assert.Equal(15, stroke.Points[^1].X);
        Assert.Equal(25, stroke.Points[^1].Y);
    }

    [Fact]
    public void Invalid_pan_coordinates_do_not_interrupt_an_active_ink_stroke()
    {
        var surface = CanvasAppSurface.Create();
        surface.Interaction.Tool = CanvasTool.Pen;
        Assert.True(surface.Interaction.Begin(new CanvasPointerSample(10, 20)));

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Pan(0, 0, double.PositiveInfinity, 40));

        Assert.True(surface.Interaction.Move(new CanvasPointerSample(20, 30)));
        Assert.Equal(20, Assert.Single(surface.Board.Strokes).Points[^1].X);
        Assert.Equal(0, surface.Board.OffsetX);
        Assert.Equal(0, surface.Board.OffsetY);
    }
}
