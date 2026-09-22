// Lossless-by-merge projection between the contract rich model and the
// CUI editor working model. Reads project everything the editor supports;
// writes apply ONLY deltas versus the baseline, so contract-only state
// (multi-run styling, extra table cells, canvas, attachments, image/divider
// blocks, ink strokes) survives editor round-trips untouched.

using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

internal static class Projector
{
    public static void Project(HavenRichNotes rich, RichBoardDocument view)
    {
        ArgumentNullException.ThrowIfNull(rich);
        ArgumentNullException.ThrowIfNull(view);
        view.Title = rich.Title;
        view.Sections = rich.Sections.Select(s => new RichBoardSection
        {
            Id = s.Id,
            Title = s.Title,
            Pages = s.Pages.Select(ProjectPage).ToList()
        }).ToList();
    }

    private static RichBoardPage ProjectPage(HavenRichPage page) => new()
    {
        Id = page.Id,
        Title = page.Title,
        Blocks = ProjectBlocks(page)
    };

    private static List<RichBoardBlock> ProjectBlocks(HavenRichPage page)
    {
        var blocks = new List<RichBoardBlock>();
        foreach (var block in page.Blocks)
        {
            switch (block.Kind)
            {
                case HavenRichBlockKind.Paragraph:
                case HavenRichBlockKind.Heading:
                    blocks.Add(new RichBoardBlock
                    {
                        Id = block.Id,
                        Kind = block.Kind == HavenRichBlockKind.Heading ? "heading" : "paragraph",
                        Text = block.PlainText,
                        StyleId = block.StyleId,
                        Bold = AllStyled(block, r => r.Bold),
                        Italic = AllStyled(block, r => r.Italic),
                        Underline = AllStyled(block, r => r.Underline),
                        Strike = AllStyled(block, r => r.StrikeThrough),
                        Baseline = CommonBaseline(block),
                        FontFamily = CommonText(block, r => r.FontFamily),
                        FontSize = CommonFontSize(block),
                        Foreground = CommonText(block, r => r.Foreground),
                        Background = CommonText(block, r => r.Background),
                        Alignment = block.Alignment.ToString().ToLowerInvariant(),
                        LineSpacing = block.LineSpacing,
                        SpaceBefore = block.SpaceBefore,
                        SpaceAfter = block.SpaceAfter,
                        IndentLevel = block.IndentLevel,
                        AttachmentId = block.Attachment?.Id,
                        AttachmentName = block.Attachment?.DisplayName,
                        AttachmentSize = block.Attachment?.SizeBytes ?? 0
                    });
                    break;
                case HavenRichBlockKind.Checklist:
                case HavenRichBlockKind.BulletList:
                case HavenRichBlockKind.NumberedList:
                    foreach (var item in block.Items)
                        blocks.Add(new RichBoardBlock
                        {
                            Id = $"{block.Id}:{item.Id}",
                            Kind = "checklist",
                            Text = item.Text,
                            StyleId = block.StyleId,
                            IsChecked = item.Checked,
                            Level = item.Level,
                            Bold = item.Bold,
                            Italic = item.Italic,
                            Underline = item.Underline,
                            Strike = item.StrikeThrough,
                            Baseline = item.Baseline.ToString().ToLowerInvariant(),
                            Foreground = item.Foreground
                        });
                    break;
                case HavenRichBlockKind.Table when block.Table is not null:
                    blocks.Add(ProjectTable(block));
                    break;
                case HavenRichBlockKind.Image when block.Image is not null:
                    blocks.Add(new RichBoardBlock
                    {
                        Id = block.Id,
                        Kind = "image",
                        Text = block.Image.DisplayName,
                        StyleId = block.StyleId,
                        Image = new RichImageView
                        {
                            DisplayName = block.Image.DisplayName,
                            MediaType = block.Image.MediaType,
                            DataBase64 = null,
                            Width = block.Image.Width,
                            Alignment = block.Image.Alignment.ToString().ToLowerInvariant(),
                            AltText = block.Image.AltText
                        },
                        AttachmentId = block.Attachment?.Id,
                        AttachmentName = block.Attachment?.DisplayName,
                        AttachmentSize = block.Attachment?.SizeBytes ?? 0
                    });
                    break;
                case HavenRichBlockKind.Divider when block.Divider is not null:
                    blocks.Add(new RichBoardBlock
                    {
                        Id = block.Id,
                        Kind = "divider",
                        StyleId = block.StyleId,
                        Divider = new RichDividerView
                        {
                            Thickness = block.Divider.Thickness,
                            LineStyle = block.Divider.LineStyle.ToString().ToLowerInvariant(),
                            Color = block.Divider.Color
                        }
                    });
                    break;
                case HavenRichBlockKind.Graph when block.Graph is not null:
                    blocks.Add(new RichBoardBlock
                    {
                        Id = block.Id,
                        Kind = "graph",
                        StyleId = block.StyleId,
                        Graph = ProjectGraph(block.Graph)
                    });
                    break;
                default:
                    break;
            }
        }
        blocks.Add(new RichBoardBlock
        {
            Id = $"{page.Id}:ink",
            Kind = "ink",
            Text = page.Ink.Count == 0 ? "No ink yet" : $"{page.Ink.Count} stroke(s)",
            InkStrokeCount = page.Ink.Count
        });
        return blocks;
    }

