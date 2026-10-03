using System.Text.Json;
using Haven.Application;
using Haven.Core;

// This probe executes unmodified production domain sources. It is not a browser,
// Writer layout engine, storage/provider test, or replacement product model.
var count = 0;
void Check(bool result, string assertion)
{
    count++;
    if (!result) throw new InvalidOperationException($"Assertion {count} failed: {assertion}");
}
void Run(string id, Action test)
{
    test();
    Console.WriteLine($"PASS {id}");
}

Run("B2-DOM-WRITE-01-selection-format", () =>
{
    var doc = NotesDocument.Create();
    var block = doc.Sections[0].Pages[0].Blocks[0];
    block.PlainText = "Alpha beta gamma";
    block.Runs = [new NotesTextRun { Text = block.PlainText }];
    var editor = new WriteDocumentEditor(doc);
    var documentId = doc.Id;
    var blockId = block.Id;
    editor.SelectBlock(blockId, 10, 6, 10);
    editor.ToggleSelectionCharacter(WriteCharacterFormat.Bold);
    Check(block.Runs.Count == 3, "selection splits run into three ranges");
    Check(block.Runs[0].Text == "Alpha " && !block.Runs[0].Bold, "prefix remains unformatted");
    Check(block.Runs[1].Text == "beta" && block.Runs[1].Bold, "selected exact range receives bold");
    Check(block.Runs[2].Text == " gamma" && !block.Runs[2].Bold, "suffix remains unformatted");
    Check(string.Concat(block.Runs.Select(r => r.Text)) == "Alpha beta gamma", "formatting preserves exact text");
    Check(doc.Id == documentId && block.Id == blockId, "formatting retains canonical IDs");
    editor.Undo();
    var restored = editor.Blocks().Single(b => b.Id == blockId);
    Check(string.Concat(restored.Runs.Select(r => r.Text)) == "Alpha beta gamma" && restored.Runs.All(r => !r.Bold), "undo restores original text and style");
    Check(editor.CanRedo, "undo records redo operation");
    editor.Redo();
    restored = editor.Blocks().Single(b => b.Id == blockId);
    Check(restored.Runs.Count == 3 && restored.Runs[1].Text == "beta" && restored.Runs[1].Bold, "redo restores exact selected formatting");
});

Run("B2-DOM-WRITE-02-existing-runs", () =>
{
    var doc = NotesDocument.Create();
    var block = doc.Sections[0].Pages[0].Blocks[0];
    block.PlainText = "One Two Three";
    block.Runs = [new NotesTextRun { Text = "One ", Italic = true }, new NotesTextRun { Text = "Two ", Bold = true }, new NotesTextRun { Text = "Three", Underline = true }];
    var editor = new WriteDocumentEditor(doc);
    editor.SelectBlock(block.Id, 9, 2, 9);
    editor.SetFontFamily("Aptos");
    Check(block.Runs.Count == 5, "selection splits existing runs precisely");
    Check(block.Runs[0].Text == "On" && block.Runs[0].FontFamily != "Aptos", "prefix retains original font");
    Check(block.Runs[1].Text == "e " && block.Runs[1].FontFamily == "Aptos" && block.Runs[1].Italic, "first selected fragment retains italic");
    Check(block.Runs[2].Text == "Two " && block.Runs[2].FontFamily == "Aptos" && block.Runs[2].Bold, "middle run retains bold");
    Check(block.Runs[3].Text == "T" && block.Runs[3].FontFamily == "Aptos" && block.Runs[3].Underline, "last selected fragment retains underline");
    Check(block.Runs[4].Text == "hree" && block.Runs[4].FontFamily != "Aptos", "suffix retains original font");
});

Run("B2-DOM-WRITE-03-range-comment", () =>
{
    var doc = NotesDocument.Create();
    var block = doc.Sections[0].Pages[0].Blocks[0];
    block.PlainText = "Alpha beta";
    block.Runs = [new NotesTextRun { Text = block.PlainText }];
    var editor = new WriteDocumentEditor(doc);
    editor.SelectBlock(block.Id, 10, 6, 10);
    editor.AddComment("Review");
    Check(doc.Comments.Count == 1, "one comment created");
    var comment = doc.Comments.Single();
    Check(comment.BlockId == block.Id && comment.StartOffset == 6 && comment.EndOffset == 10, "comment anchors selected semantic range");
    Check(comment.Text == "Review", "comment retains actual content");
    var json = JsonSerializer.Serialize(doc);
    var reopened = JsonSerializer.Deserialize<NotesDocument>(json) ?? throw new InvalidOperationException("model deserialization failed");
    Check(reopened.Id == doc.Id && reopened.Sections[0].Pages[0].Blocks[0].Id == block.Id, "model round trip preserves document/block IDs");
    Check(reopened.Comments.Single().Text == "Review" && reopened.Comments.Single().StartOffset == 6, "model round trip preserves comment content/range");
    Check(reopened.Sections[0].Pages[0].Blocks[0].PlainText == "Alpha beta", "model round trip preserves document content");
});

// Safe test-quality negative control: the actual check function must reject a
// deliberately false assertion. No production data or code is changed.
var rejected = false;
try { Check(false, "intentional isolated negative control"); }
catch (InvalidOperationException error) when (error.Message.Contains("intentional isolated negative control")) { rejected = true; }
if (!rejected) throw new InvalidOperationException("Negative control failed to detect a false assertion");
Console.WriteLine($"PASS B2-DOM-WRITE-04-assertion-negative-control; assertions={count}; cases=4; scope=domain-only");
