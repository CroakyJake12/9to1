using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace NineToOne.Web.Wave.Tests;

// Native UNIT criterion, not a text renderer. All ink/source data comes from the
// actual owner TextLine/ShapedTextRun/GlyphRun objects in the rendered control.
// The supported anonymous fixture is plain LTR baseline text at native scale1.
// Unknown run paint, geometry clips or transformations fail closed.
public static class WaveNativePaintCriteria
{
    public const double Tolerance = 0.1;

    public sealed record Evidence(bool Supported, bool SourceComplete,
        bool OwnPaintFits, bool RootAndAncestorPaintFits, string[] Reasons,
        Rect[] InkInText, string ReconstructedSource, object[] SourceRuns,
        object[] ClipFrames, bool DeliberateVerticalViewport);

    public static Evidence Inspect(TextBlock text, Control root)
    {
        var reasons = new List<string>();
        var inks = new List<Rect>();
        var sourceRuns = new List<object>();
        var clips = new List<object>();
        var reconstructed = new System.Text.StringBuilder();
        var supported = true;
        var sourceComplete = true;
        var ownFits = true;
        var rootFits = true;
        void Unsupported(string reason) { supported = false; reasons.Add(reason); }
        bool Finite(Rect box) => double.IsFinite(box.X) && double.IsFinite(box.Y)
            && double.IsFinite(box.Width) && double.IsFinite(box.Height)
            && double.IsFinite(box.Right) && double.IsFinite(box.Bottom);
        bool Contains(Rect allocation, Rect ink, bool checkY = true) =>
            ink.Left >= allocation.Left - Tolerance && ink.Right <= allocation.Right + Tolerance
            && (!checkY || (ink.Top >= allocation.Top - Tolerance && ink.Bottom <= allocation.Bottom + Tolerance));

        var authored = text.Text ?? "";
        var topLevel = TopLevel.GetTopLevel(text);
        if (topLevel is null || Math.Abs(topLevel.RenderScaling - 1) > 0.000001)
            Unsupported("Native scale other than1 requires public rounded-origin proof.");
        var padding = text.Padding;
        if (text.UseLayoutRounding && new[] { padding.Left, padding.Top, padding.Right, padding.Bottom }
                .Any(value => !double.IsFinite(value) || Math.Abs(value - Math.Round(value)) > 0.000001))
            Unsupported("Nonintegral rounded padding is not approximated.");
        if (text.FlowDirection != FlowDirection.LeftToRight)
            Unsupported("RTL paint ordering is outside this anonymous LTR fixture.");
        if (text.TextDecorations is { Count: > 0 }) Unsupported("Decoration paint not represented by glyph ink.");
        if (!text.IsMeasureValid || !text.IsArrangeValid || !Finite(new Rect(text.Bounds.Size)))
            Unsupported("Actual arranged native text allocation missing.");
        var lines = text.TextLayout.TextLines;
        var cursor = 0;
        var paragraphSentinels = 0;
        var y = padding.Top;
        if (text.TextLayout.Height + padding.Top + padding.Bottom > text.Bounds.Height + Tolerance)
        {
            ownFits = false;
            reasons.Add("Full owner text layout exceeds own height.");
            // Owner may move a too-tall Center/Bottom layout. Do not fabricate that origin.
            if (text.VerticalAlignment is VerticalAlignment.Center or VerticalAlignment.Bottom)
                Unsupported("Too-tall centered/bottom text origin requires separate owner proof.");
        }
        for (var li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            if (line.FirstTextSourceIndex != cursor || line.HasCollapsed || line.HasOverflowed)
                sourceComplete = false;
            var lineStart = cursor;
            var x = padding.Left + line.Start;
            foreach (var run in line.TextRuns)
            {
                if (run is TextEndOfParagraph)
                {
                    var validSentinel = li == lines.Count - 1 && cursor == authored.Length
                        && run.Length == 1 && run.Text.Length == 0;
                    if (!validSentinel) sourceComplete = false;
                    paragraphSentinels++;
                    sourceRuns.Add(new { line = li, start = cursor, run.Length,
                        type = run.GetType().FullName, terminalParagraphSentinel = validSentinel });
                    cursor += run.Length;
                    continue;
                }
                if (run is not ShapedTextRun shaped)
                {
                    Unsupported("Unsupported owner run: " + run.GetType().FullName);
                    sourceComplete = false;
                    continue;
                }
                var value = shaped.Text.ToString();
                var matches = shaped.Length == value.Length && cursor >= 0
                    && (long)cursor + value.Length <= authored.Length
                    && authored.AsSpan(cursor, value.Length).SequenceEqual(value.AsSpan());
                sourceComplete &= matches;
                sourceRuns.Add(new { line = li, start = cursor, length = shaped.Length,
                    type = shaped.GetType().FullName, text = value, exactSourceMatch = matches });
                reconstructed.Append(value);
                cursor += shaped.Length;
                if (shaped.Properties.BaselineAlignment != BaselineAlignment.Baseline
                    || shaped.Properties.BackgroundBrush is not null
                    || shaped.Properties.TextDecorations is { Count: > 0 }
                    || shaped.GlyphRun.BiDiLevel % 2 != 0)
                    Unsupported("Only plain LTR baseline glyph paint is certified here.");
                if (shaped.Properties.ForegroundBrush is null || shaped.Properties.Typeface == default)
                { sourceComplete = false; reasons.Add("Actual owner run has no drawable foreground/typeface."); }
                var glyph = shaped.GlyphRun;
                if (!glyph.Characters.Span.SequenceEqual(shaped.Text.Span)
                    || (value.Any(ch => !char.IsWhiteSpace(ch))
                        && (glyph.GlyphInfos.Count == 0 || glyph.GlyphInfos.Any(info => info.GlyphIndex == 0))))
                    sourceComplete = false;
                var ink = glyph.InkBounds;
                // GlyphRun.InkBounds already includes its actual BaselineOrigin;
                // plain owner run origin is line.Baseline minus the public run.Baseline.
                var positioned = ink.Translate(new Vector(x, y + line.Baseline - shaped.Baseline));
                if (!Finite(positioned)) Unsupported("Nonfinite actual owner glyph ink.");
                if (positioned.Width > 0 && positioned.Height > 0) inks.Add(positioned);
                x += shaped.Size.Width;
            }
            if (cursor - lineStart != line.Length) sourceComplete = false;
            y += line.Height;
        }
        sourceComplete &= paragraphSentinels == 1 && cursor == authored.Length + 1
            && reconstructed.ToString() == authored;
        if (!sourceComplete) reasons.Add("Exact source/run/paragraph-sentinel retention failed.");
        if (authored.Any(ch => !char.IsWhiteSpace(ch)) && inks.Count == 0)
            sourceComplete = false;

        // Assigned allocation remains mandatory even when clipping is disabled.
        foreach (var ink in inks)
            if (!Contains(new Rect(text.Bounds.Size), ink)) ownFits = false;

        var chain = text.GetVisualAncestors().Prepend(text).ToArray();
        var verticalViewportSeen = false;
        ScrollContentPresenter? viewport = null;
        foreach (var visual in chain)
        {
            if (visual is not Control control) { Unsupported("Non-Control visual ancestor."); continue; }
            if (control.RenderTransform is { } transform && !transform.Value.IsIdentity
                || control.HasMirrorTransform || control.OpacityMask is not null)
                Unsupported("Transformed/mirrored/masked paint requires exact public geometry proof.");
            Rect? clip = null;
            if (control.Clip is { } geometry)
            {
                if (geometry is not RectangleGeometry rectangle || rectangle.RadiusX != 0 || rectangle.RadiusY != 0
                    || (geometry.Transform is { } clipTransform && !clipTransform.Value.IsIdentity))
                    Unsupported("Arbitrary or rounded Clip is not accepted from its bounding box.");
                else clip = rectangle.Rect;
            }
            if (control.ClipToBounds)
            {
                if (control is Border border && border.CornerRadius != default
                    || control is ContentPresenter presenter && presenter.CornerRadius != default)
                    Unsupported("Rounded control clipping requires exact shape proof.");
                var own = new Rect(control.Bounds.Size);
                clip = clip is Rect geometryBox ? own.Intersect(geometryBox) : own;
            }
            if (control is ScrollContentPresenter scp)
            {
                var owner = scp.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                if (owner is not null && owner.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                    && owner.Extent.Height > owner.Viewport.Height + Tolerance)
                {
                    viewport = scp;
                    verticalViewportSeen = true;
                }
            }
            clips.Add(new { control.Name, type = control.GetType().FullName,
                control.ClipToBounds, actualRectangularClip = clip,
                deliberateVerticalViewportAtOrAbove = verticalViewportSeen });
            if (clip is not Rect allocation) continue;
            if (!Finite(allocation)) { Unsupported("Nonfinite actual clip."); continue; }
            var translation = text.TranslatePoint(default, control);
            if (translation is not Point origin) { Unsupported("Detached text/clip ancestor."); continue; }
            foreach (var ink in inks)
            {
                var relative = ink.Translate(new Vector(origin.X, origin.Y));
                if (!Contains(allocation, relative, checkY: !verticalViewportSeen))
                {
                    rootFits = false;
                    if (ReferenceEquals(control, text)) ownFits = false;
                    reasons.Add("Actual ink exceeds clip: " + (control.Name ?? control.GetType().Name));
                }
            }
            // Deliberate outer vertical scrolling masks offscreen content, not missing
            // source. Its real viewport must itself fit all higher clipping frames.
            if (verticalViewportSeen && viewport is not null && !ReferenceEquals(control, viewport))
            {
                var point = viewport.TranslatePoint(default, control);
                if (point is not Point vp || !Contains(allocation, new Rect(vp, viewport.Bounds.Size)))
                {
                    rootFits = false;
                    reasons.Add("Actual vertical viewport is clipped by a higher frame.");
                }
            }
        }
        var inRoot = text.TranslatePoint(default, root);
        if (inRoot is not Point rootOrigin) Unsupported("Detached root allocation.");
        else foreach (var ink in inks)
        {
            if (!Contains(new Rect(root.Bounds.Size), ink.Translate(new Vector(rootOrigin.X, rootOrigin.Y))))
                rootFits = false;
        }
        return new Evidence(supported, sourceComplete, ownFits && supported && sourceComplete,
            rootFits && supported && sourceComplete, reasons.Distinct().ToArray(),
            inks.ToArray(), reconstructed.ToString(), sourceRuns.ToArray(), clips.ToArray(), verticalViewportSeen);
    }
}
