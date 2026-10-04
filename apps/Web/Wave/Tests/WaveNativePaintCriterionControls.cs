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
                <TextBlock id="probe-space" text="Local audio editor · projects and original WAV sources are stored in this browser. They are not synced to Files or another device. Browser data clearing removes these copies." text-wrapping="Wrap" />
              </StackPanel></DefaultTheme>
            </Cui>
            """, "public-native-paint-probe.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Actual criterion probe markup must parse.");
        using var loader = new CuiControlLoader(WaveNativePresentation.CreateControlRegistry());
        var root = loader.Load(document) ?? throw new InvalidDataException("Actual owner registry must load the native criterion probe.");
        var caption = root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "probe-caption");
        var whitespace = root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "probe-space");
        caption.FontFamily = actualReference.FontFamily;
        caption.FontStyle = actualReference.FontStyle;
        caption.FontWeight = actualReference.FontWeight;
        caption.FontStretch = actualReference.FontStretch;
        whitespace.FontFamily = actualReference.FontFamily;
        whitespace.FontStyle = actualReference.FontStyle;
        whitespace.FontWeight = actualReference.FontWeight;
        whitespace.FontStretch = actualReference.FontStretch;
        caption.HorizontalAlignment = HorizontalAlignment.Left;
        caption.VerticalAlignment = VerticalAlignment.Top;
        whitespace.Width = 358;
        whitespace.HorizontalAlignment = HorizontalAlignment.Left;
        whitespace.FontSize = actualBaseFont;
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
            caption.FontSize = actualBaseFont;
            Capture();
            var font1 = WaveNativePaintCriteria.Inspect(caption, root);
            Record("real-production-factory-font1-padding-allocation", caption.Padding == new Thickness(1, 0)
                && caption.ClipToBounds && font1.OwnPaintFits && font1.RootAndAncestorPaintFits, font1);
            caption.FontSize = actualBaseFont * 2;
            Capture();
            var font2 = WaveNativePaintCriteria.Inspect(caption, root);
            Record("real-production-factory-font2-padding-allocation", caption.Padding == new Thickness(1, 0)
                && caption.ClipToBounds && font2.OwnPaintFits && font2.RootAndAncestorPaintFits, font2);
            caption.FontSize = actualBaseFont;
            caption.Padding = default;
            Capture();
            var noPadding = WaveNativePaintCriteria.Inspect(caption, root);
            Record("real-native-font1-padding0-nonblank-clip-rejected", caption.ClipToBounds
                && noPadding.Supported && noPadding.SourceComplete && !noPadding.OwnPaintFits
                && noPadding.InkInText.Any(ink => ink.Right > caption.Bounds.Width + WaveNativePaintCriteria.Tolerance), noPadding);
            caption.Padding = new Thickness(1, 0);
            caption.Height = 1;
            Capture();
            var tooShort = WaveNativePaintCriteria.Inspect(caption, root);
            Record("real-native-own-height-nonblank-clip-rejected", tooShort.Supported && tooShort.SourceComplete
                && !tooShort.OwnPaintFits, tooShort);
            caption.ClearValue(Control.HeightProperty);
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
            window.Content = null;
            window.Close();
        }
    }
}
