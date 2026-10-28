using System.Text.Json;

namespace CakeOS.Apps.Boards.Contract;

public static class HavenTabberOps
{
    public static void Validate(HavenRichNotes notes)
    {
        foreach (var section in notes.Sections)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in section.Pages)
            foreach (var block in page.Blocks.Where(block => block.Kind == HavenRichBlockKind.Tabber))
            {
                var tabs = block.Tabber ?? throw new InvalidDataException("Tabber panes are missing.");
                if (page.PageType != HavenRichPageTypes.Native || page.TabberOwnerBlockId is not null || tabs.Panes.Count is < 1 or > 32 ||
                    tabs.Panes.Select(pane => pane.Id).Distinct().Count() != tabs.Panes.Count ||
                    !tabs.Panes.Any(pane => pane.Id == tabs.SelectedPaneId))
                    throw new InvalidDataException("A Tabber requires one to thirty-two unique panes and a valid selection; nested Tabbers are not supported.");
                foreach (var pane in tabs.Panes)
                {
                    var content = section.Pages.SingleOrDefault(item => item.Id == pane.PageId);
                    if (string.IsNullOrWhiteSpace(pane.Id) || string.IsNullOrWhiteSpace(pane.Title) || pane.Title.Length > 128 ||
                        content is null || content.Id == page.Id || content.TabberOwnerBlockId != block.Id ||
                        content.ParentPageId != page.Id || !owned.Add(pane.PageId))
                        throw new InvalidDataException("A Tabber pane must own one distinct Native page in its section.");
                    if (content.PageType != HavenRichPageTypes.Native) throw new InvalidDataException("Tabber panes use Native content.");
                }
            }
            if (section.Pages.Any(page => page.TabberOwnerBlockId is not null && !owned.Contains(page.Id)))
                throw new InvalidDataException("An owned Tabber page has no matching pane.");
        }
    }

    public static HavenRichBlock Add(HavenRichNotes notes, string pageID, string? after)
    {
        var section = notes.Sections.Single(item => item.Pages.Any(page => page.Id == pageID));
        var page = section.Pages.Single(item => item.Id == pageID);
        if (page.TabberOwnerBlockId is not null) throw new InvalidOperationException("Nested Tabbers are not supported.");
        var block = new HavenRichBlock { Kind = HavenRichBlockKind.Tabber, Tabber = new HavenTabber() };
        var index = after is null ? page.Blocks.Count - 1 : page.Blocks.FindIndex(item => item.Id == after);
        if (after is not null && index < 0) throw new InvalidOperationException("The insertion block is no longer present.");
        page.Blocks.Insert(index + 1, block);
        AddPane(notes, pageID, block.Id);
        for (var order = 0; order < page.Blocks.Count; order++) page.Blocks[order].Order = order;
        return block;
    }

    public static void AddPane(HavenRichNotes notes, string pageID, string blockID)
    {
        var section = notes.Sections.Single(item => item.Pages.Any(page => page.Id == pageID));
        var block = section.Pages.Single(page => page.Id == pageID).Blocks.Single(item => item.Id == blockID);
        var tabs = block.Tabber ?? throw new InvalidOperationException("Tabber missing.");
        if (tabs.Panes.Count >= 32) throw new InvalidOperationException("This Tabber already contains 32 panes.");
        var title = "Tab " + (tabs.Panes.Count + 1);
        var page = new HavenRichPage { Title = title, ParentPageId = pageID, TabberOwnerBlockId = blockID,
            Blocks = [HavenRichBlock.Paragraph()] };
        section.Pages.Add(page);
        var pane = new HavenTabberPane("pane-" + Guid.NewGuid().ToString("N"), title, page.Id);
        tabs.Panes.Add(pane); tabs.SelectedPaneId = pane.Id;
        HavenRichNotesOps.TouchNotes(notes);
    }

    public static void Change(HavenRichNotes notes, string pageID, string blockID, string action, string? paneID, string? title)
    {
        if (action == "add") { AddPane(notes, pageID, blockID); return; }
        var section = notes.Sections.Single(item => item.Pages.Any(page => page.Id == pageID));
        var tabs = section.Pages.Single(page => page.Id == pageID).Blocks.Single(block => block.Id == blockID).Tabber
            ?? throw new InvalidOperationException("Tabber missing.");
        var index = tabs.Panes.FindIndex(pane => pane.Id == paneID);
        if (index < 0) throw new InvalidOperationException("This tab no longer exists.");
        var pane = tabs.Panes[index];
        switch (action)
        {
            case "select": if (tabs.SelectedPaneId == pane.Id) return; tabs.SelectedPaneId = pane.Id; break;
            case "rename":
                var name = title?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new InvalidOperationException("Use a tab name of 1–128 characters.");
                if (pane.Title == name) return;
                tabs.Panes[index] = pane with { Title = name };
                section.Pages.Single(page => page.Id == pane.PageId).Title = name;
                break;
            case "remove":
                if (tabs.Panes.Count == 1) throw new InvalidOperationException("Keep at least one pane in the Tabber.");
                section.Pages.RemoveAll(page => page.Id == pane.PageId);
                tabs.Panes.RemoveAt(index);
                if (tabs.SelectedPaneId == pane.Id) tabs.SelectedPaneId = tabs.Panes[Math.Min(index, tabs.Panes.Count - 1)].Id;
                break;
            default: throw new InvalidOperationException("Unsupported Tabber operation.");
        }
        HavenRichNotesOps.TouchNotes(notes);
    }

    public static void RemoveOwnedPanes(HavenRichSection section, HavenRichBlock block)
    {
        if (block.Tabber is not { } tabs) return;
        var ids = tabs.Panes.Select(pane => pane.PageId).ToHashSet(StringComparer.Ordinal);
        section.Pages.RemoveAll(page => ids.Contains(page.Id) && page.TabberOwnerBlockId == block.Id);
    }

    public static void CopyOwnedPanes(HavenRichNotes notes, HavenRichPage parent, HavenRichBlock source, HavenRichBlock copy)
    {
        var section = notes.Sections.Single(item => item.Pages.Contains(parent));
        var panes = new List<HavenTabberPane>();
        string selected = "";
        foreach (var pane in source.Tabber!.Panes)
        {
            var original = section.Pages.Single(page => page.Id == pane.PageId);
            var page = JsonSerializer.Deserialize<HavenRichPage>(JsonSerializer.Serialize(original))!;
            HavenRichNotesOps.FreshPageIds(page);
            page.ParentPageId = parent.Id; page.TabberOwnerBlockId = copy.Id;
            section.Pages.Add(page);
            var next = pane with { Id = "pane-" + Guid.NewGuid().ToString("N"), PageId = page.Id };
            panes.Add(next); if (pane.Id == source.Tabber.SelectedPaneId) selected = next.Id;
        }
        copy.Tabber = new HavenTabber { Panes = panes, SelectedPaneId = selected };
    }
}
