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
        Func<bool> originStillEditable, CancellationToken cancellationToken = default)
    {
        var target = insertion.Block;
        var wholeList = insertion.WholeList;
        var prepared = MergeDeltas.PrepareInsertion(preparedView);
        if (prepared is null) return new(false, "IncompleteBlock", null);
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
                var anchor = rich.Sections.SelectMany(section => section.Pages)
                    .FirstOrDefault(page => page.Id == target.PageId)?.Blocks.FirstOrDefault(block => block.Id == parentId);
                return anchor is not null && (split < 0 || anchor.Items.Any(item => item.Id == itemId)) &&
                    // Absolute geometry has no flow insertion edge. Keep that combination explicit;
                    // replacement of the complete spatial block can preserve its exact placement.
                    (anchor.Layout != HavenRichBlockLayouts.Absolute ||
                        (placement == BlockInsertionPlacement.Replace && (split < 0 || wholeList)));
            }
            if (!Applicable(_real.Rich)) return new(false, "StaleOrUnsupportedPlacement", null);
            var applied = await _real.MutateIfAsync(Applicable, rich =>
            {
                var page = rich.Sections.SelectMany(section => section.Pages).Single(page => page.Id == target.PageId);
                var anchor = page.Blocks.Single(block => block.Id == parentId);
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
                    page.Blocks.Insert(at++, prepared);
                    if (after.Count > 0) { trailing.Items = after; page.Blocks.Insert(at, trailing); }
                }
                else
                {
                    if (placement == BlockInsertionPlacement.Replace)
                    {
                        prepared.Layout = anchor.Layout; prepared.X = anchor.X; prepared.Y = anchor.Y;
                        prepared.Width = anchor.Width; prepared.Height = anchor.Height; prepared.ZOrder = anchor.ZOrder;
                        HavenRichNotesOps.RemoveBlock(rich, page.Id, anchor.Id);
                    }
                    else if (placement == BlockInsertionPlacement.Below) at++;
                    page.Blocks.Insert(at, prepared);
                }
                for (var index = 0; index < page.Blocks.Count; index++) page.Blocks[index].Order = index;
                HavenRichNotesOps.TouchNotes(rich);
            }, cancellationToken).ConfigureAwait(false);
            if (!applied) return new(false, "StaleOrReadOnly", null);
            RefreshView();
            var selected = prepared.Items.Count > 0 ? prepared.Id + ":" + prepared.Items[0].Id : prepared.Id;
            return new(true, "Applied", selected);
        }
        finally { _mergeGate.Release(); }
    }
}
