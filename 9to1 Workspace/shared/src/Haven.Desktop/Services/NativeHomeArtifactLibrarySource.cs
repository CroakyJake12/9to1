using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>Page-owned metadata publication over existing canonical owners, never a store.</summary>
internal interface IHomeOriginalLibrarySource : IHomeArtifactSearchIndex, IHomeArtifactAuthorization, IAsyncDisposable
{
    HomeLibraryService CreateService(IHomeDeepLinkRouter router);
    Task RevalidateAsync(CancellationToken token);
    bool IsExpectedRefusal(Exception error);
    void DemandExternalOriginalJoin();
    string? AvailabilityNotice { get; }
}

/// <summary>The existing shared Library index contract over a real reviewed Notes snapshot.
/// Notes owns every document, UUID, revision, read witness and one-use Home admission. This
/// adapter publishes Boards metadata only; it creates no index file, document copy or grant.</summary>
internal sealed class NativeHomeArtifactLibrarySource : IHomeOriginalLibrarySource
{
    private sealed record Entry(HomeArtifactReference View, NotesDocumentSummary Summary);
    private sealed record WriteEntry(HomeArtifactReference View, NotesDocumentSummary Summary);
    private sealed record Read(INotesOriginalLibraryReadRequest Request, HomeNotesLibraryReadAdmission Admission);
    private sealed record Operation(Task Task, CancellationToken Token);
    private readonly HomeNativeWindowsComposition _home;
    private readonly NotesRepository _notes;
    private readonly VerifiedNotesRepository _verified;
    private readonly HomeNotesResourceAdmissionSource _admission;
    private readonly AuthenticatedResourceActor _actor;
    private readonly string _session;
    private readonly Func<HomePermissionAuthorization, CancellationToken, Task> _present;
    private readonly Action<Action> _parent;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly Func<bool> _alive;
    private readonly bool _writeSelectionMode;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly List<Operation> _operations = [];
    private readonly HashSet<Exception> _refusals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WriteEntry> _writeEntries = new(StringComparer.Ordinal);
    private IBoardsNotebookSummarySource? _metadata;
    private HomeLibraryQuery? _query;
    private (INotesOriginalLibraryReadRequest Request, HomeNotesLibraryReadReview Review)? _pending;
    private Read? _auditPending;
    private bool _retiring;
    private Task? _close;
    private CloudflareOriginalTaskLedger? _currentOriginal;