    private static RichBoardBlock ProjectTable(HavenRichBlock block)
    {
        var table = block.Table!;
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        var styles = new Dictionary<string, RichCellStyleView>(StringComparer.Ordinal);
        for (var r = 0; r < table.Rows.Count; r++)
            for (var c = 0; c < table.Rows[r].Cells.Count; c++)
            {
                var cell = table.Rows[r].Cells[c];
                cells[$"{r},{c}"] = cell.Text;
                styles[$"{r},{c}"] = new RichCellStyleView
                {
                    AlignH = cell.AlignmentH.ToString().ToLowerInvariant(),
                    AlignV = cell.AlignmentV.ToString().ToLowerInvariant(),
                    Background = cell.Background,
                    Foreground = cell.Foreground,
                    Bold = cell.Bold,
                    Italic = cell.Italic,
                    Underline = cell.Underline,
                    Baseline = cell.Baseline.ToString().ToLowerInvariant()
                };
            }
        return new RichBoardBlock
        {
            Id = block.Id,
            Kind = "table",
            StyleId = block.StyleId,
            TableCells = cells,
            TableStyle = new RichTableStyleView
            {
                Background = table.Background,
                BorderColor = table.BorderColor,
                BorderWidth = table.BorderWidth,
                BorderStyle = table.BorderStyle.ToString().ToLowerInvariant(),
                CornerRadius = table.CornerRadius,
                AlternatingRows = table.AlternatingRows
            },
            CellStyles = styles,
            TableRows = table.Rows.Count,
            TableCols = table.Rows.Count > 0 ? table.Rows[0].Cells.Count : 0,
            ColumnWidths = [.. table.ColumnWidths],
            AttachmentId = block.Attachment?.Id,
            AttachmentName = block.Attachment?.DisplayName,
            AttachmentSize = block.Attachment?.SizeBytes ?? 0
        };
    }

    private static RichGraphView ProjectGraph(HavenRichGraph graph) => new()
    {
        Expressions = graph.Expressions.Select(e => new RichGraphExpressionView
        {
            Id = e.Id,
            Text = e.Text,
            Visible = e.Visible,
            Color = e.Color,
            LineWidth = e.LineWidth,
            DomainMin = e.DomainMin,
            DomainMax = e.DomainMax
        }).ToList(),
        XMin = graph.Viewport.XMin,
        XMax = graph.Viewport.XMax,
        YMin = graph.Viewport.YMin,
        YMax = graph.Viewport.YMax,
        ShowGrid = graph.ShowGrid,
        ShowAxes = graph.ShowAxes,
        XLabel = graph.XLabel,
        YLabel = graph.YLabel,
        Points = graph.Points.Select(p => new RichGraphPointView { X = p.X, Y = p.Y, Label = p.Label }).ToList()
    };

    private static bool AllStyled(HavenRichBlock block, Func<HavenRichTextRun, bool> style) =>
        block.Runs.Count > 0 && block.Runs.All(style);

    private static string CommonBaseline(HavenRichBlock block)
    {
        if (block.Runs.Count == 0) return "normal";
        var first = block.Runs[0].Baseline;
        return block.Runs.All(r => r.Baseline == first) ? first.ToString().ToLowerInvariant() : "normal";
    }

    private static string CommonText(HavenRichBlock block, Func<HavenRichTextRun, string> pick)
    {
        if (block.Runs.Count == 0) return string.Empty;
        var first = pick(block.Runs[0]) ?? string.Empty;
        return block.Runs.All(r => (pick(r) ?? string.Empty) == first) ? first : string.Empty;
    }

    private static double CommonFontSize(HavenRichBlock block)
    {
        if (block.Runs.Count == 0) return 0;
        var first = block.Runs[0].FontSize;
        return block.Runs.All(r => r.FontSize == first) ? first : 0;
    }
}

internal static class MergeDeltas
{
    public static bool HasChanges(RichBoardDocument view, RichBoardDocument baseline) =>
        !WorkerJson(view).Equals(WorkerJson(baseline), StringComparison.Ordinal);

    public static void Apply(HavenRichNotes rich, RichBoardDocument view, RichBoardDocument baseline)
    {
        ArgumentNullException.ThrowIfNull(rich);
        if (view.Title != baseline.Title)
            rich.Title = view.Title;

        var baselineSections = baseline.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var contractSections = rich.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);

        if (view.Sections.Count > 0 && rich.Sections.Count > 0 &&
            !view.Sections.Any(s => contractSections.ContainsKey(s.Id)))
        {
            // Full replacement (e.g. New board): rebuild from the working model.
            rich.Sections.Clear();
            foreach (var section in view.Sections)
                ImportSectionTree(rich, section);
            HavenRichNotesOps.TouchNotes(rich);
            return;
        }

