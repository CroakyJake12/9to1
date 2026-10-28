namespace CakeOS.Apps.Boards.Contract;

public static partial class HavenRichNotesOps
{
    /// <summary>Empty Enter removes one nesting level, or ends the list with a paragraph.
    /// Following rows retain their item identities in a trailing list when necessary.</summary>
    public static string ExitEmptyListItem(HavenRichNotes notes, string pageId, string blockId, string itemId)
    {
        var page = RequirePage(notes, pageId);
        var list = RequireListBlock(notes, pageId, blockId);
        var index = list.Items.FindIndex(item => item.Id == itemId);
        if (index < 0) throw new KeyNotFoundException("List item was not found.");
        var item = list.Items[index];
        if (!string.IsNullOrEmpty(item.Text)) throw new InvalidOperationException("Only an empty row exits its list.");
        if (item.Level > 0)
        {
            item.Level--; TouchNotes(notes); return blockId + ":" + itemId;
        }
        var paragraph = new HavenRichBlock
        {
            Id = "block-" + Guid.NewGuid().ToString("N"), Kind = HavenRichBlockKind.Paragraph,
            StyleId = list.StyleId, PlainText = "", Layout = list.Layout,
            X = list.X, Y = list.Y, Width = list.Width, Height = list.Height
        };
        var before = list.Items.Take(index).ToList();
        var after = list.Items.Skip(index + 1).ToList();
        var at = page.Blocks.IndexOf(list);
        if (before.Count == 0)
        {
            if (after.Count == 0) page.Blocks[at] = paragraph;
            else { list.Items = after; page.Blocks.Insert(at, paragraph); }
        }
        else
        {
            list.Items = before; page.Blocks.Insert(at + 1, paragraph);
            if (after.Count > 0)
            {
                var trailing = System.Text.Json.JsonSerializer.Deserialize<HavenRichBlock>(
                    System.Text.Json.JsonSerializer.Serialize(list))!;
                trailing.Id = "block-" + Guid.NewGuid().ToString("N");
                trailing.Items = after; page.Blocks.Insert(at + 2, trailing);
            }
        }
        for (var order = 0; order < page.Blocks.Count; order++) page.Blocks[order].Order = order;
        TouchNotes(notes); return paragraph.Id;
    }

    public static bool MoveListItemTo(HavenRichNotes notes, string pageId, string blockId, string itemId, int destination)
    {
        var list = RequireListBlock(notes, pageId, blockId);
        var from = list.Items.FindIndex(item => item.Id == itemId);
        if (from < 0 || destination < 0 || destination >= list.Items.Count || from == destination) return false;
        var item = list.Items[from]; list.Items.RemoveAt(from); list.Items.Insert(destination, item);
        TouchNotes(notes); return true;
    }

    public static string DuplicateListItem(HavenRichNotes notes, string pageId, string blockId, string itemId)
    {
        var list = RequireListBlock(notes, pageId, blockId);
        var index = list.Items.FindIndex(item => item.Id == itemId);
        if (index < 0) throw new KeyNotFoundException("List item was not found.");
        var copy = System.Text.Json.JsonSerializer.Deserialize<HavenRichListItem>(
            System.Text.Json.JsonSerializer.Serialize(list.Items[index]))!;
        copy.Id = "item-" + Guid.NewGuid().ToString("N");
        list.Items.Insert(index + 1, copy); TouchNotes(notes); return blockId + ":" + copy.Id;
    }

    public static string? DeleteListItem(HavenRichNotes notes, string pageId, string blockId, string itemId)
    {
        var page = RequirePage(notes, pageId); var list = RequireListBlock(notes, pageId, blockId);
        var index = list.Items.FindIndex(item => item.Id == itemId);
        if (index < 0) return null;
        list.Items.RemoveAt(index);
        if (list.Items.Count > 0)
        { TouchNotes(notes); return blockId + ":" + list.Items[Math.Min(index, list.Items.Count - 1)].Id; }
        var at = page.Blocks.IndexOf(list); page.Blocks.RemoveAt(at);
        for (var order = 0; order < page.Blocks.Count; order++) page.Blocks[order].Order = order;
        TouchNotes(notes); return page.Blocks.ElementAtOrDefault(Math.Min(at, page.Blocks.Count - 1))?.Id;
    }

    private static HavenRichBlock RequireListBlock(HavenRichNotes notes, string pageId, string blockId)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Kind is not (HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList))
            throw new InvalidOperationException("The target is not a list.");
        return block;
    }
}
