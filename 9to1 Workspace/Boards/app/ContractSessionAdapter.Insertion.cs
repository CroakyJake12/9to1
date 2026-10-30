using System.Text.Json;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

internal sealed record BoardInsertionTarget(BoardBlockActionTarget Block, bool WholeList, string? ParentStamp);

public sealed partial class ContractSessionAdapter
{
    internal BoardInsertionTarget? CaptureInsertionTarget(string pageId, string rowId, bool wholeList)
    {
        var block = CaptureBlockAction(pageId, rowId);
        return block is null ? null : new(block, wholeList, wholeList ? InsertionParentStamp(block) : null);
    }

    private string InsertionParentStamp(BoardBlockActionTarget target)
    {
        var parent = target.BlockId[..target.BlockId.IndexOf(':')] + ":";
        var rows = _view.Sections.SelectMany(section => section.Pages).Single(page => page.Id == target.PageId)
            .Blocks.Where(row => row.Id.StartsWith(parent, StringComparison.Ordinal)).ToArray();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows, Json)));
    }

    internal async ValueTask<BoardBlockActionResult> InsertRelativeAsync(BoardInsertionTarget insertion,
        RichBoardBlock preparedView, BlockInsertionPlacement placement,
        Func<bool> originStillEditable, CancellationToken cancellationToken = default, string? attachmentSource = null)
    {
        var target = insertion.Block;
        var wholeList = insertion.WholeList;
        var insertTabber = preparedView.Kind == "tabber";
        var insertDatabase = preparedView.Kind == "database";
        var prepared = insertTabber ? null : MergeDeltas.PrepareInsertion(preparedView);
        if (!insertTabber && prepared is null || insertTabber && attachmentSource is not null)
            return new(false, "IncompleteBlock", null);
        if (_disposed) return new(false, "Disposed", null);
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool CurrentView() => !_disposed && IsIssuedBlockAction(target) && target.OwnerId == _textOwner &&
                originStillEditable() && (target.BlockId.Contains(':') ? CurrentListRowTicket(target)
                    : target.ViewStamp == BlockActionStamp()) &&
                (!wholeList || insertion.ParentStamp == InsertionParentStamp(target));
            if (!CurrentView() || target.Revision != DocumentVersion) return new(false, "StaleOrReadOnly", null);
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var version = DocumentVersion;
            var split = target.BlockId.IndexOf(':');
            var parentId = split < 0 ? target.BlockId : target.BlockId[..split];
            var itemId = split < 0 ? null : target.BlockId[(split + 1)..];
            bool Applicable(HavenRichNotes rich)
            {
                if (!CurrentView() || rich.Version != version) return false;
                var page = rich.Sections.SelectMany(section => section.Pages).FirstOrDefault(page => page.Id == target.PageId);
                if (insertTabber && (page?.PageType != HavenRichPageTypes.Native || page.TabberOwnerBlockId is not null)) return false;
                var anchor = page?.Blocks.FirstOrDefault(block => block.Id == parentId);
                return anchor is not null && (split < 0 || anchor.Items.Any(item => item.Id == itemId)) &&
                    // Absolute geometry has no flow insertion edge. Keep that combination explicit;
                    // replacement of the complete spatial block can preserve its exact placement.
                    (anchor.Layout != HavenRichBlockLayouts.Absolute ||
                        (placement == BlockInsertionPlacement.Replace && (split < 0 || wholeList)));
            }
            if (!Applicable(_real.Rich)) return new(false, "StaleOrUnsupportedPlacement", null);
            if (attachmentSource is not null)
            {
                if (!CanImportLocalAttachments)
                    return new(false, "AttachmentImportUnavailable", null, "This board source does not grant local attachment import access.");
                // Save As uses this same merge gate. Hold it through payload preparation and commit
                // so a sidecar cannot be written beside one path and referenced from another.
                try { prepared!.Attachment = await _real.ImportAttachmentAsync(attachmentSource, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                { return new(false, "AttachmentImportFailed", null, "The attachment could not be imported: " + error.Message.Split('\n')[0]); }
                // Applicable runs again under the canonical gate, including lifetime/content checks.
                // A completed immutable blob is retained on refusal; another reference/history may own it.
            }
            string? selected = null;
            var applied = await _real.MutateIfAsync(Applicable, rich =>
            {
                var page = rich.Sections.SelectMany(section => section.Pages).Single(page => page.Id == target.PageId);
                var anchor = page.Blocks.Single(block => block.Id == parentId);
                // Metadata-only projection imports cannot create owned pages. Construct the real
                // Tabber inside this rollback/history boundary, then splice that same block below.
                var inserted = insertTabber ? HavenTabberOps.Add(rich, page.Id, null) : prepared!;
                if (insertTabber) page.Blocks.Remove(inserted);
                var at = page.Blocks.IndexOf(anchor);
                if (split >= 0 && !wholeList)
                {
                    var row = anchor.Items.FindIndex(item => item.Id == itemId);
                    var boundary = placement == BlockInsertionPlacement.Below ? row + 1 : row;
                    var before = anchor.Items.Take(boundary).ToList();
                    var after = anchor.Items.Skip(boundary + (placement == BlockInsertionPlacement.Replace ? 1 : 0)).ToList();
                    // Preserve complete list metadata and each retained item. Only a newly separated
                    // trailing list needs a new parent identity; the original parent is retained.
                    var trailing = JsonSerializer.Deserialize<HavenRichBlock>(JsonSerializer.Serialize(anchor))!;
                    page.Blocks.RemoveAt(at);
                    if (before.Count > 0)
                    {
                        anchor.Items = before; page.Blocks.Insert(at++, anchor);
                        trailing.Id = "block-" + Guid.NewGuid().ToString("N");
                    }
                    page.Blocks.Insert(at++, inserted);
                    if (after.Count > 0) { trailing.Items = after; page.Blocks.Insert(at, trailing); }
                }
                else
                {
                    if (placement == BlockInsertionPlacement.Replace)
                    {
                        inserted.Layout = anchor.Layout; inserted.X = anchor.X; inserted.Y = anchor.Y;
                        inserted.Width = anchor.Width; inserted.Height = anchor.Height; inserted.ZOrder = anchor.ZOrder;
                        HavenRichNotesOps.RemoveBlock(rich, page.Id, anchor.Id);
                    }
                    else if (placement == BlockInsertionPlacement.Below) at++;
                    page.Blocks.Insert(at, inserted);
                }
                for (var index = 0; index < page.Blocks.Count; index++) page.Blocks[index].Order = index;
                HavenRichNotesOps.TouchNotes(rich);
                selected = inserted.Items.Count > 0 ? inserted.Id + ":" + inserted.Items[0].Id : inserted.Id;
            }, cancellationToken, requireReadableDocument: insertTabber || insertDatabase || attachmentSource is not null).ConfigureAwait(false);
            if (!applied) return new(false, "StaleOrReadOnly", null);
            RefreshView();
            return new(true, "Applied", selected);
        }
        catch (InvalidDataException error) when (insertTabber || insertDatabase || attachmentSource is not null)
        { return new(false, insertTabber ? "InvalidTabberInsertion" : insertDatabase ? "InvalidDatabaseInsertion" : "InvalidAttachmentInsertion", null, error.Message.Split('\n')[0]); }
        finally { _mergeGate.Release(); }
    }
}
