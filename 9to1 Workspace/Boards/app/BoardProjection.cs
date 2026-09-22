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
                        Bold = AllStyled(block, r => r.Bold),
                        Italic = AllStyled(block, r => r.Italic),
                        Underline = AllStyled(block, r => r.Underline)
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
                            IsChecked = item.Checked
                        });
                    break;
                case HavenRichBlockKind.Table when block.Table is not null:
                    var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (var r = 0; r < block.Table.Rows.Count; r++)
                        for (var c = 0; c < block.Table.Rows[r].Cells.Count; c++)
                            cells[$"{r},{c}"] = block.Table.Rows[r].Cells[c].Text;
                    blocks.Add(new RichBoardBlock { Id = block.Id, Kind = "table", TableCells = cells });
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

    private static bool AllStyled(HavenRichBlock block, Func<HavenRichTextRun, bool> style) =>
        block.Runs.Count > 0 && block.Runs.All(style);
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
            switch (viewBlock.Kind)
            {
                case "paragraph":
                case "heading":
                    if (baselineBlock is null || viewBlock.Text != baselineBlock.Text)
                        HavenRichNotesOps.UpdateParagraphText(rich, page.Id, contractBlock.Id, viewBlock.Text);
                    if (baselineBlock is null || FlagsChanged(viewBlock, baselineBlock))
                        HavenRichNotesOps.SetBlockRuns(rich, page.Id, contractBlock.Id,
                            [new HavenRichTextRun
                            {
                                Text = viewBlock.Text,
                                Bold = viewBlock.Bold,
                                Italic = viewBlock.Italic,
                                Underline = viewBlock.Underline
                            }]);
                    break;
                case "checklist":
                    // Plain-id checklist block created in the editor: a brand-new item.
                    MergeNewItem(rich, page, viewBlock);
                    break;
                case "table":
                    MergeTableCells(rich, page, contractBlock, viewBlock, baselineBlock);
                    break;
            }
        }
    }

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
            return;
        }
        MergeNewItem(rich, page, NewViewBlock(itemId, viewBlock));
    }

    private static void MergeNewItem(HavenRichNotes rich, HavenRichPage page, RichBoardBlock viewBlock)
    {
        var target = page.Blocks.FirstOrDefault(b =>
            b.Kind is HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList);
        target ??= HavenRichNotesOps.ImportBlock(page, HavenRichBlockKind.Checklist, NewBlockId(page));
        var itemId = TrySplitItemId(viewBlock.Id, out _, out var item) ? item : NewItemId(target);
        if (target.Items.Any(i => i.Id == itemId))
            return;
        HavenRichNotesOps.ImportListItem(target, itemId, viewBlock.Text, viewBlock.IsChecked);
    }

    private static void MergeTableCells(
        HavenRichNotes rich, HavenRichPage page, HavenRichBlock contractBlock,
        RichBoardBlock viewBlock, RichBoardBlock? baselineBlock)
    {
        if (contractBlock.Table is null)
            return;
        var baselineCells = baselineBlock?.TableCells ?? new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, text) in viewBlock.TableCells)
        {
            if (baselineCells.TryGetValue(key, out var old) && old == text)
                continue;
            var parts = key.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var row) || !int.TryParse(parts[1], out var col))
                continue;
            if (row < 0 || row >= contractBlock.Table.Rows.Count)
                continue;
            if (col < 0 || col >= contractBlock.Table.Rows[row].Cells.Count)
                continue;
            HavenRichNotesOps.UpdateTableCell(
                rich, page.Id, contractBlock.Id, contractBlock.Table.Rows[row].Cells[col].Id, text);
        }
    }

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
                if (viewBlock.Bold || viewBlock.Italic || viewBlock.Underline)
                    block.Runs = [new HavenRichTextRun
                    {
                        Text = viewBlock.Text,
                        Bold = viewBlock.Bold,
                        Italic = viewBlock.Italic,
                        Underline = viewBlock.Underline
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
                    foreach (var (key, text) in viewBlock.TableCells)
                    {
                        var parts = key.Split(',');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var row) && int.TryParse(parts[1], out var col) &&
                            row >= 0 && row < block.Table.Rows.Count && col >= 0 && col < block.Table.Rows[row].Cells.Count)
                            block.Table.Rows[row].Cells[col].Text = text;
                    }
                break;
            }
        }
    }

    private static bool FlagsChanged(RichBoardBlock current, RichBoardBlock baseline) =>
        current.Bold != baseline.Bold || current.Italic != baseline.Italic || current.Underline != baseline.Underline;

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
        IsChecked = source.IsChecked,
        Bold = source.Bold,
        Italic = source.Italic,
        Underline = source.Underline,
        TableCells = new Dictionary<string, string>(source.TableCells, StringComparer.Ordinal),
        InkStrokeCount = source.InkStrokeCount
    };

    private static string WorkerJson(RichBoardDocument document) =>
        System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions(
            System.Text.Json.JsonSerializerDefaults.Web));
}
