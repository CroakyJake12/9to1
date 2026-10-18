namespace Haven.UI;

/// <summary>
/// Minimal canonical line-icon geometry used by the Haven renderer. Geometry
/// is expressed in a 24x24 Haven view box; platform backends only rasterise it.
/// Unknown keys deliberately return a visible fallback instead of disappearing.
/// </summary>
public static class HavenIconCatalog
{
    private static readonly HavenRect ViewBox = new(0, 0, 24, 24);

    public static HavenGeometry Resolve(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "home" => Geometry(Line((3, 11), (12, 3), (21, 11)), Line((5, 9), (5, 21), (10, 21), (10, 15), (14, 15), (14, 21), (19, 21), (19, 9))),
        "wave" => Geometry(Line((3, 10), (3, 14)), Line((7, 6), (7, 18)), Line((12, 3), (12, 21)), Line((17, 7), (17, 17)), Line((21, 10), (21, 14))),
        "shield" => Geometry(new HavenPathFigure(new HavenPoint(12, 3),
            [new HavenLineSegment(new HavenPoint(20, 6)), new HavenLineSegment(new HavenPoint(19, 15)), new HavenLineSegment(new HavenPoint(16, 19)), new HavenLineSegment(new HavenPoint(12, 22)), new HavenLineSegment(new HavenPoint(8, 19)), new HavenLineSegment(new HavenPoint(5, 15)), new HavenLineSegment(new HavenPoint(4, 6))], true), Line((8, 12), (11, 15), (16, 9))),
        "boards" => Geometry(new HavenPathFigure(new HavenPoint(3, 4),
            [new HavenLineSegment(new HavenPoint(21, 4)), new HavenLineSegment(new HavenPoint(21, 20)), new HavenLineSegment(new HavenPoint(3, 20))], true), Line((9, 4), (9, 20)), Line((15, 4), (15, 20)), Line((5, 8), (7, 8)), Line((11, 8), (13, 8)), Line((17, 8), (19, 8)), Line((5, 12), (7, 12)), Line((11, 12), (13, 12))),
        "plus" or "add" => Geometry(Line((12, 4), (12, 20)), Line((4, 12), (20, 12))),
        "close" or "x" => Geometry(Line((5, 5), (19, 19)), Line((19, 5), (5, 19))),
        "check" => Geometry(Line((4, 13), (9, 18), (20, 6))),
        "more" or "ellipsis" => Geometry(Circle(6, 12, 1.25), Circle(12, 12, 1.25), Circle(18, 12, 1.25)),
        "chevron-left" => Geometry(Line((16, 5), (9, 12), (16, 19))),
        "chevron-right" => Geometry(Line((8, 5), (15, 12), (8, 19))),
        "window" => Geometry(new HavenPathFigure(new HavenPoint(3, 5), [new HavenLineSegment(new HavenPoint(21, 5)), new HavenLineSegment(new HavenPoint(21, 19)), new HavenLineSegment(new HavenPoint(3, 19))], true), Line((3, 9), (21, 9)), Circle(6, 7, .8), Circle(9, 7, .8)),
        "cpu" => Geometry(new HavenPathFigure(new HavenPoint(7, 7), [new HavenLineSegment(new HavenPoint(17, 7)), new HavenLineSegment(new HavenPoint(17, 17)), new HavenLineSegment(new HavenPoint(7, 17))], true), Line((10, 3), (10, 7)), Line((14, 3), (14, 7)), Line((10, 17), (10, 21)), Line((14, 17), (14, 21)), Line((3, 10), (7, 10)), Line((3, 14), (7, 14)), Line((17, 10), (21, 10)), Line((17, 14), (21, 14))),
        "bell" => Geometry(Line((5, 17), (7, 15), (7, 10)), Arc((7, 10), (17, 10), (5, 5)), Line((17, 10), (17, 15), (19, 17), (5, 17)), Arc((10, 19), (14, 19), (2, 2))),
        "chevron-down" => Geometry(Line((5, 8), (12, 15), (19, 8))),
        "chevron-up" => Geometry(Line((5, 16), (12, 9), (19, 16))),
        "arrow-up" => Geometry(Line((5, 12), (12, 5), (19, 12)), Line((12, 5), (12, 20))),
        "search" => Geometry(Circle(10, 10, 6), Line((14.5, 14.5), (20, 20))),
        "chat" => Geometry(new HavenPathFigure(new HavenPoint(4, 5),
            [new HavenLineSegment(new HavenPoint(20, 5)), new HavenLineSegment(new HavenPoint(20, 16)), new HavenLineSegment(new HavenPoint(11, 16)), new HavenLineSegment(new HavenPoint(6, 20)), new HavenLineSegment(new HavenPoint(6, 16)), new HavenLineSegment(new HavenPoint(4, 16))], true)),
        "folder" => Geometry(new HavenPathFigure(new HavenPoint(3, 6),
            [new HavenLineSegment(new HavenPoint(10, 6)), new HavenLineSegment(new HavenPoint(12, 9)), new HavenLineSegment(new HavenPoint(21, 9)), new HavenLineSegment(new HavenPoint(21, 19)), new HavenLineSegment(new HavenPoint(3, 19))], true)),
        "refresh" => Geometry(Line((19, 8), (19, 4), (15, 4)), Line((19, 4), (16, 7)), Arc((16, 7), (6, 17), (8, 8)), Line((5, 16), (5, 20), (9, 20))),
        "study" => Geometry(new HavenPathFigure(new HavenPoint(3, 5),
            [new HavenLineSegment(new HavenPoint(10, 7)), new HavenLineSegment(new HavenPoint(10, 20)), new HavenLineSegment(new HavenPoint(3, 18))], true),
            new HavenPathFigure(new HavenPoint(21, 5),
            [new HavenLineSegment(new HavenPoint(14, 7)), new HavenLineSegment(new HavenPoint(14, 20)), new HavenLineSegment(new HavenPoint(21, 18))], true)),
        "file" or "notes" => Geometry(new HavenPathFigure(new HavenPoint(6, 3),
            [new HavenLineSegment(new HavenPoint(15, 3)), new HavenLineSegment(new HavenPoint(20, 8)), new HavenLineSegment(new HavenPoint(20, 21)), new HavenLineSegment(new HavenPoint(6, 21))], true),
            Line((15, 3), (15, 8), (20, 8)), Line((9, 12), (17, 12)), Line((9, 16), (16, 16))),
        "agents" => Geometry(Circle(8, 8, 3), Circle(16.5, 8.5, 2.5), Line((3, 20), (3, 17), (5, 14), (11, 14), (13, 17), (13, 20)), Line((14, 14), (19, 14), (21, 17), (21, 20))),
        "bolt" or "rapid" or "build" => Geometry(new HavenPathFigure(new HavenPoint(13, 2),
            [new HavenLineSegment(new HavenPoint(5, 13)), new HavenLineSegment(new HavenPoint(11, 13)), new HavenLineSegment(new HavenPoint(10, 22)), new HavenLineSegment(new HavenPoint(19, 10)), new HavenLineSegment(new HavenPoint(13, 10))], true)),
        "prompt" => Geometry(Circle(12, 9, 6), Line((9, 15), (9, 18), (15, 18), (15, 15)), Line((10, 21), (14, 21))),
        "rocket" => Geometry(new HavenPathFigure(new HavenPoint(14, 3),
            [new HavenLineSegment(new HavenPoint(21, 3)), new HavenLineSegment(new HavenPoint(21, 10)), new HavenLineSegment(new HavenPoint(13, 18)), new HavenLineSegment(new HavenPoint(8, 16)), new HavenLineSegment(new HavenPoint(6, 11))], true), Circle(16.5, 7.5, 1.8), Line((7, 15), (3, 21), (9, 19))),
        "browse" => Geometry(Circle(12, 12, 9), Line((3, 12), (21, 12)), Line((12, 3), (12, 21)), Arc((12, 3), (12, 21), (5, 9)), Arc((12, 21), (12, 3), (5, 9))),
        "tasks" or "plan" => Geometry(Line((5, 5), (8, 5), (8, 8), (5, 8), (5, 5)), Line((11, 6.5), (20, 6.5)), Line((5, 11), (8, 11), (8, 14), (5, 14), (5, 11)), Line((11, 12.5), (20, 12.5)), Line((5, 17), (8, 17), (8, 20), (5, 20), (5, 17)), Line((11, 18.5), (18, 18.5))),
        "studio" => Geometry(Line((9, 5), (3, 12), (9, 19)), Line((15, 5), (21, 12), (15, 19)), Line((13, 4), (11, 20))),
        "test" or "experiment" => Geometry(Line((9, 3), (15, 3)), Line((11, 3), (11, 9), (5, 20), (19, 20), (13, 9), (13, 3)), Line((8, 15), (16, 15))),
        "bookmark" => Geometry(new HavenPathFigure(new HavenPoint(7, 3),
            [new HavenLineSegment(new HavenPoint(17, 3)), new HavenLineSegment(new HavenPoint(17, 21)), new HavenLineSegment(new HavenPoint(12, 17)), new HavenLineSegment(new HavenPoint(7, 21))], true)),
        "book" => Resolve("study"),
        "code" => Resolve("studio"),
        "globe" => Resolve("browse"),
        "calendar" => Geometry(new HavenPathFigure(new HavenPoint(4, 6), [new HavenLineSegment(new HavenPoint(20, 6)), new HavenLineSegment(new HavenPoint(20, 20)), new HavenLineSegment(new HavenPoint(4, 20))], true), Line((4, 10), (20, 10)), Line((8, 3), (8, 7)), Line((16, 3), (16, 7))),
        "target" => Geometry(Circle(12, 12, 8), Circle(12, 12, 3), Line((12, 2), (12, 6)), Line((12, 18), (12, 22)), Line((2, 12), (6, 12)), Line((18, 12), (22, 12))),
        "palette" => Geometry(Circle(11, 12, 8), Circle(8, 8, 1), Circle(13, 7, 1), Circle(16, 11, 1), Arc((13, 19), (19, 15), (5, 5))),
        "present" => Geometry(new HavenPathFigure(new HavenPoint(3, 4), [new HavenLineSegment(new HavenPoint(21, 4)), new HavenLineSegment(new HavenPoint(21, 17)), new HavenLineSegment(new HavenPoint(3, 17))], true), Line((8, 21), (12, 17), (16, 21))),
        "data" => Geometry(Arc((5, 6), (19, 6), (7, 3)), Arc((19, 6), (5, 6), (7, 3)), Line((5, 6), (5, 18)), Arc((5, 18), (19, 18), (7, 3)), Line((19, 18), (19, 6))),
        "vision" => Geometry(Arc((3, 12), (21, 12), (10, 7)), Arc((21, 12), (3, 12), (10, 7)), Circle(12, 12, 3)),
        "play" => Geometry(new HavenPathFigure(new HavenPoint(8, 5), [new HavenLineSegment(new HavenPoint(19, 12)), new HavenLineSegment(new HavenPoint(8, 19))], true)),
        "translate" => Geometry(Line((4, 6), (12, 6)), Line((8, 3), (8, 15)), Line((4, 11), (12, 3)), Line((13, 18), (17, 8), (21, 18)), Line((14.5, 14), (19.5, 14))),
        "dashboard" => Geometry(new HavenPathFigure(new HavenPoint(4, 4), [new HavenLineSegment(new HavenPoint(10, 4)), new HavenLineSegment(new HavenPoint(10, 10)), new HavenLineSegment(new HavenPoint(4, 10))], true), new HavenPathFigure(new HavenPoint(14, 4), [new HavenLineSegment(new HavenPoint(20, 4)), new HavenLineSegment(new HavenPoint(20, 10)), new HavenLineSegment(new HavenPoint(14, 10))], true), new HavenPathFigure(new HavenPoint(4, 14), [new HavenLineSegment(new HavenPoint(10, 14)), new HavenLineSegment(new HavenPoint(10, 20)), new HavenLineSegment(new HavenPoint(4, 20))], true), new HavenPathFigure(new HavenPoint(14, 14), [new HavenLineSegment(new HavenPoint(20, 14)), new HavenLineSegment(new HavenPoint(20, 20)), new HavenLineSegment(new HavenPoint(14, 20))], true)),
        "settings" => Geometry(Circle(12, 12, 4), Circle(12, 12, 8), Line((12, 2), (12, 5)), Line((12, 19), (12, 22)), Line((2, 12), (5, 12)), Line((19, 12), (22, 12))),
        "pin" => Geometry(new HavenPathFigure(new HavenPoint(8, 4), [new HavenLineSegment(new HavenPoint(16, 4)), new HavenLineSegment(new HavenPoint(15, 10)), new HavenLineSegment(new HavenPoint(19, 14)), new HavenLineSegment(new HavenPoint(5, 14)), new HavenLineSegment(new HavenPoint(9, 10))], true), Line((12, 14), (12, 22))),
        "pen" => Geometry(new HavenPathFigure(new HavenPoint(4, 20), [new HavenLineSegment(new HavenPoint(8, 12)), new HavenLineSegment(new HavenPoint(12, 16))], true), Line((10, 14), (19, 5)), Line((6.5, 17.5), (9, 15))),        "highlighter" => Geometry(new HavenPathFigure(new HavenPoint(3, 17), [new HavenLineSegment(new HavenPoint(5, 15)), new HavenLineSegment(new HavenPoint(13, 7)), new HavenLineSegment(new HavenPoint(17, 11)), new HavenLineSegment(new HavenPoint(9, 19))], true), Line((14, 4), (20, 10)), Line((3, 21), (8, 21))),
        "eraser" => Geometry(new HavenPathFigure(new HavenPoint(6, 13), [new HavenLineSegment(new HavenPoint(11, 8)), new HavenLineSegment(new HavenPoint(18, 15)), new HavenLineSegment(new HavenPoint(13, 20)), new HavenLineSegment(new HavenPoint(8, 20))], true), Line((4, 21), (10, 21))),
        "select" => Geometry(new HavenPathFigure(new HavenPoint(7, 3), [new HavenLineSegment(new HavenPoint(17, 12)), new HavenLineSegment(new HavenPoint(12, 12)), new HavenLineSegment(new HavenPoint(14, 19)), new HavenLineSegment(new HavenPoint(11, 20)), new HavenLineSegment(new HavenPoint(9, 13)), new HavenLineSegment(new HavenPoint(5, 16))], true)),
        "shape" => Geometry(RoundedBox(3, 5, 10, 10, 2), Circle(16.5, 16, 4.5)),
        "rect" or "rectangle" => Geometry(new HavenPathFigure(new HavenPoint(5, 7), [new HavenLineSegment(new HavenPoint(19, 7)), new HavenLineSegment(new HavenPoint(19, 17)), new HavenLineSegment(new HavenPoint(5, 17))], true)),
        "ellipse" => Geometry(Circle(12, 12, 7)),
        "line" => Geometry(Line((5, 19), (19, 5))),
        "arrow" => Geometry(Line((5, 19), (19, 5)), Line((12, 5), (19, 5), (19, 12))),
        "layout" => Geometry(RoundedBox(3, 4, 18, 16, 2), Line((3, 10), (21, 10)), Line((11, 10), (11, 20))),
        "transition" => Geometry(Line((3, 5), (14, 5), (14, 19), (3, 19), (3, 5)), Line((17, 5), (21, 5), (21, 19), (17, 19)), Line((8, 12), (18, 12)), Line((15, 9), (18, 12), (15, 15))),
        "text" => Geometry(Line((5, 5), (19, 5)), Line((12, 5), (12, 20)), Line((8, 20), (16, 20))),
        "bold" => Geometry(Line((7, 4), (7, 20), (14, 20)), Arc((14, 12), (14, 20), (4, 4)), Line((14, 12), (7, 12)), Arc((14, 4), (14, 12), (4, 4)), Line((14, 4), (7, 4))),
        "italic" => Geometry(Line((10, 4), (19, 4)), Line((5, 20), (14, 20)), Line((15, 4), (9, 20))),
        "underline" => Geometry(Line((6, 4), (6, 11)), new HavenPathFigure(new HavenPoint(6, 11),
            [new HavenArcSegment(new HavenPoint(18, 11), new HavenSize(6, 6), SweepDirection: HavenSweepDirection.CounterClockwise)]),
            Line((18, 11), (18, 4)), Line((4, 21), (20, 21))),
        "text-color" => Geometry(Line((6, 17), (12, 3), (18, 17)), Line((8, 12), (16, 12)), Line((4, 21), (20, 21))),
        "font-increase" => Geometry(Line((3, 20), (8, 6), (13, 20)), Line((5, 15), (11, 15)), Line((15, 7), (22, 7)), Line((18.5, 3.5), (18.5, 10.5))),
        "font-decrease" => Geometry(Line((3, 20), (8, 6), (13, 20)), Line((5, 15), (11, 15)), Line((15, 7), (22, 7))),
        "stroke" => Geometry(RoundedBox(4, 4, 16, 16, 2), Line((4, 22), (20, 22))),
        "stroke-width" => Geometry(Line((4, 5), (20, 5)), Line((4, 11), (20, 11)), Line((4, 17), (20, 17)), Line((4, 19), (20, 19))),
        "align-text" => Geometry(Line((4, 5), (20, 5)), Line((7, 10), (17, 10)), Line((4, 15), (20, 15)), Line((7, 20), (17, 20))),
        "align-text-vertical" => Geometry(Line((4, 9), (20, 9)), Line((7, 15), (17, 15)),
            Line((12, 2), (12, 6)), Line((9, 3), (12, 6), (15, 3)),
            Line((12, 18), (12, 22)), Line((9, 21), (12, 18), (15, 21))),
        "align-objects" => Geometry(Line((4, 3), (4, 21)), RoundedBox(8, 5, 12, 5, 1.5), RoundedBox(8, 14, 8, 5, 1.5)),
        "layers" => Geometry(Line((3, 8), (12, 3), (21, 8), (12, 13), (3, 8)), Line((3, 13), (12, 18), (21, 13)), Line((3, 18), (12, 23), (21, 18))),
        "copy" => Geometry(RoundedBox(8, 8, 13, 13, 2), Line((16, 4), (16, 3), (3, 3), (3, 16), (4, 16))),
        "paste" => Geometry(RoundedBox(5, 5, 14, 16, 2), RoundedBox(8, 3, 8, 4, 1.5), Line((9, 12), (15, 12)), Line((9, 16), (15, 16))),
        "duplicate-slide" => Geometry(RoundedBox(7, 7, 14, 13, 2), Line((17, 3), (3, 3), (3, 16))),
        "delete" => Geometry(Line((4, 6), (20, 6)), Line((9, 3), (15, 3)), Line((6, 6), (7, 21), (17, 21), (18, 6)), Line((10, 10), (10, 17)), Line((14, 10), (14, 17))),
        "history" => Geometry(Circle(12, 12, 8), Line((2, 3), (2, 9), (8, 9)), Line((12, 7), (12, 12), (16, 14))),
        "properties" => Geometry(Line((5, 3), (5, 6)), Line((5, 10), (5, 21)), Circle(5, 8, 2), Line((12, 3), (12, 14)), Line((12, 18), (12, 21)), Circle(12, 16, 2), Line((19, 3), (19, 8)), Line((19, 12), (19, 21)), Circle(19, 10, 2)),
        "slides" => Geometry(Line((3, 4), (21, 4), (21, 20), (3, 20), (3, 4)), Line((9, 4), (9, 20)), Line((3, 10), (9, 10)), Line((3, 15), (9, 15))),
        "add-slide" => Geometry(RoundedBox(3, 4, 18, 16, 2), Line((12, 8), (12, 16)), Line((8, 12), (16, 12))),
        "undo" => Geometry(Line((8, 4), (3, 9), (8, 14)), Line((3, 9), (15, 9)), Arc((15, 9), (15, 21), (6, 6))),
        "redo" => Geometry(Line((16, 4), (21, 9), (16, 14)), Line((21, 9), (9, 9)), new HavenPathFigure(new HavenPoint(9, 9), [new HavenArcSegment(new HavenPoint(9, 21), new HavenSize(6, 6), SweepDirection: HavenSweepDirection.CounterClockwise)])),
        "rotate-left" => Geometry(Line((3, 3), (3, 9), (9, 9)), new HavenPathFigure(new HavenPoint(3, 9),
            [new HavenArcSegment(new HavenPoint(6, 19), new HavenSize(9, 9), IsLargeArc: true, SweepDirection: HavenSweepDirection.Clockwise)])),
        "rotate-right" => Geometry(Line((21, 3), (21, 9), (15, 9)), new HavenPathFigure(new HavenPoint(21, 9),
            [new HavenArcSegment(new HavenPoint(18, 19), new HavenSize(9, 9), IsLargeArc: true, SweepDirection: HavenSweepDirection.CounterClockwise)])),
        "fit" => Geometry(Line((4, 9), (4, 4), (9, 4)), Line((15, 4), (20, 4), (20, 9)), Line((20, 15), (20, 20), (15, 20)), Line((9, 20), (4, 20), (4, 15))),
        "save" => Geometry(new HavenPathFigure(new HavenPoint(5, 4), [new HavenLineSegment(new HavenPoint(19, 4)), new HavenLineSegment(new HavenPoint(19, 20)), new HavenLineSegment(new HavenPoint(5, 20))], true), Line((9, 4), (9, 9), (15, 9), (15, 4)), Line((8, 20), (8, 15), (16, 15), (16, 20))),
        "folder-open" => Geometry(new HavenPathFigure(new HavenPoint(3, 9), [new HavenLineSegment(new HavenPoint(21, 9)), new HavenLineSegment(new HavenPoint(21, 19)), new HavenLineSegment(new HavenPoint(3, 19))], true), Line((3, 9), (9, 9), (12, 5), (21, 5))),
        "zoom-in" => Geometry(Circle(11, 11, 7), Line((11, 8), (11, 14)), Line((8, 11), (14, 11)), Line((16, 16), (21, 21))),
        "zoom-out" => Geometry(Circle(11, 11, 7), Line((8, 11), (14, 11)), Line((16, 16), (21, 21))),
        "cart" => Geometry(new HavenPathFigure(new HavenPoint(3, 6),
            [new HavenLineSegment(new HavenPoint(6, 6)), new HavenLineSegment(new HavenPoint(8, 18)), new HavenLineSegment(new HavenPoint(18, 18)), new HavenLineSegment(new HavenPoint(20, 6))], true),
            Line((8, 6), (20, 6)), Circle(9, 21, 1.5), Circle(17, 21, 1.5)),
        "experiences" => Geometry(Circle(12, 12, 9), Circle(12, 12, 4), Line((12, 3), (12, 8)), Line((12, 16), (12, 21)), Line((3, 12), (8, 12)), Line((16, 12), (21, 12))),
        "sparkles" => Geometry(Line((12, 3), (12, 7)), Line((10, 5), (14, 5)), Line((19, 8), (19, 12)), Line((17, 10), (21, 10)), Line((5, 14), (5, 18)), Line((3, 16), (7, 16)), Line((15, 16), (15, 20)), Line((13, 18), (17, 18))),
        _ => Geometry(new HavenPathFigure(new HavenPoint(4, 4),
            [new HavenLineSegment(new HavenPoint(20, 4)), new HavenLineSegment(new HavenPoint(20, 20)), new HavenLineSegment(new HavenPoint(4, 20))], true), Line((7, 7), (17, 17)), Line((17, 7), (7, 17)))
    };