    internal NativeHomeArtifactLibrarySource(HomeNativeWindowsComposition home, NotesRepository notes,
        VerifiedNotesRepository verified, HomeNotesResourceAdmissionSource admission,
        AuthenticatedResourceActor actor, string session,
        Func<HomePermissionAuthorization, CancellationToken, Task> present,
        Func<CancellationToken, Task> admit, Action<Action> parent, Func<bool> alive, bool writeSelectionMode = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session);
        _home = home; _notes = notes; _verified = verified; _admission = admission; _actor = actor;
        _session = session; _present = present; _admit = admit; _parent = parent; _alive = alive;
        _writeSelectionMode = writeSelectionMode;
        if (!admission.IsBoundToOriginalNotes(notes) ||
            !ReferenceEquals(home.Services.GetService(typeof(NotesRepository)), notes) ||
            !ReferenceEquals(home.Services.GetService(typeof(HomeNotesResourceAdmissionSource)), admission))
            throw new UnauthorizedAccessException("Library must borrow the SAME original Home, Notes and read-admission owners.");
    }

    public HomeLibraryService CreateService(IHomeDeepLinkRouter router) =>
        new(this, this, router, new HomeCoreLibraryPreferences(_home.StateStore));
    internal Task? OriginalClose { get { lock (_sync) return _close; } }
    public string? AvailabilityNotice => null;
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);


    public Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<HomeArtifactReference>>(async (originals, token) =>
        {
            if (query.Cursor is not null || query.Text?.Length > 256)
                throw Refuse("InvalidQuery", "Use a search of at most 256 characters and the current Library page.");
            var identity = query with { Offset = 0, Cursor = null };
            if (query.Offset == 0)
            {
                _entries.Clear(); _writeEntries.Clear(); _metadata = null; _query = null;
                var read = await ReadAsync(null, originals, token, _writeSelectionMode).ConfigureAwait(false);
                if (_writeSelectionMode)
                {
                    foreach (var metadata in read.Snapshot.Metadata.Where(value => !value.IsDeleted &&
                                 !StringComparer.Ordinal.Equals(value.Product, "boards")))
                    {
                        var row = metadata.Summary;
                        if (row.Id == Guid.Empty || row.Version < 1 || string.IsNullOrWhiteSpace(row.Title))
                            throw new InvalidDataException("The original Notes owner returned invalid Write metadata.");
                        var id = row.Id.ToString("D");
                        var reference = new HomeArtifactReference("write", id, row.Title, "Write document",
                            "Write library", _actor.ProfileId, null, row.UpdatedAt, false, false, false, false, [],
                            new(HomeRoute.Library, "write", id, "select", "write.document"));
                        if (!_writeEntries.TryAdd(id, new(reference, row)))
                            throw new InvalidDataException("The original Notes owner returned duplicate Write identities.");
                    }
                }
                else
                    _metadata = _notes.CreateOriginalBoardsSummarySource(read.Request, read.Snapshot, read.Admission);
                _query = identity;
            }
            else if (_query != identity || (!_writeSelectionMode && _metadata is null))
                throw Refuse("LibraryPageChanged", "Refresh Library before opening another page.");
            if (_writeSelectionMode)
            {
                await DemandCurrentAsync(originals, token).ConfigureAwait(false);
                var selectedText = query.Text?.Trim();
                return _writeEntries.Values.Select(value => value.View).Where(value => string.IsNullOrEmpty(selectedText) ||
                    value.DisplayName.Contains(selectedText, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            }
            var rows = await CurrentSummariesAsync(originals, token).ConfigureAwait(false);
            _entries.Clear();
            foreach (var row in rows)
            {
                if (row.Id == Guid.Empty || row.Version < 1 || string.IsNullOrWhiteSpace(row.Title))
                    throw new InvalidDataException("The original Notes owner returned invalid notebook metadata.");
                var id = row.Id.ToString("D");
                var reference = new HomeArtifactReference("boards", id, row.Title, "Boards notebook",
                    "Boards library", _actor.ProfileId, null, row.UpdatedAt, false, false, false, false, [],
                    new(HomeRoute.Library, "boards", id, "open", "boards.notebook"));
                if (!_entries.TryAdd(id, new(reference, row)))
                    throw new InvalidDataException("The original Notes owner returned duplicate notebook identities.");
            }
            await DemandCurrentAsync(originals, token).ConfigureAwait(false);
            var text = query.Text?.Trim();
            return _entries.Values.Select(value => value.View).Where(value => string.IsNullOrEmpty(text) ||
                value.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        }, cancellationToken);

    public Task<HomeArtifactReference?> GetByIdAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) =>
        RunAsync<HomeArtifactReference?>(async (originals, token) =>
        {
            if (_writeSelectionMode && ownerApp == "write" && _writeEntries.TryGetValue(artifactId, out var writeEntry))
            {
                await DemandCurrentAsync(originals, token).ConfigureAwait(false);
                return writeEntry.View;
            }
            if (ownerApp != "boards" || !_entries.TryGetValue(artifactId, out var entry)) return null;
            return await IsCurrentEntryAsync(entry, originals, token).ConfigureAwait(false) ? entry.View : null;
        }, cancellationToken);

    public Task<bool> CanReadAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) =>
        RunAsync(async (originals, token) =>
        {
            if (_writeSelectionMode && ownerApp == "write" && _writeEntries.ContainsKey(artifactId))
            { await DemandCurrentAsync(originals, token).ConfigureAwait(false); return true; }
            return ownerApp == "boards" && _entries.TryGetValue(artifactId, out var entry) &&
                await IsCurrentEntryAsync(entry, originals, token).ConfigureAwait(false);
        }, cancellationToken);

    internal Task<BoardEmbeddedArtifactReference?> SelectWriteDocumentAsync(HomeArtifactReference selected,
        CancellationToken cancellationToken = default) => RunAsync<BoardEmbeddedArtifactReference?>(async (originals, token) =>
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (!_writeSelectionMode || selected.OwnerApp != "write" ||
            !_writeEntries.TryGetValue(selected.ArtifactId, out var entry) ||
            !ReferenceEquals(selected.OpenLink, entry.View.OpenLink) || selected.ArtifactId != entry.Summary.Id.ToString("D"))
            throw new UnauthorizedAccessException("Choose a Write document from this exact reviewed Home Library page.");
        await DemandCurrentAsync(originals, token).ConfigureAwait(false);
        // This is a metadata reference only. The embedding host performs a fresh, exact
        // write.document.read review and rejects a changed version before showing content.
        var reference = new BoardEmbeddedArtifactReference("write", entry.Summary.Id,
            new BoardEmbeddedArtifactRevision(entry.Summary.Version, null));
        reference.Validate();
        return reference;
    }, cancellationToken);

    public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken cancellationToken) => RunAsync(async (originals, token) =>
    {
        if (link.OwnerApp != "boards" || link.TargetRouteId != "boards.notebook" ||
            !_entries.TryGetValue(link.ObjectId, out var entry) || !ReferenceEquals(link, entry.View.OpenLink) ||
            !await IsCurrentEntryAsync(entry, originals, token).ConfigureAwait(false)) return false;
        // A displayed row is never a content grant. The exact canonical document needs its
        // own fresh one-use Home review/read and verified Notes transaction before navigation.
        var read = await ReadAsync(entry.Summary.Id, originals, token).ConfigureAwait(false);
        var document = read.Snapshot.SelectedDocument;
        if (document is null || document.Id != entry.Summary.Id || document.Version != entry.Summary.Version ||
            !BoardsWorkspaceService.IsBoardsNotebook(document)) return false;
        return await IsCurrentEntryAsync(entry, originals, token).ConfigureAwait(false);
    }, cancellationToken);

    private async Task<bool> IsCurrentEntryAsync(Entry entry, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var rows = await CurrentSummariesAsync(originals, token).ConfigureAwait(false);
        var row = rows.SingleOrDefault(value => value.Id == entry.Summary.Id);
        await DemandCurrentAsync(originals, token).ConfigureAwait(false);
        return row is not null && row == entry.Summary;
    }
    private async Task<IReadOnlyList<NotesDocumentSummary>> CurrentSummariesAsync(CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var source = _metadata ?? throw Refuse("LibraryRefreshRequired", "Refresh Library to read an authorised snapshot.");
        // This is the Notes-issued already-read presentation source. It checks the actual
        // Notes physical witness, current full actor, ownership and completed Home review.
        var rows = await originals.AwaitAsync(originals.Invoke(() => source.ListAsync(false, token))).ConfigureAwait(false);
        await DemandCurrentAsync(originals, token).ConfigureAwait(false);
        return rows;
    }

    private async Task<(INotesOriginalLibraryReadRequest Request, NotesOriginalLibrarySnapshot Snapshot,
        HomeNotesLibraryReadAdmission Admission)> ReadAsync(Guid? documentId, CloudflareOriginalTaskLedger originals,
        CancellationToken token, bool writeLibraryRead = false)
    {
        // Stable producer callbacks resolve the currently serialized ORIGINAL operation,
        // rather than capturing an already-settled query ledger in the saved read witness.
        Action<Action> Own = OwnCurrentSource;
        Action<Task> Retain = RetainCurrentSource;
        if (_auditPending is { } prior)
        {
            // Recovery retries the observed terminal audit only, NEVER the one-use read.
            var audit = await originals.AwaitAsync(originals.Invoke(() =>
                _notes.RetryOriginalLibraryReadAuditAsync(prior.Request, prior.Admission, token))).ConfigureAwait(false);
            if (!audit.Succeeded) throw Refuse("LibraryAuditPending", "The previous Library read needs its retained Home audit.");
            _auditPending = null;
        }
        if (_pending is { } unresolved)
        {
            if (unresolved.Review.WasDispatchAttempted)
            {
                var audit = await originals.AwaitAsync(originals.Invoke(() =>
                    _admission.RetryLibrarySnapshotDispatchAuditWithinOriginalSourceAsync(unresolved.Review, Own, Retain, token))).ConfigureAwait(false);
                if (!audit.Succeeded) throw Refuse("LibraryAuditPending", "The prior Library dispatch needs its original audit before another read.");
                _pending = null;
            }
            else
            {
                var state = await originals.AwaitAsync(originals.Invoke(() =>
                    _admission.GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(unresolved.Review, Own, Retain, token))).ConfigureAwait(false);
                if (state.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled)
                    _pending = null; // Actual explicit refusal, no execution claim or read to replay.
            }
        }
        if (_pending is { } pending && pending.Request.DocumentId != documentId)
            throw Refuse("LibraryReviewPending", "Finish the existing Library review before choosing another item.");
        if (_pending is null)
        {
            var inspection = await originals.AwaitAsync(originals.Invoke(() =>
                _notes.InspectOriginalStoreSetupWithinOriginalSourceAsync(Own, Retain, token).AsTask())).ConfigureAwait(false);
            if (!_notes.IsIssuedOriginalSetupInspection(inspection))
                throw new InvalidDataException("The original Notes setup observation has a different issuer.");
            if (inspection.State != NotesOriginalStoreSetupState.Available)
                throw Refuse(inspection.Code, inspection.Message);
            var request = await originals.AwaitAsync(originals.Invoke(() =>
                _notes.PrepareOriginalLibraryReadAsync(documentId, token).AsTask())).ConfigureAwait(false);
            var scope = new ResourceScope(HomeNotesResourceAdmissionSource.ScopeKind,
                request.StoreObservation.StoreIdentity.StoreId.ToString("D"), request.StoreObservation.Revision, ResourceAccess.Read);
            var readAction = writeLibraryRead ? HomeNotesResourceAdmissionSource.WriteLibraryReadAction : HomeNotesResourceAdmissionSource.ReadAction;
            var allowed = await originals.AwaitAsync(originals.Invoke(() => _admission.EvaluateWithinOriginalSourceAsync(
                _actor, readAction, scope, Own, Retain, token))).ConfigureAwait(false);
            if (!allowed.Allowed) throw Refuse("LibrarySetupRequired", "Set up or explicitly import this existing Notes store in Boards before browsing its Library references.");
            var review = await originals.AwaitAsync(originals.Invoke(() => writeLibraryRead
                ? _admission.RequestWriteLibrarySnapshotWithinOriginalSourceAsync(request, _session, Own, Retain, token)
                : _admission.RequestLibrarySnapshotWithinOriginalSourceAsync(request, _session, Own, Retain, token))).ConfigureAwait(false);
            _pending = (request, review); // Retain before any presenter, dispatch or source fault.
        }
        var original = _pending!.Value;
        var authorization = await originals.AwaitAsync(originals.Invoke(() =>
            _admission.GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(original.Review, Own, Retain, token))).ConfigureAwait(false);
        if (!authorization.IsAllowed || authorization.State != HomePermissionRequestState.Approved)
        {
            if (authorization.State != HomePermissionRequestState.PendingApproval)
                throw Refuse("LibraryPermissionRequired", authorization.Message);
            await originals.AwaitAsync(originals.Invoke(() => _present(authorization, token))).ConfigureAwait(false);
        }
        await DemandCurrentAsync(originals, token).ConfigureAwait(false);
        var admission = await originals.AwaitAsync(originals.Invoke(() =>
            _admission.BeginLibrarySnapshotWithinOriginalSourceAsync(original.Review, Own, Retain, token))).ConfigureAwait(false)
            ?? throw Refuse("LibraryPermissionRequired", "The Library read still needs its genuine Home decision.");
        _pending = null; _auditPending = new(original.Request, admission);
        var snapshot = await originals.AwaitAsync(originals.Invoke(() =>
            _notes.ReadOriginalLibraryWithHomeAsync(original.Request, admission, _verified, token))).ConfigureAwait(false);
        if (snapshot.AuditWarning is not null)
            throw Refuse("LibraryAuditPending", "The Library read completed but its original Home audit needs recovery.");
        _auditPending = null;
        await DemandCurrentAsync(originals, token).ConfigureAwait(false);
        return (original.Request, snapshot, admission);
    }

    private void DemandOwner()
    {
        if (!_alive() || _home.OriginalCloseTask is not null || _home.OriginalProcessRetirementRequestTask is not null)
            throw new ObjectDisposedException(nameof(NativeHomeArtifactLibrarySource));
    }
    private async Task DemandCurrentAsync(CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); DemandOwner();
        await originals.AwaitAsync(originals.Invoke(() => _admit(token))).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); DemandOwner();
        // Stable producer callbacks resolve the currently serialized ORIGINAL operation,
        // rather than capturing an already-settled query ledger in the saved read witness.
        Action<Action> Own = OwnCurrentSource;
        Action<Task> Retain = RetainCurrentSource;
        var read = await originals.AwaitAsync(originals.Invoke(() => _home.StateStore.ReadAsync(token))).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidDataException("Home profile state is unavailable; preserve it for recovery.");
        var current = await originals.AwaitAsync(originals.Invoke(() => _home.Profiles.CheckAsync(read.State!, _actor,
            HomeStateCommitPhase.Publication, Own, Retain, token).AsTask())).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); DemandOwner();
        if (!current) throw Refuse("LibraryProfileChanged", "Library belongs to the original Home profile. Reopen Home.");
        if (_home.OriginalStartTask is not { IsCompletedSuccessfully: true } ||
            !new[] { "home.core", "home.state", "permissions.trust" }.All(id => _home.Runtime.Current.Services.Any(service =>
                service.ServiceId == id && new HomeServiceRequirement(id, 1).Accepts(service.ContractVersion) &&
                service.IsAvailable && service.State == HomeServiceLifecycleState.Ready)))
            throw Refuse("HomeUnavailable", "The required original Home services are unavailable.");
    }
    private HomeFeatureProviderException Refuse(string code, string message)
    {
        var refusal = new HomeFeatureProviderException(code, message);
        lock (_sync) _refusals.Add(refusal);
        return refusal;
    }
    private bool IsExpected(Exception error) => error is AggregateException group
        ? group.InnerExceptions.Count > 0 && group.InnerExceptions.All(IsExpected)
        : _refusals.Contains(error);
    private Task<T> RunAsync<T>(Func<CloudflareOriginalTaskLedger, CancellationToken, Task<T>> body, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retiring, this); DemandOwner();
            _operations.RemoveAll(operation => operation.Task.IsCompletedSuccessfully ||
                operation.Task.IsFaulted && IsExpected(operation.Task.Exception!));
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle the original Library failures before admitting more work.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = Run(start.Task); _operations.Add(new(actual, token)); start.SetResult(); return ExposeExpectedRefusal(actual);
        }
        async Task<T> Run(Task start)
        {
            await start.ConfigureAwait(false);
            var gate = _gate.WaitAsync(token); // Same real gate task; no protected work before admission.
            await gate.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this); originals.BindOriginalCallerCallback(_parent);
            lock (_sync) _currentOriginal = originals;
            try
            {
                return await originals.RunToOriginalSettlementAsync(async () =>
                { await DemandCurrentAsync(originals, token).ConfigureAwait(false); return await body(originals, token).ConfigureAwait(false); }).ConfigureAwait(false);
            }
            finally
            {
                // Keep the physical slot until every retained child has acknowledged. A later
                // metadata presentation must never publish children onto the old query ledger.
                lock (_sync) _currentOriginal = null;
                _gate.Release();
            }
        }
    }
    private CloudflareOriginalTaskLedger CurrentOriginal()
    {
        lock (_sync) return _currentOriginal ??
            throw new InvalidOperationException("The actual serialized Library source must own every nested callback.");
    }
    private void OwnCurrentSource(Action body)
    {
        var original = CurrentOriginal();
        original.Invoke(() => { DemandOwner(); body(); return true; });
    }
    private void RetainCurrentSource(Task actual) { _ = CurrentOriginal().Track(actual); }

    public Task RevalidateAsync(CancellationToken token) => RunAsync(async (originals, caller) =>
    { await DemandCurrentAsync(originals, caller).ConfigureAwait(false); return true; }, token);
    public bool IsExpectedRefusal(Exception error) { lock (_sync) return IsExpected(error); }
    private async Task<T> ExposeExpectedRefusal<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch
        {
            var terminal = actual.Exception;
            if (terminal is not null)
                lock (_sync)
                    if (IsExpected(terminal))
                    {
                        Exception first = terminal;
                        while (first is AggregateException group) first = group.InnerExceptions[0];
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
                    }
            throw; // Complete raw/mixed failures remain retained on the original task.
        }
    }
    internal static object[] ProjectRows(HomeLibraryPage? page) => page?.Items.Select(item => (object)new Dictionary<string, object?>
    {
        ["Key"] = item.OwnerApp + "/" + item.ArtifactId, ["DisplayName"] = item.DisplayName,
        ["OwnerApp"] = item.OwnerApp, ["ArtifactType"] = item.ArtifactType, ["ArtifactId"] = item.ArtifactId,
        ["Location"] = item.Location, ["Owner"] = item.Owner, ["SharedBy"] = item.SharedBy,
        ["LastModified"] = item.LastModified.ToLocalTime().ToString("g"), ["IsPinned"] = item.IsPinned,
        ["Target"] = item.OpenLink
    }).ToArray() ?? [];

    public ValueTask DisposeAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseAsync(start.Task, _operations.ToArray()); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseAsync(Task start, Operation[] operations)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        foreach (var operation in operations)
            try { await operation.Task.ConfigureAwait(false); }
            catch (OperationCanceledException cause) when (operation.Task.IsCanceled && operation.Token.IsCancellationRequested && cause.CancellationToken == operation.Token) { }
            catch (Exception cause)
            {
                var terminal = operation.Task.Exception ?? cause;
                lock (_sync) { if (!IsExpected(terminal)) failures.Add(terminal); }
            }
        _entries.Clear(); _metadata = null; _gate.Dispose();
        if (failures.Count != 0) throw new AggregateException("Original Library sources settled with failures.", failures);
        // The Notes store, Home process, review windows and documents are borrowed, never closed here.
    }
}

