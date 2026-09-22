// In-memory IRichBoardSession used until the persistence worker's
// schema-v2 RichBoardSession lands. Backed by a static store keyed by
// path so edit -> save -> dispose -> reopen round-trips preserve content.

using System.Collections.Concurrent;
using System.Text.Json;

namespace CakeOS.Apps.Boards.App;

public sealed class InMemoryRichBoardSession : IRichBoardSession
{
    private static readonly ConcurrentDictionary<string, string> Store = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Timer _autosaveTimer;
    private readonly object _gate = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private bool _dirty;
    private bool _disposed;
    private string _status = "Ready";

    public InMemoryRichBoardSession()
    {
        _autosaveTimer = new Timer(_ => _ = AutosaveTickAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public RichBoardDocument Document { get; private set; } = CreateDefault();

    public string Snapshot => JsonSerializer.Serialize(Document, JsonOptions);

    public string Status
    {
        get { lock (_gate) return _status; }
        private set { lock (_gate) _status = value; }
    }

    public string? FilePath { get; private set; }

    public event EventHandler<string>? StatusChanged;

    public ValueTask OpenAsync(string? path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FilePath = string.IsNullOrWhiteSpace(path) ? "memory://boards/default" : path;
        if (Store.TryGetValue(FilePath, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                Document = JsonSerializer.Deserialize<RichBoardDocument>(json, JsonOptions) ?? CreateDefault();
            }
            catch (JsonException)
            {
                Document = CreateDefault();
            }
        }
        else
        {
            Document = CreateDefault();
        }
        lock (_gate)
        {
            _undo.Clear();
            _redo.Clear();
        }
        SetStatus("Ready");
        return ValueTask.CompletedTask;
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Store[FilePath ?? "memory://boards/default"] = Snapshot;
            lock (_gate) _dirty = false;
            SetStatus($"Saved {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    public async ValueTask SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FilePath = path;
        await SaveAsync(cancellationToken);
    }

    public void MarkDirty()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            _undo.Push(JsonSerializer.Serialize(Document, JsonOptions));
            while (_undo.Count > 60)
            {
                var kept = _undo.Take(60).Reverse().ToArray();
                _undo.Clear();
                foreach (var entry in kept) _undo.Push(entry);
            }
            _redo.Clear();
            _dirty = true;
        }
        SetStatus("Unsaved changes");
        _autosaveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
    }

    public IReadOnlyList<RichStyleView> Styles { get; } =
    [
        new() { Id = "normal", Name = "Paragraph", IsBuiltIn = true, BlockKind = "paragraph" },
        new() { Id = "title", Name = "Title", IsBuiltIn = true, BlockKind = "heading" },
        new() { Id = "subtitle", Name = "Subtitle", IsBuiltIn = true, BlockKind = "heading" },
        new() { Id = "heading-1", Name = "Header 1", IsBuiltIn = true, BlockKind = "heading" },
        new() { Id = "heading-2", Name = "Header 2", IsBuiltIn = true, BlockKind = "heading" },
        new() { Id = "heading-3", Name = "Header 3", IsBuiltIn = true, BlockKind = "heading" },
        new() { Id = "quote", Name = "Quote", IsBuiltIn = true, BlockKind = "paragraph" },
        new() { Id = "code", Name = "Code", IsBuiltIn = true, BlockKind = "paragraph" },
    ];

    public bool CanUndo { get { lock (_gate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_gate) return _redo.Count > 0; } }

    public ValueTask<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? previous;
        lock (_gate)
        {
            if (!_undo.TryPop(out previous)) return ValueTask.FromResult(false);
            _redo.Push(JsonSerializer.Serialize(Document, JsonOptions));
        }
        Document = JsonSerializer.Deserialize<RichBoardDocument>(previous, JsonOptions) ?? CreateDefault();
        lock (_gate) _dirty = true;
        SetStatus("Undone — editing…");
        _autosaveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? next;
        lock (_gate)
        {
            if (!_redo.TryPop(out next)) return ValueTask.FromResult(false);
            _undo.Push(JsonSerializer.Serialize(Document, JsonOptions));
        }
        Document = JsonSerializer.Deserialize<RichBoardDocument>(next, JsonOptions) ?? CreateDefault();
        lock (_gate) _dirty = true;
        SetStatus("Redone — editing…");
        _autosaveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        return ValueTask.FromResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        bool flush;
        string? snapshot;
        lock (_gate)
        {
            flush = _dirty && !_disposed;
            snapshot = flush ? JsonSerializer.Serialize(Document, JsonOptions) : null;
            _disposed = true;
        }
        _autosaveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        // Flush directly (not via SaveAsync: this instance is already disposed).
        if (flush && snapshot is not null)
        {
            Store[FilePath ?? "memory://boards/default"] = snapshot;
            lock (_gate) _dirty = false;
            SetStatus($"Saved {DateTime.Now:HH:mm:ss}");
        }
        _autosaveTimer.Dispose();
        await Task.CompletedTask;
    }

    private async Task AutosaveTickAsync()
    {
        bool dirty;
        lock (_gate) dirty = _dirty;
        if (!dirty)
            return;
        await SaveAsync();
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(InMemoryRichBoardSession));
        }
    }

    public static void ClearStore() => Store.Clear();

    internal static RichBoardDocument CreateDefault()
    {
        return new RichBoardDocument
        {
            Title = "Untitled board",
            Sections =
            [
                new RichBoardSection
                {
                    Id = "section-1",
                    Title = "Getting started",
                    Pages =
                    [
                        new RichBoardPage
                        {
                            Id = "page-1",
                            Title = "Welcome",
                            Blocks =
                            [
                                new RichBoardBlock { Id = "block-heading", Kind = "heading", Text = "Welcome to Boards" },
                                new RichBoardBlock { Id = "block-para", Kind = "paragraph", Text = "Edit this paragraph. Multiline editing is supported." },
                                new RichBoardBlock { Id = "block-check-1", Kind = "checklist", Text = "Try the checklist", IsChecked = false },
                                new RichBoardBlock { Id = "block-check-2", Kind = "checklist", Text = "Save the board", IsChecked = false },
                                new RichBoardBlock { Id = "block-table", Kind = "table", Text = "Quarterly plan", TableCells = new Dictionary<string, string>(StringComparer.Ordinal) { ["0,0"] = "Q1", ["0,1"] = "Q2", ["1,0"] = "Q3", ["1,1"] = "Q4" } },
                                new RichBoardBlock { Id = "block-ink", Kind = "ink", Text = "Sketch here" },
                            ],
                        },
                    ],
                },
            ],
        };
    }
}
