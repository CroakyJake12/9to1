using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Present;

namespace Haven.Desktop.Tests;

/// <summary>
/// The Present header save indicator must report the state the page actually observed.
/// These are unit checks of the scene projection, not acceptance of the rendered shell.
/// </summary>
public sealed class PresentHeaderSaveStateTests
{
    [AvaloniaFact]
    public void New_scene_starts_without_claiming_a_saved_result()
    {
        using var scene = new PresentHavenScene();
        Assert.Equal("unknown", scene.SaveStateKind);
        Assert.Equal("Opening…", scene.SaveStateText.Content);
        Assert.DoesNotContain("Saved", scene.SaveStateText.Content);
    }

    [AvaloniaFact]
    public void Save_states_project_their_actual_labels_and_accessibility_names()
    {
        using var scene = new PresentHavenScene();

        scene.SetSaveState("saving");
        Assert.Equal("Saving…", scene.SaveStateText.Content);
        Assert.Equal("Presentation save state: Saving…", scene.SaveStatePill.Accessibility.AccessibleName);

        scene.SetSaveState("unsaved");
        Assert.Equal("Unsaved changes", scene.SaveStateText.Content);

        scene.SetSaveState("saved", "09:41 · v3");
        Assert.Equal("Saved · 09:41 · v3", scene.SaveStateText.Content);

        scene.SetSaveState("failed", "disk full");
        Assert.Equal("Save failed · disk full", scene.SaveStateText.Content);

        scene.SetSaveState("none");
        Assert.Equal("No deck open", scene.SaveStateText.Content);
    }

    [AvaloniaFact]
    public void Header_keeps_badge_title_and_save_state_inside_900_pixels()
    {
        using var scene = new PresentHavenScene();
        scene.SetDocument(PresentDocument.Create("Narrow deck"), 0, 1, 0);
        scene.SetSaveState("saved", "09:41 · v3");
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 900, Height = 700, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(scene.DeckBadge.Bounds.Width >= 40);
            Assert.True(scene.SaveStatePill.Bounds.Width >= 40);
            Assert.True(scene.SaveStatePill.Bounds.Right <= scene.Root.Bounds.Right + .01);
            Assert.True(scene.SaveStatePill.Bounds.Top >= scene.Root.Bounds.Top - .01);
        }
        finally { window.Content = null; window.Close(); }
    }
}
