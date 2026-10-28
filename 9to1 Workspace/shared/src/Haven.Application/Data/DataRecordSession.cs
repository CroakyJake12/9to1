using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed record DataArtifactReference(Guid StoreID, Guid WorkbookID, int Version, Guid RevisionID);

/// <summary>One Data-owned editing session used by Data and embedded consumers. All returned
/// workbooks are detached. View changes and record changes share the same canonical artifact.</summary>
public sealed class DataRecordSession : IAsyncDisposable
{
    private readonly IDataGuardedWorkbookRepository _repository;
    private readonly IDataWorkbookCommitAuthority _authority;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationToken _ownerLifetime;
    private readonly Stack<DataWorkbook> _undo = new(), _redo = new();
    private DataWorkbook? _current;
    private AuthenticatedResourceActor? _actor;
    private Guid _store;
    private bool _disposed;
    public event EventHandler? Changed;
    public DataWorkbook Snapshot
    {
        get { _ownerLifetime.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
            return Clone(_current ?? throw new InvalidOperationException("Open a Data artifact first.")); }
    }
    public DataArtifactReference Reference => _current is { } current ? new(_store, current.Id, current.Version, current.RevisionId)
        : throw new InvalidOperationException("Open a Data artifact first.");
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;

    public DataRecordSession(IDataGuardedWorkbookRepository repository, IDataWorkbookCommitAuthority authority,
        IAuthenticatedResourceActorSource actors, CancellationToken ownerLifetime = default)
    { _repository = repository; _authority = authority; _actors = actors; _ownerLifetime = ownerLifetime; }

