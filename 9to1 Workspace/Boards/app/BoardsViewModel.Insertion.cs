using Avalonia.Media.Imaging;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private long _insertionOperationEpoch;
    private string? _insertionOperationPage;

    private void TrackInsertionOrigin()
    {
        if (_insertionOperationPage == SelectedPageId) return;
        _insertionOperationPage = SelectedPageId;
        Interlocked.Increment(ref _insertionOperationEpoch);
    }

    internal string? InsertionDestinationUnavailableReason(string? blockId, BlockInsertionPlacement placement)
    {
        var row = PageBlocks.FirstOrDefault(block => block.Id == blockId) ??
            PageBlocks.FirstOrDefault(block => blockId is not null && block.Id.StartsWith(blockId + ":", StringComparison.Ordinal));
        if (row is null) return "That block is no longer on this page. Choose it again.";
        if (row.Layout == HavenRichBlockLayouts.Absolute && placement != BlockInsertionPlacement.Replace)
            return "Above and Below require a block in vertical flow. Replace keeps this block's position.";
        if (row.Layout == HavenRichBlockLayouts.Absolute && blockId?.Contains(':') == true)
            return "Choose the complete positioned list to replace it.";
        return null;
    }

    internal string? RelativeInsertionUnavailableReason(string kind, string? blockId, string? position)
    {
        if (!TryInsertionPlacement(position, out var placement))
            return "Choose Add Block Above, Add Block Below or Replace Current Block.";
        if (InsertionDestinationUnavailableReason(blockId, placement) is { } destination) return destination;
        if (kind.StartsWith("style:", StringComparison.Ordinal))
            return StyleList.Any(style => style.Id == kind[6..]) ? null : "That style is no longer available.";
        if (kind is "database" or "database-tabs" or "database-timeline" or "database-retry" or "tabber" or "freeform" or "attachment")
            return "This item cannot yet use a block destination. Add it from the toolbar Insert menu.";
        if (kind is not ("paragraph" or "heading" or "checklist" or "bulleted" or "numbered" or "table" or "divider" or "graph" or "image"))
            return "This block type is not available for insertion.";
        if (kind == "image" && PickFileAsync is null) return "Image selection needs the desktop file picker.";
        return null;
    }

    private static bool TryInsertionPlacement(string? value, out BlockInsertionPlacement placement)
    {
        placement = value?.Trim().ToLowerInvariant() switch
        {
            "above" => BlockInsertionPlacement.Above, "below" => BlockInsertionPlacement.Below,
            "replace" => BlockInsertionPlacement.Replace, _ => (BlockInsertionPlacement)(-1)
        };
        return Enum.IsDefined(placement);
    }

    /// <summary>Prepare first, then publish the exact destination as one edit. Refusal or a cancelled
    /// picker cannot remove the original. Explicit replacement is separate from Design conversion.</summary>
    public async Task InsertKindRelativeToAsync(string kindTag, string? blockId, string position, bool focusEditor = true)
    {
        if (!RequireEditMode()) return;
        if (RelativeInsertionUnavailableReason(kindTag, blockId, position) is { } reason)
        { Set("StatusText", reason); return; }
        TryInsertionPlacement(position, out var placement);
        var page = CurrentPage();
        var row = page.Blocks.FirstOrDefault(block => block.Id == blockId);
        var wholeList = row is null;
        row ??= page.Blocks.First(block => block.Id.StartsWith(blockId + ":", StringComparison.Ordinal));
        var epoch = Interlocked.Read(ref _insertionOperationEpoch);
        var ticket = (_session as ContractSessionAdapter)?.CaptureInsertionTarget(page.Id, row.Id, wholeList);
        bool OriginCurrent() => !_disposed && !SessionIsDisposed && _isEditMode && CanEdit &&
            SelectedPageId == page.Id && Interlocked.Read(ref _insertionOperationEpoch) == epoch;
        var style = kindTag.StartsWith("style:", StringComparison.Ordinal)
            ? StyleList.First(style => style.Id == kindTag[6..]) : null;
        var prepared = CreateInsertionBlock(kindTag, null, style);
        if (kindTag == "image")
        {
            var path = await PickFileAsync!("image");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!OriginCurrent()) { Set("StatusText", "The page changed while choosing an image. Choose the block again."); return; }
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                using var stream = new MemoryStream(bytes, writable: false);
                using var decoded = new Bitmap(stream);
                prepared.Image = new RichImageView
                {
                    DisplayName = Path.GetFileName(path), DataBase64 = Convert.ToBase64String(bytes),
                    // Decode verifies the asset. Zero display size preserves natural sizing; source
                    // pixels may exceed the contract's 8000-unit display dimension limit.
                    Width = 0, Height = 0,
                    MediaType = Path.GetExtension(path).ToLowerInvariant() switch
                    { ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".bmp" => "image/bmp", ".webp" => "image/webp", _ => "image/png" }
                };
                prepared.Text = prepared.Image.DisplayName;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { Set("StatusText", "The image could not be read: " + error.Message); return; }
        }
        if (!OriginCurrent()) { Set("StatusText", "The insertion target changed. Choose it again."); return; }
        string selected;
        if (_session is ContractSessionAdapter adapter)
        {
            if (ticket is null) { Set("StatusText", "That block cannot be edited here."); return; }
            var result = await adapter.InsertRelativeAsync(ticket, prepared, placement, OriginCurrent);
            if (!result.Applied) { Set("StatusText", "The insertion target changed or is unavailable. Open the menu and choose again."); return; }
            selected = result.SelectedBlockId!;
        }
        else
        {
            if (!ReferenceEquals(CurrentPage(), page) || !page.Blocks.Contains(row))
            { Set("StatusText", "That block is no longer on this page. Choose it again."); return; }
            var at = page.Blocks.IndexOf(row);
            if (placement == BlockInsertionPlacement.Replace)
            {
                prepared.Layout = row.Layout; prepared.X = row.X; prepared.Y = row.Y;
                prepared.Width = row.Width; prepared.Height = row.Height; prepared.ZOrder = row.ZOrder;
                page.Blocks[at] = prepared;
            }
            else page.Blocks.Insert(at + (placement == BlockInsertionPlacement.Below ? 1 : 0), prepared);
            _session.MarkDirty();
            selected = prepared.Id;
        }
        if (!OriginCurrent()) return;
        _selectedBlockId = selected;
        if (focusEditor && FindBlock(selected)?.Kind is "paragraph" or "heading" or "checklist" or "bulleted" or "numbered")
            _pendingTextFocus = (page.Id, selected, 0);
        RefreshAfterEdit();
    }

    public Task ReplaceBlockWithKindAsync(string kindTag, string? blockId, bool focusEditor = true) =>
        InsertKindRelativeToAsync(kindTag, blockId, "replace", focusEditor);

    private static RichBoardBlock CreateInsertionBlock(string kindTag, string? initialText, RichStyleView? insertionStyle)
    {
        RichBoardBlock block;
        if (insertionStyle is not null)
        {
            block = new RichBoardBlock
            {
                Kind = insertionStyle.BlockKind == "heading" ? "heading" : "paragraph",
                Text = insertionStyle.BlockKind == "heading" ? "New heading" : "New paragraph",
                StyleId = insertionStyle.Id,
            };
        }
        else
        {
            block = kindTag switch
            {
                "paragraph" => new RichBoardBlock { Kind = "paragraph", Text = initialText ?? "New paragraph" },
                "heading" => new RichBoardBlock { Kind = "heading", Text = "New heading" },
                "checklist" => new RichBoardBlock { Kind = "checklist", Text = "New item" },
                "bulleted" => new RichBoardBlock { Kind = "bulleted", Text = "New item" },
                "numbered" => new RichBoardBlock { Kind = "numbered", Text = "New item" },
                "table" => new RichBoardBlock
                {
                    Kind = "table", Text = "New table", TableRows = 2, TableCols = 2,
                    TableCells = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["0,0"] = string.Empty, ["0,1"] = string.Empty,
                        ["1,0"] = string.Empty, ["1,1"] = string.Empty,
                    },
                },
                "image" => new RichBoardBlock
                {
                    Kind = "image", Text = "image",
                    Image = new RichImageView { DisplayName = "image (choose a file)", MediaType = "image/png" },
                },
                "divider" => new RichBoardBlock
                {
                    Kind = "divider",
                    Divider = new RichDividerView { Thickness = 1, LineStyle = "solid", Color = "#FF5F6368" },
                },
                "graph" => new RichBoardBlock
                {
                    Kind = "graph",
                    Graph = new RichGraphView
                    {
                        Expressions = [new RichGraphExpressionView { Text = "y = x" }],
                        XMin = -10, XMax = 10, YMin = -10, YMax = 10,
                    },
                },
                _ => new RichBoardBlock { Kind = "paragraph", Text = "New block" },
            };
        }
        return block;
    }
}
