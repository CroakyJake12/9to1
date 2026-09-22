// Bridges the CUI editor's working model to the durable contract session.
// The worker RichBoardDocument stays the editable surface; every merge projects
// user deltas into HavenRichNotes through typed contract ops, preserving
// contract-only state (runs, extra table cells, canvas, attachments, unknown
// block kinds) by writing back ONLY fields that changed versus the baseline.
// Single persistence path: JsonFileHavenBoardStore (.9to1board schema v2).

using System.Text.Json;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed class ContractSessionAdapter : IRichBoardSession
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly JsonFileHavenBoardStore _store;
    private readonly RichBoardSession _real;
    private readonly SemaphoreSlim _mergeGate = new(1, 1);
    private readonly RichBoardDocument _view = new() { Title = "Untitled board", Sections = [] };
    private string _baselineJson = "{}";
    private string? _lastError;
    private bool _disposed;

    private ContractSessionAdapter(JsonFileHavenBoardStore store, RichBoardSession real)
    {
        _store = store;
        _real = real;
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
            real = await RichBoardSession.OpenAtPathAsync(store, fallback, cancellationToken).ConfigureAwait(false)
                ?? await CreateAtAsync(store, fallback, "My Board", cancellationToken).ConfigureAwait(false);
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
    public string Snapshot => JsonSerializer.Serialize(_view, Json);
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
        _ = MergeAsync();
        _ = _real.RequestAutosaveAsync();
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.SaveAsync(cancellationToken).ConfigureAwait(false);
            RefreshView();
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
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            await _real.SaveAsAsync(path, cancellationToken).ConfigureAwait(false);
            RefreshView();
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
        IReadOnlyList<(double X, double Y)> points, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return;
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
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
    public async ValueTask<int> EraseInkAtCurrentPageAsync(
        double x, double y, double radius = 12, CancellationToken cancellationToken = default)    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
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
    public async ValueTask AddSampleInkStrokeAsync(CancellationToken cancellationToken = default)
    {
        var random = new Random();
        var x = 40 + random.Next(0, 200);
        var y = 40 + random.Next(0, 80);
        await CommitInkStrokeAsync(
            [(x, y), (x + 60, y + 20), (x + 120, y - 10)], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects the nearest stroke within radius of a point (Select tool).</summary>
    public async ValueTask<bool> SelectInkAtCurrentPageAsync(
        double x, double y, double radius = 14, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
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

    private HavenRichPage CurrentContractPage()
    {
        var rich = _real.Rich;
        var viewSection = _view.Sections.FirstOrDefault();
        var viewPage = viewSection?.Pages.FirstOrDefault();
        if (viewSection is not null && viewPage is not null)
        {
            var section = rich.Sections.FirstOrDefault(s => s.Id == viewSection.Id);
            var page = section?.Pages.FirstOrDefault(p => p.Id == viewPage.Id);
            if (page is not null)
                return page;
        }
        return rich.Sections.SelectMany(s => s.Pages).First();
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
        if (!MergeDeltas.HasChanges(_view, baseline))
            return;
        await _real.MutateAsync(rich => MergeDeltas.Apply(rich, _view, baseline), cancellationToken)
            .ConfigureAwait(false);
    }

    private void RefreshView()
    {
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

    public async ValueTask ClearInkAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.ClearInk(rich, page.Id), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> RemoveLastInkStrokeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
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
        IReadOnlyList<(double X, double Y, double Pressure)> points,
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
            var page = CurrentContractPage();
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

    public async ValueTask SetInkViewAsync(double panX, double panY, double zoom, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
            await _real.MutateAsync(rich =>
                HavenRichNotesOps.SetInkView(rich, page.Id, panX, panY, zoom), cancellationToken).ConfigureAwait(false);
            RefreshView();
        }
        finally { _mergeGate.Release(); }
    }

    public Task<IReadOnlyList<CanvasBoxView>> GetCanvasObjectsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var page = CurrentContractPage();
        IReadOnlyList<CanvasBoxView> boxes = page.Canvas.Select(o => new CanvasBoxView
        {
            Id = o.Id, Kind = o.Kind, Text = o.Text,
            X = o.X, Y = o.Y, Width = o.Width, Height = o.Height
        }).ToArray();
        return Task.FromResult(boxes);
    }

    public async ValueTask<string> AddCanvasObjectAsync(
        string kind, string? text, double x, double y,
        double width = 260, double height = 160, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
            string id = string.Empty;
            await _real.MutateAsync(rich =>
                id = HavenRichNotesOps.AddCanvasObject(rich, page.Id, kind, text, x, y, width, height).Id,
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return id;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> MoveCanvasObjectAsync(string objectId, double x, double y, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
            var moved = false;
            await _real.MutateAsync(rich =>
                moved = HavenRichNotesOps.MoveCanvasObject(rich, page.Id, objectId, x, y),
                cancellationToken).ConfigureAwait(false);
            RefreshView();
            return moved;
        }
        finally { _mergeGate.Release(); }
    }

    public async ValueTask<bool> RemoveCanvasObjectAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mergeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MergeCoreAsync(cancellationToken).ConfigureAwait(false);
            var page = CurrentContractPage();
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
}
