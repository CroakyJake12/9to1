#if !ANDROID
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Automations;

/// <summary>Canonical CUI caller for the SAME private saved-graph editor. The
/// structured view changes its local draft only; publication remains a separate
/// protected workflow and no local result is represented as a saved revision.</summary>
internal sealed partial class OriginalAutomationLibraryBindings
{
    private ICanonicalAutomationOriginalGraphPublicationRecoverySource? _graphRecoverySource =>
        _writer as ICanonicalAutomationOriginalGraphPublicationRecoverySource;
    private ICanonicalAutomationOriginalGraphPublicationRecovery? _graphPublicationRecovery;
    private RowTarget? _graphPublicationRecoveryRow;
    private string _graphPublicationRecoveryStatus = "";
    private bool CanRecoverGraphPublication => !_retiring && !_busy && HasCurrentGraphRecovery &&
        _graphRecoverySource?.IsIssuedOriginalGraphPublicationRecovery(_graphPublicationRecovery!) == true;
    private bool HasCurrentGraphRecovery => _graphPublicationRecovery is { } recovery &&
        _graphPublicationRecoveryRow is { } row && ReferenceEquals(_selectedTarget, row) && row.Epoch == _epoch &&
        _issued.Contains(row) && recovery.AutomationId == row.Row.Value.Id;
    private void ClearGraphRecovery()
    { _graphPublicationRecovery = null; _graphPublicationRecoveryRow = null; _graphPublicationRecoveryStatus = ""; }
    private const int DraftGraphPageSize = 24;
    private ICanonicalAutomationOriginalGraphEditSource? _graphEdits => _writer as ICanonicalAutomationOriginalGraphEditSource;
    private ICanonicalAutomationOriginalGraphEditSession? _graphEditSession;
    private RowTarget? _graphEditRow;
    private GraphDocument? _graphEditDocument;
    private Guid? _graphEditNode, _graphEditOutput, _graphEditInput;
    private string _graphEditStatus = "", _graphEditSearch = "", _graphEditValue = "", _graphEditOriginalValue = "";
    private int _graphEditPage;
    private readonly HashSet<object> _graphEditTargets = new(ReferenceEqualityComparer.Instance);
    private sealed record DraftNodeTarget(ICanonicalAutomationOriginalGraphEditSession Session, long Revision, Guid NodeId);
    private sealed record DraftTypeTarget(ICanonicalAutomationOriginalGraphEditSession Session, long Revision, string TypeId);
    private sealed record DraftPortTarget(ICanonicalAutomationOriginalGraphEditSession Session, long Revision, Guid PortId, GraphPortDirection Direction);
    private sealed record DraftLinkTarget(ICanonicalAutomationOriginalGraphEditSession Session, long Revision, Guid ConnectionId);
    private sealed record DraftGraphRow(string Key, string Title, string Detail, object Target);
    private IReadOnlyList<DraftGraphRow> _graphEditNodes = [], _graphEditTypes = [], _graphEditPorts = [], _graphEditLinks = [];
    private bool GraphValueChanged => _graphEditValue != _graphEditOriginalValue;
    private bool HasGraphUnsubmittedChanges => _graphEditSession is { } session &&
        (session.HasUnsubmittedChanges || GraphValueChanged);
    private bool HasCurrentGraphEditor => _graphEditSession is { } session && _graphEditRow is { } row &&
        ReferenceEquals(_selectedTarget, row) && row.Epoch == _epoch && _issued.Contains(row) &&
        _graphEdits?.IsIssuedOriginalGraphEditSession(session) == true;
    private bool CanMutateGraphDraft => !_retiring && !_busy && HasCurrentGraphEditor &&
        _graphEditSession!.CanEdit && !_editSubmitted && !EditValuesChanged;
    private object? GraphEditingValue(string path) => path switch
    {
        "CanOpenGraphEditor" => GraphEditingActionAvailable("open-graph-editor"),
        "CanSaveGraphDraft" => GraphEditingActionAvailable("save-graph-draft"),
        "CanActivateGraphDraft" => GraphEditingActionAvailable("activate-graph-draft"),
        "GraphPublicationStatus" => _graphPublicationStatus,
        "HasGraphPublicationRecovery" => HasCurrentGraphRecovery,
        "CanRecoverGraphPublication" => CanRecoverGraphPublication,
        "GraphPublicationRecoveryStatus" => _graphPublicationRecoveryStatus,
        "HasGraphEditor" => _graphEditSession is not null,
        "GraphEditorStatus" => _graphEditStatus,
        "GraphEditorIdentity" => _graphEditDocument is { } document
            ? $"Graph {document.GraphId:D} · saved Draft {_graphEditSession!.SavedDraft.Revision} · local edit {document.Revision}" : "",
        "GraphEditorNodes" => _graphEditNodes, "GraphEditorTypes" => _graphEditTypes,
        "GraphEditorPorts" => _graphEditPorts, "GraphEditorConnections" => _graphEditLinks,
        "GraphEditorSearch" => _graphEditSearch, "GraphEditorValue" => _graphEditValue,
        "CanEditGraphValue" => CanMutateGraphDraft && SelectedDraftNode()?.TypeId == CanonicalAutomationCoreNodes.EmitType,
        "HasGraphValue" => SelectedDraftNode()?.TypeId == CanonicalAutomationCoreNodes.EmitType,
        "CanApplyGraphValue" => GraphEditingActionAvailable("apply-graph-value"),
        "CanRemoveGraphNode" => GraphEditingActionAvailable("remove-graph-node"),
        "CanConnectGraphPorts" => GraphEditingActionAvailable("connect-graph-ports"),
        "CanUndoGraphEdit" => GraphEditingActionAvailable("undo-graph-edit"),
        "CanRedoGraphEdit" => GraphEditingActionAvailable("redo-graph-edit"),
        "CanDiscardGraphEdits" => GraphEditingActionAvailable("discard-graph-edits"),
        "CanSearchGraphTypes" => CanMutateGraphDraft && !GraphValueChanged,
        "CanPreviousGraphDraftPage" => GraphEditingActionAvailable("draft-graph-back"),
        "CanNextGraphDraftPage" => GraphEditingActionAvailable("draft-graph-next"),
        "GraphEditorPageLabel" => _graphEditDocument is { } graph
            ? $"Nodes {_graphEditPage * DraftGraphPageSize + (_graphEditNodes.Count == 0 ? 0 : 1)}–{_graphEditPage * DraftGraphPageSize + _graphEditNodes.Count} of {graph.Nodes.Count}" : "",
        "GraphEditorPortsLabel" => $"Output: {PortDescription(_graphEditOutput)} · Input: {PortDescription(_graphEditInput)}",
        _ => null
    };
    private GraphNode? SelectedDraftNode() => _graphEditDocument?.Nodes.SingleOrDefault(node => node.NodeId == _graphEditNode);
    private string PortDescription(Guid? id)
    {
        if (id is null) return "not selected";
        var node = _graphEditDocument?.Nodes.SingleOrDefault(value => value.Ports.Any(port => port.PortId == id));
        var port = node?.Ports.Single(value => value.PortId == id);
        return node is null || port is null ? "not selected" : $"{node.TypeId} / {port.Key} ({port.DataType})";
    }
    private bool GraphEditingActionAvailable(string action) => action switch
    {
        "open-graph-editor" => !_retiring && !_busy && !HasUnsubmittedEdits && IsCurrentSelection &&
            _selectedTarget!.Row is { RequiresRecovery: false, Value: { IsEnabled: false, ArchivedAt: null, GraphBinding: not null } } &&
            _graphEdits?.SupportsOriginalSavedGraphEditing == true,
        "save-graph-draft" => CanMutateGraphDraft && !GraphValueChanged && _graphEditSession!.HasUnsubmittedChanges && _graphPublisher is not null,
        "activate-graph-draft" => CanMutateGraphDraft && !GraphValueChanged && !_graphEditSession!.HasUnsubmittedChanges &&
            _graphPublisher is not null && _graphEditDocument is { } saved &&
            _graphEditRow!.Row.Value.GraphBinding!.ActiveRevision != saved.Revision &&
            saved.Nodes.Count(node => node.TypeId == CanonicalAutomationCoreNodes.ManualType) == 1 &&
            saved.Nodes.Any(node => node.TypeId == CanonicalAutomationCoreNodes.EmitType),
        "discard-graph-edits" => !_retiring && !_busy && _graphEditSession is not null,
        "add-graph-node" or "select-edit-node" or "select-graph-port" or "disconnect-graph-link" => CanMutateGraphDraft && !GraphValueChanged,
        "remove-graph-node" => CanMutateGraphDraft && !GraphValueChanged && SelectedDraftNode() is not null,
        "apply-graph-value" => CanMutateGraphDraft && GraphValueChanged && SelectedDraftNode()?.TypeId == CanonicalAutomationCoreNodes.EmitType,
        "connect-graph-ports" => CanMutateGraphDraft && !GraphValueChanged && _graphEditInput is not null && _graphEditOutput is not null,
        "undo-graph-edit" or "redo-graph-edit" => CanMutateGraphDraft && !GraphValueChanged,
        "draft-graph-back" => CanMutateGraphDraft && !GraphValueChanged && _graphEditPage > 0,
        "draft-graph-next" => CanMutateGraphDraft && !GraphValueChanged && _graphEditDocument is { } document &&
            (_graphEditPage + 1) * DraftGraphPageSize < document.Nodes.Count,
        _ => false
    };
    private bool SetGraphEditingValue(string path, string text)
    {
        if (path is not ("GraphEditorSearch" or "GraphEditorValue")) return false;
        if (!CanMutateGraphDraft || text.Length > (path == "GraphEditorValue" ? 4000 : 256)) return false;
        if (path == "GraphEditorSearch" && GraphValueChanged ||
            path == "GraphEditorValue" && SelectedDraftNode()?.TypeId != CanonicalAutomationCoreNodes.EmitType) return false;
        _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            if (path == "GraphEditorSearch") { _graphEditSearch = text; PopulateGraphEditorRows(); }
            else _graphEditValue = text;
            Changed(); return true;
        })); return true;
    }
    private bool TryGetGraphEditingItemValue(object item, string path, out object? value)
    {
        value = item is DraftGraphRow row && _graphEditNodes.Concat(_graphEditTypes).Concat(_graphEditPorts).Concat(_graphEditLinks)
            .Any(current => ReferenceEquals(current, row)) ? path switch
        {
            "Key" => row.Key, "Title" => row.Title, "Detail" => row.Detail, "Target" => row.Target,
            "CanUse" => CanMutateGraphDraft && !GraphValueChanged && _graphEditTargets.Contains(row.Target), _ => null
        } : null;
        return value is not null;
    }
    private T DemandGraphEditTarget<T>(object? value) where T : class
    {
        if (value is not T target || !_graphEditTargets.Contains(target) || !HasCurrentGraphEditor)
            throw new InvalidOperationException("Use a current node, type, port or connection from this draft.");
        var (session, revision) = target switch
        {
            DraftNodeTarget node => (node.Session, node.Revision), DraftTypeTarget type => (type.Session, type.Revision),
            DraftPortTarget port => (port.Session, port.Revision), DraftLinkTarget link => (link.Session, link.Revision),
            _ => throw new InvalidOperationException("This is not an editor-issued target.")
        };
        if (!ReferenceEquals(session, _graphEditSession) || revision != session.LocalRevision)
            throw new InvalidOperationException("This draft target is stale. Its replacement must come from the current editor.");
        return target;
    }
    private void ClearGraphEditor()
    {
        _graphEditSession = null; _graphEditRow = null; _graphEditDocument = null;
        _graphEditNode = _graphEditOutput = _graphEditInput = null; _graphEditPage = 0;
        _graphEditStatus = _graphEditSearch = _graphEditValue = _graphEditOriginalValue = "";
        _graphEditNodes = []; _graphEditTypes = []; _graphEditPorts = []; _graphEditLinks = []; _graphEditTargets.Clear();
    }
    private void PopulateGraphEditorRows()
    {
        _graphEditTargets.Clear(); _graphEditNodes = []; _graphEditTypes = []; _graphEditPorts = []; _graphEditLinks = [];
        if (_graphEditSession is not { } session || !HasCurrentGraphEditor) return;
        var graph = session.CurrentDraft; _graphEditDocument = graph;
        if (graph.Nodes.Count > 512 || graph.Connections.Count > 2048)
            throw new InvalidDataException("This structured editor supports up to 512 nodes and 2048 connections; the saved graph was preserved.");
        _graphEditPage = Math.Clamp(_graphEditPage, 0, Math.Max(0, (graph.Nodes.Count - 1) / DraftGraphPageSize));
        DraftGraphRow Row(string key, string title, string detail, object target)
        { _graphEditTargets.Add(target); return new(key, title, detail, target); }
        _graphEditTypes = Array.AsReadOnly(session.RegisteredNodeTypes.Where(type =>
            type.TypeId.Contains(_graphEditSearch, StringComparison.OrdinalIgnoreCase) || type.CapabilityId.Contains(_graphEditSearch, StringComparison.OrdinalIgnoreCase))
            .Take(64).Select(type => Row("draft-type-" + type.TypeId + "-" + type.Version, type.TypeId,
                $"{type.AppId} · {type.CapabilityId} · version {type.Version}", new DraftTypeTarget(session, graph.Revision, type.TypeId))).ToArray());
        _graphEditNodes = Array.AsReadOnly(graph.Nodes.Skip(_graphEditPage * DraftGraphPageSize).Take(DraftGraphPageSize)
            .Select(node => Row("draft-node-" + node.NodeId.ToString("N"), node.TypeId,
                $"Node {node.NodeId:D} · {node.Ports.Count} typed ports" + (node.NodeId == _graphEditNode ? " · selected" : ""),
                new DraftNodeTarget(session, graph.Revision, node.NodeId))).ToArray());
        if (_graphEditNode is { } selected && !graph.Nodes.Any(node => node.NodeId == selected)) _graphEditNode = null;
        var selectedNode = SelectedDraftNode();
        _graphEditOriginalValue = selectedNode?.TypeId == CanonicalAutomationCoreNodes.EmitType
            ? selectedNode.Configuration.GetProperty("value").GetString()! : "";
        _graphEditValue = _graphEditOriginalValue;
        if (selectedNode is not null)
        {
            _graphEditPorts = Array.AsReadOnly(selectedNode.Ports.Select(port => Row("draft-port-" + port.PortId.ToString("N"),
                $"{port.Direction}: {port.Key}", $"{port.DataType} · {(port.Multiple ? "multiple connections" : "single connection")} · {port.PortId:D}",
                new DraftPortTarget(session, graph.Revision, port.PortId, port.Direction))).ToArray());
            var ids = selectedNode.Ports.Select(port => port.PortId).ToHashSet();
            _graphEditLinks = Array.AsReadOnly(graph.Connections.Where(link => ids.Contains(link.OutputPortId) || ids.Contains(link.InputPortId))
                .Select(link => Row("draft-link-" + link.ConnectionId.ToString("N"), $"Connection {link.ConnectionId:D}",
                    $"{PortDescription(link.OutputPortId)} → {PortDescription(link.InputPortId)}",
                    new DraftLinkTarget(session, graph.Revision, link.ConnectionId))).ToArray());
        }
        if (_graphEditOutput is { } output && !graph.Nodes.Any(node => node.Ports.Any(port => port.PortId == output))) _graphEditOutput = null;
        if (_graphEditInput is { } input && !graph.Nodes.Any(node => node.Ports.Any(port => port.PortId == input))) _graphEditInput = null;
    }
    private Task StartGraphEditing(string action, object? parameter)
    {
        if (GraphEditingActionAvailable(action) != true)
            throw new InvalidOperationException("This draft action is unavailable. Apply or discard pending changes first.");
        if (action is "select-edit-node" or "select-graph-port" or "draft-graph-back" or "draft-graph-next" or "discard-graph-edits")
        {
            _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                if (action == "select-edit-node") _graphEditNode = DemandGraphEditTarget<DraftNodeTarget>(parameter).NodeId;
                else if (action == "select-graph-port")
                {
                    var target = DemandGraphEditTarget<DraftPortTarget>(parameter);
                    if (target.Direction == GraphPortDirection.Output) _graphEditOutput = target.PortId; else _graphEditInput = target.PortId;
                }
                else
                {
                    if (parameter is not null) throw new ArgumentException("This action does not accept a target.");
                    if (action == "discard-graph-edits") { ClearGraphEditor(); Changed(); return true; }
                    _graphEditPage += action == "draft-graph-next" ? 1 : -1;
                }
                PopulateGraphEditorRows(); Changed(); return true;
            })); return Task.CompletedTask;
        }
        CanonicalAutomationGraphEdit? edit = action switch
        {
            "add-graph-node" => new(CanonicalAutomationGraphEditKind.AddNode, NodeTypeId: DemandGraphEditTarget<DraftTypeTarget>(parameter).TypeId),
            "disconnect-graph-link" => new(CanonicalAutomationGraphEditKind.Disconnect, ConnectionId: DemandGraphEditTarget<DraftLinkTarget>(parameter).ConnectionId),
            "remove-graph-node" => new(CanonicalAutomationGraphEditKind.RemoveNode, NodeId: _graphEditNode),
            "apply-graph-value" => new(CanonicalAutomationGraphEditKind.SetValue, NodeId: _graphEditNode, Value: _graphEditValue),
            "connect-graph-ports" => new(CanonicalAutomationGraphEditKind.Connect, OutputPortId: _graphEditOutput, InputPortId: _graphEditInput),
            "undo-graph-edit" => new(CanonicalAutomationGraphEditKind.Undo), "redo-graph-edit" => new(CanonicalAutomationGraphEditKind.Redo),
            "open-graph-editor" => null, _ => throw new InvalidOperationException("Unknown draft action.")
        };
        if (action is not ("add-graph-node" or "disconnect-graph-link") && parameter is not null)
            throw new ArgumentException("This draft action does not accept a target.");
        var source = _graphEdits!; var row = DemandRow(_selectedTarget); var session = _graphEditSession;
        var expected = session?.LocalRevision; var epoch = _epoch; var command = new Command(this);
        lock (_gate)
        {
            _commands.RemoveAll(actual => actual.Driver?.IsCompletedSuccessfully == true && actual.IsIndependentlyJoined);
            if (_commands.Count >= 128) throw new InvalidOperationException("Retain the current draft sources for inspection before more work.");
            _commands.Add(command);
        }
        _busy = true;
        try
        {
            return _work.RunAsync(async original =>
            {
                command.Bind(original);
                try
                {
                    if (!_work.IsRetiring) Changed();
                    var actor = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain, CancellationToken.None).AsTask())
                        ?? throw new UnauthorizedAccessException("Your current Home profile is unavailable.");
                    command.Run(() => DemandActualRow(row, actor));
                    if (edit is null)
                    {
                        var opened = await command.Read(() => source.OpenOriginalGraphEditWithinSourceAsync(row.Observation, row.Row,
                            command.Run, command.Retain, CancellationToken.None));
                        command.Run(() =>
                        {
                            if (!source.IsIssuedOriginalGraphEditSession(opened) || opened.Actor != actor || opened.AutomationId != row.Row.Value.Id ||
                                opened.SavedDraft.GraphId != row.Row.Value.GraphBinding!.GraphId)
                                throw new UnauthorizedAccessException("The SAME protected source did not issue this exact draft session.");
                        });
                        await command.Join();
                        if (!_work.IsRetiring && epoch == _epoch)
                        {
                            ClearGraphEditor(); _graphEditRow = row; _graphEditSession = opened; PopulateGraphEditorRows();
                            _graphEditStatus = opened.CanEdit ? "Draft opened. Edit locally, then save these exact changes for Home review."
                                : "This saved graph needs a supported profile migration before editing. Its original graph is preserved.";
                        }
                    }
                    else
                    {
                        if (session is null || expected is null || !source.IsIssuedOriginalGraphEditSession(session) || session.Actor != actor)
                            throw new UnauthorizedAccessException("The SAME current draft and Home actor are required.");
                        var result = await command.Read(() => source.EditOriginalGraphWithinSourceAsync(session, expected.Value, edit,
                            command.Run, command.Retain, CancellationToken.None));
                        command.Run(() =>
                        {
                            if (!source.IsIssuedOriginalGraphEditSession(session) || result.Document.GraphId != session.SavedDraft.GraphId ||
                                JsonSerializer.Serialize(result.Document) != JsonSerializer.Serialize(session.CurrentDraft))
                                throw new UnauthorizedAccessException("The editing result is not the actual source's current draft.");
                        });
                        await command.Join();
                        if (!_work.IsRetiring && epoch == _epoch && ReferenceEquals(session, _graphEditSession))
                        {
                            if (result.Succeeded) PopulateGraphEditorRows();
                            _graphEditStatus = result.Succeeded ? "Local draft changed. The saved Draft, Active revision and runs are unchanged."
                                : "Draft change refused: " + result.Code + (result.Diagnostics.Count == 0 ? "" : " · " + string.Join(", ", result.Diagnostics.Select(value => value.Code)));
                        }
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause);
                    if (!_work.IsRetiring) _graphEditStatus = "This draft needs inspection. Local edits were retained and no saved graph was substituted.";
                    throw;
                }
                finally { try { await command.Join(); } finally { _busy = false; if (!_work.IsRetiring) Changed(); } }
            }, actual => { command.Driver = actual; OriginalCommand = actual; });
        }
        catch { if (command.Driver is null) { lock (_gate) _commands.Remove(command); _busy = false; } throw; }
    }
    private ICanonicalAutomationOriginalGraphPublicationSource? _graphPublisher => _writer as ICanonicalAutomationOriginalGraphPublicationSource;
    private string _graphPublicationStatus = "";
    private sealed class GraphPublicationPresentation
    {
        internal ICanonicalAutomationOriginalGraphPublicationObservation? Observation;
        internal Task? Close;
        internal bool IssuerVerified, CloseJoined;
    }
    private readonly List<GraphPublicationPresentation> _graphPublicationPresentations = [];
    private Task StartGraphPublication(string action, object? parameter)
    {
        if (parameter is not null || IsActionAvailable(action) != true || _graphPublisher is not { } publisher ||
            _graphEditSession is not { } session)
            throw new InvalidOperationException("Open the actual saved graph Draft and apply pending values before publication.");
        var row = DemandRow(_selectedTarget); var expected = session.LocalRevision; var epoch = _epoch;
        var kind = action == "save-graph-draft" ? GraphPublicationKind.SaveDraft : GraphPublicationKind.Activate;
        var command = new Command(this); var retained = new GraphPublicationPresentation();
        lock (_gate)
        {
            _commands.RemoveAll(value => value.Driver?.IsCompletedSuccessfully == true && value.IsIndependentlyJoined);
            _graphPublicationPresentations.RemoveAll(value => value.CloseJoined && value.Close?.IsCompletedSuccessfully == true);
            if (_commands.Count >= 128 || _graphPublicationPresentations.Count >= 128)
                throw new InvalidOperationException("Retain the current original graph publications for inspection before more work.");
            _commands.Add(command); _graphPublicationPresentations.Add(retained);
        }
        _busy = true; _graphPublicationStatus = "Waiting for your graph review, followed by a separate Automation-link review. Execution stays disabled.";
        try
        {
            return _work.RunAsync(async original =>
            {
                command.Bind(original);
                try
                {
                    if (!_work.IsRetiring) Changed();
                    var actor = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain, CancellationToken.None).AsTask())
                        ?? throw new UnauthorizedAccessException("Your current Home profile is unavailable.");
                    command.Run(() =>
                    {
                        DemandActualRow(row, actor);
                        if (!ReferenceEquals(_graphEditSession, session) || !publisher.IsIssuedOriginalGraphEditSession(session) ||
                            session.Actor != actor || session.AutomationId != row.Row.Value.Id || session.LocalRevision != expected)
                            throw new UnauthorizedAccessException("The SAME actual saved graph editing session/current actor is required.");
                    });
                    var observation = await command.Read(() => publisher.StartOriginalGraphPublicationWithinSourceAsync(session,
                        expected, kind, command.Run, command.Retain, CancellationToken.None), actual =>
                    {
                        retained.Observation = actual;
                        command.Run(() =>
                        {
                            if (!publisher.IsIssuedOriginalGraphPublicationObservation(actual) || actual.AutomationId != session.AutomationId ||
                                actual.GraphId != session.SavedDraft.GraphId || actual.PublicationKind != kind ||
                                actual.OperationId == Guid.Empty || actual.GraphOperationId == Guid.Empty || actual.OperationId == actual.GraphOperationId)
                                throw new UnauthorizedAccessException("The SAME original graph publisher did not issue this exact delivery.");
                            retained.IssuerVerified = true; if (_retiring) actual.RequestOriginalRetirement();
                        });
                    });
                    var completed = await command.Read(() => observation.WaitOriginalCompletionAsync(CancellationToken.None));
                    command.Run(() =>
                    {
                        if (!observation.IsIssuedOriginalCompletion(completed) || !ReferenceEquals(completed.OriginalObservation, observation) || !Enum.IsDefined(completed.Kind))
                            throw new UnauthorizedAccessException("The SAME graph publication delivery must issue its actual typed completion.");
                    });
                    await command.Read(() => retained.Close ??= observation.CloseAndDrainOriginalAsync()); retained.CloseJoined = true;
                    await command.Join();
                    if (!_work.IsRetiring && epoch == _epoch)
                    {
                        _graphPublicationStatus = completed.Message;
                        if (completed.Kind is CanonicalAutomationGraphPublicationCompletionKind.SavedDisabled or
                            CanonicalAutomationGraphPublicationCompletionKind.ActiveGraphDisabled or
                            CanonicalAutomationGraphPublicationCompletionKind.GraphPublishedBindingPending)
                        {
                            // The local editor and SQL/library selection are stale
                            // after a durable Home publication, whether its binding
                            // completed or needs a fresh recovery review. A fresh
                            // original READ is required before further edits.
                            _epoch++; _issued.Clear(); _rows = []; _next = null; _currentPage = null; _creationRows = [];
                            ClearSelection();
                            _status = completed.Kind == CanonicalAutomationGraphPublicationCompletionKind.GraphPublishedBindingPending
                                ? "The Home graph was published, but its Automation link still needs review. Refresh the library and reopen the Automation to recover the existing publication."
                                : "Graph publication completed. Refresh the library to open its current saved revision.";
                        }
                        else if (completed.Kind == CanonicalAutomationGraphPublicationCompletionKind.DeclinedBeforeEffect)
                            _graphEditStatus = completed.Message + " Your local draft is preserved; submit this action again to request a fresh Home review.";
                        else _graphEditStatus = completed.Message + " Local edits remain in this session; no replacement publication was started.";
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause);
                    if (!_work.IsRetiring) _graphPublicationStatus = "This graph publication needs inspection. Its exact operation and local draft were retained; no success or replacement operation was inferred.";
                    throw;
                }
                finally { try { await command.Join(); } finally { _busy = false; if (!_work.IsRetiring) Changed(); } }
            }, actual => { command.Driver = actual; OriginalCommand = actual; });
        }
        catch
        {
            if (command.Driver is null) { lock (_gate) { _commands.Remove(command); _graphPublicationPresentations.Remove(retained); } _busy = false; }
            throw;
        }
    }

    private Task StartGraphPublicationRecovery(string action, object? parameter)
    {
        if (parameter is not null || action != "recover-graph-publication" || !CanRecoverGraphPublication ||
            _graphRecoverySource is not { } source || _graphPublicationRecovery is not { } recovery ||
            _graphPublicationRecoveryRow is not { } row)
            throw new InvalidOperationException("Refresh the selected Automation and inspect its exact Home publication before recovery.");
        var epoch = _epoch; var command = new Command(this);
        lock (_gate)
        {
            _commands.RemoveAll(value => value.Driver?.IsCompletedSuccessfully == true && value.IsIndependentlyJoined);
            if (_commands.Count >= 128) throw new InvalidOperationException("Retain current Automation recovery operations before starting another.");
            _commands.Add(command);
        }
        _busy = true; _graphPublicationRecoveryStatus = "Waiting for a fresh Home review to bind the already-published graph. The graph will not be published again.";
        try
        {
            return _work.RunAsync(async original =>
            {
                command.Bind(original);
                try
                {
                    if (!_work.IsRetiring) Changed();
                    var actor = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain, CancellationToken.None).AsTask())
                        ?? throw new UnauthorizedAccessException("Your current Home profile is unavailable.");
                    command.Run(() =>
                    {
                        DemandActualRow(row, actor);
                        if (!source.IsIssuedOriginalGraphPublicationRecovery(recovery) ||
                            recovery.AutomationId != row.Row.Value.Id || recovery.GraphId == Guid.Empty ||
                            recovery.Kind == GraphPublicationKind.SaveDraft && recovery.PublishedGraphRevision <= recovery.ExpectedGraphRevision ||
                            recovery.Kind == GraphPublicationKind.Activate && recovery.PublishedGraphRevision != recovery.ExpectedGraphRevision)
                            throw new UnauthorizedAccessException("The exact current source-issued Home publication recovery is required.");
                    });
                    var acknowledgment = await command.Read(() => source.RecoverOriginalGraphPublicationWithinSourceAsync(
                        row.Observation, row.Row, recovery, command.Run, command.Retain, CancellationToken.None));
                    command.Run(() =>
                    {
                        var intent = acknowledgment.OriginalIntent;
                        var expectedActive = recovery.Kind == GraphPublicationKind.Activate
                            ? recovery.PublishedGraphRevision : recovery.PublishedActiveRevision;
                        if (_writer?.IsIssuedOriginalChangeIntent(intent) != true || intent.Actor != actor ||
                            intent.ChangeKind != (recovery.Kind == GraphPublicationKind.Activate
                                ? CanonicalAutomationOriginalChangeKind.BindGraphActiveDisabled
                                : CanonicalAutomationOriginalChangeKind.BindGraphDraftDisabled) ||
                            intent.ProposedDefinition.Id != recovery.AutomationId ||
                            intent.ProposedDefinition.GraphBinding != new AutomationDefinitionGraphBinding(
                                recovery.GraphId, recovery.PublishedGraphRevision, expectedActive) ||
                            acknowledgment.Definition.IsEnabled || acknowledgment.Definition.NextRunAt is not null)
                            throw new UnauthorizedAccessException("The source did not return the same Home-reviewed disabled graph binding.");
                    });
                    await command.Join();
                    if (!_work.IsRetiring && epoch == _epoch && ReferenceEquals(_selectedTarget, row))
                    {
                        ClearGraphRecovery();
                        _epoch++; _issued.Clear(); _rows = []; _next = null; _currentPage = null; _creationRows = [];
                        ClearSelection(); _graphPublicationRecoveryStatus = "The already-published Home graph is now linked to the same Automation. It remains disabled and unscheduled.";
                        _status = "Graph publication recovery completed. Refresh the library to open its current saved revision.";
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause);
                    if (!_work.IsRetiring) _graphPublicationRecoveryStatus = "Recovery needs inspection. The current Home receipt and saved definition were retained; the graph was not republished.";
                    throw;
                }
                finally { try { await command.Join(); } finally { _busy = false; if (!_work.IsRetiring) Changed(); } }
            }, actual => { command.Driver = actual; OriginalCommand = actual; });
        }
        catch
        {
            if (command.Driver is null) { lock (_gate) _commands.Remove(command); _busy = false; }
            throw;
        }
    }
    private async Task RetireGraphPublicationAndLibraryAsync()
    {
        var failures = new List<Exception>();
        try { await RetireLibraryDeliveriesAsync(); } catch (Exception cause) { AddChangeCause(failures, cause); }
        GraphPublicationPresentation[] all; lock (_gate) all = _graphPublicationPresentations.ToArray();
        foreach (var retained in all)
            if (retained.Observation is { } observation)
                try { _work.RunCloseCallback(() =>
                {
                    if (!retained.IssuerVerified || _graphPublisher?.IsIssuedOriginalGraphPublicationObservation(observation) != true)
                        throw new UnauthorizedAccessException("An unverified graph publication delivery remains retained for inspection.");
                    observation.RequestOriginalRetirement();
                }); } catch (Exception cause) { AddChangeCause(failures, cause); }
        ThrowCreationCauses(failures);
    }
    private async Task JoinGraphPublicationAndLibraryAsync()
    {
        var failures = new List<Exception>();
        try { await JoinLibraryDeliveriesAsync(); } catch (Exception cause) { AddChangeCause(failures, cause); }
        GraphPublicationPresentation[] all; lock (_gate) all = _graphPublicationPresentations.ToArray();
        foreach (var retained in all)
            if (retained.Observation is { } observation)
            {
                try { _work.RunCloseCallback(() =>
                {
                    if (!retained.IssuerVerified || _graphPublisher?.IsIssuedOriginalGraphPublicationObservation(observation) != true)
                        throw new UnauthorizedAccessException("An unverified graph publication delivery remains retained for inspection.");
                    observation.RequestOriginalRetirement(); retained.Close ??= observation.CloseAndDrainOriginalAsync();
                }); } catch (Exception cause) { AddChangeCause(failures, cause); }
                if (retained.Close is { } close)
                    try { await close; retained.CloseJoined = true; } catch (Exception cause) { AddChangeCause(failures, close.Exception ?? cause); }
            }
        ThrowCreationCauses(failures);
    }
}
#endif
