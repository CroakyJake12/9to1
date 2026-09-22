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
        else if (File.Exists(path))
        {
            real = await RichBoardSession.OpenAtPathAsync(store, path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            real = await CreateAtAsync(store, Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path), cancellationToken).ConfigureAwait(false);
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
                    BlockKind = s.BlockKind.ToString().ToLowerInvariant()
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
    public async ValueTask AddSampleInkStrokeAsync(CancellationToken cancellationToken = default)
    {
        var random = new Random();
        var x = 40 + random.Next(0, 200);
        var y = 40 + random.Next(0, 80);
        await CommitInkStrokeAsync(
            [(x, y), (x + 60, y + 20), (x + 120, y - 10)], cancellationToken).ConfigureAwait(false);
    }

    public Task<HavenRichAttachmentRef> ImportAttachmentAsync(
        string sourcePath, CancellationToken cancellationToken = default) =>
        _real.ImportAttachmentAsync(sourcePath, cancellationToken);

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

    private static bool IsMemoryPath(string path) =>
        path.StartsWith("memory://", StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
