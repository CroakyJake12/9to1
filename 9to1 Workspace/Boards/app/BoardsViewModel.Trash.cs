using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// The Trash flow the owner meets: an impact-before-action confirmation on the page row, and a Trash panel
/// where a trashed page can be put back. The requirement (UX27-BOARD-015) is not only that content survives -
/// the session layer already guarantees that - but that the owner is told what leaving navigation means, and
/// told plainly when a restore cannot return a page to where it was.
///
/// The counts come from the session's own impact query, which is the same operation the action performs, so a
/// confirmation cannot disagree with what actually happens.
/// </summary>
public sealed partial class BoardsViewModel
{
    private string? _pendingTrashPageId;
    private IReadOnlyList<RichBoardTrashEntry> _trashEntries = [];

    /// <summary>Asks for confirmation, stating the impact, before anything leaves navigation.</summary>
    public async Task PromptMovePageToTrashAsync(string sectionId, string pageId,
        CancellationToken cancellationToken = default)
    {
        if (!RequireEditMode()) return;
        var impact = await _session.GetTrashImpactAsync(pageId, cancellationToken);
        if (impact is null)
        {
            Set("StatusText", "That page is not in this board any more.");
            return;
        }
        if (!impact.CanTrash)
        {
            // Refuse before offering: canonical v2 line 9397 requires visible actions to have real
            // implementations, so a confirmation whose accept button is known in advance to do nothing is not
            // allowed. The reason is what the owner reads instead.
            Set("StatusText", impact.Refusal);
            return;
        }
        var page = _session.Document.Sections.FirstOrDefault(item => item.Id == sectionId)?.Pages
            .FirstOrDefault(item => item.Id == pageId);
        _pendingTrashPageId = pageId;
        Set("TrashConfirmTitle", $"Move {(string.IsNullOrWhiteSpace(page?.Title) ? "this page" : page!.Title.Trim())} to Trash?");
        var (subpages, blocks) = (impact.SubpageCount, impact.BlockCount);
        Set("TrashConfirmImpact", (subpages, blocks) switch
        {
            // A page with nothing in it still leaves navigation, and the confirmation has to say that plainly
            // instead of staying blank or reading like a placeholder count.
            (0, 0) => "No subpages, and this page has no blocks of its own.",
            (0, 1) => "No subpages. 1 block will move with it.",
            (0, _) => $"No subpages. {blocks} blocks will move with it.",
            (1, 0) => "1 subpage will move with it, and this page has no blocks of its own.",
            (1, 1) => "1 subpage and 1 block will move with it.",
            (1, _) => $"1 subpage and {blocks} blocks will move with it.",
            (_, 0) => $"{subpages} subpages will move with it, and this page has no blocks of its own.",
            (_, 1) => $"{subpages} subpages and 1 block will move with it.",
            (_, _) => $"{subpages} subpages and {blocks} blocks will move with it.",
        });
        Set("IsTrashConfirmOpen", true);
    }

    public void CancelTrashConfirm()
    {
        _pendingTrashPageId = null;
        Set("IsTrashConfirmOpen", false);
        Set("TrashConfirmTitle", string.Empty);
        Set("TrashConfirmImpact", string.Empty);
    }

    public async Task ConfirmMovePageToTrashAsync(CancellationToken cancellationToken = default)
    {
        if (!RequireEditMode()) return;
        var pageId = _pendingTrashPageId;
        CancelTrashConfirm();
        if (pageId is null)
        {
            Set("StatusText", "Nothing was chosen to move to the Trash.");
            return;
        }
        var result = await _session.MovePageToTrashAsync(pageId, cancellationToken);
        if (!result.Succeeded)
        {
            Set("StatusText", result.Reason ?? "The page could not be moved to the Trash.");
            return;
        }
        ReselectAfterNavChange();
        RefreshAfterEdit();
        NavRebuildRequested?.Invoke();
        Set("StatusText", "Moved to the Trash. Open Trash from the Board menu to restore it.");
    }

    /// <summary>Shows the trashed pages. The list is projected from the file, so it cannot invent entries.</summary>
    public void OpenTrashPanel()
    {
        _trashEntries = _session.Document.Trash.ToArray();
        Set("TrashEntries", _trashEntries);
        Set("HasTrashEntries", _trashEntries.Count > 0);
        // The panel's empty state is its own published key rather than a negation written into the markup:
        // an authored <If> condition has to read a value the view model actually holds.
        Set("HasNoTrashEntries", _trashEntries.Count == 0);
        Set("IsTrashPanelOpen", true);
        Set("CanOpenTrash", CanOpenBoardMenu);
    }

    public void CloseTrashPanel()
    {
        _trashEntries = [];
        Set("TrashEntries", Array.Empty<RichBoardTrashEntry>());
        Set("HasTrashEntries", false);
        Set("HasNoTrashEntries", true);
        Set("IsTrashPanelOpen", false);
    }

    public async Task RestoreTrashedPageAsync(RichBoardTrashEntry entry, CancellationToken cancellationToken = default)
    {
        if (!RequireEditMode()) return;
        if (entry is null || !_trashEntries.Any(known => ReferenceEquals(known, entry)))
        {
            Set("StatusText", "That Trash entry is no longer on screen. Reopen Trash and try again.");
            return;
        }
        var result = await _session.RestorePageFromTrashAsync(entry.PageId, cancellationToken);
        if (!result.Succeeded)
        {
            Set("StatusText", result.Reason ?? "The page could not be restored.");
            return;
        }
        RefreshAfterEdit();
        NavRebuildRequested?.Invoke();
        _trashEntries = _session.Document.Trash.ToArray();
        Set("TrashEntries", _trashEntries);
        Set("HasTrashEntries", _trashEntries.Count > 0);
        Set("HasNoTrashEntries", _trashEntries.Count == 0);
        // The outcome says where it actually went. A restore that had to relocate the page is reported as
        // such, because a page silently landing somewhere else is the failure mode the requirement calls out.
        Set("StatusText", result.RelocatedToSectionRoot
            ? $"Restored {entry.Title} to {result.SectionTitle}. Its original parent page is in the Trash, " +
              "so it came back at the top level."
            : $"Restored {entry.Title} to {result.SectionTitle}.");
    }

    private bool TryGetItemValueForTrash(string path, object item, out object? value)
    {
        if (item is RichBoardTrashEntry entry && _trashEntries.Any(known => ReferenceEquals(known, entry)))
        {
            value = path switch
            {
                "PageId" => entry.PageId,
                "Title" => entry.Title,
                "SectionTitle" => entry.SectionTitle,
                "TrashedLabel" => entry.TrashedLabel,
                "IconIsEmoji" => entry.IconIsEmoji,
                "IconGlyph" => entry.IconGlyph,
                "IconAccessibleName" => entry.IconIsEmoji ? $"{entry.Title} icon" : "Page",
                "Counts" => entry.SubtreePageCount == 0
                    ? $"1 page · {entry.BlockCount} blocks"
                    : $"{entry.SubtreePageCount + 1} pages · {entry.BlockCount} blocks",
                "Target" => entry,
                _ => null,
            };
            return value is not null;
        }
        value = null;
        return false;
    }
}
