using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>RC2 subject acceptance: Maths, Law and Computer Science boards through save/reopen.</summary>
public sealed class HavenBoardSubjectTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Maths_board_full_workflow_survives_reopen()
    {
        await WithSubjectAsync("A-Level Maths.9to1board", async (store, path) =>
        {
            await using (var session = await RichBoardSession.CreateNewAsync(store, "A-Level Maths"))
            {
                await session.SaveAsAsync(path);
                var formula = await session.MutateAndReturnAsync(rich =>
                {
                    var style = HavenRichNotesOps.CreateCustomStyle(rich, "Key Formula");
                    HavenRichNotesOps.UpdateStyle(rich, style.Id, s =>
                    {
                        s.Bold = true;
                        s.FontSize = 16;
                        s.Background = "#FFFBBC04";
                    });
                    return style.Id;
                });
                await session.MutateAsync(rich =>
                {
                    var section = HavenRichNotesOps.AddSection(rich, "Pure Mathematics");
                    var page = HavenRichNotesOps.AddPage(rich, section.Id, "Trigonometry");
                    var h1 = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Heading, "Trigonometry");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, h1.Id, "heading-1");
                    var h2 = HavenRichNotesOps.AddBlock(rich, pageId: page.Id, HavenRichBlockKind.Heading, "Identities");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, h2.Id, "heading-2");
                    var eq = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph, "sin2x");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, eq.Id, formula);
                    HavenRichNotesOps.SetBlockRuns(rich, page.Id, eq.Id,
                    [
                        new HavenRichTextRun { Text = "sin" },
                        new HavenRichTextRun { Text = "2x", Baseline = HavenRichBaseline.Superscript, Bold = true },
                        new HavenRichTextRun { Text = " and H" },
                        new HavenRichTextRun { Text = "2", Baseline = HavenRichBaseline.Subscript },
                        new HavenRichTextRun { Text = "O" },
                    ]);
                    var check = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Checklist, "Memorise double-angle");
                    HavenRichNotesOps.UpdateListItem(rich, page.Id, check.Id, check.Items[0].Id, null, true);
                    HavenRichNotesOps.AddListItem(rich, pageId: page.Id, blockId: check.Id, "Practise R-formulae");
                    var table = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Table);
                    HavenRichNotesOps.SetTableStyle(rich, page.Id, table.Id,
                        borderColor: "#FF1A73E8", borderWidth: 2, borderStyle: HavenRichBorderStyle.Solid,
                        cornerRadius: 6, alternatingRows: true);
                    HavenRichNotesOps.UpdateTableCell(rich, page.Id, table.Id, table.Table!.Rows[0].Cells[0].Id, "Angle");
                    HavenRichNotesOps.SetCellStyle(rich, page.Id, table.Id, table.Table.Rows[0].Cells[0].Id,
                        alignH: HavenRichAlignment.Center, bold: true, background: "#FFE8EDF3");
                    var graph = HavenRichNotesOps.AddGraphBlock(rich, page.Id);
                    HavenRichNotesOps.UpdateGraphExpression(rich, page.Id, graph.Id,
                        graph.Graph!.Expressions[0].Id, text: "y = sin(x)");
                    HavenRichNotesOps.AddGraphExpression(rich, page.Id, graph.Id, "y = x^2");
                    HavenRichNotesOps.AddInkStroke(rich, page.Id, new HavenRichInkStroke
                    {
                        Tool = HavenRichInkTool.Pen,
                        Points = [new() { X = 5, Y = 5, Pressure = 0.8 }, new() { X = 60, Y = 40, Pressure = 0.5 }]
                    });
                    HavenRichNotesOps.AddImageBlock(rich, page.Id, new HavenRichImage
                    {
                        DisplayName = "unit-circle.png",
                        MediaType = "image/png",
                        DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("unit-circle-bytes")),
                        SizeBytes = 17,
                        AltText = "Unit circle"
                    });
                    var attach = new HavenRichAttachmentRef
                    {
                        DisplayName = "formulae.txt",
                        MediaType = "text/plain",
                        DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("sin2x = 2sinx cosx")),
                        SizeBytes = 19
                    };
                    var note = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph, "See attached sheet");
                    HavenRichNotesOps.AttachToBlock(rich, page.Id, note.Id, attach);
                    HavenRichNotesOps.AddCanvasObject(rich, page.Id, "Text", "CAST diagram", 100, 200);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var rich = reopened.Rich;
            Assert.Contains(rich.Styles, s => s.Name == "Key Formula" && s.Bold && s.FontSize == 16);
            var page = rich.Sections.First(s => s.Title == "Pure Mathematics").Pages.First(p => p.Title == "Trigonometry");
            Assert.Equal("heading-1", page.Blocks.First(b => b.PlainText == "Trigonometry").StyleId);
            var eq = page.Blocks.First(b => b.StyleId == rich.Styles.First(s => s.Name == "Key Formula").Id);
            Assert.Equal(HavenRichBaseline.Superscript, eq.Runs[1].Baseline);
            Assert.Equal(HavenRichBaseline.Subscript, eq.Runs[3].Baseline);
            var check = page.Blocks.First(b => b.Kind == HavenRichBlockKind.Checklist);
            Assert.True(check.Items[0].Checked);
            Assert.Equal(2, check.Items.Count);
            var table = page.Blocks.First(b => b.Kind == HavenRichBlockKind.Table).Table!;
            Assert.Equal(HavenRichBorderStyle.Solid, table.BorderStyle);
            Assert.True(table.AlternatingRows);
            Assert.Equal(HavenRichAlignment.Center, table.Rows[0].Cells[0].AlignmentH);
            var graph = page.Blocks.First(b => b.Kind == HavenRichBlockKind.Graph).Graph!;
            Assert.Contains(graph.Expressions, e => e.Text == "y = sin(x)");
            Assert.Contains(graph.Expressions, e => e.Text == "y = x^2");
            Assert.Equal(HavenRichInkTool.Pen, Assert.Single(page.Ink).Tool);
            Assert.Equal("unit-circle.png", page.Blocks.First(b => b.Kind == HavenRichBlockKind.Image).Image!.DisplayName);
            Assert.Equal("formulae.txt", page.Blocks.First(b => b.Attachment is not null).Attachment!.DisplayName);
            Assert.Equal("CAST diagram", Assert.Single(page.Canvas).Text);
        });
    }

    [Fact]
    public async Task Law_board_custom_styles_persist_with_applications()
    {
        await WithSubjectAsync("A-Level Law.9to1board", async (store, path) =>
        {
            await using (var session = await RichBoardSession.CreateNewAsync(store, "A-Level Law"))
            {
                await session.SaveAsAsync(path);
                await session.MutateAsync(rich =>
                {
                    var @case = HavenRichNotesOps.CreateCustomStyle(rich, "Case");
                    HavenRichNotesOps.UpdateStyle(rich, @case.Id, s => { s.Bold = true; s.Foreground = "#FF0D47A1"; });
                    var statute = HavenRichNotesOps.CreateCustomStyle(rich, "Statute");
                    HavenRichNotesOps.UpdateStyle(rich, statute.Id, s => { s.Italic = true; s.Background = "#FFFFF3E0"; });
                    var eval = HavenRichNotesOps.CreateCustomStyle(rich, "Evaluation");
                    HavenRichNotesOps.UpdateStyle(rich, eval.Id, s => { s.Underline = true; });

                    var section = HavenRichNotesOps.AddSection(rich, "Criminal Law");
                    var page = HavenRichNotesOps.AddPage(rich, section.Id, "Murder");
                    var h = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Heading, "Actus reus");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, h.Id, "heading-1");
                    var longPara = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph,
                        "The actus reus of murder is the unlawful killing of a reasonable person in being under the Queen's peace. " +
                        "This requires detailed analysis of causation in fact and causation in law across the leading authorities.");
                    HavenRichNotesOps.SetBlockParagraph(rich, page.Id, longPara.Id,
                        HavenRichAlignment.Justify, 1.5, 6, 6, 0);
                    var caseBlock = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph,
                        "R v White [1910] 2 KB 124 — but-for test");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, caseBlock.Id, @case.Id);
                    var statuteBlock = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph,
                        "Homicide Act 1957, s 1 — abolished constructive malice");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, statuteBlock.Id, statute.Id);
                    var evalBlock = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph,
                        "Reform evaluation: Law Commission favours a ladder of homicide offences.");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, evalBlock.Id, eval.Id);
                    var runBlock = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph, "formatted");
                    HavenRichNotesOps.SetBlockRuns(rich, page.Id, runBlock.Id,
                        [new HavenRichTextRun { Text = "important", Bold = true, Foreground = "#FFB00020" }]);
                    var bullets = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.BulletList, "Factual causation");
                    HavenRichNotesOps.AddListItem(rich, pageId: page.Id, blockId: bullets.Id, "Legal causation");
                    var numbered = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.NumberedList, "Step one");
                    var nested = HavenRichNotesOps.AddListItem(rich, pageId: page.Id, blockId: numbered.Id, "Sub-step");
                    HavenRichNotesOps.SetListItemLevel(rich, page.Id, numbered.Id, nested.Id, 1);
                    var check = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Checklist, "Read judgment");
                    HavenRichNotesOps.UpdateListItem(rich, page.Id, check.Id, check.Items[0].Id, null, true);
                    var table = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Table);
                    HavenRichNotesOps.UpdateTableCell(rich, page.Id, table.Id, table.Table!.Rows[0].Cells[0].Id, "Case");
                    HavenRichNotesOps.UpdateTableCell(rich, page.Id, table.Id, table.Table.Rows[0].Cells[1].Id, "Principle");
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var rich = reopened.Rich;
            foreach (var name in new[] { "Case", "Statute", "Evaluation" })
                Assert.Contains(rich.Styles, s => s.Name == name && !s.IsBuiltIn);
            var page = rich.Sections.First(s => s.Title == "Criminal Law").Pages.First(p => p.Title == "Murder");
            var caseId = rich.Styles.First(s => s.Name == "Case").Id;
            var caseBlock = Assert.Single(page.Blocks, b => b.StyleId == caseId);
            Assert.Contains("White", caseBlock.PlainText, StringComparison.Ordinal);
            Assert.Contains(page.Blocks, b => b.StyleId == rich.Styles.First(s => s.Name == "Statute").Id);
            Assert.Contains(page.Blocks, b => b.StyleId == rich.Styles.First(s => s.Name == "Evaluation").Id);
            var para = page.Blocks.First(b => b.PlainText.StartsWith("The actus reus"));
            Assert.Equal(HavenRichAlignment.Justify, para.Alignment);
            var bullets = page.Blocks.First(b => b.Kind == HavenRichBlockKind.BulletList);
            Assert.Equal(2, bullets.Items.Count);
            var numbered = page.Blocks.First(b => b.Kind == HavenRichBlockKind.NumberedList);
            Assert.Equal(1, numbered.Items[1].Level);
            Assert.True(page.Blocks.First(b => b.Kind == HavenRichBlockKind.Checklist).Items[0].Checked);
            Assert.Equal("Principle", page.Blocks.First(b => b.Kind == HavenRichBlockKind.Table)
                .Table!.Rows[0].Cells[1].Text);
        });
    }

    [Fact]
    public async Task Computer_science_board_full_workflow_survives_reopen()
    {
        await WithSubjectAsync("A-Level Computer Science.9to1board", async (store, path) =>
        {
            await using (var session = await RichBoardSession.CreateNewAsync(store, "A-Level Computer Science"))
            {
                await session.SaveAsAsync(path);
                await session.MutateAsync(rich =>
                {
                    var section = HavenRichNotesOps.AddSection(rich, "Algorithms");
                    var page = HavenRichNotesOps.AddPage(rich, section.Id, "Sorting");
                    var h = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Heading, "Quicksort");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, h.Id, "heading-2");
                    var code = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph,
                        "def quicksort(xs):\n    return sorted(xs)");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, code.Id, "code");
                    var steps = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.NumberedList, "Pick a pivot");
                    HavenRichNotesOps.AddListItem(rich, pageId: page.Id, blockId: steps.Id, "Partition");
                    HavenRichNotesOps.AddListItem(rich, pageId: page.Id, blockId: steps.Id, "Recurse");
                    var table = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Table);
                    HavenRichNotesOps.SetTableStyle(rich, page.Id, table.Id,
                        background: "#FFECEFF1", borderColor: "#FF37474F", borderWidth: 1,
                        borderStyle: HavenRichBorderStyle.Dashed, cornerRadius: 4);
                    HavenRichNotesOps.UpdateTableCell(rich, page.Id, table.Id, table.Table!.Rows[0].Cells[0].Id, "Algorithm");
                    HavenRichNotesOps.UpdateTableCell(rich, page.Id, table.Id, table.Table.Rows[0].Cells[1].Id, "Average");
                    HavenRichNotesOps.SetCellStyle(rich, page.Id, table.Id, table.Table.Rows[0].Cells[0].Id,
                        alignH: HavenRichAlignment.Left, alignV: HavenRichCellVerticalAlignment.Middle,
                        background: "#FFCFD8DC");
                    HavenRichNotesOps.AddInkStroke(rich, page.Id, new HavenRichInkStroke
                    {
                        Points = [new() { X = 2, Y = 2 }, new() { X = 40, Y = 60 }]
                    });
                    var attach = new HavenRichAttachmentRef
                    {
                        DisplayName = "notes.md",
                        MediaType = "text/plain",
                        DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("# sorting")),
                        SizeBytes = 9
                    };
                    var note = HavenRichNotesOps.AddBlock(rich, page.Id, HavenRichBlockKind.Paragraph, "cheatsheet");
                    HavenRichNotesOps.AttachToBlock(rich, page.Id, note.Id, attach);
                    HavenRichNotesOps.AddCanvasObject(rich, page.Id, "Text", "partition diagram", 50, 50);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var page = reopened.Rich.Sections.First(s => s.Title == "Algorithms").Pages.First(p => p.Title == "Sorting");
            Assert.Equal("code", page.Blocks.First(b => b.PlainText.StartsWith("def quicksort")).StyleId);
            Assert.Equal(3, page.Blocks.First(b => b.Kind == HavenRichBlockKind.NumberedList).Items.Count);
            var table = page.Blocks.First(b => b.Kind == HavenRichBlockKind.Table).Table!;
            Assert.Equal(HavenRichBorderStyle.Dashed, table.BorderStyle);
            Assert.Equal("#FFECEFF1", table.Background);
            Assert.Equal(4, table.CornerRadius);
            Assert.Equal(HavenRichCellVerticalAlignment.Middle, table.Rows[0].Cells[0].AlignmentV);
            Assert.Single(page.Ink);
            Assert.Equal("notes.md", page.Blocks.First(b => b.Attachment is not null).Attachment!.DisplayName);
            Assert.Equal("partition diagram", Assert.Single(page.Canvas).Text);
        });
    }

    private static async Task WithSubjectAsync(string fileName, Func<JsonFileHavenBoardStore, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-subjects", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await test(store, Path.Combine(root, fileName));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

file static class RichBoardSessionTestExtensions
{
    public static async Task<T> MutateAndReturnAsync<T>(
        this RichBoardSession session, Func<HavenRichNotes, T> mutation)
    {
        var result = default(T);
        await session.MutateAsync(rich => result = mutation(rich));
        return result!;
    }
}
