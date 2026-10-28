using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Data.RecordsUI;

namespace CakeOS.Apps.Boards.App;

/// <summary>Owns Data session/control lifetimes for reference blocks in one Boards presentation.</summary>
public sealed class BoardsDatabaseBlocks : IAsyncDisposable
{
    private sealed record ViewEntry(Guid WorkbookID, DataRecordViewControl View);
    private readonly BoardsViewModel _model;
    private readonly IBoardsDatabaseSource? _source;
    private readonly Dictionary<Guid, Task<DataRecordSession>> _sessions = [];
    private readonly Dictionary<(string Page, string Block), ViewEntry> _views = [];
    private readonly List<Task> _loads = [];
    private readonly List<Exception> _openDiagnostics = [];
    public IReadOnlyList<Exception> OpenDiagnostics { get { lock (_gate) return _openDiagnostics.ToArray(); } }
    private readonly object _gate = new();
    private bool _disposed, _prepared;
    private Preparation? _preparation;
    private readonly Dictionary<Border, (string Page, string Block, HavenBoardDatabaseReference Reference)> _deferredMounts = [];

    public BoardsDatabaseBlocks(BoardsViewModel model, IBoardsDatabaseSource? source)
    { _model = model; _source = source; }
    public bool IsAvailable => _source is not null;
    public bool HasUnsavedDrafts => _views.Values.Any(entry => entry.View.HasUnsavedDrafts);

