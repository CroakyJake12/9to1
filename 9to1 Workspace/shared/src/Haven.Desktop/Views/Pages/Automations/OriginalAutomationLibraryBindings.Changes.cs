#if !ANDROID
using System.Runtime.ExceptionServices;
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
    private sealed record ChangeOperation(RowTarget Target, CanonicalAutomationOriginalChangeKind Kind, Guid Id);
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
        var kind = action == "recover" ? CanonicalAutomationOriginalChangeKind.RecoverLegacy : CanonicalAutomationOriginalChangeKind.Disable;
        var submission = _changeOperations.SingleOrDefault(value => ReferenceEquals(value.Target, target) && value.Kind == kind);
        if (submission is null)
        {
            if (_changeOperations.Count >= 128) throw new InvalidOperationException("Keep the pending changes for inspection before starting another.");
            submission = new(target, kind, Guid.NewGuid()); _changeOperations.Add(submission);
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
        _busy = true; _changeStatus = "Waiting for your individual Home review. Saved settings remain visible.";
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
                    var prepared = await command.Read(() => _writer.PrepareOriginalChangeProcessWithinSourceAsync(
                        target.Observation, target.Row, kind, operation, command.Run, command.Retain, CancellationToken.None),
                        actual => CaptureChangePreparation(command, delivery, actual));
                    command.Run(() => DemandChangePreparation(delivery, prepared, actor, target, kind, operation));
                    if (delivery.DeclinedBeforeEffect)
                    {
                        if (!_work.IsRetiring && epoch == _epoch)
                            _changeStatus = "Review is unavailable for this saved automation. Its saved settings were kept.";
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
                             acknowledgment.Definition.IsEnabled || acknowledgment.Definition.OperationalState !=
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
                            CanonicalAutomationOriginalChangeCompletionKind.Changed => kind == CanonicalAutomationOriginalChangeKind.RecoverLegacy
                                ? "Saved settings are marked for attention and disabled. Refresh to see the updated list."
                                : "The saved automation is disabled. Refresh to see the updated list.",
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
        RowTarget target, CanonicalAutomationOriginalChangeKind kind, Guid operation)
    {
        if (!retained.IssuerVerified || !ReferenceEquals(retained.Preparation, actual) ||
            !_writer!.IsIssuedOriginalProcessPreparation(actual))
            throw new UnauthorizedAccessException("The current view has no actual issuer-owned change delivery.");
        if (retained.Observation is { } observation)
        {
            var intent = observation.OriginalIntent;
            if (!_writer.IsIssuedOriginalChangeObservation(observation) || !_writer.IsIssuedOriginalChangeIntent(intent) ||
                intent.Actor != actor || intent.OriginalDefinition.Id != target.Row.Value.Id || intent.OperationId != operation ||
                intent.ChangeKind != kind || intent.OriginalStoreIdentity != target.Observation.OriginalStoreIdentity)
                throw new UnauthorizedAccessException("The actual review belongs to different saved settings or Home profile.");
        }
    }
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
