using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using Haven.Desktop.Mathematics;

namespace Haven.Desktop.Tests;

public sealed class SharedMathNativeEditorTests
{
    [Fact]
    public async Task Authored_visual_and_LaTeX_controls_edit_same_ID_real_layout_and_recover_invalid_input()
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await session.Dispatch(async () =>
        {
            var original = new MathExpression(Guid.NewGuid(), 1, "1", "One");
            using var editor = new SharedMathEditorControl(original);
            var window = new Window { Content = editor, Width = 640, Height = 680 };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            try
            {
                await Click(editor, "math-fraction"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(original.ExpressionID, editor.Snapshot.LastValid.ExpressionID);
                Assert.Equal(2, editor.Snapshot.LastValid.Revision);
                Assert.Contains(@"\frac", editor.Snapshot.LastValid.LaTeX);
                var image = Assert.Single(editor.GetVisualDescendants().OfType<Image>());
                var bitmap = Assert.IsAssignableFrom<Bitmap>(image.Source);
                var beforePixels = Pixels(bitmap); var width = bitmap.PixelSize.Width; var height = bitmap.PixelSize.Height;
                var longest = 0; var barY = -1;
                for (var y = 0; y < height; y++)
                {
                    var run = 0;
                    for (var x = 0; x < width; x++)
                    {
                        run = beforePixels[(y * width + x) * 4 + 3] > 128 ? run + 1 : 0;
                        if (run > longest) { longest = run; barY = y; }
                    }
                }
                Assert.True(longest >= width / 3);
                Assert.InRange(barY, height / 4, height * 3 / 4);
                Assert.True(InkRows(beforePixels, width, 0, barY - 2) >= 4);
                Assert.True(InkRows(beforePixels, width, barY + 3, height) >= 4);
                var canonical = MathObjectCodec.Encode(editor.Snapshot.LastValid);
                await Click(editor, "math-mode");
                var source = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "math-source");
                source.Text = @"\frac{1}{"; Dispatcher.UIThread.RunJobs();
                await Click(editor, "math-apply-source");
                Assert.Equal(canonical, MathObjectCodec.Encode(editor.Snapshot.LastValid));
                Assert.Equal(@"\frac{1}{", editor.Snapshot.DraftLaTeX);
                Assert.False(string.IsNullOrWhiteSpace(editor.Snapshot.Diagnostic));
                Assert.Same(image, Assert.Single(editor.GetVisualDescendants().OfType<Image>()));
                Assert.Equal(beforePixels, Pixels(bitmap));
                await Click(editor, "math-restore");
                Assert.Null(editor.Snapshot.Diagnostic); Assert.Equal(editor.Snapshot.LastValid.LaTeX, source.Text);
                source.Text = "x^{2}"; Dispatcher.UIThread.RunJobs(); await Click(editor, "math-apply-source");
                Assert.Equal(original.ExpressionID, editor.Snapshot.LastValid.ExpressionID);
                Assert.Equal(3, editor.Snapshot.LastValid.Revision);
                Assert.NotSame(image, Assert.Single(editor.GetVisualDescendants().OfType<Image>()));
                var last = MathObjectCodec.Encode(editor.Snapshot.LastValid);
                var retired = Button(editor, "math-fraction"); editor.Dispose();
                retired.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await editor.WhenActionsIdleAsync();
                Assert.False(editor.TrySetValue("Draft", "foreign"));
                Assert.Throws<ObjectDisposedException>(() => { _ = editor.DispatchAsync("Fraction", null, TestContext.Current.CancellationToken); });
                Assert.Equal(last, MathObjectCodec.Encode(editor.Snapshot.LastValid));
            }
            finally { window.Close(); }
        }, token);
    }

    [Fact]
    public void Maintained_parser_and_editor_enforce_real_source_limits_stale_revision_and_no_registry_mutation()
    {
        var parser = new CSharpMathSyntaxAdapter(); var limits = new MathServiceLimits();
        var initial = new MathExpression(Guid.NewGuid(), 1, "x"); var editor = new MathEditorSession(initial, parser, limits);
        var first = editor.EditVisual(1, new(MathVisualOperation.SquareRoot, 0, 1));
        Assert.Contains(@"\sqrt", first.LastValid.LaTeX); Assert.Equal(2, first.LastValid.Revision);
        Assert.Equal(CSharpMathSyntaxAdapter.Implementation, first.ParserImplementation);
        var before = MathObjectCodec.Encode(first.LastValid);
        Assert.Throws<InvalidOperationException>(() => editor.EditLaTeX(1, "y"));
        Assert.Equal(before, MathObjectCodec.Encode(editor.Snapshot().LastValid));
        var unknown = editor.EditLaTeX(2, @"\notARegisteredMathCommand{1}");
        Assert.NotNull(unknown.Diagnostic); Assert.Equal(before, MathObjectCodec.Encode(unknown.LastValid));
        Assert.Throws<InvalidOperationException>(() => editor.EditVisual(2, new(MathVisualOperation.Power, 0, 1)));
        var depth = new string('{', limits.MaxSyntaxDepth + 1) + "x" + new string('}', limits.MaxSyntaxDepth + 1);
        Assert.Equal("MathSyntaxDepthExceeded", parser.Parse(depth, limits).Diagnostic);
        Assert.Equal("MathCommandBudgetExceeded", parser.Parse(string.Concat(Enumerable.Repeat(@"\alpha", limits.MaxSyntaxCommands + 1)), limits).Diagnostic);
        Assert.True(parser.Parse(@"\frac{x^{2}}{\sqrt{y}}", limits).Succeeded);
        Assert.False(parser.Parse(@"\notARegisteredMathCommand{1}", limits).Succeeded);
        Assert.Equal(before, MathObjectCodec.Encode(editor.Snapshot().LastValid));
    }
    private static Button Button(SharedMathEditorControl editor, string name) =>
        Assert.Single(editor.GetVisualDescendants().OfType<Button>(), x => x.Name == name);
    private static async Task Click(SharedMathEditorControl editor, string name)
    {
        Button(editor, name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await editor.WhenActionsIdleAsync(); Dispatcher.UIThread.RunJobs();
    }
    private static int InkRows(byte[] pixels, int width, int first, int end) =>
        Enumerable.Range(first, Math.Max(0, end - first)).Count(y =>
            Enumerable.Range(0, width).Any(x => pixels[(y * width + x) * 4 + 3] > 128));
    private static byte[] Pixels(Bitmap bitmap)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return bytes;
    }
}
