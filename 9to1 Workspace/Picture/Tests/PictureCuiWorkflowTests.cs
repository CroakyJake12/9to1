using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;

namespace HavenOS.Images.Tests;

// This is an explicit local UI fixture, never an installed Home grant.
internal sealed class PictureFixtureReadiness : ICuiSceneReadiness
{
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready,
            "picture-ui-fixture", "Local Picture controls fixture"));
    }
}

public sealed class PictureCuiWorkflowTests
{
    private static readonly List<(MainWindow Window, Exception Failure)> RetainedFailedUiOwners = [];
    [AvaloniaFact]
    public async Task Standalone_startup_requires_Home_and_mounts_no_editing_actions()
    {
        var window = new MainWindow();
        try
        {
            var availability = await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture initialization");
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, availability.State);
            Assert.Equal("HomeStartupAttachmentUnavailable", availability.Code);
            Assert.Contains("Home", availability.Message);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => button.Name == "NewButton");
            Assert.False(window.IsActionAvailable("9to1.Picture.New"));
            await window.DispatchAsync("9to1.Picture.New", null, TestContext.Current.CancellationToken);
            Assert.Null(CurrentSession(window));
        }
        finally { await RetireAsync(window); }
    }

    [AvaloniaFact]
    public async Task Actual_readiness_is_awaited_before_the_editor_or_actions_are_published()
    {
        var readiness = new HeldReadiness();
        var window = new MainWindow(readiness);
        try
        {
            var original = window.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.False(original.IsCompleted);
            Assert.Same(original, window.OriginalInitialization);
            // Await actual readiness entry, or expose the actual initialization
            // failure. The captured driver need not run synchronously on return.
            var first = await Task.WhenAny(readiness.Entered.Task, original).ObserveOriginalAsync(window, "Picture held-readiness entry");
            if (ReferenceEquals(first, original)) await original;
            await readiness.Entered.Task.ObserveOriginalAsync(window, "Picture readiness callback entry");
            Assert.Equal(1, readiness.Calls);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => button.Name == "NewButton");
            Assert.False(window.IsActionAvailable("9to1.Picture.New"));
            readiness.Release.SetResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready,
                "picture-ui-fixture", "Local Picture controls fixture"));
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await original).State);
            Assert.Same(original, window.InitializeAsync());
            Assert.True(Find<Button>(window, "NewButton").IsEnabled);
            Assert.True(Find<Button>(window, "OpenButton").IsEnabled);
            Assert.True(Find<Button>(window, "MetadataPreserveButton").IsEnabled);
            Assert.False(Find<Button>(window, "ResizeButton").IsEnabled);
            Assert.False(Find<Button>(window, "ApplyCropButton").IsEnabled);
            Assert.False(Find<Button>(window, "UndoButton").IsEnabled);
        }
        finally
        {
            readiness.Release.TrySetResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready,
                "picture-ui-fixture", "Local Picture controls fixture"));
            await RetireAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task Real_CUI_actions_create_transform_undo_and_redo_the_same_editing_session()
    {
        var window = new MainWindow(new PictureFixtureReadiness());
        Exception? firstBodyFailure = null;
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture initialization");
            Find<NumericUpDown>(window, "WidthBox").Value = 40;
            Find<NumericUpDown>(window, "HeightBox").Value = 20;
            await window.DispatchAsync("9to1.Picture.New", null, TestContext.Current.CancellationToken);
            var session = Assert.IsType<PictureEditorSession>(CurrentSession(window));
            var id = session.Document.DocumentId;
            Assert.Equal((40, 20), (session.Document.CanvasWidth, session.Document.CanvasHeight));
            await window.DispatchAsync("9to1.Picture.RotateRight", null, TestContext.Current.CancellationToken);
            Assert.Same(session, CurrentSession(window));
            Assert.Equal((20, 40), (session.Document.CanvasWidth, session.Document.CanvasHeight));
            Assert.IsType<RotateOperation>(Assert.Single(session.Document.Operations));
            await window.DispatchAsync("9to1.Picture.Undo", null, TestContext.Current.CancellationToken);
            Assert.Equal((40, 20), (session.Document.CanvasWidth, session.Document.CanvasHeight));
            await window.DispatchAsync("9to1.Picture.Redo", null, TestContext.Current.CancellationToken);
            Assert.Equal((20, 40), (session.Document.CanvasWidth, session.Document.CanvasHeight));
            Assert.Equal(id, session.Document.DocumentId);
            await window.DispatchAsync("9to1.Picture.Undo", null, TestContext.Current.CancellationToken);
            // Settle the fixture through a genuine save. Undo does not pretend
            // the current revision was already saved; leave the file as evidence.
            var saved = Path.Combine(Path.GetTempPath(), "picture-cui-controls-" + Guid.NewGuid().ToString("N") + ".picture.json");
            await session.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(File.Exists(saved));
            Assert.False(session.IsDirty);
        }
        catch (Exception error)
        {
            firstBodyFailure = error;
            RetainedFailedUiOwners.Add((window, error));
            throw;
        }
        finally
        {
            if (firstBodyFailure is null) await RetireAsync(window);
        }
    }

    [AvaloniaFact]
    public void Canonical_font_bootstrap_uses_the_bundled_Montserrat_face()
    {
        var family = FontManager.Current.DefaultFontFamily;
        Assert.Equal("Montserrat", family.Name);
        Assert.NotNull(family.Key);
        Assert.Equal(new Uri("avares://CakeOS.Cui.Runtime/Assets/Fonts/MontserratStatic"), family.Key.Source);
        Assert.Contains("Montserrat", new Typeface(family, weight: FontWeight.SemiBold).GlyphTypeface.FamilyName,
            StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(CuiAppearance.SuperBright, 1080, 760)]
    [InlineData(CuiAppearance.Bright, 1080, 760)]
    [InlineData(CuiAppearance.Dark, 1080, 760)]
    [InlineData(CuiAppearance.SuperDark, 1080, 760)]
    [InlineData(CuiAppearance.SuperBright, 430, 860)]
    [InlineData(CuiAppearance.Bright, 430, 860)]
    [InlineData(CuiAppearance.Dark, 430, 860)]
    [InlineData(CuiAppearance.SuperDark, 430, 860)]
    public async Task Authored_CUI_editor_renders_in_all_appearances_at_desktop_and_compact_sizes(
        CuiAppearance appearance, int width, int height)
    {
        var window = new MainWindow(new PictureFixtureReadiness(), appearance) { Width = width, Height = height };
        try
        {
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture initialization")).State);
            window.Show(); window.UpdateLayout();
            var root = Assert.IsAssignableFrom<Control>(window.SceneHost.Content);
            Assert.True(root.Bounds.Width > 0 && root.Bounds.Height > 0);
            var crop = Find<TextBox>(window, "CropBoundsBox");
            Assert.Contains("Montserrat", new Typeface(crop.FontFamily, weight: FontWeight.SemiBold).GlyphTypeface.FamilyName,
                StringComparison.Ordinal);
            Assert.True(window.SceneHost.Resources.TryGetResource("CuiAppearance", window.SceneHost.ActualThemeVariant, out var actualAppearance));
            Assert.Equal(appearance.ToString(), actualAppearance);
            Assert.True(window.SceneHost.Resources.TryGetResource("CuiPanelBrush", window.SceneHost.ActualThemeVariant, out var panelBrush));
            var canvas = Find<Grid>(window, "CanvasPanel");
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(panelBrush).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(canvas.Background).Color);
            if (width >= 880)
            {
                var inspector = Find<StackPanel>(window, "PictureInspector");
                Assert.True(Math.Abs(canvas.Bounds.Y - inspector.Bounds.Y) < 1,
                    "The original canvas must start alongside the inspector, not centered below it.");
            }
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(width, height), frame.PixelSize);
            // Inspect the SAME fully rendered checkbox/template rectangles.
            // Compact layout must not overflow a half-grid or overlap labels.
            var comparisons = Find<WrapPanel>(window, "ComparisonOptions");
            var original = Find<CheckBox>(window, "CompareButton"); var sideBySide = Find<CheckBox>(window, "SideBySideButton");
            Assert.Equal(2, Grid.GetColumnSpan(comparisons));
            Assert.Same(comparisons, original.GetVisualParent()); Assert.Same(comparisons, sideBySide.GetVisualParent());
            Rect BoundsInWindow(Control control)
            {
                var location = control.TranslatePoint(default, window); Assert.NotNull(location);
                Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                return new Rect(location!.Value, control.Bounds.Size);
            }
            var visibleWindow = new Rect(window.Bounds.Size); var parent = BoundsInWindow(comparisons);
            Assert.True(visibleWindow.Contains(parent));
            var first = BoundsInWindow(original); var second = BoundsInWindow(sideBySide);
            Assert.True(parent.Contains(first)); Assert.True(parent.Contains(second)); Assert.False(first.Intersects(second));
            foreach (var checkbox in new[] { original, sideBySide })
            {
                var content = Assert.IsType<string>(checkbox.Content);
                var label = checkbox.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == content);
                Assert.True(BoundsInWindow(checkbox).Contains(BoundsInWindow(label)));
                Assert.True(label.DesiredSize.Width <= label.Bounds.Width && label.DesiredSize.Height <= label.Bounds.Height,
                    "The actual checkbox content must fit its arranged control without clipping.");
            }
            var destination = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(destination))
            {
                Directory.CreateDirectory(destination);
                using var file = File.Create(Path.Combine(destination, $"picture-cui-{appearance}-{width}x{height}.png"));
                frame.Save(file);
            }
        }
        finally { await RetireAsync(window); }
    }

    [AvaloniaFact]
    public async Task Responsive_layout_preserves_original_editor_control_and_viewport_identity()
    {
        var window = new MainWindow(new PictureFixtureReadiness()) { Width = 1080 };
        try
        {
            await window.InitializeAsync(TestContext.Current.CancellationToken).ObserveOriginalAsync(window, "Picture initialization");
            window.Show(); window.UpdateLayout();
            var crop = Find<TextBox>(window, "CropBoundsBox");
            var viewport = Find<Image>(window, "PreviewImage");
            crop.Text = "10, 20, 30, 40";
            window.Width = 430; window.UpdateLayout();
            Assert.Same(crop, Find<TextBox>(window, "CropBoundsBox"));
            Assert.Same(viewport, Find<Image>(window, "PreviewImage"));
            Assert.Equal("10, 20, 30, 40", crop.Text);
            var workspace = Find<Grid>(window, "PictureWorkspace");
            Assert.Single(workspace.ColumnDefinitions);
            Assert.Equal(2, workspace.RowDefinitions.Count);
        }
        finally { await RetireAsync(window); }
    }

    private static T Find<T>(MainWindow window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static PictureEditorSession? CurrentSession(MainWindow window) =>
        (PictureEditorSession?)typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static async Task RetireAsync(MainWindow window)
    {
        if (PictureOriginalSourceFixture.HasRetainedPendingSource(window)) return;
        window.Close();
        if (window.OriginalClose is { } close) await close.ObserveOriginalAsync(window, "Picture original window retirement");
    }

    private sealed class HeldReadiness : ICuiSceneReadiness
    {
        internal int Calls;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<CuiSceneAvailability> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            return new(Release.Task.WaitAsync(cancellationToken));
        }
    }
}
