#if !ANDROID
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Automations;

/// <summary>Read presentation over the configured protected producer. This view
/// owns its finite observations; Home, the store and schedulers remain borrowed.</summary>
internal sealed partial class OriginalAutomationLibraryBindings : ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, ICuiRepeatItemBindingContext,
    INotifyPropertyChanged, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    internal sealed record LibraryRow(string Key, string Name, string Status, string Schedule, object Target);
    private sealed record RowTarget(OriginalAutomationLibraryBindings Owner, long Epoch,
        ICanonicalAutomationLibraryOriginalObservation Observation, AutomationOwnerRead<AutomationDefinition> Row);
    private readonly ICanonicalAutomationLibraryOriginalReadSource? _source;
    private readonly HomeLocalProfileIdentity? _profiles;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    private readonly List<Command> _commands = [];
    private readonly HashSet<object> _issued = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<LibraryRow> _rows = [];
    private ICanonicalAutomationLibraryOriginalContinuation? _next;
    private string _search = "", _status, _selectedName = "Select a saved automation", _instruction = "",
        _selectedSchedule = "", _selectedState = "", _review = "";
    private long _epoch;
    private bool _busy, _includeArchived, _retiring;
    private int _pageNumber;
    internal Task? OriginalInitialization { get; private set; }
    internal Task? OriginalCommand { get; private set; }
    internal Task? OriginalClose => _work.OriginalClose;
    internal ICanonicalAutomationLibraryOriginalReadSource? OriginalSource => _source;
    internal HomeLocalProfileIdentity? OriginalProfiles => _profiles;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal OriginalAutomationLibraryBindings(ICanonicalAutomationLibraryOriginalReadSource? actualSource,
        HomeLocalProfileIdentity? actualProfiles, ICanonicalAutomationDefinitionOriginalProcessSource? actualWriter = null)
    {
        if ((actualSource is null) != (actualProfiles is null))
            throw new ArgumentException("Retain the configured library source and its actual Home profile together.");
        _source = actualSource; _profiles = actualProfiles; _writer = actualWriter;
        if (actualWriter is not null && actualSource is null) throw new ArgumentException("The actual writer requires its configured saved library.");
        _status = actualSource is null ? "Open Automations from Home to view your saved library."
            : "Your saved automations will appear here.";
        _work = new(RetireChangeDeliveriesAsync, JoinChangeDeliveriesAsync);
    }
    internal Task InitializeAsync() => OriginalInitialization ??= _source is null ? Task.CompletedTask : Start("refresh", null);

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Search" => _search, "Status" => _status, "Rows" => _rows,
            "HasRows" => _rows.Count != 0, "Empty" => !_busy && _rows.Count == 0,
            "Busy" => _busy, "CanRead" => _source is not null && !_retiring,
            "CanRefresh" => IsActionAvailable("refresh") == true,
            "CanOlder" => IsActionAvailable("older") == true,
            "ArchiveLabel" => _includeArchived ? "Hide archived" : "Show archived",
            "PageLabel" => _pageNumber == 0 ? "Saved library" : "Saved library · page " + _pageNumber,
            "SelectedName" => _selectedName, "Instruction" => _instruction,
            "SelectedSchedule" => _selectedSchedule, "SelectedState" => _selectedState,
            "Review" => _review, "CanRecover" => IsActionAvailable("recover") == true,
            "CanDisable" => IsActionAvailable("disable") == true, "ChangeStatus" => _changeStatus, _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_retiring || _busy || path != "Search" || value is not string text || text.Length > 256) return false;
        if (_search == text) return true;
        _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            _search = text; _epoch++; _issued.Clear(); _rows = []; _next = null; _pageNumber = 0;
            ClearSelection(); Changed(); return true;
        }));
        return true;
    }
    public bool? IsActionAvailable(string command) => !_retiring && !_busy && _source is not null && (command switch
    {
        "refresh" or "archives" => true,
        "older" => _next is not null,
        "select" => _rows.Count != 0,
        "recover" or "disable" => _writer is not null && _selectedTarget is { } current &&
            current.Epoch == _epoch && _issued.Contains(current),
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(command is "recover" or "disable" ? StartChange(command, parameter) : Start(command, parameter));
    }
    private Task Start(string action, object? parameter)
    {
        RowTarget? target = null;
        if (IsActionAvailable(action) != true) throw new InvalidOperationException("This saved-library action is unavailable.");
        if (action == "select") target = DemandRow(parameter);
        else if (parameter is not null) throw new ArgumentException("This library action does not accept a saved row.");
        var search = _search; var archived = action == "archives" ? !_includeArchived : _includeArchived;
        var cursor = action == "older" ? _next : null; var epoch = _epoch;
        var command = new Command(this);
        lock (_gate)
        {
            _commands.RemoveAll(actual => actual.Driver?.IsCompletedSuccessfully == true && actual.IsIndependentlyJoined);
            if (_commands.Count >= 128)
                throw new InvalidOperationException("The saved-library observations require inspection before more work.");
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
                    var actor = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain,
                        CancellationToken.None).AsTask()) ?? throw new UnauthorizedAccessException("Your current Home profile is unavailable.");
                    if (target is not null)
                    {
                        command.Run(() => DemandActualRow(target, actor));
                        await command.Read(() => _source!.RevalidateOriginalObservationWithinSourceAsync(
                            target.Observation, actor, command.Run, command.Retain, CancellationToken.None));
                        var current = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain,
                            CancellationToken.None).AsTask());
                        if (current != actor) throw new UnauthorizedAccessException("Your Home profile changed while opening this saved automation.");
                        await command.Join();
                        if (!_work.IsRetiring && epoch == _epoch)
                        {
                            command.Run(() => DemandActualRow(target, actor));
                            var saved = target.Row.Value;
                            _selectedTarget = target; _selectedName = saved.Name; _instruction = saved.Instruction;
                            _selectedSchedule = saved.ScheduleKind + " · " + saved.ScheduleJson;
                            _selectedState = DescribeState(saved);
                            _review = target.Row.RequiresRecovery
                                ? "Review is needed before this saved automation can be edited or run here. Its saved settings are preserved."
                                : "This view shows saved settings. Editing and running require their own approval.";
                            _status = "Saved automation opened.";
                        }
                    }
                    else
                    {
                        var page = await command.Read(() => _source!.ReadOriginalLibraryWithinSourceAsync(actor,
                            new(IncludeArchived: archived, Search: search, Limit: 32), command.Run, command.Retain,
                            CancellationToken.None, cursor));
                        command.Run(() =>
                        {
                            if (!_source!.IsIssuedOriginalObservation(page) || page.Actor != actor ||
                                page.NextContinuation is { } next && !_source.IsIssuedOriginalContinuation(next))
                                throw new UnauthorizedAccessException("The configured saved library did not issue this page.");
                        });
                        var current = await command.Read(() => _profiles!.GetCurrentAsync(command.Run, command.Retain,
                            CancellationToken.None).AsTask());
                        if (current != actor) throw new UnauthorizedAccessException("Your Home profile changed while reading the saved library.");
                        await command.Join();
                        if (!_work.IsRetiring && epoch == _epoch && search == _search)
                        {
                            _epoch++; _issued.Clear(); _includeArchived = archived; _next = page.NextContinuation;
                            _pageNumber = action == "older" ? _pageNumber + 1 : 1;
                            _rows = Array.AsReadOnly(page.Definitions.Select((row, index) =>
                            {
                                var selected = new RowTarget(this, _epoch, page, row); _issued.Add(selected);
                                return new LibraryRow("saved-" + _epoch + "-" + index, row.Value.Name,
                                    DescribeState(row.Value) + (row.RequiresRecovery ? " · review needed" : ""),
                                    row.Value.ScheduleKind.ToString(), selected);
                            }).ToArray());
                            ClearSelection();
                            _status = page.State == CanonicalAutomationLibraryReadState.SetupRequired
                                ? "Allow this saved store in Home before viewing its automations. No saved settings have changed."
                                : _rows.Count == 0 ? "No saved automations match this search."
                                : _rows.Count + " saved automation" + (_rows.Count == 1 ? "" : "s") + ". Select one to view its settings.";
                        }
                    }
                }
                catch (Exception cause)
                {
                    original.Retain(cause);
                    if (!_work.IsRetiring) _status = "The saved library could not be read. Refresh to check the current list.";
                    throw;
                }
                finally
                {
                    try { await command.Join(); } finally
                    { _busy = false; if (!_work.IsRetiring) Changed(); }
                }
            }, actual => { command.Driver = actual; OriginalCommand = actual; });
        }
        catch
        {
            if (command.Driver is null) { lock (_gate) _commands.Remove(command); _busy = false; }
            throw;
        }
    }
    private RowTarget DemandRow(object? value) => value is RowTarget target &&
        ReferenceEquals(target.Owner, this) && target.Epoch == _epoch && _issued.Contains(target)
        ? target : throw new InvalidOperationException("Refresh the saved library before using this older row.");
    private void DemandActualRow(RowTarget target, AuthenticatedResourceActor actor)
    {
        if (!ReferenceEquals(DemandRow(target), target) || !_source!.IsIssuedOriginalObservation(target.Observation) ||
            target.Observation.Actor != actor || !target.Observation.Definitions.Any(row => ReferenceEquals(row, target.Row)))
            throw new UnauthorizedAccessException("The actual source-issued saved row/current profile is required.");
    }
    private static string DescribeState(AutomationDefinition definition) => definition.ArchivedAt is not null
        ? "Archived" : definition.IsEnabled ? "Enabled in saved settings" : "Disabled in saved settings";
    private void ClearSelection()
    { _selectedTarget = null; _selectedName = "Select a saved automation"; _instruction = _selectedSchedule = _selectedState = _review = ""; }
    private void Changed() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
    { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); return true; });
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = item is LibraryRow row && _rows.Any(current => ReferenceEquals(current, row)) ? path switch
        { "Key" => row.Key, "Name" => row.Name, "Status" => row.Status, "Schedule" => row.Schedule, "Target" => row.Target,
          "CanOpen" => !_retiring && !_busy && row.Target is RowTarget target &&
              target.Epoch == _epoch && _issued.Contains(target), _ => null } : null;
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public void DemandExternalOriginalRetirementJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); }
    public void RequestRetirement() { _retiring = true; _work.RequestRetirement(); }
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); _retiring = true; return _work.CloseAndDrainAsync(); }

    private sealed class Command(OriginalAutomationLibraryBindings owner)
    {
        private readonly object _gate = new();
        private readonly Dictionary<Task, Task> _observations = new(ReferenceEqualityComparer.Instance);
        private DesktopOriginalWorkLifetime.Original _original = null!;
        internal Task? Driver;
        private bool _joined;
        internal bool IsIndependentlyJoined { get { lock (_gate) return _joined; } }
        internal void Bind(DesktopOriginalWorkLifetime.Original actual) => _original = actual;
        internal void Run(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
        {
            try { body(); return true; }
            catch (Exception cause) { _original.Retain(cause); throw; }
        });
        internal void Retain(Task actual)
        {
            lock (_gate)
            {
                if (_observations.ContainsKey(actual)) return;
                _joined = false;
                foreach (var successful in _observations.Where(pair => pair.Value.IsCompletedSuccessfully).Select(pair => pair.Key).ToArray())
                    _observations.Remove(successful); // Each observer has independently awaited its SAME raw original.
                _observations.Add(actual, _original.AwaitAsync(actual)); // Enrol before any capacity/postguard refusal.
                if (_observations.Count > 2048)
                {
                    var cause = new InvalidOperationException("Actual saved-library source custody requires inspection.");
                    _original.Retain(cause); throw cause;
                }
            }
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? captureActual = null)
        {
            Task<T>? actual = null; T result = default!; var causes = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); } catch (Exception cause) { causes.Add(cause); }
            if (actual is not null) try { result = await _original.AwaitAsync(actual); }
                catch (Exception cause) { _original.Capture(actual, cause); causes.Add(cause); }
            if (actual is { IsCompletedSuccessfully: true } && captureActual is not null)
                try { captureActual(result); } catch (Exception cause) { _original.Retain(cause); causes.Add(cause); }
            Throw(causes); return actual is null ? throw new InvalidOperationException("No actual library source Task was captured.") : result;
        }
        internal async Task Read(Func<Task> factory)
        {
            Task? actual = null; var causes = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); } catch (Exception cause) { causes.Add(cause); }
            if (actual is not null) try { await _original.AwaitAsync(actual); }
                catch (Exception cause) { _original.Capture(actual, cause); causes.Add(cause); }
            Throw(causes);
        }
        internal async Task Join()
        {
            Task[] observations; lock (_gate) observations = _observations.Values.ToArray();
            var failures = new List<Exception>();
            foreach (var actual in observations) try { await actual; } catch (Exception cause) { failures.Add(cause); }
            Throw(failures);
            lock (_gate)
                _joined = _observations.Values.All(actual => observations.Contains(actual, ReferenceEqualityComparer.Instance));
        }
        private static void Throw(List<Exception> causes)
        {
            if (causes.Count == 0) return;
            if (causes.Count == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw();
            throw new AggregateException("Actual library sources and callbacks failed.", causes);
        }
    }
}
#endif