/// <summary>Thin multi-source publication into the EXISTING shared Library service.
/// Sources retain their own canonical data, permissions and exact private metadata witnesses.</summary>
internal sealed class NativeCompositeHomeLibrarySource : IHomeOriginalLibrarySource
{
    internal sealed record Member(string OwnerApp, string DisplayName, IHomeOriginalLibrarySource Source);
    private readonly IHomeCoreStateStore _home;
    private readonly Member[] _members;
    private readonly Dictionary<string, Member> _owners;
    private readonly object _sync = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Exception, object> _refusals = new();
    private bool _retiring;
    private Task? _close;
    private string? _notice;
    internal NativeCompositeHomeLibrarySource(IHomeCoreStateStore home, params Member[] members)
    {
        if (members.Length == 0 || members.Any(member => string.IsNullOrWhiteSpace(member.OwnerApp) || member.Source is null) ||
            members.Select(member => member.OwnerApp).Distinct(StringComparer.Ordinal).Count() != members.Length)
            throw new ArgumentException("Each canonical Library app must have one original source.");
        _home = home; _members = members.ToArray(); _owners = _members.ToDictionary(member => member.OwnerApp, StringComparer.Ordinal);
    }
    public string? AvailabilityNotice { get { lock (_sync) return _notice; } }
    public HomeLibraryService CreateService(IHomeDeepLinkRouter router) => new(this, this, router, new HomeCoreLibraryPreferences(_home));
    private void Demand() { lock (_sync) ObjectDisposedException.ThrowIf(_retiring, this); }
    public async Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken token)
    {
        Demand(); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var result = new List<HomeArtifactReference>(); var notices = new List<string>(); var succeeded = false;
        foreach (var member in _members.Where(member => query.OwnerApp is null || member.OwnerApp == query.OwnerApp))
        {
            try
            {
                var rows = await member.Source.SearchAsync(query, token).ConfigureAwait(false);
                if (rows.Any(row => row.OwnerApp != member.OwnerApp))
                    throw new InvalidDataException("A canonical Library source published rows for another owner.");
                result.AddRange(rows); succeeded = true;
                if (!string.IsNullOrWhiteSpace(member.Source.AvailabilityNotice)) notices.Add(member.Source.AvailabilityNotice!);
            }
            catch (Exception cause) when (member.Source.IsExpectedRefusal(cause))
            { notices.Add(member.DisplayName + ": " + cause.Message); }
        }
        token.ThrowIfCancellationRequested(); Demand();
        lock (_sync) _notice = notices.Count == 0 ? null : string.Join(" ", notices);
        if (!succeeded && notices.Count > 0)
        {
            var refusal = new HomeFeatureProviderException("LibrarySourcesUnavailable", string.Join(" ", notices));
            _refusals.Add(refusal, new()); throw refusal;
        }
        return result;
    }
    public Task<HomeArtifactReference?> GetByIdAsync(string app, string id, CancellationToken token)
    { Demand(); return _owners.TryGetValue(app, out var owner) ? owner.Source.GetByIdAsync(app, id, token) : Task.FromResult<HomeArtifactReference?>(null); }
    public Task<bool> CanReadAsync(string app, string id, CancellationToken token)
    { Demand(); return _owners.TryGetValue(app, out var owner) ? owner.Source.CanReadAsync(app, id, token) : Task.FromResult(false); }
    public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken token)
    { Demand(); return _owners.TryGetValue(link.OwnerApp, out var owner) ? owner.Source.CanOpenAsync(link, token) : Task.FromResult(false); }
    public async Task RevalidateAsync(CancellationToken token)
    { Demand(); foreach (var member in _members) await member.Source.RevalidateAsync(token).ConfigureAwait(false); Demand(); }
    public bool IsExpectedRefusal(Exception error) => error is AggregateException group
        ? group.InnerExceptions.Count > 0 && group.InnerExceptions.All(IsExpectedRefusal)
        : _refusals.TryGetValue(error, out _) || _members.Any(member => member.Source.IsExpectedRefusal(error));
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        // Check ALL actual children before mutating any sibling's retirement state.
        foreach (var member in _members) member.Source.DemandExternalOriginalJoin();
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin();
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _retiring = true; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseAsync(start.Task); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseAsync(Task start)
    {
        await start.ConfigureAwait(false); var originals = new List<Task>(); var failures = new List<Exception>();
        foreach (var member in _members)
            try { originals.Add(member.Source.DisposeAsync().AsTask()); }
            catch (Exception cause) { failures.Add(cause); }
        foreach (var actual in originals)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(actual.Exception ?? cause); }
        if (failures.Count != 0) throw new AggregateException("Original canonical Library sources failed while closing.", failures);
    }
}
