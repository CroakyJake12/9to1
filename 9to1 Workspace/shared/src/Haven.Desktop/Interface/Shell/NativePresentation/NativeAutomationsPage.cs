using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Automations;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Compatibility host for the Automations route. Every visible control is owned by Haven.UI
/// through one HavenSceneControl; this class only bridges repositories and runtime services.
/// </summary>
internal sealed class NativeAutomationsPage : ContentControl, IDisposable
{
    private const string GraphHistorySettingsKey = "automations.graph-run-history.v1";
    private readonly IAutomationRepository _automations;
    private readonly Guid? _containerId;
    private readonly Func<Task> _startOneTimeTask;
    private readonly Func<string, Task> _runTask;
    private readonly DeviceActionRouter? _deviceActions;
    private readonly IAutomationGraphAiEditor? _graphAiEditor;
    private readonly IVersionedSettingsStore? _historySettings;
    private readonly IAutomationDefinitionReviewCaller? _ownerCaller;
    private readonly IAutomationDefinitionCallerSelection? _originalSelection;
    private IAutomationDefinitionCallerReview? _pendingEditorReview;
    private string? _pendingEditorFingerprint;
    private readonly Dictionary<Guid, (string Fingerprint, IAutomationDefinitionCallerReview Review)> _pendingLibraryChanges = [];
    private readonly AutomationLinkedDefinitionReviewCaller? _linkedOwnerCaller;
    private readonly Dictionary<Guid, (string Fingerprint, AutomationLinkedDefinitionReview Review)> _pendingLinkedChanges = [];
    private bool _definitionPageComplete;
    private readonly SemaphoreSlim _definitionChanges = new(1, 1);
    private readonly AutomationsHavenScene _scene;
    private readonly HavenSceneControl _sceneHost;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private AutomationGraphHistoryState _history = new(AutomationGraphHistoryJournal.CurrentVersion, []);
    private bool _historyLoaded;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _disposed;
    private IReadOnlyList<ReusableTaskDefinition> _workflows = [];
    private IReadOnlyList<AutomationDefinition> _scheduled = [];

    public NativeAutomationsPage(
        IWorkspaceStateRepository tasks,
        IAutomationRepository automations,
        Guid? containerId,
        Func<Task> startOneTimeTask,
        Func<string, Task> runTask,
        IVersionedSettingsStore? versionedSettings = null,
        IAutomationDefinitionReviewCaller? ownerCaller = null,
        IAutomationDefinitionCallerSelection? originalSelection = null,
        AutomationLinkedDefinitionReviewCaller? linkedOwnerCaller = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _automations = automations ?? throw new ArgumentNullException(nameof(automations));
        _containerId = containerId;
        _ownerCaller = ownerCaller;
        _originalSelection = originalSelection;
        _linkedOwnerCaller = linkedOwnerCaller;
        _startOneTimeTask = startOneTimeTask ?? throw new ArgumentNullException(nameof(startOneTimeTask));
        _runTask = runTask ?? throw new ArgumentNullException(nameof(runTask));
        _historySettings = versionedSettings ?? Haven.Desktop.App.Services?.GetService(typeof(IVersionedSettingsStore)) as IVersionedSettingsStore;
        _deviceActions = Haven.Desktop.App.Services?.GetService(typeof(DeviceActionRouter)) as DeviceActionRouter;
        _graphAiEditor = Haven.Desktop.App.Services?.GetService(typeof(IAutomationGraphAiEditor)) as IAutomationGraphAiEditor;

        _scene = new AutomationsHavenScene();
        _sceneHost = new HavenSceneControl { Root = _scene.Root };
        Content = _sceneHost;
        Background = Brushes.Transparent;

        _scene.RefreshRequested += OnRefreshRequested;
        _scene.OpenTasksRequested += OnOpenTasksRequested;
        _scene.NewWorkflowRequested += OnNewWorkflowRequested;
        _scene.RunWorkflowRequested += OnRunWorkflowRequested;
        _scene.EditWorkflowRequested += OnEditWorkflowRequested;
        _scene.TestWorkflowRequested += OnTestWorkflowRequested;
        _scene.DeleteWorkflowRequested += OnDeleteWorkflowRequested;
        _scene.SetWorkflowEnabledRequested += OnSetWorkflowEnabledRequested;
        _scene.OpenScheduledRequested += OnOpenScheduledRequested;
        _scene.BackRequested += OnBackRequested;
        _scene.SaveRequested += OnSaveRequested;
        _scene.TestGraphRequested += OnTestGraphRequested;
        _scene.AiEditRequested += OnAiEditRequested;
        AttachedToVisualTree += OnAttached;
    }

