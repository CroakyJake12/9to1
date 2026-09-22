using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>RC2 schema-v3 coverage: formatting, styles, lists, tables, graphs, images, ink tools, undo, attachments.</summary>
public sealed class HavenRichBoardV3Tests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Runs: baseline + formatting ----------

    [Fact]
    public async Task Subscript_superscript_and_mixed_baselines_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "base.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Baseline"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "x2 + an");
                    HavenRichNotesOps.SetBlockRuns(rich, pageId, block.Id,
                    [
                        new HavenRichTextRun { Text = "x" },
                        new HavenRichTextRun { Text = "2", Baseline = HavenRichBaseline.Superscript, Bold = true },
                        new HavenRichTextRun { Text = " + a" },
                        new HavenRichTextRun { Text = "n", Baseline = HavenRichBaseline.Subscript, Italic = true },
                    ]);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var runs = reopened.Rich.Sections[0].Pages[0].Blocks[1].Runs;
            Assert.Equal(4, runs.Count);
            Assert.Equal(HavenRichBaseline.Normal, runs[0].Baseline);
            Assert.Equal(HavenRichBaseline.Superscript, runs[1].Baseline);
            Assert.True(runs[1].Bold);
            Assert.Equal(HavenRichBaseline.Subscript, runs[3].Baseline);
            Assert.True(runs[3].Italic);
            Assert.Equal("x2 + an", reopened.Rich.Sections[0].Pages[0].Blocks[1].PlainText);
        });
    }

    [Fact]
    public async Task Run_font_size_colors_and_clear_formatting_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "fmt.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Fmt"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "styled");
                    HavenRichNotesOps.SetBlockRuns(rich, pageId, block.Id,
                        [new HavenRichTextRun { Text = "styled" }]);
                    HavenRichNotesOps.SetRunFont(rich, pageId, block.Id, 0, "Georgia", 18);
                    HavenRichNotesOps.SetRunColors(rich, pageId, block.Id, 0, "#FFFF0000", "#FFFFFF00");
                    var plain = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "clear me");
                    HavenRichNotesOps.SetBlockRuns(rich, pageId, plain.Id,
                        [new HavenRichTextRun { Text = "clear me", Bold = true, Baseline = HavenRichBaseline.Superscript }]);
                    HavenRichNotesOps.ClearRunFormatting(rich, pageId, plain.Id, 0);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var run = reopened.Rich.Sections[0].Pages[0].Blocks[1].Runs[0];
            Assert.Equal("Georgia", run.FontFamily);
            Assert.Equal(18, run.FontSize);
            Assert.Equal("#FFFF0000", run.Foreground);
            Assert.Equal("#FFFFFF00", run.Background);
            var cleared = reopened.Rich.Sections[0].Pages[0].Blocks[2].Runs[0];
            Assert.False(cleared.Bold);
            Assert.Equal(HavenRichBaseline.Normal, cleared.Baseline);
        });
    }

    [Fact]
    public async Task Paragraph_alignment_spacing_and_indent_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "para.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Para"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "centred");
                    HavenRichNotesOps.SetBlockParagraph(rich, pageId, block.Id,
                        HavenRichAlignment.Center, 1.5, 12, 6, 2);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var block = reopened.Rich.Sections[0].Pages[0].Blocks[1];
            Assert.Equal(HavenRichAlignment.Center, block.Alignment);
            Assert.Equal(1.5, block.LineSpacing);
            Assert.Equal(12, block.SpaceBefore);
            Assert.Equal(6, block.SpaceAfter);
            Assert.Equal(2, block.IndentLevel);
        });
    }

    // ---------- Styles ----------

    [Fact]
    public void Built_in_styles_resolve_with_sensible_defaults()
    {
        var notes = HavenRichNotes.Create("S");
        Assert.Contains(notes.Styles, s => s.Id == "normal" && s.IsBuiltIn);
        Assert.Contains(notes.Styles, s => s.Id == "heading-1" && s.Name == "Header 1");
        Assert.Contains(notes.Styles, s => s.Id == "code" && s.FontFamily == "Cascadia Mono");
        var block = notes.Sections[0].Pages[0].Blocks[0];
        var resolved = HavenRichStyleResolver.Resolve(notes, block);
        Assert.Equal("normal", resolved.Id);
        Assert.Equal(HavenRichAlignment.Left, HavenRichStyleResolver.EffectiveAlignment(resolved, block));
    }

    [Fact]
    public async Task Custom_style_lifecycle_apply_persist_reopen_delete_fallback()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "styles.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Styles"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var style = HavenRichNotesOps.CreateCustomStyle(rich, "Key Formula");
                    HavenRichNotesOps.UpdateStyle(rich, style.Id, s =>
                    {
                        s.Bold = true;
                        s.FontSize = 16;
                        s.Background = "#FFFBBC04";
                    });
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "E = mc^2");
                    HavenRichNotesOps.ApplyStyleToBlock(rich, pageId, block.Id, style.Id);
                    var dupe = HavenRichNotesOps.DuplicateStyle(rich, style.Id, "Key Formula backup");
                    Assert.False(dupe.IsBuiltIn);
                });
                await session.SaveAsync();
            }

            await using (var reopened = await RichBoardSession.OpenAtPathAsync(store, path))
            {
                var style = Assert.Single(reopened.Rich.Styles, s => s.Name == "Key Formula");
                Assert.False(style.IsBuiltIn);
                Assert.True(style.Bold);
                Assert.Equal(16, style.FontSize);
                var block = reopened.Rich.Sections[0].Pages[0].Blocks
                    .First(b => b.PlainText == "E = mc^2");
                Assert.Equal(style.Id, block.StyleId);
                var resolved = HavenRichStyleResolver.Resolve(reopened.Rich, block);
                Assert.Equal("Key Formula", resolved.Name);

                await reopened.MutateAsync(rich => HavenRichNotesOps.DeleteCustomStyle(rich, style.Id));
                await reopened.SaveAsync();
                Assert.DoesNotContain(reopened.Rich.Styles, s => s.Name == "Key Formula");
                var fallback = reopened.Rich.Sections[0].Pages[0].Blocks
                    .First(b => b.PlainText == "E = mc^2");
                Assert.Equal(HavenRichStyles.NormalId, fallback.StyleId);
            }

            await using var final = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.DoesNotContain(final.Rich.Styles, s => s.Name == "Key Formula");
        });
    }

    [Fact]
    public void Built_in_styles_cannot_be_deleted_and_direct_props_win()
    {
        var notes = HavenRichNotes.Create("S");
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesOps.DeleteCustomStyle(notes, "normal"));
        var block = notes.Sections[0].Pages[0].Blocks[0];
        block.Alignment = HavenRichAlignment.Right;
        var resolved = HavenRichStyleResolver.Resolve(notes, block);
        Assert.Equal(HavenRichAlignment.Right, HavenRichStyleResolver.EffectiveAlignment(resolved, block));
    }

    // ---------- Lists ----------

    [Fact]
    public async Task Checklist_items_add_remove_nest_format_and_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "lists.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Lists"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Checklist, "first");
                    var second = HavenRichNotesOps.AddListItem(rich, pageId, block.Id, "second");
                    var third = HavenRichNotesOps.InsertListItem(rich, pageId, block.Id, 1, "middle");
                    HavenRichNotesOps.SetListItemLevel(rich, pageId, block.Id, third.Id, 1);
                    HavenRichNotesOps.SetListItemFormatting(rich, pageId, block.Id, second.Id,
                        bold: true, baseline: HavenRichBaseline.Superscript, foreground: "#FF00FF00");
                    HavenRichNotesOps.UpdateListItem(rich, pageId, block.Id, block.Items[0].Id, "FIRST", true);
                    Assert.True(HavenRichNotesOps.RemoveListItem(rich, pageId, block.Id, "nope") == false);
                    Assert.True(HavenRichNotesOps.RemoveListItem(rich, pageId, block.Id, third.Id));
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var items = reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Checklist).Items;
            Assert.Equal(2, items.Count);
            Assert.Equal("FIRST", items[0].Text);
            Assert.True(items[0].Checked);
            Assert.Equal("second", items[1].Text);
            Assert.True(items[1].Bold);
            Assert.Equal(HavenRichBaseline.Superscript, items[1].Baseline);
            Assert.Equal("#FF00FF00", items[1].Foreground);
        });
    }

    // ---------- Tables ----------

    [Fact]
    public async Task Table_structure_style_and_formatted_cells_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "tables.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Tables"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Table);
                    HavenRichNotesOps.InsertTableRow(rich, pageId, block.Id, 3);
                    HavenRichNotesOps.InsertTableColumn(rich, pageId, block.Id, 3);
                    Assert.Equal(4, block.Table!.Rows.Count);
                    Assert.Equal(4, block.Table.Rows[0].Cells.Count);
                    Assert.True(HavenRichNotesOps.DeleteTableRow(rich, pageId, block.Id, 3));
                    Assert.True(HavenRichNotesOps.DeleteTableColumn(rich, pageId, block.Id, 3));
                    HavenRichNotesOps.SetColumnWidth(rich, pageId, block.Id, 0, 220);
                    HavenRichNotesOps.SetTableStyle(rich, pageId, block.Id,
                        background: "#FFFFFFFF", borderColor: "#FF1A73E8", borderWidth: 2,
                        borderStyle: HavenRichBorderStyle.Solid, cornerRadius: 8, alternatingRows: true);
                    var cellId = block.Table.Rows[1].Cells[1].Id;
                    HavenRichNotesOps.UpdateTableCell(rich, pageId, block.Id, cellId, "x²");
                    HavenRichNotesOps.SetCellStyle(rich, pageId, block.Id, cellId,
                        alignH: HavenRichAlignment.Center, alignV: HavenRichCellVerticalAlignment.Middle,
                        background: "#FFFFFF00", foreground: "#FF000000", bold: true,
                        baseline: HavenRichBaseline.Superscript);
                    HavenRichNotesOps.SetCellRuns(rich, pageId, block.Id, cellId,
                        [new HavenRichTextRun { Text = "x" }, new HavenRichTextRun { Text = "2", Baseline = HavenRichBaseline.Superscript, Bold = true }]);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var table = reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Table).Table!;
            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(3, table.Rows[0].Cells.Count);
            Assert.Equal("#FFFFFFFF", table.Background);
            Assert.Equal("#FF1A73E8", table.BorderColor);
            Assert.Equal(2, table.BorderWidth);
            Assert.Equal(HavenRichBorderStyle.Solid, table.BorderStyle);
            Assert.Equal(8, table.CornerRadius);
            Assert.True(table.AlternatingRows);
            Assert.Equal(220, table.ColumnWidths[0]);
            var cell = table.Rows[1].Cells[1];
            Assert.Equal("x2", cell.Text);
            Assert.Equal(HavenRichAlignment.Center, cell.AlignmentH);
            Assert.Equal(HavenRichCellVerticalAlignment.Middle, cell.AlignmentV);
            Assert.Equal("#FFFFFF00", cell.Background);
            Assert.True(cell.Bold);
            Assert.Equal(2, cell.Runs.Count);
            Assert.Equal(HavenRichBaseline.Superscript, cell.Runs[1].Baseline);
        });
    }

    // ---------- Graphs ----------

    [Fact]
    public void Graph_expression_evaluator_is_deterministic_and_safe()
    {
        Assert.True(HavenGraphExpression.TryEvaluate("y = x^2", 3, out var square) && square == 9);
        Assert.True(HavenGraphExpression.TryEvaluate("2x + 5", 3, out var linear) && linear == 11);
        Assert.True(HavenGraphExpression.TryEvaluate("y = sin(x)", 0, out var sine) && sine == 0);
        Assert.True(HavenGraphExpression.TryEvaluate("e^x", 0, out var exp) && exp == 1);
        Assert.True(HavenGraphExpression.TryEvaluate("ln(e)", 0, out var ln) && Math.Abs(ln - 1) < 1e-9);
        Assert.True(HavenGraphExpression.TryEvaluate("1/x", 2, out var inv) && inv == 0.5);
        Assert.False(HavenGraphExpression.TryEvaluate("1/x", 0, out _));
        Assert.False(HavenGraphExpression.TryEvaluate("bogus + (", 1, out _));
        Assert.False(HavenGraphExpression.TryEvaluate("os.execute('x')", 1, out _));
        Assert.Equal("x^2", HavenGraphExpression.StripFunctionPrefix("y = x^2"));
        var samples = HavenGraphExpression.Sample("y = x", null, null, -1, 1, 5);
        Assert.Equal(5, samples.Count);
        Assert.Equal(-1, samples[0].X);
    }

    [Fact]
    public async Task Graph_block_full_lifecycle_survives_reopen_editable()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "graph.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Graphs"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddGraphBlock(rich, pageId);
                    HavenRichNotesOps.UpdateGraphExpression(rich, pageId, block.Id,
                        block.Graph!.Expressions[0].Id, text: "y = sin(x)", color: "#FFFF0000", lineWidth: 3);
                    var second = HavenRichNotesOps.AddGraphExpression(rich, pageId, block.Id, "y = x^2");
                    HavenRichNotesOps.UpdateGraphExpression(rich, pageId, block.Id, second.Id,
                        domainMin: -5, domainMax: 5);
                    HavenRichNotesOps.SetGraphViewport(rich, pageId, block.Id, new HavenRichGraphViewport
                        { XMin = -6, XMax = 6, YMin = -2, YMax = 2 });
                    HavenRichNotesOps.SetGraphOptions(rich, pageId, block.Id,
                        showGrid: true, showAxes: true, xLabel: "time", yLabel: "value");
                    HavenRichNotesOps.AddGraphPoint(rich, pageId, block.Id, Math.PI, 0, "root");
                    Assert.Throws<InvalidOperationException>(() =>
                        HavenRichNotesOps.UpdateGraphExpression(rich, pageId, block.Id, second.Id,
                            domainMin: 5, domainMax: -5));
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var graph = reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Graph).Graph!;
            Assert.Equal(2, graph.Expressions.Count);
            Assert.Equal("y = sin(x)", graph.Expressions[0].Text);
            Assert.Equal("#FFFF0000", graph.Expressions[0].Color);
            Assert.Equal("y = x^2", graph.Expressions[1].Text);
            Assert.Equal(-5, graph.Expressions[1].DomainMin);
            Assert.Equal(-6, graph.Viewport.XMin);
            Assert.Equal("time", graph.XLabel);
            var point = Assert.Single(graph.Points);
            Assert.Equal(Math.PI, point.X);
            Assert.Equal("root", point.Label);

            // Still editable after reopen: toggle, pan, zoom, remove.
            await reopened.MutateAsync(rich =>
            {
                var pageId = rich.Sections[0].Pages[0].Id;
                var blockId = rich.Sections[0].Pages[0].Blocks
                    .First(b => b.Kind == HavenRichBlockKind.Graph).Id;
                HavenRichNotesOps.UpdateGraphExpression(rich, pageId, blockId,
                    graph.Expressions[1].Id, visible: false);
                HavenRichNotesOps.PanGraphViewport(rich, pageId, blockId, 0.1, 0);
                HavenRichNotesOps.ZoomGraphViewport(rich, pageId, blockId, 2, 0, 0);
                HavenRichNotesOps.RemoveGraphPoint(rich, pageId, blockId, 0);
            });
            await reopened.SaveAsync();
            Assert.False(reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Graph).Graph!.Expressions[1].Visible);
            Assert.Empty(reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Graph).Graph!.Points);
        });
    }

    // ---------- Images / dividers ----------

    [Fact]
    public async Task Image_and_divider_blocks_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "media.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Media"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var image = HavenRichNotesOps.AddImageBlock(rich, pageId, new HavenRichImage
                    {
                        DisplayName = "diagram.png",
                        MediaType = "image/png",
                        DataBase64 = Convert.ToBase64String("fake-png"u8.ToArray()),
                        SizeBytes = 8
                    });
                    HavenRichNotesOps.UpdateImage(rich, pageId, image.Id, img =>
                    {
                        img.Width = 480;
                        img.Alignment = HavenRichAlignment.Center;
                        img.AltText = "Sine wave diagram";
                    });
                    var divider = HavenRichNotesOps.AddDividerBlock(rich, pageId);
                    HavenRichNotesOps.UpdateDivider(rich, pageId, divider.Id, 3, HavenRichBorderStyle.Dashed, "#FF00FF00");
                    Assert.True(HavenRichNotesOps.RemoveBlock(rich, pageId, "missing") == false);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var image = reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Image).Image!;
            Assert.Equal("diagram.png", image.DisplayName);
            Assert.Equal(480, image.Width);
            Assert.Equal(HavenRichAlignment.Center, image.Alignment);
            Assert.Equal("Sine wave diagram", image.AltText);
            Assert.Equal("fake-png"u8.ToArray(), Convert.FromBase64String(image.DataBase64!));
            var divider = reopened.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Kind == HavenRichBlockKind.Divider).Divider!;
            Assert.Equal(3, divider.Thickness);
            Assert.Equal(HavenRichBorderStyle.Dashed, divider.LineStyle);

            await reopened.MutateAsync(rich =>
            {
                var pageId = rich.Sections[0].Pages[0].Id;
                Assert.True(HavenRichNotesOps.RemoveBlock(rich, pageId,
                    rich.Sections[0].Pages[0].Blocks.First(b => b.Kind == HavenRichBlockKind.Divider).Id));
            });
            await reopened.SaveAsync();
            Assert.DoesNotContain(reopened.Rich.Sections[0].Pages[0].Blocks,
                b => b.Kind == HavenRichBlockKind.Divider);
        });
    }

    // ---------- Ink tools ----------

    [Fact]
    public async Task Ink_tools_pressure_eraser_selection_and_view_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "ink.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Ink"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    HavenRichNotesOps.AddInkStroke(rich, pageId, new HavenRichInkStroke
                    {
                        Tool = HavenRichInkTool.Highlighter,
                        Color = "#80FFFF00",
                        Width = 12,
                        Points = [new() { X = 1, Y = 1, Pressure = 0.9 }, new() { X = 50, Y = 50, Pressure = 0.4 }]
                    });
                    HavenRichNotesOps.AddInkStroke(rich, pageId, new HavenRichInkStroke
                    {
                        Points = [new() { X = 500, Y = 500 }]
                    });
                    HavenRichNotesOps.SetInkStrokeTool(rich, pageId, 1, HavenRichInkTool.Eraser);
                    HavenRichNotesOps.SetInkStrokeSelection(rich, pageId, 0, true);
                    HavenRichNotesOps.SetInkView(rich, pageId, 100, 200, 2);
                    Assert.Equal(0, HavenRichNotesOps.EraseInkAt(rich, pageId, 10, 10, 2));
                    Assert.Equal(1, HavenRichNotesOps.EraseInkAt(rich, pageId, 500, 500, 5));
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var stroke = Assert.Single(reopened.Rich.Sections[0].Pages[0].Ink);
            Assert.Equal(HavenRichInkTool.Highlighter, stroke.Tool);
            Assert.True(stroke.Selected);
            Assert.Equal(0.9, stroke.Points[0].Pressure);
            var view = reopened.Rich.Sections[0].Pages[0].InkView;
            Assert.Equal(100, view.PanX);
            Assert.Equal(2, view.Zoom);
        });
    }

    [Fact]
    public async Task RC1_native_ink_without_tool_fields_migrates_with_pen_defaults()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "rc1ink.9to1board");
            var v2 = new
            {
                format = HavenBoardDocument.FormatIdentity,
                schemaVersion = 2,
                documentId = Guid.NewGuid(),
                createdUtc = DateTimeOffset.UtcNow,
                modifiedUtc = DateTimeOffset.UtcNow,
                snapshot = (object?)null,
                richNotes = new
                {
                    title = "RC1 ink",
                    version = 1L,
                    sections = new[]
                    {
                        new
                        {
                            id = "sec-1",
                            title = "Notes",
                            pages = new[]
                            {
                                new
                                {
                                    id = "page-1",
                                    title = "Start here",
                                    order = 0,
                                    canvasWidth = 1200.0,
                                    canvasHeight = 900.0,
                                    blocks = new object[]
                                    {
                                        new { id = "block-1", kind = 0, order = 0, styleId = "normal", plainText = "hello" }
                                    },
                                    ink = new[]
                                    {
                                        new
                                        {
                                            points = new[] { new { x = 5.0, y = 6.0 } },
                                            width = 2.5,
                                            color = "#FF1A73E8"
                                        }
                                    },
                                    canvas = new object[] { }
                                }
                            }
                        }
                    }
                }
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(v2, Json));

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal(HavenBoardLoadDisposition.MigratedSchema, store.LastLoadDisposition);
            var stroke = Assert.Single(reopened.Rich.Sections[0].Pages[0].Ink);
            Assert.Equal(HavenRichInkTool.Pen, stroke.Tool);
            Assert.Equal(0.5, stroke.Points[0].Pressure);
            Assert.False(stroke.Selected);
            Assert.Contains(reopened.Rich.Styles, s => s.Id == "normal");
        });
    }

    // ---------- Undo / redo ----------

    [Fact]
    public async Task Undo_redo_round_trip_and_reopen_shows_undone_state()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "undo.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Undo"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                    HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "v1"));
                await session.MutateAsync(rich =>
                    HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "v2"));
                Assert.True(session.CanUndo);

                Assert.True(await session.UndoAsync());
                Assert.DoesNotContain(session.Rich.Sections[0].Pages[0].Blocks, b => b.PlainText == "v2");
                Assert.True(session.CanRedo);

                Assert.True(await session.RedoAsync());
                Assert.Contains(session.Rich.Sections[0].Pages[0].Blocks, b => b.PlainText == "v2");

                Assert.True(await session.UndoAsync());
                Assert.True(await session.UndoAsync());
                Assert.False(session.CanUndo);
                Assert.False(await session.UndoAsync());
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.DoesNotContain(reopened.Rich.Sections[0].Pages[0].Blocks, b => b.PlainText == "v1");
        });
    }

    // ---------- Attachments ----------

    [Fact]
    public async Task Six_mb_attachment_embeds_and_round_trips()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var source = Path.Combine(root, "six.bin");
            var payload = RandomNumberGenerator.GetBytes(6 * 1024 * 1024);
            await File.WriteAllBytesAsync(source, payload);
            var path = Path.Combine(root, "big-embed.9to1board");

            await using (var session = await RichBoardSession.CreateNewAsync(store, "Big embed"))
            {
                await session.SaveAsAsync(path);
                var attachment = await session.ImportAttachmentAsync(source);
                Assert.NotNull(attachment.DataBase64);
                Assert.Null(attachment.LocalReference);
                Assert.Equal(payload.Length, attachment.SizeBytes);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "file below");
                    HavenRichNotesOps.AttachToBlock(rich, pageId, block.Id, attachment);
                });
                await session.SaveAsync();
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var resolved = await reopened.ResolveAttachmentAsync(
                reopened.Rich.Sections[0].Pages[0].Blocks.First(b => b.Attachment is not null).Attachment!);
            Assert.Equal(HavenAttachmentStatus.Available, resolved.Status);
            Assert.Equal(payload, resolved.EmbeddedBytes);
        });
    }

    [Fact]
    public async Task Large_attachment_uses_portable_sidecar_and_survives_move()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var source = Path.Combine(root, "large.bin");
            var payload = RandomNumberGenerator.GetBytes(26 * 1024 * 1024);
            await File.WriteAllBytesAsync(source, payload);
            var path = Path.Combine(root, "Sidecar Board.9to1board");

            string blobPath;
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Sidecar"))
            {
                await session.SaveAsAsync(path);
                var attachment = await session.ImportAttachmentAsync(source);
                Assert.Null(attachment.DataBase64);
                Assert.StartsWith("sidecar:", attachment.LocalReference, StringComparison.Ordinal);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Paragraph, "large file");
                    HavenRichNotesOps.AttachToBlock(rich, pageId, block.Id, attachment);
                });
                await session.SaveAsync();
                var resolved = await session.ResolveAttachmentAsync(attachment);
                Assert.Equal(HavenAttachmentStatus.Sidecar, resolved.Status);
                Assert.NotNull(resolved.SidecarPath);
                blobPath = resolved.SidecarPath!;
                Assert.Equal(payload, await File.ReadAllBytesAsync(blobPath));
            }

            // Move board + sidecar dir together: resolution keeps working relatively.
            var movedDir = Path.Combine(root, "moved");
            Directory.CreateDirectory(movedDir);
            var movedBoard = Path.Combine(movedDir, "Sidecar Board.9to1board");
            File.Copy(path, movedBoard);
            var sidecarSource = Path.Combine(root, "Sidecar Board.files");
            var sidecarTarget = Path.Combine(movedDir, "Sidecar Board.files");
            CopyDirectory(sidecarSource, sidecarTarget);

            await using var moved = await RichBoardSession.OpenAtPathAsync(store, movedBoard);
            var movedAttachment = moved.Rich.Sections[0].Pages[0].Blocks
                .First(b => b.Attachment is not null).Attachment!;
            var movedResolved = await moved.ResolveAttachmentAsync(movedAttachment);
            Assert.Equal(HavenAttachmentStatus.Sidecar, movedResolved.Status);
            Assert.Equal(payload, await File.ReadAllBytesAsync(movedResolved.SidecarPath!));

            // Sidecar left behind: honest Missing, never blank.
            File.Delete(movedResolved.SidecarPath!);
            var missing = await moved.ResolveAttachmentAsync(movedAttachment);
            Assert.Equal(HavenAttachmentStatus.Missing, missing.Status);
            Assert.Contains(".files", missing.Message, StringComparison.Ordinal);
        });
    }

    // ---------- Migration / update safety ----------

    [Fact]
    public async Task V2_RC1_content_migrates_losslessly_to_v3()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "rc1full.9to1board");
            var styleId = "custom-gone";
            var v2Json = JsonSerializer.Serialize(new
            {
                format = HavenBoardDocument.FormatIdentity,
                schemaVersion = 2,
                documentId = Guid.NewGuid(),
                createdUtc = DateTimeOffset.UtcNow,
                modifiedUtc = DateTimeOffset.UtcNow,
                snapshot = (object?)null,
                richNotes = new
                {
                    title = "RC1 full",
                    version = 7L,
                    sections = new[]
                    {
                        new
                        {
                            id = "sec-a",
                            title = "Topics",
                            pages = new[]
                            {
                                new
                                {
                                    id = "page-a",
                                    title = "Notes",
                                    order = 0,
                                    canvasWidth = 1200.0,
                                    canvasHeight = 900.0,
                                    blocks = new object[]
                                    {
                                        new { id = "b-para", kind = 0, order = 0, styleId = "heading-1", plainText = "Intro", runs = new[] { new { text = "Intro", bold = true } } },
                                        new { id = "b-check", kind = 4, order = 1, styleId = styleId, plainText = "", items = new[] { new { id = "i-1", text = "Revise", @checked = true, level = 0 } } },
                                        new { id = "b-table", kind = 5, order = 2, styleId = "normal", plainText = "", table = new { rows = new[] { new { cells = new[] { new { id = "c-1", text = "A" }, new { id = "c-2", text = "B" } } } } } },
                                    },
                                    ink = new[] { new { points = new[] { new { x = 1.0, y = 2.0 } }, width = 2.5, color = "#FF1A73E8" } },
                                    canvas = new[] { new { id = "cv-1", kind = "Text", text = "Idea", x = 10.0, y = 20.0, width = 260.0, height = 160.0 } }
                                }
                            }
                        }
                    }
                }
            }, Json);
            await File.WriteAllTextAsync(path, v2Json);

            await using var session = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal(HavenBoardLoadDisposition.MigratedSchema, store.LastLoadDisposition);
            var page = session.Rich.Sections[0].Pages[0];
            Assert.Equal("Intro", page.Blocks[0].PlainText);
            Assert.True(page.Blocks[0].Runs[0].Bold);
            // Unknown style repaired to Paragraph, content kept.
            Assert.Equal("normal", page.Blocks[1].StyleId);
            Assert.Equal("Revise", page.Blocks[1].Items[0].Text);
            Assert.True(page.Blocks[1].Items[0].Checked);
            Assert.Equal("B", page.Blocks[2].Table!.Rows[0].Cells[1].Text);
            Assert.Single(page.Ink);
            Assert.Equal("Idea", Assert.Single(page.Canvas).Text);
            Assert.Contains(session.Rich.Styles, s => s.Id == "normal");
            await session.SaveAsync();

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal("Intro", reopened.Rich.Sections[0].Pages[0].Blocks[0].PlainText);
            Assert.Equal("Revise", reopened.Rich.Sections[0].Pages[0].Blocks[1].Items[0].Text);
        });
    }

    [Fact]
    public async Task Failed_migration_preserves_original_bytes()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "bad.9to1board");
            const string corrupt = "{ \"format\": \"9to1.board\", \"schemaVersion\": 2, broken";
            await File.WriteAllTextAsync(path, corrupt);
            await Assert.ThrowsAsync<InvalidDataException>(() => RichBoardSession.OpenAtPathAsync(store, path));
            Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        });
    }

    // ---------- Performance sanity ----------

    [Fact]
    public async Task Substantial_board_opens_saves_and_edits_within_generous_bounds()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "perf.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Perf"))
            {
                await session.SaveAsAsync(path);
                await session.MutateAsync(rich =>
                {
                    rich.Sections.Clear();
                    for (var s = 0; s < 8; s++)
                    {
                        var section = new HavenRichSection { Title = $"Section {s}" };
                        section.Pages.Clear();
                        var pages = s == 0 ? 8 : 6;
                        for (var p = 0; p < pages; p++)
                        {
                            var page = new HavenRichPage { Title = $"Page {s}-{p}" };
                            page.Blocks.Clear();
                            for (var b = 0; b < 6; b++)
                                page.Blocks.Add(HavenRichBlock.Paragraph($"Paragraph {s}-{p}-{b} with revision content."));
                            var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(4, 4) };
                            page.Blocks.Add(table);
                            page.Blocks.Add(new HavenRichBlock
                            {
                                Kind = HavenRichBlockKind.Graph,
                                Graph = new HavenRichGraph
                                {
                                    Expressions = [new HavenRichGraphExpression { Text = "y = sin(x)" }, new HavenRichGraphExpression { Text = "y = x^2" }]
                                }
                            });
                            page.Ink.Add(new HavenRichInkStroke
                            {
                                Points = [new() { X = 1, Y = 1 }, new() { X = 20, Y = 30 }]
                            });
                            section.Pages.Add(page);
                        }
                        rich.Sections.Add(section);
                    }
                });
                var saveStart = DateTimeOffset.UtcNow;
                await session.SaveAsync();
                Assert.True(DateTimeOffset.UtcNow - saveStart < TimeSpan.FromSeconds(20));
            }

            var openStart = DateTimeOffset.UtcNow;
            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.True(DateTimeOffset.UtcNow - openStart < TimeSpan.FromSeconds(20));
            Assert.Equal(50, reopened.Rich.Sections.SelectMany(s => s.Pages).Count());
            await reopened.MutateAsync(rich =>
            {
                for (var i = 0; i < 10; i++)
                    HavenRichNotesOps.AddBlock(rich, rich.Sections[0].Pages[0].Id,
                        HavenRichBlockKind.Paragraph, $"Rapid {i}");
            });
            var flushStart = DateTimeOffset.UtcNow;
            await reopened.FlushAsync();
            Assert.True(DateTimeOffset.UtcNow - flushStart < TimeSpan.FromSeconds(20));
            Assert.Contains(reopened.Rich.Sections[0].Pages[0].Blocks, b => b.PlainText == "Rapid 9");
        });
    }

    // ---------- helpers ----------

    private static async Task WithStoreAsync(Func<JsonFileHavenBoardStore, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-v3-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await test(store, root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
    }
}
