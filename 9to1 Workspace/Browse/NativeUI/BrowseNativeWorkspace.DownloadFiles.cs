using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseNativeWorkspace : IBrowserOriginalDownloadRecordSource
{
    private readonly IBrowserOriginalDownloadFilesService? _originalDownloadFilesService;
    private readonly ConditionalWeakTable<IBrowserOriginalDownloadRecordObservation, DownloadRecordObservation> _issuedDownloadRecords = new();
    private string _downloadFilesStatus = "Connect Files to locate or keep completed downloads.";
    private Guid? _selectedDownloadId;
    private IBrowserOriginalDownloadContent? _selectedOriginalContent;
    private DownloadDestinationRow[] _downloadDestinations = [];
    private long _downloadDestinationEpoch;
    private DownloadRegistrationAttempt? _originalRegistrationAttempt;
    private readonly List<DownloadRegistrationAttempt> _registrationAttempts = [];
    private sealed class DownloadRecordObservation(BrowseNativeWorkspace owner, BrowserDownloadRecord record) : IBrowserOriginalDownloadRecordObservation
    { internal BrowseNativeWorkspace Owner { get; } = owner; public BrowserDownloadRecord OriginalRecord { get; } = record; }
    private sealed record DownloadDestinationTarget(BrowseNativeWorkspace Owner, long Epoch,
        IBrowserOriginalDownloadFilesDestination OriginalDestination);
    public sealed record DownloadDestinationRow(object Target, string Key, string Label);
    private sealed class DownloadRegistrationAttempt(IBrowserOriginalDownloadContent content,
        IBrowserOriginalDownloadFilesDestination destination, Guid operation)
    {
        public IBrowserOriginalDownloadContent Content { get; } = content;
        public IBrowserOriginalDownloadFilesDestination Destination { get; } = destination;
        public Guid Operation { get; } = operation;
        public IBrowserOriginalDownloadFilesRegistration? OriginalResult { get; set; }
        public Task<IBrowserOriginalDownloadFilesRegistration>? OriginalTask { get; set; }
        public bool ValidatedPending { get; set; }
        public bool ValidatedRegistered { get; set; }
    }
    private bool CanRetryOriginalDownloadRegistration => _originalDownloadFilesService is not null &&
        _originalRegistrationAttempt is { ValidatedPending: true } &&
        IsSelectedOriginalDownloadCurrent();
    private bool TryGetDownloadFilesBinding(string path, out object? value)
    {
        value = path switch
        {
            "DownloadDestinations" => _downloadDestinations,
            "DownloadDestinationPrompt" => _selectedDownloadId is not null && _downloadDestinations.Length > 0
                ? "Choose where to keep this download in Files." : string.Empty,
            _ => null
        };
        return value is not null;
    }
    private bool TryGetDownloadDestinationItem(object item, string path, out object? value)
    {
        value = null;
        if (item is DownloadDestinationRow row && _downloadDestinations.Any(current => ReferenceEquals(current, row)))
            value = path switch { "Key" => row.Key, "Label" => row.Label, "Target" => row.Target, _ => null };
        return value is not null;
    }
    private bool ValidateOriginalDownloadAdmission(string command, object? parameter) => command switch
    {
        "9to1.Browse.ShowDownloadInFiles" => IsCurrentSavedTarget(parameter, SavedEntryKind.Download),
        "9to1.Browse.RegisterDownloadDestination" => IsCurrentDownloadDestination(parameter),
        "9to1.Browse.RetryDownloadRegistration" => CanRetryOriginalDownloadRegistration,
        _ => true
    };
    private bool IsCurrentDownloadDestination(object? parameter) => parameter is DownloadDestinationTarget target &&
        ReferenceEquals(target.Owner, this) && target.Epoch == _downloadDestinationEpoch &&
        _downloadDestinations.Any(row => ReferenceEquals(row.Target, target)) && IsSelectedOriginalDownloadCurrent() &&
        WithinOriginalFiles(() => _originalDownloadFilesService?.IsIssuedOriginalDestination(target.OriginalDestination) == true);
    private bool IsSelectedOriginalDownloadCurrent() => WithinOriginalFiles(() =>
        _selectedDownloadId is { } id && _chrome.State.Downloads.Items.Count(record => record.Id == id) == 1 &&
        _selectedOriginalContent is { } content && content.OriginalRecord.Id == id &&
        _originalDownloadFilesService?.OriginalContentOwner.IsIssuedOriginalContent(content) == true);
    private Task<T> CaptureOriginalFiles<T>(Func<Task<T>> acquire)
    {
        lock (_sourceGate) CheckAdmission(_originalOperations);
        var original = WithinOriginalFiles(acquire) ?? throw new InvalidOperationException("Files did not issue its original source Task.");
        lock (_sourceGate) Retain(_originalOperations, original); return original;
    }
    private Task CaptureOriginalFiles(Func<Task> acquire)
    {
        lock (_sourceGate) CheckAdmission(_originalOperations);
        var original = WithinOriginalFiles(acquire) ?? throw new InvalidOperationException("Files did not issue its original source Task.");
        lock (_sourceGate) Retain(_originalOperations, original); return original;
    }
    private void RetainOriginalFiles(Task original) { lock (_sourceGate) Retain(_originalOperations, original); }
    private async Task ShowOriginalDownloadInFilesAsync(object parameter, CancellationToken token)
    {
        if (!IsCurrentSavedTarget(parameter, SavedEntryKind.Download)) throw new InvalidOperationException("Select the current completed-download row.");
        var actual = _chrome.State.Downloads.Items.Single(record => record.Id == ((SavedEntrySelection)parameter).Id);
        var observation = new DownloadRecordObservation(this, actual); _issuedDownloadRecords.Add(observation, observation);
        var service = _originalDownloadFilesService!;
        if (_originalRegistrationAttempt is { ValidatedRegistered: false } pending)
        {
            _downloadFilesStatus = pending.ValidatedPending
                ? "Continue the current Files approval before choosing another download."
                : "The original Files registration has an unresolved failure. Its source is retained; recover this same transfer before starting another registration.";
            return;
        }
        var registration = await CaptureOriginalFiles(() => service.ReadOriginalRegistrationWithinSourceAsync(this,
            observation, ScopeOriginalFiles, RetainOriginalFiles, token));
        if (registration is not null)
        {
            if (!WithinOriginalFiles(() => service.IsIssuedOriginalRegistration(registration)) || registration.OriginalRecord.Id != actual.Id ||
                registration.State != BrowserOriginalDownloadFilesRegistrationState.Registered)
                throw new InvalidOperationException("Files returned a foreign completed-download registration.");
            await CaptureOriginalFiles(() => service.RevealOriginalRegistrationWithinSourceAsync(registration,
                ScopeOriginalFiles, RetainOriginalFiles, token));
            _downloadFilesStatus = "Opened this download in Files."; return;
        }
        var contents = _chrome.State.Tabs.Select(tab => _chrome.ObserveOriginalEngine(tab.Id))
            .OfType<IBrowseOriginalDownloadContentSource>().Select(source => WithinOriginalFiles(() => source.ObserveOriginalDownloadContent(actual)))
            .Where(content => content is not null).Distinct(ReferenceEqualityComparer.Instance).ToArray();
        if (contents.Length != 1 || !WithinOriginalFiles(() => service.OriginalContentOwner.IsIssuedOriginalContent(contents[0]!)))
        { _downloadFilesStatus = "This download has no active original transfer or Files registration. Recover its browser transfer to keep it in Files."; return; }
        _selectedDownloadId = actual.Id; _selectedOriginalContent = contents[0]!;
        _originalRegistrationAttempt = null;
        var catalogue = await CaptureOriginalFiles(() => service.ReadOriginalDestinationsWithinSourceAsync(
            _selectedOriginalContent, ScopeOriginalFiles, RetainOriginalFiles, token));
        if (!IsSelectedOriginalDownloadCurrent()) throw new InvalidOperationException("The original completed download changed before Files destination publication.");
        var epoch = checked(++_downloadDestinationEpoch);
        _downloadDestinations = catalogue.Destinations.Select((destination, index) =>
        {
            if (!WithinOriginalFiles(() => service.IsIssuedOriginalDestination(destination))) throw new InvalidOperationException("Files returned a foreign destination.");
            return new DownloadDestinationRow(new DownloadDestinationTarget(this, epoch, destination),
                epoch + ":" + index, destination.DisplayName);
        }).ToArray();
        _downloadFilesStatus = catalogue.Detail;
    }
    private async Task RegisterOriginalDownloadDestinationAsync(object parameter, CancellationToken token)
    {
        if (!IsCurrentDownloadDestination(parameter)) throw new InvalidOperationException("Choose the current Files destination.");
        var destination = ((DownloadDestinationTarget)parameter).OriginalDestination;
        if (_originalRegistrationAttempt is { ValidatedRegistered: false })
        { _downloadFilesStatus = "Finish or recover the current Files registration before selecting another folder."; return; }
        _registrationAttempts.RemoveAll(attempt => attempt.ValidatedRegistered && attempt.OriginalTask?.IsCompletedSuccessfully == true);
        if (_registrationAttempts.Count >= 64) throw new InvalidOperationException("Browse retains unresolved original Files registrations. Retire this same owner before continuing.");
        _originalRegistrationAttempt = new(_selectedOriginalContent!, destination, Guid.NewGuid());
        _registrationAttempts.Add(_originalRegistrationAttempt);
        await PublishOriginalDownloadRegistrationAsync(_originalRegistrationAttempt, token);
    }
    private async Task RetryOriginalDownloadRegistrationAsync(CancellationToken token)
    {
        if (!CanRetryOriginalDownloadRegistration) throw new InvalidOperationException("The current Files approval is unavailable.");
        await PublishOriginalDownloadRegistrationAsync(_originalRegistrationAttempt!, token);
    }
    private async Task PublishOriginalDownloadRegistrationAsync(DownloadRegistrationAttempt attempt, CancellationToken token)
    {
        var service = _originalDownloadFilesService!;
        await CaptureOriginalFiles(() => service.RevalidateOriginalDestinationWithinSourceAsync(attempt.Destination,
            ScopeOriginalFiles, RetainOriginalFiles, token));
        if (!IsSelectedOriginalDownloadCurrent()) throw new InvalidOperationException("The original completed source is no longer current.");
        attempt.ValidatedPending = false;
        attempt.OriginalTask = CaptureOriginalFiles(() => service.RegisterOriginalDownloadWithinSourceAsync(attempt.Content,
            attempt.Destination, attempt.Operation, ScopeOriginalFiles, RetainOriginalFiles, token));
        var result = await attempt.OriginalTask;
        attempt.OriginalResult = result; // Retain even foreign/invalid observations.
        var observation = WithinOriginalFiles(() =>
        {
            if (!service.IsIssuedOriginalRegistration(result) || result.OriginalOperationId != attempt.Operation ||
                result.OriginalRecord.Id != attempt.Content.OriginalRecord.Id)
                throw new InvalidOperationException("Files returned a foreign registration result.");
            return (result.State, result.Detail);
        });
        _downloadFilesStatus = observation.Detail;
        if (observation.State == BrowserOriginalDownloadFilesRegistrationState.Registered)
        {
            attempt.ValidatedRegistered = true;
            await CaptureOriginalFiles(() => service.RevealOriginalRegistrationWithinSourceAsync(result,
                ScopeOriginalFiles, RetainOriginalFiles, token));
            _downloadDestinations = []; _downloadDestinationEpoch++;
        }
        else if (observation.State == BrowserOriginalDownloadFilesRegistrationState.AwaitingApproval) attempt.ValidatedPending = true;
        else
            throw new InvalidOperationException("Files returned an unknown registration state.");
    }
    public bool IsIssuedOriginalDownloadRecord(IBrowserOriginalDownloadRecordObservation originalRecord) =>
        _issuedDownloadRecords.TryGetValue(originalRecord, out var actual) && ReferenceEquals(originalRecord, actual) && ReferenceEquals(actual.Owner, this);
    public Task RevalidateOriginalDownloadRecordWithinSourceAsync(IBrowserOriginalDownloadRecordObservation originalRecord,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        if (_retiring || _disposed) throw new InvalidOperationException("The original browser record source is retiring.");
        if (!IsIssuedOriginalDownloadRecord(originalRecord)) throw new UnauthorizedAccessException("Use the SAME current browser ledger observation.");
        lock (_sourceGate) CheckAdmission(_originalOperations);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = RevalidateOriginalRecordCoreAsync(start.Task, originalRecord, scope, retain, token);
        RetainOriginalFiles(original);
        try { WithinOriginalFiles(() => { retain(original); return true; }); start.SetResult(); }
        catch (Exception failure) { start.TrySetException(failure); }
        return original;
    }
    private async Task RevalidateOriginalRecordCoreAsync(Task start, IBrowserOriginalDownloadRecordObservation originalRecord,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var prior = LogicalFiles.Value; var driver = new FilesInvocation(this, prior); LogicalFiles.Value = driver;
        try
        {
            await start.ConfigureAwait(false);
            Task? originalUi = null;
            var failures = new List<Exception>();
            try
            {
                RunFiniteOriginalFilesScope(scope, () =>
                {
                    originalUi = Dispatcher.UIThread.InvokeAsync(() => WithinOriginalFiles(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        var current = _chrome.State.Downloads.Items.Where(record => record.Id == originalRecord.OriginalRecord.Id).ToArray();
                        if (_retiring || _disposed || !IsIssuedOriginalDownloadRecord(originalRecord) ||
                            current.Length != 1 || current[0] != originalRecord.OriginalRecord)
                            throw new InvalidOperationException("The canonical download ledger observation changed.");
                        return true;
                    })).GetTask();
                    RetainOriginalFiles(originalUi); retain(originalUi);
                });
            }
            catch (Exception failure) { failures.Add(failure); }
            // A postcallback scope/retainer failure cannot finish this finite
            // source before the SAME already-issued actual UI child settles.
            if (originalUi is not null)
                try { await originalUi.ConfigureAwait(false); }
                catch (Exception failure) { failures.Add(failure); }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Browse retains original record scope and actual UI failures.", failures);
        }
        finally { driver.Active = false; LogicalFiles.Value = prior; }
    }
    private void RunFiniteOriginalFilesScope(Action<Action> scope, Action body)
    {
        var caller = Environment.CurrentManagedThreadId; var active = true; var entered = false;
        Exception? bodyFailure = null, scopeFailure = null;
        try
        {
            WithinOriginalFiles(() =>
            {
                scope(() =>
                {
                    try
                    {
                        if (!active || entered || Environment.CurrentManagedThreadId != caller)
                            throw new InvalidOperationException("The original Files source scope must run once synchronously on its caller thread.");
                        entered = true; WithinOriginalFiles(() => { body(); return true; });
                    }
                    catch (Exception failure) { bodyFailure ??= failure; throw; }
                });
                return true;
            });
        }
        catch (Exception failure) { scopeFailure = failure; }
        finally { active = false; }
        if (!entered && scopeFailure is null) scopeFailure = new InvalidOperationException("Files did not enter its original source scope.");
        if (bodyFailure is not null && scopeFailure is not null && !ReferenceEquals(bodyFailure, scopeFailure))
            throw new AggregateException("Browse retains the original Files scope and callback failures.", bodyFailure, scopeFailure);
        var actual = bodyFailure ?? scopeFailure;
        if (actual is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual).Throw();
    }
    private static readonly AsyncLocal<FilesInvocation?> LogicalFiles = new();
    [ThreadStatic] private static FilesInvocation? PhysicalFiles;
    private sealed class FilesInvocation(BrowseNativeWorkspace owner, FilesInvocation? parent)
    { public BrowseNativeWorkspace Owner { get; } = owner; public FilesInvocation? Parent { get; } = parent; public volatile bool Active = true; }
    private T WithinOriginalFiles<T>(Func<T> callback)
    {
        var prior = PhysicalFiles; var invocation = new FilesInvocation(this, prior); PhysicalFiles = invocation;
        try { return callback(); } finally { invocation.Active = false; PhysicalFiles = prior; }
    }
    private void ScopeOriginalFiles(Action callback) => WithinOriginalFiles(() => { callback(); return true; });
    internal void DemandOriginalFilesExternalJoin()
    {
        static bool Contains(FilesInvocation? invocation, BrowseNativeWorkspace owner)
        {
            for (; invocation is not null; invocation = invocation.Parent)
                if (invocation.Active && ReferenceEquals(invocation.Owner, owner)) return true;
            return false;
        }
        if (Contains(PhysicalFiles, this) || Contains(LogicalFiles.Value, this) || ReferenceEquals(LogicalDriver.Value, this))
            throw new InvalidOperationException("The original Files callback cannot join its own browser retirement.");
    }
}
