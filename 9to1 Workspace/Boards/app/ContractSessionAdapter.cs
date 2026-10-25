// Bridges the CUI editor's working model to the durable contract session.
// The worker RichBoardDocument stays the editable surface; every merge projects
// user deltas into HavenRichNotes through typed contract ops, preserving
// contract-only state (runs, extra table cells, canvas, attachments, unknown
// block kinds) by writing back ONLY fields that changed versus the baseline.
// Single persistence path: JsonFileHavenBoardStore (.9to1board schema v2).

using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;

namespace CakeOS.Apps.Boards.App;

public enum BoardBlockAction { MoveUp, MoveDown, Duplicate, Delete }
public enum BoardTableCellAction { MergeRight, MergeDown, Split }
public sealed record BoardBlockActionRequest(BoardBlockActionTarget Target, BoardBlockAction Action);
public sealed record BoardBlockMoveRequest(BoardBlockActionTarget Target, int DestinationIndex);
public sealed record BoardBlockActionTarget(Guid OwnerId, long Revision, string PageId, string BlockId,
    string ViewStamp, int ViewIndex, int ViewCount);
public sealed record BoardBlockActionResult(bool Applied, string Code, string? SelectedBlockId);

public sealed class ContractSessionAdapter : IRichBoardSession, IBoardTextRangeSession, IBoardSessionCapabilities,
    IBoardSessionLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly JsonFileHavenBoardStore? _store;
    private readonly RichBoardSession _real;
    private readonly SemaphoreSlim _mergeGate = new(1, 1);
    private readonly RichBoardDocument _view = new() { Title = "Untitled board", Sections = [] };
    private string _baselineJson = "{}";
    private string? _lastError;
    private volatile bool _disposed;
    bool IBoardSessionLifetime.IsDisposed => _disposed;
    private readonly bool _canSwitchBoard;
    private readonly bool _canSaveAs;
    private readonly Guid _textOwner = Guid.NewGuid();
    private BoardTextSelection? _issuedTextSelection;

    private ContractSessionAdapter(JsonFileHavenBoardStore? store, RichBoardSession real,
        bool canSwitchBoard = true, bool canSaveAs = true)
    {
        _store = store;
        _real = real;
        _canSwitchBoard = canSwitchBoard;
        _canSaveAs = canSaveAs;
        _real.StatusChanged += (_, status) => StatusChanged?.Invoke(this, Status);
    }

    public static async Task<ContractSessionAdapter> OpenAsync(
        JsonFileHavenBoardStore store, string? path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        RichBoardSession real;
        if (string.IsNullOrWhiteSpace(path))
        {
            var fallback = DefaultBoardPath();
            if (!File.Exists(fallback) && !File.Exists(fallback + ".bak"))
                return await CreateNewAtPathAsync(store, fallback, cancellationToken).ConfigureAwait(false);
            real = await RichBoardSession.OpenAtPathAsync(store, fallback, cancellationToken).ConfigureAwait(false);
        }
        else if (IsMemoryPath(path))
        {
            real = await RichBoardSession.CreateNewAsync(store, "Untitled board", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Opening is exact; creation normalizes to a visible .9to1board document.
            // A bare name whose .9to1board sibling exists opens that sibling.
            var full = Path.GetFullPath(path.Trim());
            if (!File.Exists(full) && !full.EndsWith(".9to1board", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(full + ".9to1board"))
                full = full + ".9to1board";
            real = File.Exists(full)
                ? await RichBoardSession.OpenAtPathAsync(store, full, cancellationToken).ConfigureAwait(false)
                : await CreateAtAsync(store, EnsureBoardExtension(full),
                    Path.GetFileNameWithoutExtension(full), cancellationToken).ConfigureAwait(false);
        }

        var adapter = new ContractSessionAdapter(store, real);
        adapter.RefreshView();
        return adapter;
    }

    /// <summary>Opens a detached, non-persisting view over an already validated document.</summary>
    public static Task<ContractSessionAdapter> OpenReadOnlyAsync(HavenBoardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var adapter = new ContractSessionAdapter(null, RichBoardSession.OpenReadOnly(document));
        adapter.RefreshView();
        return Task.FromResult(adapter);
    }

    /// <summary>Opens an editable detached document whose only persistence path is the
    /// caller's explicit callback. The callback is invoked after projection and validation.</summary>
    public static Task<ContractSessionAdapter> OpenExternallyWritableAsync(
        HavenBoardDocument document,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>> save)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(save);
        var adapter = new ContractSessionAdapter(null,
            RichBoardSession.OpenExternallyWritable(document, save), canSwitchBoard: false, canSaveAs: false);
        adapter.RefreshView();
        return Task.FromResult(adapter);
    }

    /// <summary>Creates and durably saves a new board at a distinct local path.</summary>
    public static async Task<ContractSessionAdapter> CreateNewAtPathAsync(
        JsonFileHavenBoardStore store, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = EnsureBoardExtension(Path.GetFullPath(path.Trim()));
        if (Directory.Exists(fullPath) || File.Exists(fullPath) || File.Exists(fullPath + ".bak"))
            throw new IOException("A board or recovery backup already exists at that location. Choose a different file name.");

        var title = Path.GetFileNameWithoutExtension(fullPath);
        var real = await RichBoardSession.CreateNewAsync(store, title, cancellationToken).ConfigureAwait(false);
        try
        {
            await real.SaveAsAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var adapter = new ContractSessionAdapter(store, real);
            adapter.RefreshView();
            return adapter;
        }
        catch
        {
            await real.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<RichBoardSession> CreateAtAsync(
        JsonFileHavenBoardStore store, string path, string title, CancellationToken cancellationToken)
    {
        var created = await RichBoardSession.CreateNewAsync(store, title, cancellationToken).ConfigureAwait(false);
        await created.SaveAsAsync(path, cancellationToken).ConfigureAwait(false);
        await created.SaveAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    public static string DefaultBoardPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(documents, "9to1 Boards", "Board.9to1board");
    }

    public RichBoardDocument Document => _view;
    /// <summary>Returns a full detached contract document snapshot for host metadata.</summary>
    public async Task<HavenBoardDocument> GetDetachedContractDocumentAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            return HavenBoardDocumentCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(_real.Document, Json));
        }
        finally { _mergeGate.Release(); }
    }
    public string Snapshot => JsonSerializer.Serialize(_view, Json);
    public bool CanEdit => !_real.IsReadOnly;
    public bool CanSwitchBoard => _canSwitchBoard;
    public bool CanSaveAs => _canSaveAs;
    public bool HasUnsavedChanges => _real.HasUnsavedChanges ||
        !string.Equals(_baselineJson, ProjectedViewJson(), StringComparison.Ordinal);
    private void EnsureEditable()
    {
        if (!CanEdit) throw new InvalidOperationException("This board source is open read-only.");
    }
    public string Status => _lastError ?? _real.Status;
    public string? FilePath => _real.FilePath;
    public event EventHandler<string>? StatusChanged;

    /// <summary>Monotonic contract revision for cache invalidation.</summary>
    public long DocumentVersion => _real.HasRichNotes ? _real.Rich.Version : -1;

    public IReadOnlyList<RichStyleView> Styles =>
        _real.HasRichNotes
            ? _real.Rich.Styles
                .OrderBy(s => s.IsBuiltIn ? 0 : 1)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => new RichStyleView
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsBuiltIn = s.IsBuiltIn,
                    BlockKind = s.BlockKind.ToString().ToLowerInvariant(),
                    Bold = s.Bold,
                    Italic = s.Italic,
                    Underline = s.Underline,
                    Strike = s.StrikeThrough,
                    Baseline = s.Baseline.ToString().ToLowerInvariant(),
                    FontFamily = s.FontFamily,
                    FontSize = s.FontSize,
                    Foreground = s.Foreground,
                    Background = s.Background,
                    Alignment = s.Alignment.ToString().ToLowerInvariant()
                }).ToArray()
            : [];

    private static string TextViewStamp(RichBoardBlock block) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(block, Json)));

    public BoardTextSelection? CaptureTextSelection(string pageId, string blockId, int anchor, int caret)
    {
        if (_disposed) { _issuedTextSelection = null; return null; }
        var block = _view.Sections.SelectMany(section => section.Pages).FirstOrDefault(page => page.Id == pageId)?
            .Blocks.FirstOrDefault(value => value.Id == blockId);
        if (block is null || block.Kind is not ("paragraph" or "heading") || !BoardTextRanges.Matches(block) ||
            anchor == caret || anchor < 0 || caret < 0 || anchor > block.Text.Length || caret > block.Text.Length)
        { _issuedTextSelection = null; return null; }
        var runs = BoardTextRanges.Selected(block, Math.Min(anchor, caret), Math.Max(anchor, caret));
        var candidate = new BoardTextSelection(_textOwner, DocumentVersion, pageId, blockId, block.Text,
            TextViewStamp(block), anchor, caret, BoardTextRanges.Mark(runs, BoardTextFormat.Bold),
            BoardTextRanges.Mark(runs, BoardTextFormat.Italic), BoardTextRanges.Mark(runs, BoardTextFormat.Underline),
            BoardTextRanges.Mark(runs, BoardTextFormat.StrikeThrough));
        if (candidate != _issuedTextSelection) _issuedTextSelection = candidate;
        return _issuedTextSelection;
    }

    public async ValueTask<BoardTextActionResult> ToggleTextSelectionAsync(BoardTextSelection selection,
        BoardTextFormat format, Func<bool> originStillCurrent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(originStillCurrent);
        if (_disposed) return new(false, "Disposed");
        if (!Enum.IsDefined(format)) return new(false, "UnsupportedFormat");
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool CurrentView()
            {
                if (_disposed || !ReferenceEquals(selection, _issuedTextSelection) || selection.OwnerId != _textOwner || !originStillCurrent()) return false;
                var current = _view.Sections.SelectMany(section => section.Pages).FirstOrDefault(page => page.Id == selection.PageId)?
                    .Blocks.FirstOrDefault(block => block.Id == selection.BlockId);
                return current is not null && current.Text == selection.Text && TextViewStamp(current) == selection.ViewStamp;
            }
            if (!CurrentView() || DocumentVersion != selection.Revision) return new(false, "StaleOrReadOnly");
            // Already-typed changes keep their existing separate history entry.
            // Formatting itself remains one transaction of this same owner.
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var version = DocumentVersion;
            var actualBlock = _real.Rich.Sections.SelectMany(section => section.Pages).Single(page => page.Id == selection.PageId)
                .Blocks.Single(value => value.Id == selection.BlockId);
            if (actualBlock.PlainText != selection.Text) return new(false, "StaleText");
            var block = new RichBoardBlock { Text = actualBlock.PlainText, Runs = BoardTextRanges.Copy(actualBlock.Runs) };
            if (!BoardTextRanges.Matches(block)) return new(false, "InconsistentRuns");
            var changed = BoardTextRanges.Toggle(block, Math.Min(selection.Anchor, selection.Caret), Math.Max(selection.Anchor, selection.Caret), format);
            var applied = await _real.MutateIfAsync(rich => CurrentView() && rich.Version == version &&
                rich.Sections.SelectMany(section => section.Pages).FirstOrDefault(page => page.Id == selection.PageId)?
                    .Blocks.FirstOrDefault(value => value.Id == selection.BlockId) is { } actual && actual.PlainText == selection.Text,
                rich => HavenRichNotesOps.SetBlockRuns(rich, selection.PageId, selection.BlockId, changed), cancellationToken).ConfigureAwait(false);
            if (!applied) return new(false, "StaleOrReadOnly");
            _issuedTextSelection = null;
            RefreshView();
            return new(true, "Applied");
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> ToggleBlockTextFormatAsync(string pageId, string blockId,
        BoardTextFormat format, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var separator = blockId.IndexOf(':');
            var contractBlockId = separator > 0 ? blockId[..separator] : blockId;
            var actual = _real.Rich.Sections.SelectMany(section => section.Pages)
                .FirstOrDefault(page => page.Id == pageId)?.Blocks.FirstOrDefault(block => block.Id == contractBlockId);
            if (actual is null) return false;
            if (separator > 0)
            {
                if (actual.Kind is not (HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList))
                    return false;
                var itemId = blockId[(separator + 1)..];
                var item = actual.Items.FirstOrDefault(candidate => candidate.Id == itemId);
                if (item is null) return false;
                var enabled = format switch
                {
                    BoardTextFormat.Bold => item.Bold,
                    BoardTextFormat.Italic => item.Italic,
                    BoardTextFormat.Underline => item.Underline,
                    BoardTextFormat.StrikeThrough => item.StrikeThrough,
                    _ => throw new ArgumentOutOfRangeException(nameof(format))
                };
                await _real.MutateAsync(rich => HavenRichNotesOps.SetListItemFormatting(rich, pageId,
                    contractBlockId, itemId,
                    bold: format == BoardTextFormat.Bold ? !enabled : null,
                    italic: format == BoardTextFormat.Italic ? !enabled : null,
                    underline: format == BoardTextFormat.Underline ? !enabled : null,
                    strike: format == BoardTextFormat.StrikeThrough ? !enabled : null), cancellationToken).ConfigureAwait(false);
                RefreshView();
                return true;
            }
            if (actual.Kind is not (HavenRichBlockKind.Paragraph or HavenRichBlockKind.Heading))
                return false;

            var block = new RichBoardBlock { Text = actual.PlainText, Runs = BoardTextRanges.Copy(actual.Runs) };
            if (!BoardTextRanges.Matches(block)) return false;
            List<HavenRichTextRun> changed;
            if (block.Text.Length == 0)
            {
                var runs = block.Runs.Count == 0 ? new List<HavenRichTextRun> { new() } : block.Runs;
                var enable = runs.Any(run => !BoardTextRanges.Enabled(run, format));
                foreach (var run in runs) BoardTextRanges.Set(run, format, enable);
                changed = BoardTextRanges.Copy(runs);
            }
            else
            {
                changed = BoardTextRanges.Toggle(block, 0, block.Text.Length, format);
            }

            await _real.MutateAsync(rich =>
                HavenRichNotesOps.SetBlockRuns(rich, pageId, contractBlockId, changed), cancellationToken).ConfigureAwait(false);
            RefreshView();
            return true;
        }
        finally { _mergeGate.Release(); }
    }

    // Each rendered grip gets one exact ticket for this view. Keep the whole
    // current-view set so rendering later rows cannot invalidate earlier ones.
    private readonly object _blockActionTicketGate = new();
    private readonly List<BoardBlockActionTarget> _issuedBlockActions = [];
    private string? _issuedBlockActionViewStamp;
    private bool IsIssuedBlockAction(BoardBlockActionTarget target)
    {
        lock (_blockActionTicketGate)
            return _issuedBlockActions.Any(issued => ReferenceEquals(target, issued));
    }
    private string BlockActionStamp() => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(_view, Json)));

    /// <summary>A transient current-owner ticket, never another document/history.
    /// Projection child ids and ink identities are not whole contract blocks.</summary>
    public BoardBlockActionTarget? CaptureBlockAction(string pageId, string blockId)
    {
        if (_disposed) return null;
        var page = _view.Sections.SelectMany(section => section.Pages).FirstOrDefault(value => value.Id == pageId);
        var index = page?.Blocks.FindIndex(block => block.Id == blockId) ?? -1;
        if (page is null || index < 0 || blockId.Contains(':') ||
            page.Blocks[index].Kind is not ("paragraph" or "heading" or "table" or "image" or "divider" or "graph" or
                "checklist" or "bulleted" or "numbered"))
        { return null; }
        var viewStamp = BlockActionStamp();
        lock (_blockActionTicketGate)
        {
            if (!string.Equals(_issuedBlockActionViewStamp, viewStamp, StringComparison.Ordinal))
            {
                _issuedBlockActions.Clear();
                _issuedBlockActionViewStamp = viewStamp;
            }
            var candidate = new BoardBlockActionTarget(_textOwner, DocumentVersion, pageId, blockId,
                viewStamp, index, page.Blocks.Count);
            var issued = _issuedBlockActions.FirstOrDefault(value => value == candidate);
            if (issued is not null) return issued;
            _issuedBlockActions.Add(candidate);
            return candidate;
        }
    }

    public async ValueTask<BoardBlockActionResult> ApplyBlockActionAsync(BoardBlockActionTarget target,
        BoardBlockAction action, Func<bool> originStillEditable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(originStillEditable);
        if (_disposed) return new(false, "Disposed", null);
        if (!Enum.IsDefined(action)) return new(false, "UnsupportedAction", null);
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool CurrentView() => !_disposed && IsIssuedBlockAction(target) &&
                target.OwnerId == _textOwner && originStillEditable() && target.ViewStamp == BlockActionStamp();
            if (!CurrentView() || target.Revision != DocumentVersion) return new(false, "StaleOrReadOnly", null);
            if ((action == BoardBlockAction.MoveUp && target.ViewIndex == 0) ||
                (action == BoardBlockAction.MoveDown && target.ViewIndex == target.ViewCount - 1))
                return new(false, "AtBoundary", target.BlockId);
            // Pending typed edits retain their existing separate transaction.
            // Initial refusals never merge. A later refusal may leave that
            // typed-edit transaction, but never pushes an empty block action.
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var version = DocumentVersion;
            bool Applicable(HavenRichNotes rich)
            {
                if (!CurrentView() || rich.Version != version) return false;
                var page = rich.Sections.SelectMany(section => section.Pages).FirstOrDefault(value => value.Id == target.PageId);
                var index = page?.Blocks.FindIndex(block => block.Id == target.BlockId) ?? -1;
                return page is not null && index >= 0 &&
                    (action != BoardBlockAction.MoveUp || index > 0) &&
                    (action != BoardBlockAction.MoveDown || index < page.Blocks.Count - 1);
            }
            if (!Applicable(_real.Rich)) return new(false, "StaleOrBoundary", null);
            string? selected = target.BlockId;
            var applied = await _real.MutateIfAsync(Applicable, rich =>
            {
                var page = rich.Sections.SelectMany(section => section.Pages).Single(value => value.Id == target.PageId);
                var index = page.Blocks.FindIndex(block => block.Id == target.BlockId);
                switch (action)
                {
                    case BoardBlockAction.MoveUp:
                        HavenRichNotesOps.MoveBlock(rich, page.Id, target.BlockId, index - 1); break;
                    case BoardBlockAction.MoveDown:
                        HavenRichNotesOps.MoveBlock(rich, page.Id, target.BlockId, index + 1); break;
                    case BoardBlockAction.Duplicate:
                        selected = HavenRichNotesOps.DuplicateBlock(rich, page.Id, target.BlockId).Id; break;
                    case BoardBlockAction.Delete:
                        HavenRichNotesOps.RemoveBlock(rich, page.Id, target.BlockId);
                        selected = page.Blocks.ElementAtOrDefault(Math.Min(index, page.Blocks.Count - 1))?.Id; break;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (!applied) return new(false, "StaleOrReadOnly", null);
            RefreshView();
            return new(true, "Applied", selected);
        }
        finally { _mergeGate.Release(); }
    }

    /// <summary>Moves one issued stable block ticket to a final page index as one undoable mutation.</summary>
    public async ValueTask<BoardBlockActionResult> ApplyBlockMoveAsync(BoardBlockActionTarget target,
        int destinationIndex, Func<bool> originStillEditable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(originStillEditable);
        if (_disposed) return new(false, "Disposed", null);
        if (destinationIndex < 0 || destinationIndex >= target.ViewCount)
            return new(false, "InvalidDestination", null);
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool CurrentView() => !_disposed && IsIssuedBlockAction(target) &&
                target.OwnerId == _textOwner && originStillEditable() && target.ViewStamp == BlockActionStamp();
            if (!CurrentView() || target.Revision != DocumentVersion) return new(false, "StaleOrReadOnly", null);
            if (destinationIndex == target.ViewIndex) return new(false, "NoChange", target.BlockId);
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var version = DocumentVersion;
            bool Applicable(HavenRichNotes rich)
            {
                if (!CurrentView() || rich.Version != version) return false;
                var page = rich.Sections.SelectMany(section => section.Pages).FirstOrDefault(value => value.Id == target.PageId);
                var index = page?.Blocks.FindIndex(block => block.Id == target.BlockId) ?? -1;
                return page is not null && index >= 0 && destinationIndex < page.Blocks.Count;
            }
            if (!Applicable(_real.Rich)) return new(false, "StaleOrBoundary", null);
            var applied = await _real.MutateIfAsync(Applicable, rich =>
            {
                HavenRichNotesOps.MoveBlock(rich, target.PageId, target.BlockId, destinationIndex);
            }, cancellationToken).ConfigureAwait(false);
            if (!applied) return new(false, "StaleOrReadOnly", null);
            RefreshView();
            return new(true, "Applied", target.BlockId);
        }
        finally { _mergeGate.Release(); }
    }

    /// <summary>Reorders a stable table axis as one guarded, undoable document mutation.</summary>
    public async ValueTask<bool> MoveTableAxisAsync(string pageId, string blockId, bool moveRow,
        int fromIndex, int toIndex, Func<bool> originStillSelected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originStillSelected);
        if (_disposed || string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(blockId) ||
            fromIndex < 0 || toIndex < 0 || fromIndex == toIndex || !originStillSelected())
            return false;
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureEditable();
            if (!originStillSelected()) return false;
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var version = DocumentVersion;
            bool Applicable(HavenRichNotes rich)
            {
                if (_disposed || !originStillSelected() || rich.Version != version) return false;
                var page = rich.Sections.SelectMany(section => section.Pages)
                    .FirstOrDefault(candidate => candidate.Id == pageId);
                var table = page?.Blocks.FirstOrDefault(candidate => candidate.Id == blockId)?.Table;
                if (table is null || table.Rows.Count == 0) return false;
                if (moveRow) return fromIndex < table.Rows.Count && toIndex < table.Rows.Count;
                var columnCount = table.Rows[0].Cells.Count;
                return table.Rows.All(row => row.Cells.Count == columnCount) &&
                    fromIndex < columnCount && toIndex < columnCount;
            }
            if (!Applicable(_real.Rich)) return false;
            var moved = false;
            var applied = await _real.MutateIfAsync(Applicable, rich =>
            {
                moved = moveRow
                    ? HavenRichNotesOps.MoveTableRow(rich, pageId, blockId, fromIndex, toIndex)
                    : HavenRichNotesOps.MoveTableColumn(rich, pageId, blockId, fromIndex, toIndex);
            }, cancellationToken).ConfigureAwait(false);
            if (!applied || !moved) return false;
            RefreshView();
            return true;
        }
        finally { _mergeGate.Release(); }
    }

    /// <summary>Applies one selected-cell merge or split as a guarded, undoable contract mutation.</summary>
    public async ValueTask<bool> ApplyTableCellActionAsync(string pageId, string blockId, int row, int column,
        BoardTableCellAction action, Func<bool> originStillSelected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originStillSelected);
        if (_disposed || string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(blockId) ||
            row < 0 || column < 0 || !originStillSelected())
            return false;
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureEditable();
            if (!originStillSelected()) return false;
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var version = DocumentVersion;
            bool Applicable(HavenRichNotes rich)
            {
                if (_disposed || !originStillSelected() || rich.Version != version) return false;
                var page = rich.Sections.SelectMany(section => section.Pages)
                    .FirstOrDefault(candidate => candidate.Id == pageId);
                var table = page?.Blocks.FirstOrDefault(candidate => candidate.Id == blockId)?.Table;
                if (table is null || row >= table.Rows.Count || column >= table.Rows[row].Cells.Count)
                    return false;
                var cell = table.Rows[row].Cells[column];
                return action switch
                {
                    BoardTableCellAction.MergeRight => HavenRichNotesOps.CanMergeTableCellRight(rich, pageId, blockId, row, column),
                    BoardTableCellAction.MergeDown => HavenRichNotesOps.CanMergeTableCellDown(rich, pageId, blockId, row, column),
                    BoardTableCellAction.Split => cell.CoveredByCellId is null && (cell.RowSpan > 1 || cell.ColumnSpan > 1),
                    _ => false
                };
            }
            if (!Applicable(_real.Rich)) return false;
            var appliedAction = false;
            var applied = await _real.MutateIfAsync(Applicable, rich =>
            {
                appliedAction = action switch
                {
                    BoardTableCellAction.MergeRight => HavenRichNotesOps.MergeTableCellRight(rich, pageId, blockId, row, column),
                    BoardTableCellAction.MergeDown => HavenRichNotesOps.MergeTableCellDown(rich, pageId, blockId, row, column),
                    BoardTableCellAction.Split => HavenRichNotesOps.SplitTableCell(rich, pageId, blockId, row, column),
                    _ => false
                };
            }, cancellationToken).ConfigureAwait(false);
            if (!applied || !appliedAction) return false;
            RefreshView();
            return true;
        }
        finally { _mergeGate.Release(); }
    }

    public bool CanUndo => _real.CanUndo;
    public bool CanRedo => _real.CanRedo;

    public async ValueTask<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Merge pending view edits first so undo steps back through them in order.
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var undone = await _real.UndoAsync(cancellationToken).ConfigureAwait(false);
            RefreshView();
            return undone;
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    public async ValueTask<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var redone = await _real.RedoAsync(cancellationToken).ConfigureAwait(false);
            RefreshView();
            return redone;
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    public ValueTask OpenAsync(string? path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Adapter sessions are opened via ContractSessionAdapter.OpenAsync.");

    public void MarkDirty()
    {
        ThrowIfDisposed();
        EnsureEditable();
        _ = MergeAsync();
        _ = _real.RequestAutosaveAsync();
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureEditable();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var submittedView = JsonSerializer.Serialize(_view, Json);
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            _baselineJson = ProjectedViewJson();
            await _real.SaveAsync(cancellationToken).ConfigureAwait(false);
            await RefreshAfterSaveAsync(submittedView, cancellationToken).ConfigureAwait(false);
            _lastError = null;
        }
        catch (Exception error)
        {
            SetError("Save failed: " + FirstLine(error.Message));
            throw;
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    public async ValueTask SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureEditable();
        if (!_canSaveAs) throw new InvalidOperationException("This source does not grant Save As access.");
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var submittedView = JsonSerializer.Serialize(_view, Json);
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            _baselineJson = ProjectedViewJson();
            await _real.SaveAsAsync(path, cancellationToken).ConfigureAwait(false);
            await RefreshAfterSaveAsync(submittedView, cancellationToken).ConfigureAwait(false);
            _lastError = null;
        }
        catch (Exception error)
        {
            SetError("Save failed: " + FirstLine(error.Message));
            throw;
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    /// <summary>Commits one pointer-drawn stroke as real persisted ink data.</summary>
    public async ValueTask CommitInkStrokeAsync(
        string pageId, IReadOnlyList<(double X, double Y)> points, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return;
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            await _real.MutateAsync(rich =>
            {
                var target = rich.Sections.SelectMany(s => s.Pages).First(p => p.Id == page.Id);
                HavenRichNotesOps.AddInkStroke(rich, target.Id, new HavenRichInkStroke
                {
                    Points = points.Select(p => new HavenRichInkPoint { X = p.X, Y = p.Y }).ToList(),
                    Width = 2.5,
                    Color = "#FF111111"
                });
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    /// <summary>Keyboard/non-pointer fallback: appends a small but real ink stroke.</summary>
    public async ValueTask<int> EraseInkAtPageAsync(
        string pageId, double x, double y, double radius = 12, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            var removed = 0;
            await _real.MutateAsync(rich =>
                removed = HavenRichNotesOps.EraseInkAt(rich, page.Id, x, y, radius),
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return removed;
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    /// <summary>Keyboard/non-pointer fallback: appends a small but real ink stroke.</summary>
    public async ValueTask AddSampleInkStrokeAsync(string pageId, CancellationToken cancellationToken = default)
    {
        var random = new Random();
        var x = 40 + random.Next(0, 200);
        var y = 40 + random.Next(0, 80);
        await CommitInkStrokeAsync(pageId,
            [(x, y), (x + 60, y + 20), (x + 120, y - 10)], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects the nearest stroke within radius of a point (Select tool).</summary>
    public async ValueTask<bool> SelectInkAtPageAsync(
        string pageId, double x, double y, double radius = 14, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            var selected = false;
            await _real.MutateAsync(rich =>
            {
                var target = rich.Sections.SelectMany(s => s.Pages).First(p => p.Id == page.Id);
                var best = -1;
                var bestDistance = radius;
                for (var i = 0; i < target.Ink.Count; i++)
                {
                    foreach (var point in target.Ink[i].Points)
                    {
                        var distance = Math.Sqrt(
                            (point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
                        if (distance <= bestDistance)
                        {
                            bestDistance = distance;
                            best = i;
                        }
                    }
                }
                if (best >= 0)
                {
                    for (var i = 0; i < target.Ink.Count; i++)
                        HavenRichNotesOps.SetInkStrokeSelection(rich, target.Id, i, i == best);
                    selected = true;
                }
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return selected;
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    public Task<HavenRichAttachmentRef> ImportAttachmentAsync(
        string sourcePath, CancellationToken cancellationToken = default) =>
        _real.ImportAttachmentAsync(sourcePath, cancellationToken);

    /// <summary>Embedded image bytes without file IO (sidecars need the async path).</summary>
    public byte[]? TryGetEmbeddedImageBytes(string blockId)
    {
        ThrowIfDisposed();
        var data = _real.HasRichNotes
            ? _real.Rich.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks)
                .FirstOrDefault(b => b.Id == blockId)?.Image?.DataBase64
            : null;
        if (string.IsNullOrEmpty(data))
            return null;
        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Resolves display bytes for an image block (embedded or sidecar).</summary>
    public Task<byte[]?> GetImageBytesAsync(string blockId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var image = _real.HasRichNotes
            ? _real.Rich.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks)
                .FirstOrDefault(b => b.Id == blockId)?.Image
            : null;
        if (image is null)
            return Task.FromResult<byte[]?>(null);
        if (!string.IsNullOrEmpty(image.DataBase64))
        {
            try
            {
                return Task.FromResult<byte[]?>(Convert.FromBase64String(image.DataBase64));
            }
            catch (FormatException)
            {
                return Task.FromResult<byte[]?>(null);
            }
        }
        if (!string.IsNullOrEmpty(image.LocalReference) &&
            image.LocalReference.StartsWith("sidecar:", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(_real.FilePath))
                return Task.FromResult<byte[]?>(null);
            try
            {
                var blob = Path.Combine(
                    RichBoardSession.SidecarDirectoryFor(_real.FilePath),
                    image.LocalReference["sidecar:".Length..] + ".blob");
                return Task.FromResult<byte[]?>(File.Exists(blob) ? File.ReadAllBytes(blob) : null);
            }
            catch (IOException)
            {
                return Task.FromResult<byte[]?>(null);
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult<byte[]?>(null);
            }
        }
        return Task.FromResult<byte[]?>(null);
    }
    public Task<HavenAttachmentResolution> ResolveAttachmentAsync(
        string attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = _real.Rich.Sections
            .SelectMany(s => s.Pages).SelectMany(p => p.Blocks)
            .Select(b => b.Attachment).FirstOrDefault(a => a?.Id == attachmentId)
            ?? throw new KeyNotFoundException("Attachment was not found on this board.");
        return _real.ResolveAttachmentAsync(attachment, cancellationToken);
    }

    public async ValueTask AttachFileToBlockAsync(
        string pageId, string blockId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var attachment = await _real.ImportAttachmentAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(
                rich => HavenRichNotesOps.AttachToBlock(rich, pageId, blockId, attachment),
                cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally
        {
            _mergeGate.Release();
        }
    }

    private HavenRichPage ContractPage(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        return _real.Rich.Sections.SelectMany(s => s.Pages).FirstOrDefault(p => p.Id == pageId)
            ?? throw new InvalidOperationException($"The selected board page '{pageId}' no longer exists.");
    }

    private async Task MergeAsync()
    {
        try
        {
            await _mergeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await MergeCoreAsync(CancellationToken.None).ConfigureAwait(false);
                RefreshView();
                _lastError = null;
            }
            finally
            {
                _mergeGate.Release();
            }
        }
        catch (Exception error)
        {
            SetError("Merge failed: " + FirstLine(error.Message));
        }
    }

    private async Task MergeCoreAsync(CancellationToken cancellationToken)
    {
        var baseline = JsonSerializer.Deserialize<RichBoardDocument>(_baselineJson, Json)
            ?? new RichBoardDocument();
        // The real session can await its gate. Never pass the live, mutable editor
        // graph into that later callback: this merge represents this exact draft.
        var submitted = JsonSerializer.Deserialize<RichBoardDocument>(
            JsonSerializer.Serialize(_view, Json), Json) ?? new RichBoardDocument();
        if (!MergeDeltas.HasChanges(submitted, baseline))
            return;
        await _real.MutateAsync(rich => MergeDeltas.Apply(rich, submitted, baseline), cancellationToken)
            .ConfigureAwait(false);
    }

    private string ProjectedViewJson()
    {
        var projected = new RichBoardDocument();
        Projector.Project(_real.Rich, projected);
        return JsonSerializer.Serialize(projected, Json);
    }

    private async Task RefreshAfterSaveAsync(string submittedView, CancellationToken cancellationToken)
    {
        if (string.Equals(submittedView, JsonSerializer.Serialize(_view, Json), StringComparison.Ordinal))
        {
            RefreshView();
            return;
        }
        // The physical ACK belongs to the submitted draft. Keep newer editor
        // input intact and merge it as dirty; a later Save/autosave persists it.
        await MergeCoreAsync(cancellationToken);
        _baselineJson = ProjectedViewJson();
    }

    private void RefreshView()
    {
        // Undo/redo can restore equal revision/content; never revive an issued range.
        _issuedTextSelection = null;
        lock (_blockActionTicketGate)
        {
            _issuedBlockActions.Clear();
            _issuedBlockActionViewStamp = null;
        }
        Projector.Project(_real.Rich, _view);
        _baselineJson = JsonSerializer.Serialize(_view, Json);
    }

    private void SetError(string message)
    {
        _lastError = message;
        StatusChanged?.Invoke(this, Status);
    }

    private static string FirstLine(string message) =>
        message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;

    private static string EnsureBoardExtension(string fullPath) =>
        fullPath.EndsWith(".9to1board", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath + ".9to1board";

    private static bool IsMemoryPath(string path) =>
        path.StartsWith("memory://", StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    // ----- Additive UI wrappers (permitted single exception: see task brief) -----
    // These delegate straight to the tested contract ops via _real.MutateAsync with
    // the same merge-first/refresh discipline as UndoAsync. They exist because
    // deletions, style catalog edits, canvas objects and ink clearing cannot
    // round-trip through the view-document merge (which only adds/updates).

    public async ValueTask<string> CreateCustomStyleAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.CreateCustomStyle(rich, name).Id, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask UpdateStyleAsync(string styleId, Action<HavenRichStyle> update, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(update);
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.UpdateStyle(rich, styleId, update), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask DeleteCustomStyleAsync(string styleId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.DeleteCustomStyle(rich, styleId), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> DuplicateStyleAsync(string styleId, string? newName = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.DuplicateStyle(rich, styleId, newName).Id, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> DeleteSectionAsync(string sectionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var removed = false;
            await _real.MutateAsync(rich =>
                removed = HavenRichNotesOps.RemoveSection(rich, sectionId), cancellationToken).ConfigureAwait(false);
            RefreshView();
            return removed;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> AddSectionAsync(string? title = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.AddSection(rich, title).Id, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> AddPageAsync(string sectionId, string? title = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.AddPage(rich, sectionId, title).Id, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> AddEmbeddedArtifactPageAsync(string sectionId, string pageType,
        BoardEmbeddedArtifactReference reference, string? title = null, string? parentPageId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(reference);
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich => id = HavenRichNotesOps.AddEmbeddedArtifactPage(
                rich, sectionId, pageType, reference, title, parentPageId).Id,
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask SetEmbeddedArtifactAsync(string pageId, string pageType,
        BoardEmbeddedArtifactReference? reference, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich => HavenRichNotesOps.SetEmbeddedArtifact(
                rich, pageId, pageType, reference), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> AddSubpageAsync(string sectionId, string parentPageId, string? title = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.AddSubpage(rich, sectionId, parentPageId, title).Id,
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask SetPageParentAsync(string pageId, string? parentPageId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.SetPageParent(rich, pageId, parentPageId), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> SplitTextBlockAtAsync(string pageId, string blockId, int caretIndex,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string createdId = string.Empty;
            await _real.MutateAsync(rich =>
                createdId = HavenRichNotesOps.SplitTextBlockAt(rich, pageId, blockId, caretIndex).Id,
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return createdId;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> MergeTextBlockWithPreviousAsync(string pageId, string blockId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string mergedId = string.Empty;
            await _real.MutateAsync(rich =>
                mergedId = HavenRichNotesOps.MergeTextBlockWithPrevious(rich, pageId, blockId),
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return mergedId;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask SetCanvasLockedAsync(string pageId, bool locked,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.SetCanvasLocked(rich, pageId, locked), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask RenameSectionAsync(string sectionId, string title,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.RenameSection(rich, sectionId, title), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask RenamePageAsync(string pageId, string title,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.RenamePage(rich, pageId, title), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> DuplicateSectionAsync(string sectionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.DuplicateSection(rich, sectionId).Id, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask MoveSectionAsync(string sectionId, int targetIndex, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.MoveSection(rich, sectionId, targetIndex), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> DeletePageAsync(string pageId, CancellationToken cancellationToken = default)    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var removed = false;
            await _real.MutateAsync(rich =>
                removed = HavenRichNotesOps.RemovePage(rich, pageId), cancellationToken).ConfigureAwait(false);
            RefreshView();
            return removed;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<string> DuplicatePageAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.DuplicatePage(rich, pageId).Id, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask MovePageAsync(string pageId, string targetSectionId, int targetIndex, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.MovePage(rich, pageId, targetSectionId, targetIndex), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    /// <summary>
    /// Converts a block between paragraph/heading/list kinds, preserving text
    /// and list items where they carry over. Position and identity are kept.
    /// </summary>
    public async ValueTask<bool> ConvertBlockKindAsync(
        string pageId, string blockId, HavenRichBlockKind newKind, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var converted = false;
            await _real.MutateAsync(rich =>
            {
                var page = rich.Sections.SelectMany(s => s.Pages).FirstOrDefault(p => p.Id == pageId);
                var index = page?.Blocks.FindIndex(b => b.Id == blockId) ?? -1;
                if (page is null || index < 0)
                    return;
                var old = page.Blocks[index];
                if (old.Kind == newKind)
                    return;
                var replacement = new HavenRichBlock
                {
                    Id = old.Id,
                    Kind = newKind,
                    Order = old.Order,
                    StyleId = old.StyleId,
                };
                if (IsListKind(old.Kind) && IsListKind(newKind))
                {
                    replacement.Items = old.Items;
                    replacement.PlainText = old.PlainText;
                }
                else if (IsListKind(newKind))
                {
                    replacement.Items = [new HavenRichListItem { Text = old.PlainText }];
                    replacement.PlainText = old.PlainText;
                }
                else
                {
                    replacement.PlainText = IsListKind(old.Kind) && old.Items.Count > 0
                        ? string.Join("\n", old.Items.Select(i => (i.Checked ? "[x] " : "[ ] ") + i.Text))
                        : old.PlainText;
                    replacement.Runs = old.Runs;
                }
                page.Blocks[index] = replacement;
                HavenRichNotesOps.TouchNotes(rich);
                converted = true;
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return converted;
        }
        finally { _mergeGate.Release(); }
    }

    private static bool IsListKind(HavenRichBlockKind kind) =>
        kind is HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList;

    /// <summary>
    /// Deletes a view block: checklist item ids ("parentId:itemId") remove one
    /// item, ink ids clear page ink, anything else removes the contract block.
    /// </summary>
    public async ValueTask<bool> DeleteViewBlockAsync(string pageId, string viewBlockId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var removed = false;
            await _real.MutateAsync(rich =>
            {
                var page = rich.Sections.SelectMany(s => s.Pages).FirstOrDefault(p => p.Id == pageId)
                    ?? throw new KeyNotFoundException("Page was not found.");
                if (viewBlockId == $"{page.Id}:ink" || viewBlockId.EndsWith(":ink", StringComparison.Ordinal))
                {
                    if (page.Ink.Count > 0) { HavenRichNotesOps.ClearInk(rich, page.Id); removed = true; }
                    return;
                }
                var colon = viewBlockId.IndexOf(':');
                if (colon > 0)
                {
                    var parentId = viewBlockId[..colon];
                    var itemId = viewBlockId[(colon + 1)..];
                    removed = HavenRichNotesOps.RemoveListItem(rich, page.Id, parentId, itemId);
                    return;
                }
                removed = HavenRichNotesOps.RemoveBlock(rich, page.Id, viewBlockId);
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return removed;
        }
        finally { _mergeGate.Release(); }
    }

    /// <summary>Appends one checklist item to a specific parent list block.</summary>
    public async ValueTask<string> AddChecklistItemAsync(
        string pageId, string parentBlockId, string text, bool isChecked = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            string itemId = string.Empty;
            await _real.MutateAsync(rich =>
            {
                var page = rich.Sections.SelectMany(s => s.Pages).FirstOrDefault(p => p.Id == pageId)
                    ?? throw new KeyNotFoundException("Page was not found.");
                var parent = page.Blocks.FirstOrDefault(b => b.Id == parentBlockId)
                    ?? throw new KeyNotFoundException("List block was not found.");
                do { itemId = "item-" + Guid.NewGuid().ToString("N")[..12]; }
                while (parent.Items.Any(i => i.Id == itemId));
                HavenRichNotesOps.ImportListItem(parent, itemId, text, isChecked);
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return itemId;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask DetachAttachmentAsync(string pageId, string blockId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.RemoveAttachmentFromBlock(rich, pageId, blockId), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask ClearInkAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.ClearInk(rich, page.Id), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> RemoveLastInkStrokeAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            var removed = false;
            await _real.MutateAsync(rich =>
            {
                var target = rich.Sections.SelectMany(s => s.Pages).First(p => p.Id == page.Id);
                if (target.Ink.Count > 0)
                    removed = HavenRichNotesOps.RemoveInkStroke(rich, target.Id, target.Ink.Count - 1);
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
            return removed;
        }
        finally { _mergeGate.Release(); }
    }

    /// <summary>Pointer-drawn stroke with the selected tool/width/color/pressure.</summary>
    public async ValueTask CommitInkStrokeAsync(
        string pageId, IReadOnlyList<(double X, double Y, double Pressure)> points,
        double width, string color, string toolName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return;
        if (!Enum.TryParse<HavenRichInkTool>(toolName, ignoreCase: true, out var tool))
            tool = HavenRichInkTool.Pen;
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            await _real.MutateAsync(rich =>
            {
                var target = rich.Sections.SelectMany(s => s.Pages).First(p => p.Id == page.Id);
                HavenRichNotesOps.AddInkStroke(rich, target.Id, new HavenRichInkStroke
                {
                    Points = points.Select(p => new HavenRichInkPoint
                    {
                        X = p.X, Y = p.Y,
                        Pressure = double.IsFinite(p.Pressure) && p.Pressure > 0 ? Math.Clamp(p.Pressure, 0.05, 1) : 0.5
                    }).ToList(),
                    Width = Math.Clamp(width, 0.5, 64),
                    Color = string.IsNullOrWhiteSpace(color) ? "#FF111111" : color.Trim(),
                    Tool = tool
                });
            }, cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask SetInkViewAsync(string pageId, double panX, double panY, double zoom, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.SetInkView(rich, page.Id, panX, panY, zoom), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public Task<IReadOnlyList<CanvasBoxView>> GetCanvasObjectsAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var page = ContractPage(pageId);
        IReadOnlyList<CanvasBoxView> boxes = page.Canvas.Select(o => new CanvasBoxView
        {
            Id = o.Id, Kind = o.Kind, Text = o.Text,
            X = o.X, Y = o.Y, Width = o.Width, Height = o.Height,
            CanvasWidth = page.CanvasWidth, CanvasHeight = page.CanvasHeight
        }).ToArray();
        return Task.FromResult(boxes);
    }

    /// <summary>Copies persisted page ink for the transparent editor overlay.</summary>
    public Task<IReadOnlyList<InkStrokeView>> GetInkStrokesAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var page = ContractPage(pageId);
        IReadOnlyList<InkStrokeView> strokes = page.Ink.Select(stroke => new InkStrokeView
        {
            Points = stroke.Points.Select(point => new InkPointView
            {
                X = point.X,
                Y = point.Y,
                Pressure = point.Pressure,
            }).ToList(),
            Width = stroke.Width,
            Color = stroke.Color,
            Tool = stroke.Tool.ToString(),
            Selected = stroke.Selected,
        }).ToArray();
        return Task.FromResult(strokes);
    }

    public async ValueTask<string> AddCanvasObjectAsync(
        string pageId, string kind, string? text, double x, double y,
        double width = 260, double height = 160, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.AddCanvasObject(rich, page.Id, kind, text, x, y, width, height).Id,
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> MoveCanvasObjectAsync(string pageId, string objectId, double x, double y, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            var moved = false;
            await _real.MutateAsync(rich =>
                moved = HavenRichNotesOps.MoveCanvasObject(rich, page.Id, objectId, x, y),
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return moved;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> RemoveCanvasObjectAsync(string pageId, string objectId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = ContractPage(pageId);
            var removed = false;
            await _real.MutateAsync(rich =>
                removed = HavenRichNotesOps.RemoveCanvasObject(rich, page.Id, objectId),
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return removed;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _mergeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try { await MergeCoreAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* Dispose must still flush via the real session. */ }
            await _real.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _mergeGate.Release();
            _mergeGate.Dispose();
        }
    }
}

/// <summary>Freeform canvas box surfaced to the CUI editor (read model only).</summary>
public sealed class CanvasBoxView
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "Text";
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 160;
    public double CanvasWidth { get; set; } = 1200;
    public double CanvasHeight { get; set; } = 900;
}
