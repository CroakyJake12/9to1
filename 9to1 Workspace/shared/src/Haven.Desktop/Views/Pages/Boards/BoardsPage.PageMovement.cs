using Avalonia.Controls;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    internal Task<bool> PendingPageMovement { get; private set; } = Task.FromResult(false);

    internal MenuFlyout CreatePageMovementMenu(NotesDocument original, Guid pageId)
    {
        var menu = new MenuFlyout();
        var source = original.Sections.FirstOrDefault(section => section.Pages.Any(page => page.Id == pageId));
        if (_disposed || !ReferenceEquals(_document, original) || source is null ||
            BoardsWorkspaceService.IsPageDeleted(original, pageId)) return menu;
        var active = source.Pages.Where(page => !BoardsWorkspaceService.IsPageDeleted(original, page.Id))
            .OrderBy(page => page.Order).ToArray();
        var index = Array.FindIndex(active, page => page.Id == pageId);
        var sourceOrder = source.Pages.Select(page => page.Id).ToArray();
        var editable = _boards.GetEditMode(original, pageId) == BoardsPageEditMode.Edit;
        Add("Move page up", source.Id, index > 0 ? source.Pages.IndexOf(active[index - 1]) : 0, editable && index > 0);
        Add("Move page down", source.Id, index >= 0 && index < active.Length - 1 ? source.Pages.IndexOf(active[index + 1]) : 0,
            editable && index >= 0 && index < active.Length - 1);
        foreach (var target in original.Sections.Where(section => section.Id != source.Id))
            Add("Move to · " + target.Title, target.Id, target.Pages.Count, editable && active.Length > 1);
        return menu;

        void Add(string title, Guid targetId, int targetIndex, bool enabled)
        {
            var target = original.Sections.Single(section => section.Id == targetId);
            var targetOrder = target.Pages.Select(page => page.Id).ToArray();
            var item = new MenuItem { Header = title, Tag = targetId, IsEnabled = enabled };
            item.Click += (_, _) =>
            {
                if (!enabled || !PendingPageMovement.IsCompleted) return;
                if (!sourceOrder.SequenceEqual(source.Pages.Select(page => page.Id)) ||
                    !targetOrder.SequenceEqual(target.Pages.Select(page => page.Id)))
                {
                    PendingPageMovement = Task.FromResult(false);
                    if (!_disposed && ReferenceEquals(_document, original)) SetStatus("Pages changed. Reopen the move menu to choose the current position.");
                    return;
                }
                PendingPageMovement = MovePageFromHierarchyAsync(original, pageId, targetId, targetIndex);
            };
            menu.Items.Add(item);
        }
    }

    internal async Task<bool> MovePageFromHierarchyAsync(NotesDocument original, Guid pageId, Guid targetSectionId, int targetIndex)
    {
        if (_disposed || !ReferenceEquals(_document, original)) return false;
        var source = original.Sections.FirstOrDefault(section => section.Pages.Any(page => page.Id == pageId));
        var target = original.Sections.FirstOrDefault(section => section.Id == targetSectionId);
        if (source is null || target is null || BoardsWorkspaceService.IsPageDeleted(original, pageId)) return false;
        if (source.Id != target.Id && source.Pages.Count(page => !BoardsWorkspaceService.IsPageDeleted(original, page.Id)) <= 1)
        {
            SetStatus("Keep at least one page in each section. Add another page before moving this one.");
            return false;
        }
        try
        {
            _boards.MovePage(original, pageId, targetSectionId, targetIndex);
            if (_page?.Id == pageId) _section = target;
            RebuildHierarchy(); RebuildPageTabs(); RebuildEditor();
            await _boards.SaveAsync(original, "Moved Boards page", CancellationToken.None);
            if (!_disposed && ReferenceEquals(_document, original))
            {
                SetStatus("Page moved and saved locally.");
                _bus.Fire("Boards.Saved");
            }
            return true;
        }
        catch (Exception)
        {
            if (!_disposed && ReferenceEquals(_document, original))
                SetStatus("Couldn’t save the page move. Your local arrangement remains open.");
            return false;
        }
    }
}
