using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web.Wave;

namespace NineToOne.Web.Wave.Tests;

// Separate native criterion UNIT controls. These are not additional Wave UI
// acceptance groups and do not replace the seven groups/64 unchanged purposes.
public static class WaveNativePaintCriterionControls
{
    public static object[] Run(TextBlock actualReference, double actualBaseFont)
    {
        var parser = new CuiRichParser();
        var document = parser.Parse("""
            <Cui id="paint-criterion-probe" product="9-1" version="1">
              <DefaultTheme value="Default"><StackPanel orientation="Vertical">
                <WaveCaption id="probe-caption" text="Import WAV" text-wrapping="Wrap" />
                <WaveCaption id="probe-vertical" text="Toggle track solo" text-wrapping="Wrap" />
                <WaveText id="probe-ordinary" text="Saved local project · revision 12. This is scripted geometry data, not a save receipt." text-wrapping="Wrap" />
                <TextBlock id="probe-space" text="Local audio editor · projects and original WAV sources are stored in this browser. They are not synced to Files or another device. Browser data clearing removes these copies." text-wrapping="Wrap" />
              </StackPanel></DefaultTheme>
            </Cui>
            """, "public-native-paint-probe.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Actual criterion probe markup must parse.");
        using var loader = new CuiControlLoader(WaveNativePresentation.CreateControlRegistry());
        var root = loader.Load(document) ?? throw new InvalidDataException("Actual owner registry must load the native criterion probe.");
        var caption = root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "probe-caption");
        var vertical = root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "probe-vertical");
        var ordinary = root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "probe-ordinary");
        var whitespace = root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "probe-space");
        foreach (var probe in new[] { caption, vertical, ordinary, whitespace })
        {
            probe.FontFamily = actualReference.FontFamily;
            probe.FontStyle = actualReference.FontStyle;
            probe.FontWeight = actualReference.FontWeight;
            probe.FontStretch = actualReference.FontStretch;
            probe.HorizontalAlignment = HorizontalAlignment.Left;
            probe.VerticalAlignment = VerticalAlignment.Top;
            probe.FontSize = actualBaseFont;
        }
        ordinary.Width = 358;
        whitespace.Width = 358;
        // Capture the actual shared factory allocation. Controlled negatives restore it;
        // the test does not manufacture a substitute production padding value.
        var captionPadding = caption.Padding;
        var verticalPadding = vertical.Padding;
        var ordinaryPadding = ordinary.Padding;
        var window = new Window { Content = root, Width = 400, Height = 400 };
        var outcomes = new List<object>();
        void Capture()
        {
            using var frame = window.CaptureRenderedFrame();
            if (frame is null) throw new InvalidOperationException("Actual native criterion frame required.");
        }
        void Record(string name, bool passed, object evidence)
        {
            outcomes.Add(new { name, passed, evidence });
            // Failure mechanisms survive even if the later prerequisite refuses the geometry group.
            Console.WriteLine("CRITERION_UNIT: " + System.Text.Json.JsonSerializer.Serialize(new { name, passed, evidence }));
            if (!passed) throw new InvalidDataException("Native criterion UNIT failed: " + name);
        }
        window.Show();
        try
        {
            Capture();
            var font1 = WaveNativePaintCriteria.Inspect(caption, root);
            var verticalFont1 = WaveNativePaintCriteria.Inspect(vertical, root);
            var ordinaryFont1 = WaveNativePaintCriteria.Inspect(ordinary, root);
            Record("real-production-factory-font1-padding-allocation", captionPadding == new Thickness(1, 1)
                && verticalPadding == new Thickness(1, 1) && ordinaryPadding == new Thickness(0, 1)
                && caption.ClipToBounds && vertical.ClipToBounds && ordinary.ClipToBounds
                && font1.OwnPaintFits && font1.RootAndAncestorPaintFits
                && verticalFont1.OwnPaintFits && verticalFont1.RootAndAncestorPaintFits
                && ordinaryFont1.OwnPaintFits && ordinaryFont1.RootAndAncestorPaintFits,
                new { captionPadding, verticalPadding, ordinaryPadding, caption = font1, vertical = verticalFont1, ordinary = ordinaryFont1 });
            caption.FontSize = actualBaseFont * 2;
            vertical.FontSize = actualBaseFont * 2;
            ordinary.FontSize = actualBaseFont * 2;
            Capture();
            var font2 = WaveNativePaintCriteria.Inspect(caption, root);
            var verticalFont2 = WaveNativePaintCriteria.Inspect(vertical, root);
            var ordinaryFont2 = WaveNativePaintCriteria.Inspect(ordinary, root);
            Record("real-production-factory-font2-padding-allocation", caption.Padding == captionPadding
                && vertical.Padding == verticalPadding && ordinary.Padding == ordinaryPadding
                && caption.ClipToBounds && vertical.ClipToBounds && ordinary.ClipToBounds
                && font2.OwnPaintFits && font2.RootAndAncestorPaintFits
                && verticalFont2.OwnPaintFits && verticalFont2.RootAndAncestorPaintFits
                && ordinaryFont2.OwnPaintFits && ordinaryFont2.RootAndAncestorPaintFits,
                new { captionPadding, verticalPadding, ordinaryPadding, caption = font2, vertical = verticalFont2, ordinary = ordinaryFont2 });
            caption.FontSize = actualBaseFont;
            vertical.FontSize = actualBaseFont;
            ordinary.FontSize = actualBaseFont;
            caption.Padding = default;
            Capture();
            var noPadding = WaveNativePaintCriteria.Inspect(caption, root);
            Record("real-native-font1-padding0-nonblank-clip-rejected", caption.ClipToBounds
                && noPadding.Supported && noPadding.SourceComplete && !noPadding.OwnPaintFits
                && noPadding.InkInText.Any(ink => ink.Right > caption.Bounds.Width + WaveNativePaintCriteria.Tolerance), noPadding);
            caption.Padding = captionPadding;
            caption.Height = 1;
            Capture();
            var tooShort = WaveNativePaintCriteria.Inspect(caption, root);
            caption.ClearValue(Control.HeightProperty);
            // Preserve horizontal allocation while removing ONLY the actual vertical
            // allocation; this exercises the recorded Toggle track solo bottom clip.
            vertical.Padding = new Thickness(verticalPadding.Left, 0, verticalPadding.Right, 0);
            Capture();
            var verticalNoPadding = WaveNativePaintCriteria.Inspect(vertical, root);
            Record("real-native-own-height-nonblank-clip-rejected", tooShort.Supported && tooShort.SourceComplete
                && !tooShort.OwnPaintFits && vertical.ClipToBounds
                && verticalNoPadding.Supported && verticalNoPadding.SourceComplete && !verticalNoPadding.OwnPaintFits
                && verticalNoPadding.InkInText.Any(ink => ink.Bottom > vertical.Bounds.Height + WaveNativePaintCriteria.Tolerance),
                new { heightOne = tooShort, verticalPaddingRemoved = verticalNoPadding });
            vertical.Padding = verticalPadding;
            Capture();
            var spacePaint = WaveNativePaintCriteria.Inspect(whitespace, root);
            var selection = whitespace.TextLayout.HitTestTextRange(0, whitespace.Text!.Length).ToArray();
            Record("real-native-trailing-space-advance-is-not-ink-clipping", selection.Any(box => box.Right > whitespace.Bounds.Width + WaveNativePaintCriteria.Tolerance)
                && spacePaint.OwnPaintFits && spacePaint.RootAndAncestorPaintFits, new { selection, paint = spacePaint });
            caption.RenderTransform = new TranslateTransform(0.5, 0);
            Capture();
            var transform = WaveNativePaintCriteria.Inspect(caption, root);
            Record("unsupported-render-transform-fails-closed", !transform.Supported
                && !transform.OwnPaintFits && !transform.RootAndAncestorPaintFits, transform);
            caption.RenderTransform = null;
            caption.Clip = new EllipseGeometry(new Rect(caption.Bounds.Size));
            Capture();
            var path = WaveNativePaintCriteria.Inspect(caption, root);
            Record("unsupported-nonrectangular-clip-fails-closed", !path.Supported
                && !path.OwnPaintFits && !path.RootAndAncestorPaintFits, path);
            caption.Clip = null;
            Capture();
            return outcomes.ToArray();
        }
        finally
        {
            // All mutations belong to this fresh probe; no production view is changed.
            caption.RenderTransform = null;
            caption.Clip = null;
            caption.ClearValue(Control.HeightProperty);
            caption.Padding = captionPadding;
            vertical.Padding = verticalPadding;
            ordinary.Padding = ordinaryPadding;
            window.Content = null;
            window.Close();
        }
    }
}