    public async Task<HavenBoardDatabaseReference> CreateAsync(DataRecordViewKind kind, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed || _prepared, this);
        var session = await (_source ?? throw new InvalidOperationException("Data is unavailable in this host."))
            .CreateDatabaseAsync("Untitled database", token).ConfigureAwait(false);
        lock (_gate) _sessions[session.Reference.WorkbookID] = Task.FromResult(session);
        var view = session.Snapshot.RecordViews.First(item => item.Kind == kind);
        return new(session.Reference.StoreID, session.Reference.WorkbookID, view.TableID, view.ViewID);
    }

    private Task<DataRecordSession> OpenAsync(HavenBoardDatabaseReference reference, CancellationToken token = default)
    {
        reference.Validate();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _prepared, this);
            if (_sessions.TryGetValue(reference.WorkbookID, out var session)) return session;
            var opening = (_source ?? throw new InvalidOperationException("Data is unavailable in this host."))
                .OpenDatabaseAsync(reference.WorkbookID, token);
            _sessions.Add(reference.WorkbookID, opening);
            return opening;
        }
    }

    private static void ValidateReference(DataRecordSession session, HavenBoardDatabaseReference reference)
    {
        if (session.Reference.StoreID != reference.StoreID ||
            !session.Snapshot.RecordViews.Any(view => view.ViewID == reference.ViewID && view.TableID == reference.TableID))
            throw new InvalidDataException("The Data store, table or view no longer matches this database block.");
    }

    public async Task<HavenBoardDatabaseReference> SelectKindAsync(HavenBoardDatabaseReference reference,
        DataRecordViewKind kind, CancellationToken token = default)
    {
        var session = await OpenAsync(reference, token).ConfigureAwait(false);
        ValidateReference(session, reference);
        var view = session.Snapshot.RecordViews.FirstOrDefault(item => item.TableID == reference.TableID && item.Kind == kind)
            ?? throw new InvalidOperationException("This database does not have that named view.");
        return reference with { ViewID = view.ViewID };
    }

    public Control Mount(RichBoardBlock block)
    {
        var container = new Border { MinHeight = 280, MaxHeight = 560, Height = Math.Clamp(block.Height, 280, 560) };
        var pageID = _model.SelectedPageId;
        var reference = block.Database;
        if (reference is null || _source is null)
        {
            container.Child = new TextBlock { Text = "Database access is unavailable in this host." };
            return container;
        }
        container.Child = new TextBlock { Text = "Loading database…" };
        container.AttachedToVisualTree += (_, _) =>
        {
            QueueMount(container, pageID, block.Id, reference);
        };
        container.DetachedFromVisualTree += (_, _) => _deferredMounts.Remove(container);
        return container;
    }

    private void QueueMount(Border container, string pageID, string blockID, HavenBoardDatabaseReference reference)
    {
        if (_disposed) return;
        if (_prepared)
        {
            _deferredMounts[container] = (pageID, blockID, reference);
            return;
        }
        _deferredMounts.Remove(container);
        var load = LoadAsync(container, pageID, blockID, reference);
        lock (_gate) { _loads.RemoveAll(task => task.IsCompleted); _loads.Add(load); }
    }

    private async Task LoadAsync(Border container, string pageID, string blockID, HavenBoardDatabaseReference reference)
    {
        try
        {
            var session = await OpenAsync(reference).ConfigureAwait(false);
            ValidateReference(session, reference);
            await Dispatcher.UIThread.InvokeAsync<Task>(async () =>
            {
                if (_disposed || _model.SelectedPageId != pageID || container.Parent is null ||
                    _model.FindBlock(blockID)?.Database != reference) return;
                if (_prepared)
                {
                    _deferredMounts[container] = (pageID, blockID, reference);
                    return;
                }
                var key = (pageID, blockID);
                if (_views.TryGetValue(key, out var previous) && previous.WorkbookID != reference.WorkbookID)
                {
                    if (previous.View.HasUnsavedDrafts)
                    {
                        if (previous.View.Parent is Decorator previousParent) previousParent.Child = null;
                        container.Child = previous.View;
                        _model.Set("StatusText", "Save or discard the previous database's field edits before replacing its view.");
                        return;
                    }
                    await previous.View.DisposeAsync();
                    _views.Remove(key);
                }
                if (!_views.TryGetValue(key, out var entry))
                {
                    var view = new DataRecordViewControl(session, reference.ViewID, _source?.DatabaseTheme);
                    entry = new(reference.WorkbookID, view);
                    _views.Add(key, entry);
                    view.SelectedViewChanged += (_, _) =>
                    {
                        var selected = session.Snapshot.RecordViews.Single(item => item.ViewID == view.SelectedViewID);
                        _model.UpdateDatabaseReference(pageID, blockID,
                            new(session.Reference.StoreID, session.Reference.WorkbookID, selected.TableID, selected.ViewID));
                    };
                }
                entry.View.IsReadOnly = !_model.IsEditMode;
                if (entry.View.SelectedViewID != reference.ViewID) entry.View.SelectView(reference.ViewID);
                if (entry.View.Parent is Decorator parent) parent.Child = null;
                container.Child = entry.View;
            }).GetTask().Unwrap().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && container.Parent is not null)
                {
                    var panel = new StackPanel();
                    panel.Children.Add(new TextBlock { Text = "Database could not be opened: " + error.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    var retry = new Button { Content = "Retry opening database" };
                    retry.Click += (_, _) =>
                    {
                        if (_disposed || _prepared) return;
                        retry.IsEnabled = false;
                        var task = RetryLoadAsync(container, pageID, blockID, reference);
                        lock (_gate) _loads.Add(task);
                    };
                    panel.Children.Add(retry); container.Child = panel;
                }
            });
        }
    }

    private async Task RetryLoadAsync(Border container, string pageID, string blockID, HavenBoardDatabaseReference reference)
    {
        lock (_gate)
        {
            if (_disposed || _prepared) return;
            if (_sessions.TryGetValue(reference.WorkbookID, out var failed))
            {
                // Only the terminal factory failure is retired. The factory owns and joins
                // its partially acquired session; a pending/successful owner is never replaced.
                if (!failed.IsCompleted || failed.IsCompletedSuccessfully) return;
                try { failed.GetAwaiter().GetResult(); }
                catch (Exception error) { _openDiagnostics.Add(failed.Exception ?? error); }
                if (_openDiagnostics.Count > 128) throw new InvalidOperationException("Reopen the Board after reviewing repeated Data open failures.");
                _sessions.Remove(reference.WorkbookID);
            }
        }
        await LoadAsync(container, pageID, blockID, reference).ConfigureAwait(false);
    }

    public Task WhenIdleAsync()
    {
        lock (_gate) return Task.WhenAll(_loads.ToArray());
    }

    public async Task<IBoardsPresentationClosePreparation?> PrepareCloseAsync(CancellationToken token = default)
    {
        if (_disposed) return CompletedBoardsPresentationClosePreparation.Instance;
        Task[] loads;
        lock (_gate) loads = _loads.ToArray();
        await Task.WhenAll(loads).WaitAsync(token).ConfigureAwait(false);
        return await Dispatcher.UIThread.InvokeAsync<Task<IBoardsPresentationClosePreparation?>>(async () =>
        {
            if (_preparation is { IsCurrent: true } current) return current;
            foreach (var entry in _views.Values) await entry.View.WhenIdleAsync();
            if (_views.Values.Any(entry => entry.View.HasUnsavedDrafts))
            {
                _model.Set("StatusText", "Save or discard the edited database fields before closing this board.");
                return null;
            }
            _prepared = true;
            foreach (var entry in _views.Values) entry.View.IsEnabled = false;
            return _preparation = new Preparation(this);
        }).GetTask().Unwrap().ConfigureAwait(false);
    }

    private sealed class Preparation(BoardsDatabaseBlocks owner) : IBoardsPresentationClosePreparation
    {
        private bool _finished;
        public bool IsCurrent => !_finished && ReferenceEquals(owner._preparation, this) && owner._prepared && !owner._disposed;
        public async Task CommitAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); if (_finished) return; await owner.DisposeAsync(); _finished = true; }
        public async Task AbortAsync()
        {
            if (_finished) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(owner._preparation, this)) return;
                owner._prepared = false;
                owner._preparation = null;
                foreach (var entry in owner._views.Values) { entry.View.IsEnabled = true; entry.View.IsReadOnly = !owner._model.IsEditMode; }
                // A save/close render can attach a replacement surface while preparation
                // fences Data. Resume those exact visible mounts when close is abandoned.
                var mounts = owner._deferredMounts.ToArray();
                owner._deferredMounts.Clear();
                foreach (var (container, request) in mounts)
                    if (container.Parent is not null && owner._model.SelectedPageId == request.Page &&
                        owner._model.FindBlock(request.Block)?.Database == request.Reference)
                        owner.QueueMount(container, request.Page, request.Block, request.Reference);
            });
            _finished = true;
        }
        public async ValueTask DisposeAsync() { if (!_finished) await AbortAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        Task[] loads;
        lock (_gate) loads = _loads.ToArray();
        await Task.WhenAll(loads).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync<Task>(async () =>
        {
            foreach (var entry in _views.Values) await entry.View.WhenIdleAsync();
            if (_views.Values.Any(entry => entry.View.HasUnsavedDrafts))
                throw new InvalidOperationException("Save or discard database field drafts before disposing this board.");
            _disposed = true;
            _deferredMounts.Clear();
            foreach (var entry in _views.Values) await entry.View.DisposeAsync();
            _views.Clear();
        }).GetTask().Unwrap().ConfigureAwait(false);
        foreach (var task in _sessions.Values)
            if (task.IsCompletedSuccessfully) await task.Result.DisposeAsync().ConfigureAwait(false);
        _sessions.Clear();
    }
}