    private static HavenGeometry Geometry(params HavenPathFigure[] figures) => new(new HavenPath(figures), ViewBox);

    private static HavenPathFigure RoundedBox(double x, double y, double width, double height, double radius) => new(
        new HavenPoint(x + radius, y),
        [new HavenLineSegment(new HavenPoint(x + width - radius, y)),
         new HavenQuadraticBezierSegment(new HavenPoint(x + width, y), new HavenPoint(x + width, y + radius)),
         new HavenLineSegment(new HavenPoint(x + width, y + height - radius)),
         new HavenQuadraticBezierSegment(new HavenPoint(x + width, y + height), new HavenPoint(x + width - radius, y + height)),
         new HavenLineSegment(new HavenPoint(x + radius, y + height)),
         new HavenQuadraticBezierSegment(new HavenPoint(x, y + height), new HavenPoint(x, y + height - radius)),
         new HavenLineSegment(new HavenPoint(x, y + radius)),
         new HavenQuadraticBezierSegment(new HavenPoint(x, y), new HavenPoint(x + radius, y))], true);

    private static HavenPathFigure Line(params (double X, double Y)[] points) => new(
        new HavenPoint(points[0].X, points[0].Y),
        points.Skip(1).Select(point => (HavenPathSegment)new HavenLineSegment(new HavenPoint(point.X, point.Y))).ToArray());

    private static HavenPathFigure Circle(double centerX, double centerY, double radius) => new(
        new HavenPoint(centerX + radius, centerY),
        [
            new HavenArcSegment(new HavenPoint(centerX - radius, centerY), new HavenSize(radius, radius), SweepDirection: HavenSweepDirection.Clockwise),
            new HavenArcSegment(new HavenPoint(centerX + radius, centerY), new HavenSize(radius, radius), SweepDirection: HavenSweepDirection.Clockwise)
        ],
        true);

    private static HavenPathFigure Arc((double X, double Y) start, (double X, double Y) end, (double X, double Y) radius) => new(
        new HavenPoint(start.X, start.Y),
        [new HavenArcSegment(new HavenPoint(end.X, end.Y), new HavenSize(radius.X, radius.Y), SweepDirection: HavenSweepDirection.Clockwise)]);
}
