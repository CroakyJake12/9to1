#if !ANDROID
using System.Globalization;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Automations;

/// <summary>Read presentation of source-issued lifecycle facts. It owns no run
/// state, scheduler, payload-redaction policy or independent storage.</summary>
internal sealed partial class OriginalAutomationLibraryBindings
{
    private const int RunPageSize = 16;
    private sealed record RunRow(string Key, string Identity, string Status, string Timing,
        string Revision, string Content);
    private ICanonicalAutomationRunOriginalReadSource? _runSource => _source as ICanonicalAutomationRunOriginalReadSource;
    private ICanonicalAutomationRunOriginalObservation? _runObservation;
    private ICanonicalAutomationRunOriginalContinuation? _runNext;
    private RowTarget? _runSelection;
    private IReadOnlyList<RunRow> _runRows = [];
    private string _runStatus = "Open a saved Automation, then refresh its run history.";
    private int _runPageNumber;
    private bool HasCurrentRunPage => _selectedTarget is not null && IsCurrentSelection && ReferenceEquals(_runSelection, _selectedTarget);
    private bool CanReadRunHistory => !_retiring && !_busy && !HasUnsubmittedEdits && IsCurrentSelection && _runSource is not null;
    private void ClearRunHistory()
    {
        _runObservation = null; _runNext = null; _runSelection = null; _runRows = []; _runPageNumber = 0;
        _runStatus = "Open a saved Automation, then refresh its run history.";
    }
    private object? RunsValue(string path) => path switch
    {
        "HasRunHistorySource" => _runSource is not null, "RunHistoryRows" => HasCurrentRunPage ? _runRows : Array.Empty<RunRow>(),
        "RunHistoryStatus" => _runObservation is not null && !HasCurrentRunPage
            ? "Refresh the saved library and reopen an Automation to view its run history." : _runStatus,
        "CanRefreshRuns" => RunsActionAvailable("refresh-runs"),
        "CanOlderRuns" => RunsActionAvailable("older-runs"),
        "RunHistoryPageLabel" => !HasCurrentRunPage || _runPageNumber == 0 ? "Saved runs" : "Saved runs · page " + _runPageNumber,
        _ => null
    };
    private bool RunsActionAvailable(string action) => CanReadRunHistory && (action switch
    {
        "refresh-runs" => true,
        "older-runs" => ReferenceEquals(_runSelection, _selectedTarget) && _runNext is not null &&
            _runObservation is not null && _runSource!.IsIssuedOriginalRunObservation(_runObservation) &&
            _runSource.IsIssuedOriginalRunContinuation(_runNext),
        _ => false
    });
    private bool TryGetRunItemValue(object item, string path, out object? value)
    {
        value = HasCurrentRunPage && item is RunRow row && _runRows.Any(current => ReferenceEquals(current, row)) ? path switch
        {
            "Key" => row.Key, "Identity" => row.Identity, "Status" => row.Status,
            "Timing" => row.Timing, "Revision" => row.Revision, "Content" => row.Content, _ => null
        } : null;
        return value is not null;
    }
    private Task StartRuns(string action, object? parameter)
    {
        if (IsActionAvailable(action) != true || parameter is not null)
            throw new InvalidOperationException("Open the current saved Automation before reading its run history.");
        var target = DemandRow(_selectedTarget); var epoch = _epoch;
        var cursor = action == "older-runs" ? _runNext : null;
        var command = new Command(this);
        lock (_gate)
        {
            _commands.RemoveAll(actual => actual.Driver?.IsCompletedSuccessfully == true && actual.IsIndependentlyJoined);
            if (_commands.Count >= 128) throw new InvalidOperationException("Keep unresolved original history reads for inspection.");
            _commands.Add(command);
        }
        _busy = true; _runStatus = "Reading saved run history…";
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
                    var observed = await command.Read(() => _runSource!.ReadOriginalRunsWithinSourceAsync(target.Observation,
                        target.Row, actor, RunPageSize, command.Run, command.Retain, CancellationToken.None, cursor));
                    command.Run(() =>
                    {
                        if (!_runSource!.IsIssuedOriginalRunObservation(observed) || observed.Actor != actor ||
                            observed.OriginalStoreIdentity != target.Observation.OriginalStoreIdentity ||
                            observed.AutomationId != target.Row.Value.Id ||
                            observed.Runs.Any(run => run.AutomationId != target.Row.Value.Id) ||
                            observed.NextContinuation is { } next && !_runSource.IsIssuedOriginalRunContinuation(next))
                            throw new UnauthorizedAccessException("The same protected source must issue this selected Automation's run history.");
                    });
                    var current = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain,
                        CancellationToken.None).AsTask());
                    if (current != actor) throw new UnauthorizedAccessException("Your Home profile changed while reading run history.");
                    await command.Join();
                    if (!_work.IsRetiring && epoch == _epoch && ReferenceEquals(_selectedTarget, target))
                    {
                        command.Run(() => DemandActualRow(target, actor));
                        _runObservation = observed; _runSelection = target; _runNext = observed.NextContinuation;
                        _runPageNumber = action == "older-runs" ? _runPageNumber + 1 : 1;
                        _runRows = Array.AsReadOnly(observed.Runs.Select(run => new RunRow(run.RunId.ToString("D"),
                            "Run " + run.RunId.ToString("D") + " · Automation " + run.AutomationId.ToString("D"),
                            Enum.IsDefined(run.Status) ? run.Status.ToString() : "Unknown stored status " + (int)run.Status,
                            "Scheduled " + RunTime(run.ScheduledFor) + " · Started " + RunTime(run.StartedAt) +
                                " · Finished " + RunTime(run.CompletedAt),
                            "Stored run revision: " + (run.Revision?.ToString(CultureInfo.InvariantCulture) ?? "unavailable") +
                                " · Automation revision: " + (run.DefinitionRevision?.ToString(CultureInfo.InvariantCulture) ?? "unavailable") +
                                (run.PinnedGraph is { } graph ? $" · Graph {graph.GraphId:D} revision {graph.Revision}" : " · Pinned graph unavailable"),
                            "Result: " + (run.HasResult ? "saved" : "not recorded") + " · Error: " + (run.HasError ? "saved" : "not recorded") +
                                " · Execution details: " + Presence(run.HasExecutionDetails) + " · Continuation: " + Presence(run.HasContinuation))).ToArray());
                        _runStatus = observed.State == CanonicalAutomationLibraryReadState.SetupRequired ? observed.Detail :
                            observed.Runs.Count == 0 ? "No saved runs were found for this Automation in this page." :
                            $"Showing {observed.Runs.Count} saved run(s). Newest scheduled runs appear first. Refresh for current saved state.";
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause);
                    if (!_work.IsRetiring)
                    {
                        _runObservation = null; _runNext = null; _runRows = []; _runPageNumber = 0;
                        _runStatus = "Saved run history could not be read. Refresh the library and reopen this Automation to try again.";
                    }
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
            if (command.Driver is null) { lock (_gate) _commands.Remove(command); _busy = false; }
            throw;
        }
    }
    private static string Presence(bool? present) => present is null ? "unavailable" : present.Value ? "saved" : "not recorded";
    private static string RunTime(DateTimeOffset? time) => time?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        ?? "not recorded";
}
#endif
