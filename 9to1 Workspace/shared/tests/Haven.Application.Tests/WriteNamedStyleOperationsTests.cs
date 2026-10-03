using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed class WriteNamedStyleOperationsTests
{
    [Theory]
    [InlineData("heading-1", NotesBlockKind.Heading)]
    [InlineData("heading-2", NotesBlockKind.Heading)]
    [InlineData("quote", NotesBlockKind.Quote)]
    [InlineData("code", NotesBlockKind.Code)]
    [InlineData("custom", NotesBlockKind.Paragraph)]
    public void Named_style_materializes_formatting_preserves_run_identity_and_editor_undo(string id, NotesBlockKind kind)
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "One two";
        block.Runs = [new() { Text = "One ", Italic = true }, new() { Text = "two", Underline = true }];
        var runIds = block.Runs.Select(run => run.Id).ToArray();
        var before = JsonSerializer.Serialize(block);
        var style = new NotesNamedStyle
        {
            Id = id, Name = "Visible name",
            Character = new() { Text = "Never replace content", Bold = true, FontSize = 19, FontFamily = "Example", Foreground = "#123456" },
            Paragraph = new() { Alignment = NotesTextAlignment.Center, IndentLeft = 23, KeepWithNext = true }
        };
        document.Styles = [style];
        var detached = JsonSerializer.Deserialize<NotesBlock>(before)!;
        WriteNamedStyleOperations.Apply(detached, style);
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(block.Id);
        editor.ApplyStyle(id);
        Assert.Equal(JsonSerializer.Serialize(detached), JsonSerializer.Serialize(block));
        Assert.Equal(kind, block.Kind);
        Assert.Equal(id, block.StyleId);
        Assert.Equal(runIds, block.Runs.Select(run => run.Id));
        Assert.Equal(new[] { "One ", "two" }, block.Runs.Select(run => run.Text));
        Assert.All(block.Runs, run => { Assert.True(run.Bold); Assert.False(run.Italic); Assert.False(run.Underline); Assert.Equal(19, run.FontSize); });
        Assert.Equal(23, block.Paragraph.IndentLeft);
        Assert.True(editor.Undo());
        Assert.Equal(before, JsonSerializer.Serialize(document.Sections[0].Pages[0].Blocks[0]));
        Assert.True(editor.Redo());
        Assert.Equal(JsonSerializer.Serialize(detached), JsonSerializer.Serialize(document.Sections[0].Pages[0].Blocks[0]));
    }

    [Fact]
    public void Missing_run_materializes_existing_plain_text_without_replacing_block_identity()
    {
        var block = NotesBlock.CreateParagraph();
        block.PlainText = "Preserved";
        block.Runs.Clear();
        var id = block.Id;
        WriteNamedStyleOperations.Apply(block, new() { Id = "code", Character = new() { FontFamily = "Custom mono", FontSize = 17 } });
        var run = Assert.Single(block.Runs);
        Assert.Equal(id, block.Id);
        Assert.Equal("Preserved", run.Text);
        Assert.Equal("Custom mono", run.FontFamily);
        Assert.Equal(17, run.FontSize);
    }
}
