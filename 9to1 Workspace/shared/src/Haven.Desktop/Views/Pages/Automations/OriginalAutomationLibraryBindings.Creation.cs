#if !ANDROID
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Automations;

internal sealed partial class OriginalAutomationLibraryBindings
{
    private ICanonicalAutomationDefinitionOriginalCreationProcessSource? _creationSource =>
        _writer as ICanonicalAutomationDefinitionOriginalCreationProcessSource;
    private ICanonicalAutomationLibraryOriginalObservation? _currentPage;
    private ICanonicalAutomationDefinitionOriginalCreationReservation? _creationReservation;
    private string _newAutomationName = "", _creationStatus = "";
    private bool _creationSubmitted;
    private sealed record CreationRow(string Key, string Name, string Identity, CreationTarget Target);
    private sealed record CreationTarget(OriginalAutomationLibraryBindings Owner, long Epoch,
        ICanonicalAutomationLibraryOriginalObservation Page, ICanonicalAutomationDefinitionOriginalCreationRecovery Selection);
    private IReadOnlyList<CreationRow> _creationRows = [];
    private sealed class CreationPresentation
    {
        internal ICanonicalAutomationDefinitionOriginalCreationObservation? Observation;
        internal Task? Close;
        internal bool IssuerVerified, CloseJoined;
    }
    private readonly List<CreationPresentation> _creationPresentations = [];
    private bool CanPrepareCreation => !_retiring && !_busy && !HasUnsubmittedEdits &&
        _creationSource?.SupportsOriginalDefinitionCreation == true &&
        _currentPage is { State: CanonicalAutomationLibraryReadState.Available };
    private object? CreationValue(string path) => path switch
    {
        "HasCreationSource" => _creationSource?.SupportsOriginalDefinitionCreation == true,
        "NewAutomationName" => _newAutomationName, "CreationStatus" => _creationStatus,
        "CanNameNewAutomation" => CanPrepareCreation && !_creationSubmitted,
        "CanCreateDraft" => CreationActionAvailable("create-draft"),
        "CanDiscardNew" => CreationActionAvailable("discard-new"),
        "CanRefreshCreations" => CreationActionAvailable("refresh-creations"), "CreationRows" => _creationRows,
        _ => null
    };
    private bool CreationActionAvailable(string action) => action switch
    {
        "create-draft" => CanPrepareCreation && !_creationSubmitted && !string.IsNullOrWhiteSpace(_newAutomationName) && _newAutomationName.Length <= 256,
        "discard-new" => !_retiring && !_busy && !_creationSubmitted && _newAutomationName.Length != 0,
        "refresh-creations" => CanPrepareCreation,
        "recover-creation" => CanPrepareCreation && _creationRows.Count != 0,
        _ => false
    };
    private bool SetCreationValue(string path, string text)
    {
        if (path != "NewAutomationName" || !CanPrepareCreation || _creationSubmitted || text.Length > 256) return false;
        _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        { _newAutomationName = text; Changed(); return true; })); return true;
    }
    private bool TryGetCreationItemValue(object item, string path, out object? value)
    {
        value = item is CreationRow row && _creationRows.Any(current => ReferenceEquals(current, row)) ? path switch
        {
            "Key" => row.Key, "Name" => row.Name, "Identity" => row.Identity, "Target" => row.Target,
            "CanRecover" => CanPrepareCreation && row.Target.Epoch == _epoch && ReferenceEquals(row.Target.Page, _currentPage), _ => null
        } : null;
        return value is not null;
    }
    private Task StartCreationAction(string action, object? parameter)
    {
        if (IsActionAvailable(action) != true) throw new InvalidOperationException("Refresh the actual saved library before preparing or recovering a creation.");
        if (action == "discard-new")
        {
            if (parameter is not null) throw new ArgumentException("Discard does not accept a saved creation.");
            _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            { _newAutomationName = ""; _creationStatus = "The unsaved name was discarded."; Changed(); return true; }));
            return Task.CompletedTask;
        }
        var page = _currentPage ?? throw new InvalidOperationException("Read the protected saved library first.");
        CreationTarget? target = null;
        if (action == "recover-creation")
        {
            target = parameter as CreationTarget;
            if (target is null || !ReferenceEquals(target.Owner, this) || target.Epoch != _epoch || !ReferenceEquals(target.Page, page) ||
                !_creationRows.Any(row => ReferenceEquals(row.Target, target)))
                throw new InvalidOperationException("Refresh the actual original creation list before selecting this creation.");
        }
        else if (parameter is not null) throw new ArgumentException("This creation action does not accept a target.");
        var epoch = _epoch; var capturedName = _newAutomationName.Trim(); var command = new Command(this); var retained = new CreationPresentation();
        lock (_gate)
        {
            _commands.RemoveAll(value => value.Driver?.IsCompletedSuccessfully == true && value.IsIndependentlyJoined);
            _creationPresentations.RemoveAll(value => value.CloseJoined && value.Close?.IsCompletedSuccessfully == true);
            if (_commands.Count >= 128 || _creationPresentations.Count >= 128) throw new InvalidOperationException("Keep unresolved original creation observations for inspection.");
            _commands.Add(command); if (action != "refresh-creations") _creationPresentations.Add(retained);
        }
        _busy = true;
        if (action != "refresh-creations") _creationSubmitted = true;
        _creationStatus = action == "refresh-creations" ? "Reading actual Home creation publications…" :
            "Waiting for the original creation's individual Home review. Creating a Draft does not run it.";
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
                        if (!ReferenceEquals(_currentPage, page) || !_source!.IsIssuedOriginalObservation(page) || page.Actor != actor)
                            throw new UnauthorizedAccessException("The same protected library page and current Home actor are required.");
                    });
                    await command.Read(() => _source!.RevalidateOriginalObservationWithinSourceAsync(page, actor,
                        command.Run, command.Retain, CancellationToken.None));
                    if (action == "refresh-creations")
                    {
                        var choices = await command.Read(() => _creationSource!.ReadOriginalCreationRecoveryWithinSourceAsync(page,
                            command.Run, command.Retain, CancellationToken.None));
                        command.Run(() =>
                        {
                            if (choices.Any(choice => !_creationSource!.IsIssuedOriginalCreationRecovery(choice)))
                                throw new UnauthorizedAccessException("The actual producer did not issue these original creation selections.");
                        });
                        await command.Join();
                        if (!_work.IsRetiring && epoch == _epoch)
                        {
                            _creationRows = Array.AsReadOnly(choices.Select((choice, index) => new CreationRow("creation-" + epoch + "-" + index,
                                choice.Name, $"Automation {choice.AutomationId:D} · Graph {choice.GraphId:D}", new(this, epoch, page, choice))).ToArray());
                            _creationStatus = choices.Count == 0 ? "No original creation publications were found for this protected store and Home profile."
                                : "Select an original creation to check its exact saved receipt or recover its disabled definition. Recovery uses fresh Home approval.";
                        }
                        return;
                    }
                    Task<ICanonicalAutomationDefinitionOriginalCreationObservation> Acquire()
                    {
                        if (target is not null)
                        {
                            if (!_creationSource!.IsIssuedOriginalCreationRecovery(target.Selection))
                                throw new UnauthorizedAccessException("The original creation selection is no longer issued.");
                            return _creationSource.RecoverOriginalCreationWithinSourceAsync(page, target.Selection,
                                command.Run, command.Retain, CancellationToken.None);
                        }
                        return _creationSource!.StartOriginalCreationWithinSourceAsync(_creationReservation!, command.Run, command.Retain, CancellationToken.None);
                    }
                    if (target is null && _creationReservation is null)
                    {
                        _creationReservation = await command.Read(() => _creationSource!.CaptureOriginalCreationWithinSourceAsync(page, capturedName,
                            command.Run, command.Retain, CancellationToken.None));
                        command.Run(() =>
                        {
                            if (!_creationSource!.IsIssuedOriginalCreationReservation(_creationReservation) || _creationReservation.Actor != actor ||
                                _creationReservation.Name != capturedName || _creationReservation.OriginalStoreIdentity != page.OriginalStoreIdentity)
                                throw new UnauthorizedAccessException("The same actual source did not issue this exact new creation reservation.");
                        });
                    }
                    var observation = await command.Read(Acquire, actual =>
                    {
                        retained.Observation = actual;
                        command.Run(() =>
                        {
                            if (!_creationSource!.IsIssuedOriginalCreationObservation(actual) ||
                                actual.AutomationId != (target?.Selection.AutomationId ?? _creationReservation!.AutomationId) ||
                                actual.GraphId != (target?.Selection.GraphId ?? _creationReservation!.GraphId))
                                throw new UnauthorizedAccessException("The configured actual producer did not issue this creation delivery.");
                            retained.IssuerVerified = true; if (_retiring) actual.RequestOriginalRetirement();
                        });
                    });
                    var completed = await command.Read(() => observation.WaitOriginalCompletionAsync(CancellationToken.None));
                    command.Run(() =>
                    {
                        if (!observation.IsIssuedOriginalCompletion(completed) || !ReferenceEquals(completed.OriginalObservation, observation) ||
                            completed.AutomationId != observation.AutomationId || completed.GraphId != observation.GraphId || !Enum.IsDefined(completed.Kind))
                            throw new UnauthorizedAccessException("The same creation observation did not issue this exact typed completion.");
                    });
                    await command.Read(() => retained.Close ??= observation.CloseAndDrainOriginalAsync()); retained.CloseJoined = true;
                    await command.Join();
                    if (!_work.IsRetiring && epoch == _epoch)
                    {
                        _creationStatus = completed.Message + $" · Automation {completed.AutomationId:D} · Graph {completed.GraphId:D}";
                        _currentPage = null; _creationRows = [];
                        if (completed.Kind is CanonicalAutomationCreationCompletionKind.CreatedDisabled or CanonicalAutomationCreationCompletionKind.DeclinedBeforeEffect)
                        { _creationSubmitted = false; _creationReservation = null; _newAutomationName = ""; }
                        _status = "Refresh the saved library to observe current definitions and original creations.";
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause); _currentPage = null;
                    if (!_work.IsRetiring) _creationStatus = "This original creation needs inspection. Its IDs and business operation are retained. Refresh the library, then check original creations; no success was inferred.";
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
            if (command.Driver is null) { lock (_gate) { _commands.Remove(command); _creationPresentations.Remove(retained); } _busy = false; }
            throw;
        }
    }
    private async Task RetireLibraryDeliveriesAsync()
    {
        var failures = new List<Exception>();
        try { await RetireChangeDeliveriesAsync(); } catch (Exception cause) { AddChangeCause(failures, cause); }
        CreationPresentation[] all; lock (_gate) all = _creationPresentations.ToArray();
        foreach (var retained in all)
            if (retained.Observation is { } observation)
                try { _work.RunCloseCallback(() =>
                {
                    if (!retained.IssuerVerified || !_creationSource!.IsIssuedOriginalCreationObservation(observation))
                        throw new UnauthorizedAccessException("An unverified creation delivery remains retained for inspection.");
                    observation.RequestOriginalRetirement();
                }); } catch (Exception cause) { AddChangeCause(failures, cause); }
        ThrowCreationCauses(failures);
    }
    private async Task JoinLibraryDeliveriesAsync()
    {
        var failures = new List<Exception>();
        try { await JoinChangeDeliveriesAsync(); } catch (Exception cause) { AddChangeCause(failures, cause); }
        CreationPresentation[] all; lock (_gate) all = _creationPresentations.ToArray();
        foreach (var retained in all)
            if (retained.Observation is { } observation)
            {
                try { _work.RunCloseCallback(() =>
                {
                    if (!retained.IssuerVerified || !_creationSource!.IsIssuedOriginalCreationObservation(observation))
                        throw new UnauthorizedAccessException("An unverified creation delivery remains retained for inspection.");
                    observation.RequestOriginalRetirement(); retained.Close ??= observation.CloseAndDrainOriginalAsync();
                }); } catch (Exception cause) { AddChangeCause(failures, cause); }
                if (retained.Close is { } close)
                    try { await close; retained.CloseJoined = true; } catch (Exception cause) { AddChangeCause(failures, close.Exception ?? cause); }
            }
        ThrowCreationCauses(failures);
    }
    private static void ThrowCreationCauses(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual creation observations did not settle successfully.", failures);
    }
}
#endif
