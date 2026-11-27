using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenInput = Haven.UI.Components.Input;
using HavenPage = Haven.UI.Components.Page;

namespace Haven.Desktop.Tests;

public sealed class HavenScenePointerInvalidationTests
{
    [AvaloniaFact]
    public void Unchanged_uncaptured_motion_does_not_repaint_or_remeasure_a_retained_scroll_scene()
    {
        var button = CreateButton();
        var root = CreateRoot(button);
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        for (var index = 0; index < 80; index++)
            root.Add(new Text { Content = $"Retained transcript row {index}: unchanged content." });
        WithScene(root, (window, scene) =>
        {
            Assert.True(root.MaxScrollY > 500);
            var point = Centre(button);
            window.MouseMove(point);
            window.UpdateLayout();
            Assert.True(button.State.HasFlag(HavenElementState.Hover));
            Assert.False(scene.HasActiveAnimations);
            var paints = scene.DiagRenderInvalidations;
            var measures = scene.DiagMeasurePasses;
            var reconciles = scene.DiagSubscriptionReconciles;

            for (var index = 0; index < 64; index++)
            {
                window.MouseMove(new Point(point.X + index % 8, point.Y + index % 3));
                window.UpdateLayout();
            }

            Assert.Equal(paints, scene.DiagRenderInvalidations);
            Assert.Equal(measures, scene.DiagMeasurePasses);
            Assert.Equal(reconciles, scene.DiagSubscriptionReconciles);
            Assert.True(button.State.HasFlag(HavenElementState.Hover));
            Assert.Same(button, root.Children[0]);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entering_and_leaving_a_real_button_still_changes_hover_and_repaints(bool leaveHost)
    {
        var button = CreateButton();
        WithScene(CreateRoot(button), (window, scene) =>
        {
            var outsideButton = new Point(380, 180);
            window.MouseMove(outsideButton);
            Assert.False(button.State.HasFlag(HavenElementState.Hover));
            var beforeEnter = scene.DiagRenderInvalidations;

            window.MouseMove(Centre(button));

            Assert.True(button.State.HasFlag(HavenElementState.Hover));
            Assert.True(scene.DiagRenderInvalidations > beforeEnter);
            var beforeLeave = scene.DiagRenderInvalidations;
            var leavePoint = leaveHost ? new Point(-20, -20) : outsideButton;

            window.MouseMove(leavePoint);

            Assert.False(button.State.HasFlag(HavenElementState.Hover));
            Assert.True(scene.DiagRenderInvalidations > beforeLeave);
            var afterLeave = scene.DiagRenderInvalidations;
            window.MouseMove(new Point(leavePoint.X + 2, leavePoint.Y + 2));
            Assert.Equal(afterLeave, scene.DiagRenderInvalidations);
            // Leaving the host after hover was already cleared is another visual no-op.
            window.MouseMove(new Point(-25, -25));
            Assert.Equal(afterLeave, scene.DiagRenderInvalidations);
        });
    }

    [AvaloniaFact]
    public void Captured_custom_drag_keeps_the_host_repaint_fallback_without_element_invalidation()
    {
        var target = new CapturedPaintTarget();
        target.SetValue(HavenProperties.Width, HavenLength.Px(300));
        target.SetValue(HavenProperties.Height, HavenLength.Px(160));
        WithScene(CreateRoot(target), (window, scene) =>
        {
            var start = new Point(target.Bounds.X + 20, target.Bounds.Y + 20);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Assert.True(target.IsDragging);
            var invalidations = 0;
            target.Invalidated += (_, _) => invalidations++;
            var beforeMove = scene.DiagRenderInvalidations;
            var finish = new Point(start.X + 100, start.Y + 35);

            window.MouseMove(finish, RawInputModifiers.LeftMouseButton);

            Assert.Equal(1, target.MoveCount);
            Assert.Equal(new HavenPoint(120, 55), target.LastPosition);
            Assert.Equal(0, invalidations);
            Assert.True(scene.DiagRenderInvalidations > beforeMove);
            var commands = new HavenSceneRenderer().Render(target);
            Assert.Contains(commands, command => command is HavenFillRoundedRectCommand fill &&
                fill.Rect.X == target.Bounds.X + target.LastPosition.X &&
                fill.Rect.Y == target.Bounds.Y + target.LastPosition.Y);

            window.MouseUp(finish, MouseButton.Left);
            Assert.False(target.IsDragging);
        });
    }

    [AvaloniaFact]
    public void Captured_input_selection_keeps_the_actual_selection_and_repaint()
    {
        var input = new HavenInput { Text = "Alpha Beta Gamma Delta" };
        input.SetValue(HavenProperties.Width, HavenLength.Px(340));
        input.SetValue(HavenProperties.Height, HavenLength.Px(72));
        input.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(8)));
        WithScene(CreateRoot(input), (window, scene) =>
        {
            var start = new Point(input.Bounds.X + 10, input.Bounds.Y + input.Bounds.Height / 2);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            var beforeMove = scene.DiagRenderInvalidations;
            var finish = new Point(start.X + 180, start.Y);

            window.MouseMove(finish, RawInputModifiers.LeftMouseButton);

            Assert.True(input.SelectionLength > 0);
            Assert.NotEmpty(input.SelectedText);
            Assert.True(scene.DiagRenderInvalidations > beforeMove);
            Assert.Contains(new HavenSceneRenderer().Render(input), command => command is HavenTextSelectionCommand);
            window.MouseUp(finish, MouseButton.Left);
        });
    }

    private static HavenButton CreateButton()
    {
        var button = new HavenButton { Content = "Retained hover target" };
        button.SetValue(HavenProperties.Width, HavenLength.Px(240));
        button.SetValue(HavenProperties.Height, HavenLength.Px(60));
        return button;
    }

    private static HavenPage CreateRoot(HavenElement child)
    {
        var root = new HavenPage { Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Background, "Surface");
        root.SetValue(HavenProperties.Width, HavenLength.Px(400));
        root.SetValue(HavenProperties.Height, HavenLength.Px(280));
        root.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        root.Add(child);
        return root;
    }

    private static Point Centre(HavenElement element) => new(
        element.Bounds.X + element.Bounds.Width / 2,
        element.Bounds.Y + element.Bounds.Height / 2);

    private static void WithScene(HavenElement root, Action<Window, HavenSceneControl> assertion)
    {
        var scene = new HavenSceneControl(new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(), () => true) { Root = root };
        var window = new Window { Width = 400, Height = 280, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            assertion(window, scene);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    // Exercises the existing custom-canvas contract, which may change drawing state during a
    // captured gesture without raising an element invalidation. The host retains that fallback.
    private sealed class CapturedPaintTarget : HavenElement, IHavenPointerInputTarget, IHavenDrawCommandSource
    {
        public bool IsDragging { get; private set; }
        public int MoveCount { get; private set; }
        public HavenPoint LastPosition { get; private set; }

        public bool PointerPressed(HavenPointerInput input)
        {
            IsDragging = true;
            LastPosition = input.LocalPosition;
            return true;
        }

        public bool PointerMoved(HavenPointerInput input)
        {
            if (!IsDragging) return false;
            MoveCount++;
            LastPosition = input.LocalPosition;
            return true;
        }

        public bool PointerReleased(HavenPointerInput input)
        {
            IsDragging = false;
            return true;
        }

        public void Draw(HavenDrawingContext context, double opacity) => context.Add(
            new HavenFillRoundedRectCommand(new HavenRect(Bounds.X + LastPosition.X,
                Bounds.Y + LastPosition.Y, 8, 8), new HavenTokenBrush("Accent"), 0, opacity));
    }
}
