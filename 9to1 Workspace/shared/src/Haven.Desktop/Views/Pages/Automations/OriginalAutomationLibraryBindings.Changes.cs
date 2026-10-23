#if !ANDROID
using System.Runtime.ExceptionServices;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Automations;

internal sealed partial class OriginalAutomationLibraryBindings
{
    private readonly ICanonicalAutomationDefinitionOriginalProcessSource? _writer;
    private RowTarget? _selectedTarget;
    private string _changeStatus = "";
    private sealed record ChangeOperation(RowTarget Target, CanonicalAutomationOriginalChangeKind Kind,
        CanonicalAutomationOriginalEdit? Edit, Guid Id);
    private string _editName = "", _editInstruction = "", _editScheduleValue = "", _editDay = "",
        _initialScheduleValue = "", _initialDay = "", _editScheduleHint = "", _editScheduleKind = "";
    private bool _editGraph, _editScheduleReady, _editSubmitted;
    private bool IsCurrentSelection => _selectedTarget is { } current && current.Epoch == _epoch && _issued.Contains(current);
    private bool CanEditDraft => !_retiring && !_busy && !_editSubmitted && IsCurrentSelection &&
        _writer?.SupportsOriginalDisabledEdit == true && _selectedTarget!.Row.Value is { IsEnabled: false, ArchivedAt: null } && _editScheduleReady;
    private bool EditValuesChanged => _selectedTarget is { } selected &&
        (_editName != selected.Row.Value.Name || _editInstruction != selected.Row.Value.Instruction ||
         _editScheduleValue != _initialScheduleValue || _editDay != _initialDay);
    private bool HasUnsubmittedEdits => !_editSubmitted && EditValuesChanged;
    private void ClearEditDraft()
    {
        _editName = _editInstruction = _editScheduleValue = _editDay = _initialScheduleValue = _initialDay =
            _editScheduleHint = _editScheduleKind = "";
        _editGraph = _editScheduleReady = _editSubmitted = false;
    }
    private void LoadEditDraft(AutomationDefinition saved)
    {
        ClearEditDraft(); _editName = saved.Name; _editInstruction = saved.Instruction;
        _editScheduleKind = saved.ScheduleKind.ToString();
        _editGraph = saved.GraphBinding is not null || ScheduledGraphAutomationPayloadCodec.IsPayload(saved.Instruction);
        if (_editGraph)
        {
            _editScheduleReady = true;
            _editScheduleHint = "Graph-backed automation: edit the name here. Instructions, schedule and graph binding are preserved.";
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(saved.ScheduleJson); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                throw new FormatException("The saved schedule is ambiguous.");
            string Text(string key) => root.GetProperty(key).GetString() ?? throw new FormatException("A schedule value is missing.");
            string Number(string key) => root.GetProperty(key).GetInt32().ToString(CultureInfo.InvariantCulture);
            _editScheduleValue = saved.ScheduleKind switch
            {
                AutomationScheduleKind.Once => Text("at"), AutomationScheduleKind.Hourly => Number("intervalHours"),
                AutomationScheduleKind.Daily or AutomationScheduleKind.Weekly => Text("time"),
                AutomationScheduleKind.ConditionWatch => Number("intervalMinutes"), _ => throw new FormatException("Unknown saved schedule.")
            };
            if (saved.ScheduleKind == AutomationScheduleKind.Weekly) _editDay = Text("dayOfWeek");
            _editScheduleHint = saved.ScheduleKind switch
            {
                AutomationScheduleKind.Once => "Date and time with UTC offset, for example 2026-10-24T08:00:00+01:00. Saving keeps execution disabled.",
                AutomationScheduleKind.Hourly => "Interval in hours, 1–168. Saving clears the pending run time and keeps execution disabled.",
                AutomationScheduleKind.ConditionWatch => "Check interval in minutes, 60–10080. The saved condition is preserved; execution stays disabled.",
                _ => "Time as HH:mm" + (saved.ScheduleKind == AutomationScheduleKind.Weekly ? "; day Monday–Sunday" : "") +
                    ". Runtime uses this device's local time zone: " + TimeZoneInfo.Local.DisplayName + ". Saving keeps execution disabled."
            };
            _editScheduleReady = true;
        }
        catch (Exception cause) when (cause is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { _editScheduleHint = "This saved schedule cannot be edited here. Its original settings are preserved."; }
        _initialScheduleValue = _editScheduleValue; _initialDay = _editDay;
        if (saved.IsEnabled || saved.ArchivedAt is not null)
            _editScheduleHint = "Only a recovered, disabled, unarchived automation can be edited. Disable it and refresh first.";
    }
    private bool SetEditValue(string path, string text)
    {
        if (!CanEditDraft || (path != "EditName" && _editGraph)) return false;
        var limit = path switch { "EditName" => 256, "EditInstruction" => 16000, "EditScheduleValue" => 128, "EditDay" => 16, _ => 0 };
        if (limit == 0 || text.Length > limit || (path == "EditDay" && _selectedTarget!.Row.Value.ScheduleKind != AutomationScheduleKind.Weekly)) return false;
        _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            switch (path)
            {
                case "EditName": _editName = text; break;
                case "EditInstruction": _editInstruction = text; break;
                case "EditScheduleValue": _editScheduleValue = text; break;
                case "EditDay": _editDay = text; break;
            }
            _changeStatus = "Unsaved edits. Save for individual Home review, or discard before changing the selection.";
            Changed(); return true;
        }));
        return true;
    }
    private CanonicalAutomationOriginalEdit BuildEdit(AutomationDefinition saved)
    {
        var schedule = saved.ScheduleJson;
        if (_editScheduleValue != _initialScheduleValue || _editDay != _initialDay)
        {
            var json = JsonNode.Parse(schedule) as JsonObject ?? throw new ArgumentException("The saved schedule must be an object.");
            switch (saved.ScheduleKind)
            {
                case AutomationScheduleKind.Once: json["at"] = _editScheduleValue.Trim(); break;
                case AutomationScheduleKind.Daily: json["time"] = _editScheduleValue.Trim(); break;
                case AutomationScheduleKind.Weekly:
                    json["time"] = _editScheduleValue.Trim(); json["dayOfWeek"] = _editDay.Trim(); break;
                case AutomationScheduleKind.Hourly: json["intervalHours"] = ParseInterval(); break;
                case AutomationScheduleKind.ConditionWatch: json["intervalMinutes"] = ParseInterval(); break;
                default: throw new ArgumentException("This saved schedule cannot be edited here.");
            }
            schedule = json.ToJsonString();
        }
        var edit = new CanonicalAutomationOriginalEdit(_editName.Trim(), _editInstruction, schedule);
        edit.PrepareDisabledProposal(saved, saved.UpdatedAt); // Input feedback only; the actual owner repeats validation.
        return edit;
        int ParseInterval() => int.TryParse(_editScheduleValue, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : throw new ArgumentException("Enter a whole-number interval within the displayed range.");
    }
    private readonly List<ChangeOperation> _changeOperations = [];
    private readonly List<ChangeDelivery> _changeDeliveries = [];
    internal ICanonicalAutomationDefinitionOriginalProcessSource? OriginalWriter => _writer;
    internal ICanonicalAutomationDefinitionOriginalChangeObservation? OriginalChangeObservation
    { get { lock (_gate) return _changeDeliveries.LastOrDefault()?.Observation; } }
    private sealed class ChangeDelivery
    {
        internal ICanonicalAutomationDefinitionOriginalProcessPreparation? Preparation;
        internal ICanonicalAutomationDefinitionOriginalChangeObservation? Observation;
        internal Task? Close;
        internal bool IssuerVerified;
        internal bool DeclinedBeforeEffect;
        internal bool CloseJoined;
    }
    private Task StartChange(string action, object? parameter)
    {
        if (parameter is not null || IsActionAvailable(action) != true || _writer is null)
            throw new InvalidOperationException("Select the current saved automation before reviewing a change.");
        var target = DemandRow(_selectedTarget); var epoch = _epoch;
        var kind = action switch { "recover" => CanonicalAutomationOriginalChangeKind.RecoverLegacy,
            "save-edits" => CanonicalAutomationOriginalChangeKind.EditDisabled, _ => CanonicalAutomationOriginalChangeKind.Disable };
        CanonicalAutomationOriginalEdit? edit = null;
        if (kind == CanonicalAutomationOriginalChangeKind.EditDisabled)
            try { edit = BuildEdit(target.Row.Value); }
            catch (Exception cause) when (cause is ArgumentException or JsonException or InvalidOperationException)
            {
                _changeStatus = cause.Message; Changed(); return Task.CompletedTask;
            }
        var submission = _changeOperations.SingleOrDefault(value => ReferenceEquals(value.Target, target) && value.Kind == kind && value.Edit == edit);
        if (submission is null)
        {
            if (_changeOperations.Count >= 128) throw new InvalidOperationException("Keep the pending changes for inspection before starting another.");
            submission = new(target, kind, edit, Guid.NewGuid()); _changeOperations.Add(submission);
        }
        // Retry retains the SAME privately selected row/kind operation. A fresh
        // source page/revision is a distinct explicit submission, never old approval.
        var operation = submission.Id;
        var command = new Command(this); var delivery = new ChangeDelivery();
        lock (_gate)
        {
            _commands.RemoveAll(actual => actual.Driver?.IsCompletedSuccessfully == true && actual.IsIndependentlyJoined);
            _changeDeliveries.RemoveAll(actual => actual.CloseJoined && actual.Close is { IsCompletedSuccessfully: true });
            if (_commands.Count >= 128 || _changeDeliveries.Count >= 128)
                throw new InvalidOperationException("Keep the current library observations for inspection before more changes.");
            _commands.Add(command); _changeDeliveries.Add(delivery);
        }
        _busy = true; if (edit is not null) _editSubmitted = true; _changeStatus = "Waiting for your individual Home review. Saved settings remain visible.";
        try
        {
            return _work.RunAsync(async original =>
            {
                command.Bind(original);
                try
                {
                    if (!_work.IsRetiring) Changed();
                    var actor = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain,
                        CancellationToken.None).AsTask()) ?? throw new UnauthorizedAccessException("Your current Home profile is unavailable.");
                    command.Run(() => DemandActualRow(target, actor));
                    var prepared = await command.Read(() => edit is not null
                        ? _writer.PrepareOriginalEditProcessWithinSourceAsync(target.Observation, target.Row, edit, operation,
                            command.Run, command.Retain, CancellationToken.None)
                        : _writer.PrepareOriginalChangeProcessWithinSourceAsync(
                            target.Observation, target.Row, kind, operation, command.Run, command.Retain, CancellationToken.None),
                        actual => CaptureChangePreparation(command, delivery, actual));
                    command.Run(() => DemandChangePreparation(delivery, prepared, actor, target, kind, operation, edit));
                    if (delivery.DeclinedBeforeEffect)
                    {
                        if (!_work.IsRetiring && epoch == _epoch)
                            _changeStatus = edit is null ? "Review is unavailable for this saved automation. Its saved settings were kept."
                                : "This automation is not eligible for editing. Recover legacy settings, disable it and refresh before editing. Saved settings were kept.";
                        return;
                    }
                    var observation = delivery.Observation ?? throw new InvalidOperationException("The actual review observation was not retained.");
                    var completed = await command.Read(() => observation.WaitOriginalCompletionAsync(CancellationToken.None));
                    CanonicalAutomationOriginalChangeCompletionKind completedKind = default;
                    command.Run(() =>
                    {
                        completedKind = completed.Kind;
                        if (!observation.IsIssuedOriginalCompletion(completed) || !ReferenceEquals(completed.OriginalObservation, observation))
                            throw new UnauthorizedAccessException("The actual change observation did not issue this result.");
                        if (completedKind == CanonicalAutomationOriginalChangeCompletionKind.Changed &&
                            (completed.Acknowledgment is not { } acknowledgment ||
                             !ReferenceEquals(acknowledgment.OriginalIntent, observation.OriginalIntent) ||
                             acknowledgment.OperationId != operation || acknowledgment.Definition.Id != target.Row.Value.Id ||
                             acknowledgment.Definition.IsEnabled ||
                             (edit is not null && !MatchesEdit(acknowledgment.Definition, edit)) || acknowledgment.Definition.OperationalState !=
                                 (kind == CanonicalAutomationOriginalChangeKind.RecoverLegacy ? AutomationOperationalState.NeedsAttention : AutomationOperationalState.Disabled)))
                            throw new UnauthorizedAccessException("The actual saved-automation change result has a different selection.");
                        if (completedKind != CanonicalAutomationOriginalChangeCompletionKind.Changed && completed.Acknowledgment is not null)
                            throw new UnauthorizedAccessException("A retired or declined observation cannot acknowledge a saved change.");
                    });
                    // The view closes only its SAME delivery after that delivery's
                    // successful result. The process driver remains with the writer.
                    await command.Read(() => delivery.Close ??= observation.CloseAndDrainOriginalAsync());
                    delivery.CloseJoined = true;
                    await command.Join();
                    if (!_work.IsRetiring && epoch == _epoch && ReferenceEquals(_selectedTarget, target))
                    {
                        command.Run(() => DemandActualRow(target, actor));
                        _changeStatus = completedKind switch
                        {
                            CanonicalAutomationOriginalChangeCompletionKind.Changed => kind switch
                            {
                                CanonicalAutomationOriginalChangeKind.RecoverLegacy => "Saved settings are marked for attention and disabled. Refresh to see the updated list.",
                                CanonicalAutomationOriginalChangeKind.EditDisabled => "Your reviewed edits were saved. The automation remains disabled. Refresh to see the updated settings.",
                                _ => "The saved automation is disabled. Refresh to see the updated list."
                            },
                            CanonicalAutomationOriginalChangeCompletionKind.DeclinedBeforeEffect => "The change was declined. Saved settings were kept.",
                            CanonicalAutomationOriginalChangeCompletionKind.ObservationRetired => "This review view closed. Reopen the library to check its progress.",
                            _ => throw new InvalidDataException("The actual change observation returned an unknown result.")
                        };
                        if (completedKind == CanonicalAutomationOriginalChangeCompletionKind.Changed)
                        { _selectedTarget = null; _selectedState = kind == CanonicalAutomationOriginalChangeKind.RecoverLegacy
                              ? "Needs attention · disabled in saved settings" : "Disabled in saved settings"; _review = "Refresh to view the current saved settings."; }
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause);
                    if (!_work.IsRetiring) _changeStatus = "This change needs inspection. Its original operation is retained; refresh before retrying.";
                    throw;
                }
                finally
                {
                    try { await command.Join(); } finally { _busy = false; if (!_work.IsRetiring) Changed(); }
                }
            }, actual => { command.Driver = actual; OriginalCommand = actual; });
        }
        catch
        {
            if (command.Driver is null)
            { lock (_gate) { _commands.Remove(command); _changeDeliveries.Remove(delivery); } _busy = false; }
            throw;
        }
    }
    private void CaptureChangePreparation(Command command, ChangeDelivery retained,
        ICanonicalAutomationDefinitionOriginalProcessPreparation actual)
    {
        // Custody before any later caller postguard. Foreign objects are retained
        // for inspection and never receive disposal merely because they returned.
        retained.Preparation = actual;
        command.Run(() =>
        {
            // A source product getter can invoke foreign callbacks. Capture the
            // actual returned delivery inside this SAME physical command scope,
            // before issuer queries or any later caller refusal.
            retained.Observation = actual.Observation;
            retained.DeclinedBeforeEffect = actual.IsDeclinedBeforeEffect;
            if (!_writer!.IsIssuedOriginalProcessPreparation(actual) ||
                retained.DeclinedBeforeEffect != (retained.Observation is null) ||
                retained.Observation is { } observation && !_writer.IsIssuedOriginalChangeObservation(observation))
                throw new UnauthorizedAccessException("The configured process did not issue this actual review delivery.");
            retained.IssuerVerified = true;
            // Stop may already have run while the finite acquisition was pending.
            // Retire this late actual delivery before the admitted command can wait.
            if (_retiring) retained.Observation?.RequestOriginalRetirement();
        });
    }
    private void DemandChangePreparation(ChangeDelivery retained,
        ICanonicalAutomationDefinitionOriginalProcessPreparation actual, AuthenticatedResourceActor actor,
        RowTarget target, CanonicalAutomationOriginalChangeKind kind, Guid operation, CanonicalAutomationOriginalEdit? edit)
    {
        if (!retained.IssuerVerified || !ReferenceEquals(retained.Preparation, actual) ||
            !_writer!.IsIssuedOriginalProcessPreparation(actual))
            throw new UnauthorizedAccessException("The current view has no actual issuer-owned change delivery.");
        if (retained.Observation is { } observation)
        {
            var intent = observation.OriginalIntent;
            if (!_writer.IsIssuedOriginalChangeObservation(observation) || !_writer.IsIssuedOriginalChangeIntent(intent) ||
                intent.Actor != actor || intent.OriginalDefinition.Id != target.Row.Value.Id || intent.OperationId != operation ||
                intent.ChangeKind != kind || (edit is not null && !MatchesEdit(intent.ProposedDefinition, edit)) || intent.OriginalStoreIdentity != target.Observation.OriginalStoreIdentity)
                throw new UnauthorizedAccessException("The actual review belongs to different saved settings or Home profile.");
        }
    }
    private static bool MatchesEdit(AutomationDefinition proposed, CanonicalAutomationOriginalEdit edit) =>
        proposed.Name == edit.Name && proposed.Instruction == edit.Instruction && proposed.ScheduleJson == edit.ScheduleJson &&
        !proposed.IsEnabled && proposed.NextRunAt is null;
    private Task RetireChangeDeliveriesAsync()
    {
        var failures = new List<Exception>(); ChangeDelivery[] all; lock (_gate) all = _changeDeliveries.ToArray();
        foreach (var retained in all)
            if (retained.Observation is { } observation)
                try
                {
                    _work.RunCloseCallback(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                    {
                        if (!retained.IssuerVerified || !_writer!.IsIssuedOriginalChangeObservation(observation))
                            throw new UnauthorizedAccessException("An unverified returned review remains retained for inspection.");
                        observation.RequestOriginalRetirement(); return true;
                    }));
                }
                catch (Exception cause) { AddChangeCause(failures, cause); }
        return failures.Count == 0 ? Task.CompletedTask : Task.FromException(new AggregateException("Actual review delivery retirement failed.", failures));
    }
    private async Task JoinChangeDeliveriesAsync()
    {
        var failures = new List<Exception>(); ChangeDelivery[] all; lock (_gate) all = _changeDeliveries.ToArray();
        foreach (var retained in all)
            if (retained.Observation is { } observation)
            {
                try
                {
                    _work.RunCloseCallback(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                    {
                        if (!retained.IssuerVerified || !_writer!.IsIssuedOriginalChangeObservation(observation))
                            throw new UnauthorizedAccessException("An unverified returned review remains retained for inspection.");
                        observation.RequestOriginalRetirement();
                        retained.Close ??= observation.CloseAndDrainOriginalAsync(); return true;
                    }));
                }
                catch (Exception cause) { AddChangeCause(failures, cause); }
                if (retained.Close is { } raw)
                    try { await raw; retained.CloseJoined = true; }
                    catch (Exception cause)
                    { foreach (var direct in raw.Exception?.InnerExceptions ?? new[] { cause }.AsEnumerable()) AddChangeCause(failures, direct); }
            }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual review deliveries failed; process and unknown originals remain retained.", failures);
    }
    private static void AddChangeCause(List<Exception> failures, Exception cause)
    { if (!failures.Any(same => ReferenceEquals(same, cause))) failures.Add(cause); }
}
#endif
