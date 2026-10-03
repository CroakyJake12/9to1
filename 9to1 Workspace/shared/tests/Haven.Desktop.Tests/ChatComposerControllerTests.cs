using Avalonia.Headless.XUnit;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class ChatComposerControllerTests
{
    [AvaloniaFact]
    public void Formatting_preserves_surrounding_draft_selection_and_undo()
    {
        using var scene = new ChatHavenScene();
        var controller = ChatComposerController.For(scene.Chatbox);
        scene.Instruction.Text = "Before chosen after";
        scene.Instruction.SetSelection(7, 13);
        controller.SurroundSelection("**", "**");
        Assert.Equal("Before **chosen** after", scene.Instruction.Text);
        Assert.Equal("chosen", scene.Instruction.SelectedText);
        scene.Instruction.Undo();
        Assert.Equal("Before chosen after", scene.Instruction.Text);
        controller.SetExpanded(true);
        Assert.Equal(7, scene.InstructionViewport.GetValue(HavenProperties.ColumnSpan));
        controller.SetExpanded(false);
        Assert.Equal(1, scene.InstructionViewport.GetValue(HavenProperties.ColumnSpan));
        Assert.Equal("Before chosen after", scene.Instruction.Text);
    }

    [Fact]
    public void Bundled_catalogue_includes_joined_sequences_modifiers_and_accessible_names()
    {
        Assert.True(ComposerEmojiCatalogue.Entries.Count > 3000);
        Assert.Contains(ComposerEmojiCatalogue.Entries, entry => entry.Text.Contains('\u200d'));
        Assert.Contains(ComposerEmojiCatalogue.Entries, entry => entry.Text.Contains("🏽", StringComparison.Ordinal));
        Assert.All(ComposerEmojiCatalogue.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Name)));
    }
}
