using Haven.Core;

namespace Haven.Application;

public sealed record DocumentShapePreset(string Id, string Name, string Category);

/// <summary>Built-in choices backed by the editable document vector model.
/// The gallery preview and insertion use this same geometry, never a bitmap substitute.</summary>
public static class DocumentShapePresets
{
    public static IReadOnlyList<DocumentShapePreset> All { get; } = Array.AsReadOnly<DocumentShapePreset>([
        new("rect", "Rectangle", "Basic shapes"), new("roundRect", "Rounded rectangle", "Basic shapes"),
        new("ellipse", "Ellipse", "Basic shapes"), new("triangle", "Triangle", "Basic shapes"),
        new("diamond", "Diamond", "Basic shapes"), new("hexagon", "Hexagon", "Basic shapes"),
        new("star", "Star", "Basic shapes"), new("line", "Line", "Lines and arrows"),
        new("rightArrow", "Right arrow", "Lines and arrows"), new("leftArrow", "Left arrow", "Lines and arrows"),
        new("leftRightArrow", "Two-way arrow", "Lines and arrows"),
        new("flowDecision", "Decision", "Flowchart"), new("flowProcess", "Process", "Flowchart"),
        new("flowTerminator", "Terminator", "Flowchart"), new("callout", "Speech bubble", "Callouts")]);

    public static DocumentVectorShape Create(string id)
    {
        var preset = All.FirstOrDefault(item => item.Id == id) ?? throw new ArgumentException("Unknown built-in shape.", nameof(id));
        var shape = id switch
        {
            "rect" or "flowProcess" => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle),
            "ellipse" => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse),
            "triangle" => Polygon((.5,0), (1,1), (0,1)),
            "diamond" or "flowDecision" => Polygon((.5,0), (1,.5), (.5,1), (0,.5)),
            "hexagon" => Polygon((.25,0), (.75,0), (1,.5), (.75,1), (.25,1), (0,.5)),
            "star" => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Star),
            "line" => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Line),
            "rightArrow" => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Arrow),
            "leftArrow" => Polygon((0,.5), (.4,0), (.4,.35), (1,.35), (1,.65), (.4,.65), (.4,1)),
            "leftRightArrow" => Polygon((0,.5), (.3,0), (.3,.35), (.7,.35), (.7,0), (1,.5), (.7,1), (.7,.65), (.3,.65), (.3,1)),
            "callout" => Polygon((0,0), (1,0), (1,.75), (.45,.75), (.2,1), (.2,.75), (0,.75)),
            "roundRect" => Rounded(.16),
            "flowTerminator" => Rounded(.5),
            _ => throw new ArgumentException("Unknown built-in shape.", nameof(id))
        };
        shape.Name = preset.Name;
        shape.AccessibilityDescription = preset.Name;
        shape.Metadata["creation.preset"] = preset.Id;
        return shape;
    }

    private static DocumentVectorShape Polygon(params (double X, double Y)[] points)
    {
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        shape.Paths[0].Subpaths[0].Nodes = points.Select(point => new DocumentVectorNode { X = point.X * 100, Y = point.Y * 100 }).ToList();
        return shape;
    }

    private static DocumentVectorShape Rounded(double radius)
    {
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        var r = radius * 100;
        DocumentVectorNode Line(double x, double y) => new() { X = x, Y = y };
        DocumentVectorNode Curve(double x, double y, double cx, double cy) => new()
            { X = x, Y = y, IncomingSegment = DocumentVectorSegmentKind.Quadratic, Control1 = new(cx, cy) };
        shape.Paths[0].Subpaths[0].Nodes = [Line(r,0), Line(100-r,0), Curve(100,r,100,0),
            Line(100,100-r), Curve(100-r,100,100,100), Line(r,100), Curve(0,100-r,0,100),
            Line(0,r), Curve(r,0,0,0)];
        return shape;
    }
}