        foreach (var viewSection in view.Sections)
        {
            if (!contractSections.TryGetValue(viewSection.Id, out var contractSection))
            {
                contractSection = HavenRichNotesOps.ImportSection(rich, viewSection.Id, viewSection.Title);
                foreach (var viewPage in viewSection.Pages)
                    ImportPageTree(contractSection, viewPage);
                continue;
            }
            if (baselineSections.TryGetValue(viewSection.Id, out var baselineSection) &&
                viewSection.Title != baselineSection.Title)
                HavenRichNotesOps.RenameSection(rich, contractSection.Id, viewSection.Title);
            MergePages(rich, contractSection, viewSection,
                baselineSection?.Pages.ToDictionary(p => p.Id, StringComparer.Ordinal)
                ?? new Dictionary<string, RichBoardPage>(StringComparer.Ordinal));
        }
        HavenRichNotesOps.TouchNotes(rich);
    }

    private static void MergePages(
        HavenRichNotes rich, HavenRichSection section, RichBoardSection viewSection,
        Dictionary<string, RichBoardPage> baselinePages)
    {
        var contractPages = section.Pages.ToDictionary(p => p.Id, StringComparer.Ordinal);
        if (viewSection.Pages.Count > 0 && section.Pages.Count > 0 &&
            !viewSection.Pages.Any(p => contractPages.ContainsKey(p.Id)))
        {
            section.Pages.Clear();
            foreach (var viewPage in viewSection.Pages)
                ImportPageTree(section, viewPage);
            return;
        }

        foreach (var viewPage in viewSection.Pages)
        {
            if (!contractPages.TryGetValue(viewPage.Id, out var contractPage))
            {
                ImportPageTree(section, viewPage);
                continue;
            }
            if (baselinePages.TryGetValue(viewPage.Id, out var baselinePage) &&
                viewPage.Title != baselinePage.Title)
                HavenRichNotesOps.RenamePage(rich, contractPage.Id, viewPage.Title);
            MergeBlocks(rich, contractPage, viewPage,
                baselinePage?.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal)
                ?? new Dictionary<string, RichBoardBlock>(StringComparer.Ordinal));
        }
    }

    private static void MergeBlocks(
        HavenRichNotes rich, HavenRichPage page, RichBoardPage viewPage,
        Dictionary<string, RichBoardBlock> baselineBlocks)
    {
        var contractBlocks = page.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        foreach (var viewBlock in viewPage.Blocks)
        {
            baselineBlocks.TryGetValue(viewBlock.Id, out var baselineBlock);
            if (viewBlock.Kind == "ink")
                continue;
            if (TrySplitItemId(viewBlock.Id, out var parentId, out var itemId))
            {
                MergeItem(rich, page, parentId, itemId, viewBlock, baselineBlock);
                continue;
            }
            if (!contractBlocks.TryGetValue(viewBlock.Id, out var contractBlock))
            {
                ImportViewBlock(rich, page, viewBlock);
                continue;
            }
            if (baselineBlock is not null && viewBlock.StyleId != baselineBlock.StyleId)
                HavenRichNotesOps.ApplyStyleToBlock(rich, page.Id, contractBlock.Id, viewBlock.StyleId);
            switch (viewBlock.Kind)
            {
                case "paragraph":
                case "heading":
                    MergeTextBlock(rich, page, contractBlock, viewBlock, baselineBlock);
                    break;
                case "checklist":
                    // Plain-id checklist block created in the editor: a brand-new item.
                    MergeNewItem(rich, page, viewBlock);
                    break;
                case "table":
                    MergeTableStructure(rich, page, contractBlock, viewBlock, baselineBlock);
                    MergeTableCells(rich, page, contractBlock, viewBlock, baselineBlock);
                    MergeTableStyle(rich, page, contractBlock, viewBlock, baselineBlock);
                    break;
                case "image":
                    MergeImage(rich, page, contractBlock, viewBlock, baselineBlock);
                    break;
                case "divider":
                    MergeDivider(rich, page, contractBlock, viewBlock, baselineBlock);
                    break;
                case "graph":
                    MergeGraph(rich, page, contractBlock, viewBlock, baselineBlock);
                    break;
            }
        }
    }

    private static void MergeTextBlock(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (baselineBlock is null || viewBlock.Text != baselineBlock.Text)
            HavenRichNotesOps.UpdateParagraphText(rich, page.Id, contractBlock.Id, viewBlock.Text);
        if (baselineBlock is null || FormatChanged(viewBlock, baselineBlock))
            HavenRichNotesOps.SetBlockRuns(rich, page.Id, contractBlock.Id,
                [new HavenRichTextRun
                {
                    Text = viewBlock.Text,
                    Bold = viewBlock.Bold,
                    Italic = viewBlock.Italic,
                    Underline = viewBlock.Underline,
                    StrikeThrough = viewBlock.Strike,
                    Baseline = ParseBaseline(viewBlock.Baseline),
                    FontFamily = viewBlock.FontFamily,
                    FontSize = viewBlock.FontSize,
                    Foreground = viewBlock.Foreground,
                    Background = viewBlock.Background
                }]);
        if (baselineBlock is null || ParagraphChanged(viewBlock, baselineBlock))
            HavenRichNotesOps.SetBlockParagraph(rich, page.Id, contractBlock.Id,
                ParseAlignment(viewBlock.Alignment), viewBlock.LineSpacing,
                viewBlock.SpaceBefore, viewBlock.SpaceAfter, viewBlock.IndentLevel);
    }

    private static bool FormatChanged(RichBoardBlock current, RichBoardBlock baseline) =>
        current.Bold != baseline.Bold || current.Italic != baseline.Italic ||
        current.Underline != baseline.Underline || current.Strike != baseline.Strike ||
        current.Baseline != baseline.Baseline || current.FontFamily != baseline.FontFamily ||
        current.FontSize != baseline.FontSize || current.Foreground != baseline.Foreground ||
        current.Background != baseline.Background;

    private static bool ParagraphChanged(RichBoardBlock current, RichBoardBlock baseline) =>
        current.Alignment != baseline.Alignment || current.LineSpacing != baseline.LineSpacing ||
        current.SpaceBefore != baseline.SpaceBefore || current.SpaceAfter != baseline.SpaceAfter ||
        current.IndentLevel != baseline.IndentLevel;

    private static void MergeItem(
        HavenRichNotes rich, HavenRichPage page, string parentId, string itemId,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        var parent = page.Blocks.FirstOrDefault(b => b.Id == parentId);
        var existing = parent?.Items.FirstOrDefault(i => i.Id == itemId);
        if (existing is not null)
        {
            var textChanged = baselineBlock is null || viewBlock.Text != baselineBlock.Text;
            var checkChanged = baselineBlock is null || viewBlock.IsChecked != baselineBlock.IsChecked;
            if (textChanged || checkChanged)
                HavenRichNotesOps.UpdateListItem(rich, page.Id, parent!.Id, itemId,
                    textChanged ? viewBlock.Text : null,
                    checkChanged ? viewBlock.IsChecked : null);
            if (baselineBlock is null || viewBlock.Level != baselineBlock.Level)
                HavenRichNotesOps.SetListItemLevel(rich, page.Id, parent!.Id, itemId, viewBlock.Level);
            if (baselineBlock is null || ItemFormatChanged(viewBlock, baselineBlock))
                HavenRichNotesOps.SetListItemFormatting(rich, page.Id, parent!.Id, itemId,
                    bold: viewBlock.Bold, italic: viewBlock.Italic, underline: viewBlock.Underline,
                    strike: viewBlock.Strike, baseline: ParseBaseline(viewBlock.Baseline),
                    foreground: viewBlock.Foreground);
            return;
        }
        MergeNewItem(rich, page, NewViewBlock(itemId, viewBlock));
    }

    private static bool ItemFormatChanged(RichBoardBlock current, RichBoardBlock baseline) =>
        current.Bold != baseline.Bold || current.Italic != baseline.Italic ||
        current.Underline != baseline.Underline || current.Strike != baseline.Strike ||
        current.Baseline != baseline.Baseline || current.Foreground != baseline.Foreground;

    private static void MergeNewItem(HavenRichNotes rich, HavenRichPage page, RichBoardBlock viewBlock)
    {
        var target = page.Blocks.FirstOrDefault(b =>
            b.Kind is HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList);
        target ??= HavenRichNotesOps.ImportBlock(page, HavenRichBlockKind.Checklist, NewBlockId(page));
        var itemId = TrySplitItemId(viewBlock.Id, out _, out var item) ? item : NewItemId(target);
        if (target.Items.Any(i => i.Id == itemId))
            return;
        var added = HavenRichNotesOps.ImportListItem(target, itemId, viewBlock.Text, viewBlock.IsChecked);
        added.Level = Math.Clamp(viewBlock.Level, 0, 8);
        added.Bold = viewBlock.Bold;
        added.Italic = viewBlock.Italic;
        added.Underline = viewBlock.Underline;
        added.StrikeThrough = viewBlock.Strike;
        added.Baseline = ParseBaseline(viewBlock.Baseline);
        added.Foreground = viewBlock.Foreground;
    }

    private static void MergeTableStructure(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Table is null)
            return;
        var table = contractBlock.Table;
        var targetRows = viewBlock.TableRows > 0 ? viewBlock.TableRows : table.Rows.Count;
        var targetCols = viewBlock.TableCols > 0 ? viewBlock.TableCols : table.Rows[0].Cells.Count;
        targetRows = Math.Clamp(targetRows, 1, 100);
        targetCols = Math.Clamp(targetCols, 1, 50);
        while (table.Rows.Count < targetRows)
            HavenRichNotesOps.InsertTableRow(rich, page.Id, contractBlock.Id, table.Rows.Count);
        while (table.Rows.Count > targetRows)
        {
            if (!HavenRichNotesOps.DeleteTableRow(rich, page.Id, contractBlock.Id, table.Rows.Count - 1))
                break;
        }
        while (table.Rows[0].Cells.Count < targetCols)
            HavenRichNotesOps.InsertTableColumn(rich, page.Id, contractBlock.Id, table.Rows[0].Cells.Count);
        while (table.Rows[0].Cells.Count > targetCols)
        {
            if (!HavenRichNotesOps.DeleteTableColumn(rich, page.Id, contractBlock.Id, table.Rows[0].Cells.Count - 1))
                break;
        }
        var baselineWidths = baselineBlock?.ColumnWidths ?? [];
        for (var c = 0; c < viewBlock.ColumnWidths.Count && c < table.Rows[0].Cells.Count; c++)
        {
            var old = c < baselineWidths.Count ? baselineWidths[c] : 0;
            if (viewBlock.ColumnWidths[c] != old)
                HavenRichNotesOps.SetColumnWidth(rich, page.Id, contractBlock.Id, c, viewBlock.ColumnWidths[c]);
        }
    }

    private static void MergeTableStyle(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Table is null)
            return;
        var current = viewBlock.TableStyle;
        var old = baselineBlock?.TableStyle;
        if (old is not null && current.Background == old.Background && current.BorderColor == old.BorderColor &&
            current.BorderWidth == old.BorderWidth && current.BorderStyle == old.BorderStyle &&
            current.CornerRadius == old.CornerRadius && current.AlternatingRows == old.AlternatingRows)
            return;
        HavenRichNotesOps.SetTableStyle(rich, page.Id, contractBlock.Id,
            background: current.Background, borderColor: current.BorderColor,
            borderWidth: current.BorderWidth, borderStyle: ParseBorderStyle(current.BorderStyle),
            cornerRadius: current.CornerRadius, alternatingRows: current.AlternatingRows);
    }

    private static void MergeTableCells(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Table is null)
            return;
        var baselineCells = baselineBlock?.TableCells ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var baselineStyles = baselineBlock?.CellStyles ?? new Dictionary<string, RichCellStyleView>(StringComparer.Ordinal);
        foreach (var (key, text) in viewBlock.TableCells)
        {
            if (!baselineCells.TryGetValue(key, out var old) || old != text)
            {
                if (TryCellPosition(key, contractBlock.Table, out var row, out var col))
                    HavenRichNotesOps.UpdateTableCell(
                        rich, page.Id, contractBlock.Id, contractBlock.Table.Rows[row].Cells[col].Id, text);
            }
            if (viewBlock.CellStyles.TryGetValue(key, out var style) &&
                (!baselineStyles.TryGetValue(key, out var oldStyle) || CellStyleChanged(style, oldStyle)) &&
                TryCellPosition(key, contractBlock.Table, out var styleRow, out var styleCol))
            {
                HavenRichNotesOps.SetCellStyle(
                    rich, page.Id, contractBlock.Id, contractBlock.Table.Rows[styleRow].Cells[styleCol].Id,
                    alignH: ParseAlignment(style.AlignH), alignV: ParseCellV(style.AlignV),
                    background: style.Background, foreground: style.Foreground,
                    bold: style.Bold, italic: style.Italic, underline: style.Underline,
                    baseline: ParseBaseline(style.Baseline));
            }
        }
    }

    private static bool TryCellPosition(string key, HavenRichTable table, out int row, out int col)
    {
        row = -1;
        col = -1;
        var parts = key.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out row) || !int.TryParse(parts[1], out col))
            return false;
        return row >= 0 && row < table.Rows.Count && col >= 0 && col < table.Rows[row].Cells.Count;
    }

    private static bool CellStyleChanged(RichCellStyleView current, RichCellStyleView baseline) =>
        current.AlignH != baseline.AlignH || current.AlignV != baseline.AlignV ||
        current.Background != baseline.Background || current.Foreground != baseline.Foreground ||
        current.Bold != baseline.Bold || current.Italic != baseline.Italic ||
        current.Underline != baseline.Underline || current.Baseline != baseline.Baseline;

    private static void ImportSectionTree(HavenRichNotes rich, RichBoardSection viewSection)
    {
        var section = HavenRichNotesOps.ImportSection(rich, viewSection.Id, viewSection.Title);
        foreach (var viewPage in viewSection.Pages)
            ImportPageTree(section, viewPage);
    }

    private static void ImportPageTree(HavenRichSection section, RichBoardPage viewPage)
    {
        var page = HavenRichNotesOps.ImportPage(section, viewPage.Id, viewPage.Title);
        var checklistByParent = new Dictionary<string, HavenRichBlock>(StringComparer.Ordinal);
        foreach (var viewBlock in viewPage.Blocks)
        {
            if (viewBlock.Kind == "ink")
                continue;
            if (TrySplitItemId(viewBlock.Id, out var parentId, out var itemId))
            {
                if (!checklistByParent.TryGetValue(parentId, out var parent))
                {
                    parent = HavenRichNotesOps.ImportBlock(page, HavenRichBlockKind.Checklist, parentId);
                    checklistByParent[parentId] = parent;
                }
                HavenRichNotesOps.ImportListItem(parent, itemId, viewBlock.Text, viewBlock.IsChecked);
                continue;
            }
            ImportViewBlockInner(page, viewBlock, checklistByParent);
        }
    }

    private static void ImportViewBlock(HavenRichNotes rich, HavenRichPage page, RichBoardBlock viewBlock) =>
        ImportViewBlockInner(page, viewBlock, new Dictionary<string, HavenRichBlock>(StringComparer.Ordinal));

    private static void ImportViewBlockInner(
        HavenRichPage page, RichBoardBlock viewBlock, Dictionary<string, HavenRichBlock> checklistByParent)
    {
        switch (viewBlock.Kind)
        {
            case "paragraph":
            case "heading":
            {
                var kind = viewBlock.Kind == "heading" ? HavenRichBlockKind.Heading : HavenRichBlockKind.Paragraph;
                var block = HavenRichNotesOps.ImportBlock(page, kind, viewBlock.Id, viewBlock.Text);
                block.StyleId = string.IsNullOrEmpty(viewBlock.StyleId) ? "normal" : viewBlock.StyleId;
                block.Alignment = ParseAlignment(viewBlock.Alignment);
                block.LineSpacing = viewBlock.LineSpacing;
                block.SpaceBefore = viewBlock.SpaceBefore;
                block.SpaceAfter = viewBlock.SpaceAfter;
                block.IndentLevel = viewBlock.IndentLevel;
                if (viewBlock.Bold || viewBlock.Italic || viewBlock.Underline || viewBlock.Strike ||
                    viewBlock.Baseline != "normal" || !string.IsNullOrEmpty(viewBlock.FontFamily) ||
                    viewBlock.FontSize > 0 || !string.IsNullOrEmpty(viewBlock.Foreground) ||
                    !string.IsNullOrEmpty(viewBlock.Background))
                    block.Runs = [new HavenRichTextRun
                    {
                        Text = viewBlock.Text,
                        Bold = viewBlock.Bold,
                        Italic = viewBlock.Italic,
                        Underline = viewBlock.Underline,
                        StrikeThrough = viewBlock.Strike,
                        Baseline = ParseBaseline(viewBlock.Baseline),
                        FontFamily = viewBlock.FontFamily,
                        FontSize = viewBlock.FontSize,
                        Foreground = viewBlock.Foreground,
                        Background = viewBlock.Background
                    }];
                break;
            }
            case "checklist":
            {
                var parent = page.Blocks.FirstOrDefault(b =>
                    b.Kind is HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList);
                if (parent is null)
                {
                    parent = HavenRichNotesOps.ImportBlock(page, HavenRichBlockKind.Checklist, NewBlockId(page));
                    checklistByParent[parent.Id] = parent;
                }
                HavenRichNotesOps.ImportListItem(parent, NewItemId(parent), viewBlock.Text, viewBlock.IsChecked);
                break;
            }
            case "table":
            {
                var block = HavenRichNotesOps.ImportBlock(page, HavenRichBlockKind.Table, viewBlock.Id);
                if (block.Table is not null)
                {
                    var targetRows = Math.Clamp(viewBlock.TableRows, 1, 100);
                    while (block.Table.Rows.Count < targetRows)
                    {
                        var row = new HavenRichTableRow();
                        for (var c = 0; c < 3; c++)
                            row.Cells.Add(new HavenRichTableCell());
                        block.Table.Rows.Add(row);
                    }
                    while (block.Table.Rows.Count > targetRows)
                        block.Table.Rows.RemoveAt(block.Table.Rows.Count - 1);
                    var targetCols = Math.Clamp(viewBlock.TableCols, 1, 50);
                    foreach (var row in block.Table.Rows)
                    {
                        while (row.Cells.Count < targetCols)
                            row.Cells.Add(new HavenRichTableCell());
                        while (row.Cells.Count > targetCols)
                            row.Cells.RemoveAt(row.Cells.Count - 1);
                    }
                    foreach (var (key, text) in viewBlock.TableCells)
                    {
                        if (TryCellPosition(key, block.Table, out var row, out var col))
                            block.Table.Rows[row].Cells[col].Text = text;
                    }
                    foreach (var (key, style) in viewBlock.CellStyles)
                    {
                        if (TryCellPosition(key, block.Table, out var row, out var col))
                        {
                            var cell = block.Table.Rows[row].Cells[col];
                            cell.AlignmentH = ParseAlignment(style.AlignH);
                            cell.AlignmentV = ParseCellV(style.AlignV);
                            cell.Background = style.Background;
                            cell.Foreground = style.Foreground;
                            cell.Bold = style.Bold;
                            cell.Italic = style.Italic;
                            cell.Underline = style.Underline;
                            cell.Baseline = ParseBaseline(style.Baseline);
                        }
                    }
                    block.Table.Background = viewBlock.TableStyle.Background;
                    block.Table.BorderColor = viewBlock.TableStyle.BorderColor;
                    block.Table.BorderWidth = viewBlock.TableStyle.BorderWidth;
                    block.Table.BorderStyle = ParseBorderStyle(viewBlock.TableStyle.BorderStyle);
                    block.Table.CornerRadius = viewBlock.TableStyle.CornerRadius;
                    block.Table.AlternatingRows = viewBlock.TableStyle.AlternatingRows;
                    block.Table.ColumnWidths = [.. viewBlock.ColumnWidths];
                }
                if (!string.IsNullOrEmpty(viewBlock.StyleId))
                    block.StyleId = viewBlock.StyleId;
                break;
            }
            case "image":
            {
                if (viewBlock.Image?.DataBase64 is { Length: > 0 } data)
                {
                    var created = new HavenRichBlock
                    {
                        Id = viewBlock.Id,
                        Kind = HavenRichBlockKind.Image,
                        Order = page.Blocks.Count,
                        StyleId = string.IsNullOrEmpty(viewBlock.StyleId) ? "normal" : viewBlock.StyleId,
                        Image = new HavenRichImage
                        {
                            DisplayName = viewBlock.Image.DisplayName,
                            MediaType = viewBlock.Image.MediaType,
                            DataBase64 = data,
                            Width = viewBlock.Image.Width,
                            Alignment = ParseAlignment(viewBlock.Image.Alignment),
                            AltText = viewBlock.Image.AltText
                        }
                    };
                    page.Blocks.Add(created);
                }
                break;
            }
            case "divider":
            {
                var created = new HavenRichBlock
                {
                    Id = viewBlock.Id,
                    Kind = HavenRichBlockKind.Divider,
                    Order = page.Blocks.Count,
                    StyleId = string.IsNullOrEmpty(viewBlock.StyleId) ? "normal" : viewBlock.StyleId,
                    Divider = new HavenRichDivider
                    {
                        Thickness = viewBlock.Divider?.Thickness is double t and > 0 ? t : 1,
                        LineStyle = ParseBorderStyle(viewBlock.Divider?.LineStyle ?? "solid"),
                        Color = viewBlock.Divider?.Color ?? "#FF5F6368"
                    }
                };
                page.Blocks.Add(created);
                break;
            }
            case "graph":
            {
                var created = new HavenRichBlock
                {
                    Id = viewBlock.Id,
                    Kind = HavenRichBlockKind.Graph,
                    Order = page.Blocks.Count,
                    StyleId = string.IsNullOrEmpty(viewBlock.StyleId) ? "normal" : viewBlock.StyleId,
                    Graph = new HavenRichGraph()
                };
                if (viewBlock.Graph is not null)
                {
                    var source = viewBlock.Graph;
                    created.Graph.Expressions = source.Expressions
                        .Select(expression => new HavenRichGraphExpression
                        {
                            Id = expression.Id,
                            Text = expression.Text,
                            Visible = expression.Visible,
                            Color = expression.Color,
                            LineWidth = expression.LineWidth,
                            DomainMin = expression.DomainMin,
                            DomainMax = expression.DomainMax
                        }).ToList();
                    if (created.Graph.Expressions.Count == 0)
                        created.Graph.Expressions.Add(new HavenRichGraphExpression());
                    created.Graph.Viewport = new HavenRichGraphViewport
                    {
                        XMin = source.XMin, XMax = source.XMax, YMin = source.YMin, YMax = source.YMax
                    };
                    created.Graph.ShowGrid = source.ShowGrid;
                    created.Graph.ShowAxes = source.ShowAxes;
                    created.Graph.XLabel = source.XLabel;
                    created.Graph.YLabel = source.YLabel;
                    created.Graph.Points = source.Points
                        .Select(p => new HavenRichGraphPoint { X = p.X, Y = p.Y, Label = p.Label }).ToList();
                }
                page.Blocks.Add(created);
                break;
            }
        }
    }

    private static void MergeImage(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Image is null || viewBlock.Image is null)
            return;
        var current = viewBlock.Image;
        var old = baselineBlock?.Image;
        if (old is null || current.DisplayName != old.DisplayName || current.MediaType != old.MediaType ||
            current.Width != old.Width || current.Alignment != old.Alignment || current.AltText != old.AltText ||
            !string.IsNullOrEmpty(current.DataBase64))
        {
            HavenRichNotesOps.UpdateImage(rich, page.Id, contractBlock.Id, image =>
            {
                image.DisplayName = string.IsNullOrWhiteSpace(current.DisplayName) ? image.DisplayName : current.DisplayName;
                image.MediaType = string.IsNullOrWhiteSpace(current.MediaType) ? image.MediaType : current.MediaType;
                image.Width = current.Width;
                image.Alignment = ParseAlignment(current.Alignment);
                image.AltText = current.AltText ?? string.Empty;
                if (!string.IsNullOrEmpty(current.DataBase64))
                {
                    image.DataBase64 = current.DataBase64;
                    image.LocalReference = null;
                }
            });
        }
    }

    private static void MergeDivider(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Divider is null || viewBlock.Divider is null)
            return;
        var current = viewBlock.Divider;
        var old = baselineBlock?.Divider;
        if (old is null || current.Thickness != old.Thickness || current.LineStyle != old.LineStyle || current.Color != old.Color)
            HavenRichNotesOps.UpdateDivider(rich, page.Id, contractBlock.Id,
                current.Thickness, ParseBorderStyle(current.LineStyle), current.Color);
    }

    private static void MergeGraph(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Graph is null || viewBlock.Graph is null)
            return;
        var graph = contractBlock.Graph;
        var current = viewBlock.Graph;
        var oldExpressions = baselineBlock?.Graph?.Expressions.ToDictionary(e => e.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, RichGraphExpressionView>(StringComparer.Ordinal);
        var currentIds = new HashSet<string>(current.Expressions.Select(e => e.Id), StringComparer.Ordinal);
        foreach (var doomed in graph.Expressions.Where(e => !currentIds.Contains(e.Id)).Select(e => e.Id).ToArray())
            HavenRichNotesOps.RemoveGraphExpression(rich, page.Id, contractBlock.Id, doomed);
        foreach (var expression in current.Expressions)
        {
            var existing = graph.Expressions.FirstOrDefault(e => e.Id == expression.Id);
            if (existing is null)
            {
                var added = HavenRichNotesOps.AddGraphExpression(rich, page.Id, contractBlock.Id, expression.Text);
                added.Id = expression.Id;
                HavenRichNotesOps.UpdateGraphExpression(rich, page.Id, contractBlock.Id, expression.Id,
                    visible: expression.Visible, color: expression.Color, lineWidth: expression.LineWidth,
                    domainMin: expression.DomainMin, domainMax: expression.DomainMax);
                continue;
            }
            if (!oldExpressions.TryGetValue(expression.Id, out var old) || GraphExpressionChanged(expression, old))
                HavenRichNotesOps.UpdateGraphExpression(rich, page.Id, contractBlock.Id, expression.Id,
                    text: expression.Text, visible: expression.Visible, color: expression.Color,
                    lineWidth: expression.LineWidth, domainMin: expression.DomainMin, domainMax: expression.DomainMax,
                    clearDomain: expression.DomainMin is null && expression.DomainMax is null);
        }
        var oldGraph = baselineBlock?.Graph;
        if (oldGraph is null || current.XMin != oldGraph.XMin || current.XMax != oldGraph.XMax ||
            current.YMin != oldGraph.YMin || current.YMax != oldGraph.YMax)
            HavenRichNotesOps.SetGraphViewport(rich, page.Id, contractBlock.Id, new HavenRichGraphViewport
            {
                XMin = current.XMin, XMax = current.XMax, YMin = current.YMin, YMax = current.YMax
            });
        if (oldGraph is null || current.ShowGrid != oldGraph.ShowGrid || current.ShowAxes != oldGraph.ShowAxes ||
            current.XLabel != oldGraph.XLabel || current.YLabel != oldGraph.YLabel)
            HavenRichNotesOps.SetGraphOptions(rich, page.Id, contractBlock.Id,
                showGrid: current.ShowGrid, showAxes: current.ShowAxes,
                xLabel: current.XLabel, yLabel: current.YLabel);
        MergeGraphPoints(rich, page, contractBlock, current, oldGraph);
    }

    private static bool GraphExpressionChanged(RichGraphExpressionView current, RichGraphExpressionView baseline) =>
        current.Text != baseline.Text || current.Visible != baseline.Visible || current.Color != baseline.Color ||
        current.LineWidth != baseline.LineWidth || current.DomainMin != baseline.DomainMin ||
        current.DomainMax != baseline.DomainMax;

    private static void MergeGraphPoints(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichGraphView current, RichGraphView? baseline)
    {
        var contracted = contractBlock.Graph!;
        var oldPoints = baseline?.Points ?? [];
        if (current.Points.Count == oldPoints.Count &&
            current.Points.Zip(oldPoints).All(pair =>
                pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y && pair.First.Label == pair.Second.Label))
            return;
        while (contracted.Points.Count > 0)
            HavenRichNotesOps.RemoveGraphPoint(rich, page.Id, contractBlock.Id, 0);
        foreach (var point in current.Points)
            HavenRichNotesOps.AddGraphPoint(rich, page.Id, contractBlock.Id, point.X, point.Y, point.Label);
    }

    private static HavenRichBaseline ParseBaseline(string value) =>
        value?.ToLowerInvariant() switch
        {
            "subscript" => HavenRichBaseline.Subscript,
            "superscript" => HavenRichBaseline.Superscript,
            _ => HavenRichBaseline.Normal
        };

    private static HavenRichAlignment ParseAlignment(string value) =>
        value?.ToLowerInvariant() switch
        {
            "left" => HavenRichAlignment.Left,
            "center" => HavenRichAlignment.Center,
            "right" => HavenRichAlignment.Right,
            "justify" => HavenRichAlignment.Justify,
            _ => HavenRichAlignment.Inherit
        };

    private static HavenRichCellVerticalAlignment ParseCellV(string value) =>
        value?.ToLowerInvariant() switch
        {
            "top" => HavenRichCellVerticalAlignment.Top,
            "middle" => HavenRichCellVerticalAlignment.Middle,
            "bottom" => HavenRichCellVerticalAlignment.Bottom,
            _ => HavenRichCellVerticalAlignment.Inherit
        };

    private static HavenRichBorderStyle ParseBorderStyle(string value) =>
        value?.ToLowerInvariant() switch
        {
            "none" => HavenRichBorderStyle.None,
            "solid" => HavenRichBorderStyle.Solid,
            "dashed" => HavenRichBorderStyle.Dashed,
            "dotted" => HavenRichBorderStyle.Dotted,
            _ => HavenRichBorderStyle.Inherit
        };

    private static bool TrySplitItemId(string id, out string parentId, out string itemId)
    {
        var index = id.IndexOf(':');
        if (index > 0 && index < id.Length - 1)
        {
            parentId = id[..index];
            itemId = id[(index + 1)..];
            return true;
        }
        parentId = string.Empty;
        itemId = string.Empty;
        return false;
    }

    private static string NewBlockId(HavenRichPage page)
    {
        string id;
        do { id = "block-" + Guid.NewGuid().ToString("N")[..12]; }
        while (page.Blocks.Any(b => b.Id == id));
        return id;
    }

    private static string NewItemId(HavenRichBlock block)
    {
        string id;
        do { id = "item-" + Guid.NewGuid().ToString("N")[..12]; }
        while (block.Items.Any(i => i.Id == id));
        return id;
    }

    private static RichBoardBlock NewViewBlock(string id, RichBoardBlock source) => new()
    {
        Id = id,
        Kind = source.Kind,
        Text = source.Text,
        StyleId = source.StyleId,
        IsChecked = source.IsChecked,
        Level = source.Level,
        Bold = source.Bold,
        Italic = source.Italic,
        Underline = source.Underline,
        Strike = source.Strike,
        Baseline = source.Baseline,
        FontFamily = source.FontFamily,
        FontSize = source.FontSize,
        Foreground = source.Foreground,
        Background = source.Background,
        Alignment = source.Alignment,
        LineSpacing = source.LineSpacing,
        SpaceBefore = source.SpaceBefore,
        SpaceAfter = source.SpaceAfter,
        IndentLevel = source.IndentLevel,
        TableCells = new Dictionary<string, string>(source.TableCells, StringComparer.Ordinal),
        TableStyle = new RichTableStyleView
        {
            Background = source.TableStyle.Background,
            BorderColor = source.TableStyle.BorderColor,
            BorderWidth = source.TableStyle.BorderWidth,
            BorderStyle = source.TableStyle.BorderStyle,
            CornerRadius = source.TableStyle.CornerRadius,
            AlternatingRows = source.TableStyle.AlternatingRows
        },
        CellStyles = source.CellStyles.ToDictionary(
            kv => kv.Key,
            kv => new RichCellStyleView
            {
                AlignH = kv.Value.AlignH,
                AlignV = kv.Value.AlignV,
                Background = kv.Value.Background,
                Foreground = kv.Value.Foreground,
                Bold = kv.Value.Bold,
                Italic = kv.Value.Italic,
                Underline = kv.Value.Underline,
                Baseline = kv.Value.Baseline
            }, StringComparer.Ordinal),
        TableRows = source.TableRows,
        TableCols = source.TableCols,
        ColumnWidths = [.. source.ColumnWidths],
        Image = source.Image,
        Divider = source.Divider,
        Graph = source.Graph,
        AttachmentId = source.AttachmentId,
        AttachmentName = source.AttachmentName,
        AttachmentSize = source.AttachmentSize,
        InkStrokeCount = source.InkStrokeCount
    };

    private static string WorkerJson(RichBoardDocument document) =>
        System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerDefaults.Web));
}
