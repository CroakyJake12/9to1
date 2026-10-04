using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Go;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class OverlayWorkspaceWindowTests
{
    [AvaloniaFact]
    public void Show_and_activate_focuses_the_visible_overlay_shell_in_expanded_and_collapsed_states()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new OverlaySessionState(
            Guid.NewGuid(),
            "go",
            "Test overlay",
            null,
            false,
            true,
            new OverlaySurfaceGeometry(520, 320, 120, 120),
            null,
            now,
            now,
            null);
        var backend = new GoPage(new HavenEventBus());
        var window = new OverlayWorkspaceWindow(session, backend);

        try
        {
            window.ApplySnapshot(new OverlayWorkspaceSnapshot(session.Id, [session]));
            window.ShowAndActivate();

            Assert.True(window.ShellScene.ComposerInput.State.HasFlag(HavenElementState.Focused));
            Assert.True(window.ShellControl.IsFocused);
            Assert.Equal(HavenAccessibleRole.Input, window.ShellScene.ComposerInput.Accessibility.Role);
            Assert.True(window.ShellScene.ComposerInput.Accessibility.Focusable);
            Assert.Equal("Ask Haven anything", window.ShellScene.ComposerInput.Accessibility.AccessibleName);
            // The focus transition may still have an animated value on the first frame.
            // Check the focus-state target rather than the transient animation sample.
            Assert.Equal(HavenLength.Px(2), window.ShellScene.ComposerInput.GetValue(HavenProperties.BorderWidth, HavenValueSource.State));
            Assert.Equal("AccentSecondary", window.ShellScene.ComposerInput.GetValue(HavenProperties.BorderColor, HavenValueSource.State));
            Assert.False(backend.Route.Instruction.State.HasFlag(HavenElementState.Focused));

            var collapsed = session with { IsCollapsed = true };
            window.ApplySnapshot(new OverlayWorkspaceSnapshot(session.Id, [collapsed]));
            window.ShowAndActivate();

            Assert.True(window.ShellScene.CollapsedPromptButton.State.HasFlag(HavenElementState.Focused));
            Assert.True(window.ShellControl.IsFocused);
            Assert.Equal(HavenAccessibleRole.Button, window.ShellScene.CollapsedPromptButton.Accessibility.Role);
            Assert.True(window.ShellScene.CollapsedPromptButton.Accessibility.Focusable);
            Assert.Equal("Ask Haven about your Screen", window.ShellScene.CollapsedPromptButton.Accessibility.AccessibleName);
            Assert.Equal(HavenLength.Px(2), window.ShellScene.CollapsedPromptButton.GetValue(HavenProperties.BorderWidth, HavenValueSource.State));
            Assert.Equal("AccentSecondary", window.ShellScene.CollapsedPromptButton.GetValue(HavenProperties.BorderColor, HavenValueSource.State));
            Assert.False(window.ShellScene.ComposerInput.State.HasFlag(HavenElementState.Focused));
            Assert.False(backend.Route.Instruction.State.HasFlag(HavenElementState.Focused));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Visual_root_is_shell_only_even_when_execution_backends_are_present()
    {
        var shell = new HavenSceneControl();
        var chatBackend = new Border();
        var goBackend = new Border();

        var root = OverlayWorkspaceWindow.CreateVisualRoot(shell, chatBackend, goBackend);

        Assert.Same(shell, root);
        Assert.NotSame(chatBackend, root);
        Assert.NotSame(goBackend, root);
    }

    [Fact]
    public void Restored_position_stays_on_negative_coordinate_secondary_monitor()
    {
        var secondaryWorkingArea = new PixelRect(-1920, 0, 1920, 1080);
        var desired = new PixelPoint(-1500, 120);

        var clamped = OverlayWorkspaceWindow.ClampPositionToWorkingArea(
            desired,
            secondaryWorkingArea,
            1,
            480,
            420);

        Assert.Equal(desired, clamped);
    }

    [Fact]
    public void Offscreen_restored_position_is_clamped_inside_monitor_working_area()
    {
        var workingArea = new PixelRect(0, 0, 1920, 1080);

        var clamped = OverlayWorkspaceWindow.ClampPositionToWorkingArea(
            new PixelPoint(5000, 3000),
            workingArea,
            1.5,
            480,
            420);

        Assert.Equal(new PixelPoint(1200, 450), clamped);
    }
}
