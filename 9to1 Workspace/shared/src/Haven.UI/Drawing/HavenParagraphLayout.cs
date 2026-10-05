namespace Haven.UI;

/// <summary>Immutable authored runs supplied to the platform's paragraph shaper.</summary>
public sealed record HavenParagraphRun(string Text, string FontFamily, double FontSize, int FontWeight,
    bool Italic, HavenBrush Foreground, HavenBrush? Background = null, bool Underline = false, bool StrikeThrough = false);

public enum HavenParagraphAlignment { Left, Center, Right, Justify }
public sealed record HavenParagraphLine(int Start, int Length, int NewLineLength, HavenRect Bounds, double Baseline);
public sealed record HavenParagraphSegment(int Start, int Length, int RunIndex, HavenRect Bounds);

/// <summary>
/// One immutable styled paragraph and its actual platform-shaped geometry. Character offsets are
/// canonical UTF-16 document offsets; the platform determines glyph clusters and caret stops.
/// No document ownership, input dispatch or native rendering type crosses this boundary.
/// </summary>
public abstract class HavenParagraphLayout
{
    public abstract string Text { get; }
    public abstract IReadOnlyList<HavenParagraphRun> Runs { get; }
    public abstract HavenSize Size { get; }
    public abstract IReadOnlyList<HavenParagraphLine> Lines { get; }
    public abstract IReadOnlyList<HavenParagraphSegment> Segments { get; }
    public abstract HavenRect CaretRect(int offset);
    public abstract IReadOnlyList<HavenRect> SelectionRects(int start, int length);
    public abstract int HitTest(HavenPoint point);
    public abstract int LineIndex(int offset);
    public abstract int NavigateVertical(int offset, int lineDelta);
}