    public async Task OpenAsync(DataArtifactReference reference, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _ownerLifetime);
        token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var actor = await _actors.GetCurrentAsync(token).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Data profile unavailable.");
            var admission = await _authority.CaptureAsync(reference.StoreID, reference.WorkbookID, reference.Version,
                reference.RevisionID, "data.workbooks.read", actor, token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("This Data artifact is not authorized for the current profile.");
            var context = new DataWorkbookCommitContext(reference.StoreID, reference.WorkbookID, reference.Version, reference.RevisionID, DataWorkbookCommitPhase.Admission);
            if (!await admission.CheckAsync(context, token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Data read admission changed.");
            var loaded = await _repository.LoadAsync(reference.WorkbookID, token).ConfigureAwait(false) ?? throw new FileNotFoundException("Data artifact no longer exists.");
            if (loaded.Version != reference.Version || loaded.RevisionId != reference.RevisionID) throw new InvalidOperationException("DataRevisionConflict");
            if (!await admission.CheckAsync(context with { Phase = DataWorkbookCommitPhase.Publication }, token).ConfigureAwait(false) ||
                await _actors.GetCurrentAsync(token).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("Data read authority retired.");
            _current = Clone(loaded); _actor = actor; _store = reference.StoreID; _undo.Clear(); _redo.Clear(); NotifyChanged();
        }
        finally { _gate.Release(); }
    }

    public async Task CreateAsync(DataWorkbook workbook, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _ownerLifetime);
        token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not null || workbook.Version != 0 || workbook.RevisionId != Guid.Empty)
                throw new InvalidOperationException("Creation requires a new Data artifact and an empty session.");
            _actor = await _actors.GetCurrentAsync(token).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Data profile unavailable.");
            _store = (await _repository.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId;
            var copy = Clone(workbook); copy.Normalize();
            var admission = await _authority.CaptureAsync(_store, copy.Id, 0, Guid.Empty, "data.workbooks.create", _actor, token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Local Data creation is not authorized.");
            await _repository.SaveAsync(copy, "Create record database", admission, token).ConfigureAwait(false);
            _current = copy; NotifyChanged();
        }
        finally { _gate.Release(); }
    }

    public DataRecordViewSnapshot Project(Guid viewID, int offset = 0, int limit = 200) =>
        DataRecordViews.Project(Snapshot, viewID, offset, limit);

    public Task UpdateRecordAsync(Guid tableID, Guid recordID, IReadOnlyDictionary<Guid, DataScalarRecordValue> values,
        CancellationToken token = default)
    {
        var captured = values.ToDictionary(pair => pair.Key, pair => DataRecordEdits.Capture(pair.Value));
        return MutateAsync("data.records.update", copy => DataRecordEdits.UpdateRecord(copy, tableID, recordID, captured), token);
    }
    public Task CreateRecordAsync(Guid tableID, IReadOnlyDictionary<Guid, DataScalarRecordValue> values,
        CancellationToken token = default)
    {
        var captured = values.ToDictionary(pair => pair.Key, pair => DataRecordEdits.Capture(pair.Value)); var record = Guid.NewGuid();
        return MutateAsync("data.records.create", copy =>
        {
            var result = DataRecordCreation.Prepare(copy, tableID, record, copy.Version, copy.RevisionId, captured);
            if (result.Workbook is null) throw new InvalidDataException("Record creation refused: " + string.Join(",", result.Issues.Select(i => i.Code)));
            copy.Sheets = result.Workbook.Sheets; copy.Tables = result.Workbook.Tables;
        }, token);
    }
    public Task SetViewsAsync(IReadOnlyList<DataRecordViewDefinition> views, CancellationToken token = default)
    {
        var captured = JsonSerializer.Deserialize<List<DataRecordViewDefinition>>(JsonSerializer.Serialize(views))!;
        return MutateAsync("data.views.update", copy => { copy.RecordViews = captured; DataRecordViews.Validate(copy); }, token);
    }
    public Task MoveCardAsync(Guid viewID, Guid recordID, string group, CancellationToken token = default)
    {
        var view = Snapshot.RecordViews.Single(v => v.ViewID == viewID);
        if (view.Kind != DataRecordViewKind.Kanban || view.GroupFieldID is not { } field) throw new InvalidOperationException("Choose a Board grouping field.");
        return UpdateRecordAsync(view.TableID, recordID, new Dictionary<Guid, DataScalarRecordValue>
            { [field] = new(DataCellKind.Text, JsonSerializer.SerializeToElement(group)) }, token);
    }
    public Task SetTimelineRangeAsync(Guid viewID, Guid recordID, DateTimeOffset start, DateTimeOffset? end,
        CancellationToken token = default)
    {
        var snapshot = Snapshot; var view = snapshot.RecordViews.Single(v => v.ViewID == viewID);
        if (view.Kind != DataRecordViewKind.Timeline || view.StartFieldID is not { } first || end < start)
            throw new InvalidOperationException("Invalid timeline range.");
        var schema = snapshot.Tables.Single(t => t.Id == view.TableID).RelationalSchema!;
        string Format(Guid field, DateTimeOffset value) => schema.Fields.Single(f => f.FieldID == field).Type == DataFieldType.Date
            ? value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : value.ToString("O");
        var values = new Dictionary<Guid, DataScalarRecordValue> { [first] = new(DataCellKind.Date, JsonSerializer.SerializeToElement(Format(first, start))) };
        if (view.EndFieldID is { } last) values[last] = end is { } ending
            ? new(DataCellKind.Date, JsonSerializer.SerializeToElement(Format(last, ending)))
            : new(DataCellKind.Text, JsonSerializer.SerializeToElement(""));
        return UpdateRecordAsync(view.TableID, recordID, values, token);
    }

    private async Task MutateAsync(string action, Action<DataWorkbook> mutate, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _ownerLifetime);
        token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var before = Snapshot; var copy = Clone(before); mutate(copy);
            _ = new DataFormulaEngine().Recalculate(copy); copy.Normalize();
            await CommitAsync(copy, action, token).ConfigureAwait(false);
            _undo.Push(before); while (_undo.Count > 50) { var kept = _undo.Take(50).Reverse().ToArray(); _undo.Clear(); foreach (var item in kept) _undo.Push(item); }
            _redo.Clear();
        }
        finally { _gate.Release(); }
    }
    private async Task CommitAsync(DataWorkbook copy, string action, CancellationToken token)
    {
        var actor = _actor ?? throw new InvalidOperationException("Open Data first.");
        if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("The Data profile changed.");
        var current = _current ?? throw new InvalidOperationException("Open Data first.");
        copy.Version = current.Version; copy.RevisionId = current.RevisionId;
        var admission = await _authority.CaptureAsync(_store, current.Id, current.Version, current.RevisionId, action, actor, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The current Data operation is not authorized.");
        await _repository.SaveAsync(copy, action, admission, token).ConfigureAwait(false);
        _current = copy; NotifyChanged();
    }
    public Task UndoAsync(CancellationToken token = default) => ChangeHistoryAsync(_undo, _redo, token);
    public Task RedoAsync(CancellationToken token = default) => ChangeHistoryAsync(_redo, _undo, token);
    private async Task ChangeHistoryAsync(Stack<DataWorkbook> source, Stack<DataWorkbook> destination, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _ownerLifetime);
        token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (source.Count == 0) return;
            var before = Snapshot; var copy = Clone(source.Peek());
            await CommitAsync(copy, "data.records.undo", token).ConfigureAwait(false);
            source.Pop(); destination.Push(before);
        }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; _current = null; _undo.Clear(); _redo.Clear(); }
        finally { _gate.Release(); }
    }
    private void NotifyChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Committed Data notification failed: {0}", error); }
    }
    private static DataWorkbook Clone(DataWorkbook workbook) =>
        JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook)) ?? throw new InvalidDataException("Missing Data snapshot.");

    public static DataWorkbook NewRecordDatabase(string title)
    {
        var workbook = DataWorkbook.Create(title); workbook.Metadata["data.recordSession.v1"] = "true";
        var sheet = workbook.Sheets[0];
        var names = new[] { "Name", "Status", "Start", "End" };
        for (var column = 0; column < names.Length; column++) sheet.SetCell(0, column, names[column]);
        var table = new DataTableDefinition { Name = "Records", SheetId = sheet.Id,
            Range = new DataCellRange { StartRow = 0, EndRow = 0, StartColumn = 0, EndColumn = 3 }, HasHeaders = true };
        workbook.Tables.Add(table); DataTableIdentity.Initialize(workbook, table);
        var fields = table.Fields.OrderBy(f => f.SheetColumn).ToArray();
        table.RelationalSchema = new(1, 1, [new(fields[0].FieldID, "Name", DataFieldType.Text),
            new(fields[1].FieldID, "Status", DataFieldType.Category, Categories: ["To do", "Doing", "Done"]),
            new(fields[2].FieldID, "Start", DataFieldType.Date), new(fields[3].FieldID, "End", DataFieldType.Date)], []);
        var ids = fields.Select(f => f.FieldID).ToArray();
        workbook.RecordViews = [new(Guid.NewGuid(), table.Id, "Table", DataRecordViewKind.Table, ids, [], []),
            new(Guid.NewGuid(), table.Id, "Board", DataRecordViewKind.Kanban, ids, [], [], fields[1].FieldID),
            new(Guid.NewGuid(), table.Id, "Timeline", DataRecordViewKind.Timeline, ids, [], [], StartFieldID: fields[2].FieldID, EndFieldID: fields[3].FieldID)];
        workbook.Normalize(); return workbook;
    }
}