    public Task RequireCurrentAsync(AuthenticatedResourceActor expectedActor, CancellationToken token = default)
    {
        if (_disposed || _ownerCaller is null || _originalSelection is null || _originalSelection.Actor != expectedActor)
            throw new UnauthorizedAccessException("Original automation page context is unavailable.");
        return _ownerCaller.RequireCurrentAsync(_originalSelection, token);
    }
    internal AutomationsHavenScene Scene => _scene;
    private Task _lastLibraryAction = Task.CompletedTask;
    internal Task WhenLibraryActionIdleAsync() => _lastLibraryAction;
    internal HavenSceneControl SceneHost => _sceneHost;

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        await LoadDeviceCapabilityAsync();
        await RefreshAsync();
    }

    private void OnRefreshRequested(object? sender, EventArgs e) => _ = RefreshAsync();
    private void OnOpenTasksRequested(object? sender, EventArgs e) => _ = OpenTasksAsync();
    private void OnNewWorkflowRequested(object? sender, EventArgs e) => _scene.ShowEditor(null);
    private void OnRunWorkflowRequested(Guid id) => _ = RunWorkflowAsync(id);
    private void OnEditWorkflowRequested(Guid id) => OpenWorkflow(id);
    private void OnTestWorkflowRequested(Guid id) => _ = TestWorkflowAsync(id);
    private void OnDeleteWorkflowRequested(Guid id) => _lastLibraryAction = DeleteWorkflowAsync(id);
    private void OnSetWorkflowEnabledRequested(Guid id, bool enabled) => _lastLibraryAction = SetWorkflowEnabledAsync(id, enabled);
    private void OnOpenScheduledRequested(Guid id) => OpenScheduled(id);
    private void OnBackRequested(object? sender, EventArgs e) => _scene.ShowDashboard();
    private void OnSaveRequested(object? sender, EventArgs e) => _ = SaveEditorAsync();
    private void OnTestGraphRequested(object? sender, EventArgs e) => _ = TestEditorAsync();
    private void OnAiEditRequested(string instruction) => _ = ApplyAiEditAsync(instruction);

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }
        _refreshing = true;
        try
        {
            _scene.SetStatus("Loading Automations…");
            // Legacy settings-backed graph history has no original SQL-store binding; preserve it for explicit migration.

            if (_ownerCaller is null || _originalSelection is null)
                throw new InvalidOperationException("Original automation ownership is unavailable.");
            var library = await _ownerCaller.LoadLibraryAsync(_originalSelection, new(IncludeDisabled: true, Limit: 100), _lifetime.Token);
            _definitionPageComplete = library.Definitions.NextCursor is null;
            _workflows = library.Tasks.Items.Select(item => item.Value)
                .Where(item => item.ContainerId is null || item.ContainerId == _containerId).ToArray();
            _scheduled = library.Definitions.Items.Select(item => item.Value).Where(item => item.ContainerId == _containerId).ToArray();

            var runs = new List<AutomationsRunCard>();
            foreach (var definition in _scheduled)
            {
                foreach (var run in await _automations.GetRunsAsync(definition.Id, 50, _lifetime.Token))
                {
                    var active = run.Status is AutomationRunStatus.Pending or AutomationRunStatus.Running;
                    var timestamp = run.CompletedAt ?? run.StartedAt ?? run.ScheduledFor;
                    var status = run.Status == AutomationRunStatus.SkippedDuplicate ? "Skipped duplicate" : run.Status.ToString();
                    var detail = run.Status switch
                    {
                        AutomationRunStatus.Pending => "Scheduled " + run.ScheduledFor.LocalDateTime.ToString("g"),
                        AutomationRunStatus.Running => "Started " + (run.StartedAt ?? run.ScheduledFor).LocalDateTime.ToString("g"),
                        AutomationRunStatus.Succeeded when !string.IsNullOrWhiteSpace(run.Result) => run.Result!,
                        AutomationRunStatus.Failed when !string.IsNullOrWhiteSpace(run.Error) => run.Error!,
                        _ => status + " " + timestamp.LocalDateTime.ToString("g")
                    };
                    runs.Add(new AutomationsRunCard(definition.Id, definition.Name, status, detail, active));
                }
            }

            var scheduledById = _scheduled.ToDictionary(item => item.Id);
            var workflowCards = _workflows.Select(workflow =>
            {
                var hasSchedule = scheduledById.TryGetValue(workflow.Id, out var automation) && ScheduledGraphAutomationPayloadCodec.IsPayload(automation.Instruction);
                var detail = !hasSchedule ? string.Empty : automation!.NextRunAt is null ? "Waiting for trigger" : "Next " + automation.NextRunAt.Value.LocalDateTime.ToString("g");
                return new AutomationsWorkflowCard(workflow.Id, workflow.Name, workflow.Description, workflow.IsEnabled, hasSchedule, detail);
            }).ToArray();
            var scheduledCards = _scheduled.Select(item => new AutomationsScheduledCard(
                item.Id, item.Name, item.NextRunAt is null ? "Waiting for trigger" : "Next " + item.NextRunAt.Value.LocalDateTime.ToString("g"), item.IsEnabled)).ToArray();
            var graphHistory = AutomationGraphHistoryJournal.ForContainer(new(AutomationGraphHistoryJournal.CurrentVersion, []), _containerId, 50);
            await _ownerCaller.RequireCurrentAsync(_originalSelection, _lifetime.Token);
            if (_disposed) return;
            _scene.SetDashboardData(workflowCards, scheduledCards, runs.OrderByDescending(item => item.IsActive), graphHistory);
            _scene.SetStatus($"{_workflows.Count} reusable workflow{(_workflows.Count == 1 ? string.Empty : "s")} · {_scheduled.Count} scheduled automation{(_scheduled.Count == 1 ? string.Empty : "s")}");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _workflows = []; _scheduled = [];
            _scene.SetDashboardData([], [], [], []); _scene.ShowDashboard();
            _scene.SetStatus("Automations could not be loaded: " + ex.Message, true);
        }
        finally
        {
            _refreshing = false;
            if (_refreshPending && !_disposed)
            {
                _refreshPending = false;
                _ = RefreshAsync();
            }
        }
    }

    private async Task LoadDeviceCapabilityAsync()
    {
        if (_deviceActions is null || !OperatingSystem.IsWindows())
        {
            _scene.SetDeviceCapability(null);
            return;
        }
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice);
        try
        {
            _scene.SetDeviceCapability(await _deviceActions.GetSnapshotAsync(target, _lifetime.Token));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetDeviceCapability(null);
            _scene.SetStatus("Device capabilities could not be discovered: " + ex.Message, true);
        }
    }

    private void OpenWorkflow(Guid id)
    {
        var workflow = _workflows.FirstOrDefault(item => item.Id == id);
        if (workflow is null)
        {
            _scene.SetStatus("That workflow no longer exists. Refresh Automations and try again.", true);
            return;
        }
        _scene.ShowEditor(workflow);
    }

    private async Task TestWorkflowAsync(Guid id)
    {
        OpenWorkflow(id);
        if (_scene.EditingWorkflow?.Id == id) await TestEditorAsync();
    }

    private async Task SaveEditorAsync()
    {
        if (string.IsNullOrWhiteSpace(_scene.WorkflowName))
        {
            _scene.SetStatus("Workflow name is required.", true);
            return;
        }
        if (!_scene.TryGetGraph(out var graph, out var graphError))
        {
            _scene.SetStatus(graphError ?? "The graph is not ready to save.", true);
            return;
        }

        var graphJson = graph.Nodes.Count == 0 && graph.Edges.Count == 0 ? null : AutomationGraphCodec.Serialize(graph);
        var workflowName = _scene.WorkflowName;
        var workflowGoal = _scene.WorkflowGoal;
        var workflowInstructions = _scene.BuildInstructions();
        ReusableTaskDefinition? existing;
        try
        {
            existing = _scene.EditingWorkflow is { } displayed
                ? JsonSerializer.Deserialize<ReusableTaskDefinition>(JsonSerializer.Serialize(displayed))
                    ?? throw new InvalidDataException("Displayed workflow snapshot is unavailable.")
                : null;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
        { _scene.SetStatus("Displayed workflow could not be captured: " + error.Message, true); return; }
        if (!StringComparer.Ordinal.Equals(graphJson, existing?.GraphJson))
        {
            _scene.SetStatus("Canonical graph authoring and publication are unavailable; the original legacy graph is preserved.", true);
            return;
        }
        var fingerprint = JsonSerializer.Serialize(new { ExistingId = existing?.Id, WorkflowName = workflowName, WorkflowGoal = workflowGoal,
            Instructions = workflowInstructions, GraphJson = graphJson });
        try
        {
            await _definitionChanges.WaitAsync(_lifetime.Token);
            try
            {
                if (_ownerCaller is null || _originalSelection is null) throw new InvalidOperationException("Original automation ownership is unavailable.");
                if (_pendingEditorReview is not null && _pendingEditorFingerprint != fingerprint)
                    throw new InvalidOperationException("The original pending definition review must be finished or declined before editing this selection.");
                if (_pendingEditorReview is null)
                {
                    var now = DateTimeOffset.UtcNow;
                    var workflow = existing is null ? new ReusableTaskDefinition(Guid.NewGuid(), workflowName,
                        workflowGoal, workflowInstructions, _containerId, false, now, now, graphJson)
                        : existing with { Name = workflowName, Description = workflowGoal,
                            Instruction = workflowInstructions, IsEnabled = false, UpdatedAt = now };
                    _pendingEditorReview = await _ownerCaller.ReviewAsync(_originalSelection, workflow, existing?.Revision ?? 0,
                        existing is null ? AutomationDefinitionChangeKind.Create : AutomationDefinitionChangeKind.Update, _lifetime.Token);
                    _pendingEditorFingerprint = fingerprint;
                }
                var result = await _pendingEditorReview.FinishAsync(_lifetime.Token);
                _scene.SetStatus(result.Committed == true ? $"Definition saved; {result.Code}. Run/publication authority is unavailable."
                    : $"{result.Code}. Home request: {_pendingEditorReview.RequestId}. No save is replayed.", result.Committed != true);
                if (result.Committed == true && result.Code == "DefinitionCommitted")
                { _pendingEditorReview = null; _pendingEditorFingerprint = null; await RefreshAsync(); _scene.ShowDashboard(); }
            }
            finally { _definitionChanges.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus("The workflow could not be fully saved: " + ex.Message, true);
        }
    }

    private async Task TestEditorAsync()
    {
        if (!_scene.TryGetGraph(out var graph, out var error))
        {
            _scene.SetStatus(error ?? "The graph is not ready to test.", true);
            return;
        }
        if (graph.Nodes.Count == 0)
        {
            _scene.SetStatus("Add at least one node before testing this workflow.", true);
            return;
        }
        _scene.SetStatus("Running non-destructive graph test…");
        var result = await AutomationGraphTestRunner.RunAsync(graph, _lifetime.Token);
        // Pure in-memory preview only; no canonical publication, external operation or unreviewed history write.
        _scene.SetGraphTestResult(result);
        _scene.SetStatus(result.Succeeded
            ? $"Test passed: {result.Trace.Count} node{(result.Trace.Count == 1 ? string.Empty : "s")} traced without external side effects."
            : result.FailureMessage ?? result.ValidationIssues.FirstOrDefault()?.Message ?? "Graph test failed.",
            !result.Succeeded);
    }

    private async Task ApplyAiEditAsync(string instruction)
    {
        if (_graphAiEditor is null)
        {
            _scene.SetStatus("The Automation graph AI editor is unavailable in this host.", true);
            return;
        }
        if (!_scene.TryGetGraph(out var current, out var validationError))
        {
            _scene.SetStatus(validationError ?? "Fix the current graph before applying an AI edit.", true);
            return;
        }
        _scene.SetStatus("Asking Haven for a typed graph edit…");
        try
        {
            var result = await _graphAiEditor.ProposeEditAsync(current, instruction, _lifetime.Token);
            if (!result.Succeeded || result.Graph is null)
            {
                _scene.SetStatus(result.Status, true);
                return;
            }
            _scene.ApplyAiGraph(result.Graph, result.Status);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus("The graph edit failed: " + ex.Message, true);
        }
    }

    private async Task RunWorkflowAsync(Guid id)
    {
        await Task.CompletedTask;
        _scene.SetStatus("Canonical graph/run owner admission is unavailable; no workflow is executed.", true);
    }

    private async Task SetWorkflowEnabledAsync(Guid id, bool enabled)
    {
        if (enabled) { _scene.SetStatus("Canonical graph/run publication authority is unavailable.", true); return; }
        await ReviewLibraryChangeAsync(id, AutomationDefinitionChangeKind.Disable);
    }
    private Task DeleteWorkflowAsync(Guid id) => ReviewLibraryChangeAsync(id, AutomationDefinitionChangeKind.Archive);
    private async Task ReviewLibraryChangeAsync(Guid id, AutomationDefinitionChangeKind kind)
    {
        var original = _workflows.FirstOrDefault(item => item.Id == id);
        if (original is null) return;
        var linked = _scheduled.FirstOrDefault(item => item.Id == id);
        // Capture both displayed immutable rows before waiting; never adopt later refreshed heads.
        if (linked is not null)
        {
            await ReviewLinkedLibraryChangeAsync(original, linked, kind);
            return;
        }
        if (!_definitionPageComplete)
        { _scene.SetStatus("Linked definition lookup is incomplete; no single-row change was admitted.", true); return; }
        try
        {
            await _definitionChanges.WaitAsync(_lifetime.Token);
            try
            {
                if (_ownerCaller is null || _originalSelection is null) throw new InvalidOperationException("Original automation ownership is unavailable.");
                if (_pendingLinkedChanges.ContainsKey(id))
                    throw new InvalidOperationException("Finish the original linked definition review first.");
                var fingerprint = JsonSerializer.Serialize(new { kind, original.Id, original.Revision });
                if (_pendingLibraryChanges.TryGetValue(id, out var retained) && retained.Fingerprint != fingerprint)
                    throw new InvalidOperationException("The original library review is still retained; no substituted change is admitted.");
                if (retained.Review is null)
                {
                    var proposal = original with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention };
                    var review = await _ownerCaller.ReviewAsync(_originalSelection, proposal, original.Revision, kind, _lifetime.Token);
                    retained = (fingerprint, review); _pendingLibraryChanges.Add(id, retained);
                }
                var result = await retained.Review.FinishAsync(_lifetime.Token);
                _scene.SetStatus($"{result.Code}. Home request: {retained.Review.RequestId}.", result.Committed != true);
                if (result.Committed == true && result.Code == "DefinitionCommitted")
                { _pendingLibraryChanges.Remove(id); await RefreshAsync(); }
            }
            finally { _definitionChanges.Release(); }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { _scene.SetStatus("Definition review unavailable: " + error.Message, true); }
    }

    private async Task ReviewLinkedLibraryChangeAsync(ReusableTaskDefinition original,
        AutomationDefinition schedule, AutomationDefinitionChangeKind kind)
    {
        var fingerprint = JsonSerializer.Serialize(new { kind, original.Id,
            TaskRevision = original.Revision, ScheduleRevision = schedule.Revision });
        try
        {
            await _definitionChanges.WaitAsync(_lifetime.Token);
            try
            {
                if (_disposed) return;
                if (_linkedOwnerCaller is null || _originalSelection is null)
                    throw new InvalidOperationException("Original linked definition ownership is unavailable.");
                if (_pendingLibraryChanges.ContainsKey(original.Id))
                    throw new InvalidOperationException("Finish the original single definition review first.");
                if (_pendingLinkedChanges.TryGetValue(original.Id, out var retained) && retained.Fingerprint != fingerprint)
                    throw new InvalidOperationException("The exact original linked pair review is still retained.");
                if (retained.Review is null)
                {
                    var taskProposal = original with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention };
                    var scheduleProposal = schedule with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention };
                    var review = await _linkedOwnerCaller.ReviewAsync(_originalSelection, taskProposal, scheduleProposal,
                        original.Revision, schedule.Revision, kind, _lifetime.Token);
                    retained = (fingerprint, review);
                    _pendingLinkedChanges.Add(original.Id, retained);
                }
                var result = await retained.Review.FinishAsync(_lifetime.Token);
                if (_disposed) return; // Keep exact issued handles even when their presentation closes.
                _scene.SetStatus($"{result.Code}. Home requests: {string.Join(", ", retained.Review.RequestIDs)}.", result.Committed != true);
                if (result.Committed == true && result.Code == "LinkedDefinitionsCommitted")
                { _pendingLinkedChanges.Remove(original.Id); await RefreshAsync(); }
            }
            finally { _definitionChanges.Release(); }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { if (!_disposed) _scene.SetStatus("Linked definition review unavailable: " + error.Message, true); }
    }

    private void OpenScheduled(Guid id)
    {
        var automation = _scheduled.FirstOrDefault(item => item.Id == id);
        if (automation is null) return;
        if (!ScheduledGraphAutomationPayloadCodec.IsPayload(automation.Instruction))
        {
            _ = InvokeTaskAsync(automation.Instruction);
            return;
        }
        var linked = _workflows.FirstOrDefault(workflow => workflow.Id == automation.Id);
        if (linked is not null)
        {
            _scene.ShowEditor(linked);
            _scene.SetStatus($"Opened scheduled graph {linked.Name}.");
            return;
        }
        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(automation.Instruction, out var payload))
        {
            _scene.SetStatus("This scheduled graph payload is invalid. Haven did not route it to Tasks or perform a substitute instruction.", true);
            return;
        }
        var captured = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? automation.Name : payload.WorkflowName,
            "Captured scheduled graph snapshot",
            string.Empty,
            automation.ContainerId,
            automation.IsEnabled,
            automation.CreatedAt,
            automation.UpdatedAt,
            payload.GraphJson);
        _scene.ShowEditor(captured);
        _scene.SetStatus($"Opened captured scheduled graph {captured.Name}.");
    }





    private async Task EnsureHistoryLoadedAsync(CancellationToken cancellationToken)
    {
        if (_historyLoaded) return;
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            if (_historyLoaded) return;
            var stored = _historySettings is null ? null : await _historySettings.GetAsync<AutomationGraphHistoryState>(GraphHistorySettingsKey, cancellationToken);
            _history = AutomationGraphHistoryJournal.Normalize(stored);
            _historyLoaded = true;
        }
        finally { _historyGate.Release(); }
    }

    private async Task RecordGraphHistoryAsync(ReusableTaskDefinition workflow, string graphJson, AutomationGraphRunResult result)
    {
        await EnsureHistoryLoadedAsync(_lifetime.Token);
        var entry = AutomationGraphHistoryJournal.Capture(workflow.Id, workflow.ContainerId, workflow.Name, workflow.Instruction, graphJson, result);
        await _historyGate.WaitAsync(_lifetime.Token);
        try
        {
            _history = AutomationGraphHistoryJournal.Append(_history, entry);
            if (_historySettings is not null) await _historySettings.SetAsync(GraphHistorySettingsKey, _history, _lifetime.Token);
        }
        finally { _historyGate.Release(); }
    }

    private async Task OpenTasksAsync()
    {
        try { await _startOneTimeTask(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _scene.SetStatus("The one-time task could not be opened: " + ex.Message, true); }
    }

    private async Task InvokeTaskAsync(string instruction)
    {
        try { await _runTask(instruction); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _scene.SetStatus("The task could not be opened: " + ex.Message, true); }
    }

    private static string FormatWorkflowRunStatus(ReusableDeviceWorkflowRunResult run)
    {
        if (run.Kind != ReusableDeviceWorkflowRunKind.DeviceAction || run.DeviceResult is null) return run.Message;
        var result = run.DeviceResult;
        var prefix = result.Status switch
        {
            DeviceActionResultStatus.Success => "DEVICE completed",
            DeviceActionResultStatus.Unsupported => "Unsupported",
            DeviceActionResultStatus.PermissionRequired => "Permission required",
            DeviceActionResultStatus.DeviceUnavailable => "Device unavailable",
            DeviceActionResultStatus.ConnectionLost => "Connection lost",
            DeviceActionResultStatus.ActionRejected => "Action rejected",
            DeviceActionResultStatus.PlatformError => "Platform error",
            _ => "DEVICE result"
        };
        return string.IsNullOrWhiteSpace(result.Output) ? $"{prefix}: {result.Message}" : $"{prefix}: {result.Message} {result.Output}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        AttachedToVisualTree -= OnAttached;
        _scene.RefreshRequested -= OnRefreshRequested;
        _scene.OpenTasksRequested -= OnOpenTasksRequested;
        _scene.NewWorkflowRequested -= OnNewWorkflowRequested;
        _scene.RunWorkflowRequested -= OnRunWorkflowRequested;
        _scene.EditWorkflowRequested -= OnEditWorkflowRequested;
        _scene.TestWorkflowRequested -= OnTestWorkflowRequested;
        _scene.DeleteWorkflowRequested -= OnDeleteWorkflowRequested;
        _scene.SetWorkflowEnabledRequested -= OnSetWorkflowEnabledRequested;
        _scene.OpenScheduledRequested -= OnOpenScheduledRequested;
        _scene.BackRequested -= OnBackRequested;
        _scene.SaveRequested -= OnSaveRequested;
        _scene.TestGraphRequested -= OnTestGraphRequested;
        _scene.AiEditRequested -= OnAiEditRequested;
        _scene.Dispose();
        _lifetime.Dispose();
        _historyGate.Dispose();
    }
}
