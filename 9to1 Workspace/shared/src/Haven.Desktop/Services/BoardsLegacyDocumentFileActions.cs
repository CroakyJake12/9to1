using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Haven.Application;

namespace Haven.Desktop.Services;

internal enum BoardsLegacyOpenCandidateKind { File, Transient }
internal sealed record BoardsLegacyOpenCandidate(BoardsLegacyOpenCandidateKind Kind, string Value);

/// <summary>Lexical OS input only. A candidate is neither a Files selection nor permission
/// to read, create, import, overwrite or choose a default document.</summary>
internal sealed class BoardsLegacyOpenIntent
{
    private BoardsLegacyOpenIntent(IEnumerable<BoardsLegacyOpenCandidate> candidates, string? issue)
    { Candidates = Array.AsReadOnly(candidates.ToArray()); IssueCode = issue; }

    public ReadOnlyCollection<BoardsLegacyOpenCandidate> Candidates { get; }
    public string? IssueCode { get; }
    public bool RequiresChoice => Candidates.Count > 1;

    public static BoardsLegacyOpenIntent ParseArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = arguments.ToArray();
        if (values.Length == 0) return new([], null);
        if (values.Any(string.IsNullOrWhiteSpace)) return new([], "InvalidOpenIntent");
        // Historical argv could mean either the first path or an unquoted path split
        // on spaces. Existence checks would perform IO and silently choose authority.
        var candidates = values.Length == 1 ? new[] { values[0] }
            : new[] { values[0], string.Join(" ", values) };
        var frozen = candidates.Distinct(StringComparer.Ordinal).Select(Candidate).ToArray();
        return new(frozen, frozen.Length > 1 ? "AmbiguousOpenIntent" : null);
    }

    public static BoardsLegacyOpenIntent FromPickerSelection(string? selectedPath) =>
        string.IsNullOrWhiteSpace(selectedPath) ? new([], null) : new([Candidate(selectedPath)], null);

    private static BoardsLegacyOpenCandidate Candidate(string value) => new(
        value.StartsWith("memory://", StringComparison.OrdinalIgnoreCase)
            ? BoardsLegacyOpenCandidateKind.Transient : BoardsLegacyOpenCandidateKind.File, value);
}

/// <summary>Descriptive metadata on an observation privately issued by an original source.
/// Implementing or copying this interface does not issue a Files read capability.</summary>
internal interface IBoardsLegacyOriginalFileObservation
{
    BoardsLegacySourceIdentity Source { get; }
    long ByteLength { get; }
    string Sha256 { get; }
}

/// <summary>Unbound host-adapter seam, not a Files authority or registered producer. Future
/// trusted Desktop composition must bridge genuine Files Application contracts here; Files
/// must not reference Desktop. All starts return their actual Task, never success wrappers.
/// The source must enforce the supplied positive byte budget before materializing content,
/// and own physical selection, hashes, permission and revision.
/// Descendants must be retained before their owning parent becomes terminal; retained
/// callbacks expire when the original operation and its complete task closure settle.</summary>
internal interface IBoardsLegacyOriginalFileReadSource
{
    CancellationToken OriginalLifetime { get; }
    bool IsIssuedObservation(IBoardsLegacyOriginalFileObservation observation);
    Task<bool> IsObservationCurrentAsync(IBoardsLegacyOriginalFileObservation observation,
        Action<Action> withinOriginalSource, Action<Task> retainOriginal, CancellationToken cancellationToken);
    Task<IBoardsLegacyOriginalFileReadLease> OpenReadAsync(IBoardsLegacyOriginalFileObservation observation,
        int maximumReadBytes, Action<Action> withinOriginalSource, Action<Task> retainOriginal, CancellationToken cancellationToken);
    bool IsIssuedLease(IBoardsLegacyOriginalFileReadLease lease, IBoardsLegacyOriginalFileObservation observation);
}

internal interface IBoardsLegacyOriginalFileReadLease
{
    BoardsLegacySourceIdentity Source { get; }
    // The privately owned source freezes bytes for the exact issued observation.
    // ReadOnlyMemory alone is not an immutability guarantee; the host copies and
    // verifies its original length/hash before handing data to the pure codec.
    Task<ReadOnlyMemory<byte>> ReadAsync(int maximumReadBytes, Action<Action> withinOriginalSource,
        Action<Task> retainOriginal, CancellationToken cancellationToken);
    // Close must remain possible after caller/source cancellation. The same finite
    // original owner still guards cleanup; no unscoped disposal fallback is allowed.
    Task CloseAsync(Action<Action> withinOriginalSource, Action<Task> retainOriginal);
}

internal sealed class BoardsLegacyFileInspectionResult
{
    private BoardsLegacyFileInspectionResult(IBoardsLegacyDocumentInspection? inspection,
        IEnumerable<BoardsLegacyDocumentIssue> issues)
    { Inspection = inspection; Issues = Array.AsReadOnly(issues.ToArray()); }
    public IBoardsLegacyDocumentInspection? Inspection { get; }
    public IReadOnlyList<BoardsLegacyDocumentIssue> Issues { get; }
    public bool IsInspectionAvailable => Inspection is not null;
    internal static BoardsLegacyFileInspectionResult Refused(string code, string message) =>
        new(null, [new(code, string.Empty, message, true)]);
    internal static BoardsLegacyFileInspectionResult Observed(IBoardsLegacyDocumentInspection inspection) =>
        new(inspection, inspection.Issues);
}

/// <summary>OS/data read boundary only. No producer is registered here; absent original
/// Files admission refuses before IO. This does not import, commit Notes, grant Home
/// approval, select an argv candidate or implement application/CUI orchestration.</summary>
internal sealed class BoardsLegacyDocumentFileActions(IBoardsLegacyDocumentCodec codec,
    IBoardsLegacyOriginalFileReadSource? source = null, Action<Action>? originalScope = null,
    Action<Task>? retainOriginal = null, CancellationToken actualAppLifetime = default,
    int maximumReadBytes = 256 * 1024 * 1024)
{
    internal const int MaximumSupportedReadBytes = 256 * 1024 * 1024;
    private readonly int _maximumReadBytes = ValidateReadBudget(maximumReadBytes);

    private static int ValidateReadBudget(int maximumReadBytes) =>
        maximumReadBytes is > 0 and <= MaximumSupportedReadBytes ? maximumReadBytes :
            throw new ArgumentOutOfRangeException(nameof(maximumReadBytes), maximumReadBytes,
                "The original legacy read budget must be positive and no greater than 256 MiB.");

    public async Task<BoardsLegacyFileInspectionResult> InspectOriginalAsync(
        IBoardsLegacyOriginalFileObservation? observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (source is null || originalScope is null || retainOriginal is null || !actualAppLifetime.CanBeCanceled)
            return BoardsLegacyFileInspectionResult.Refused("DependencyUnavailable",
                "An original Files read source and its actual host lifetime are not connected.");
        if (observation is null)
            return BoardsLegacyFileInspectionResult.Refused("PermissionDenied", "Select an original Files observation first.");

        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(this);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        CancellationToken sourceLifetime = default;
        int live = 1;
        var custody = new object();
        int activeCallbacks = 0;
        TaskCompletionSource? callbacksSettled = null;
        IBoardsLegacyOriginalFileReadLease? acquired = null;

        void DemandLive(bool cleanup)
        {
            if (Volatile.Read(ref live) == 0)
                throw new InvalidOperationException("The original legacy read scope has ended.");
            if (!cleanup && (actualAppLifetime.IsCancellationRequested || sourceLifetime.IsCancellationRequested))
                throw new InvalidOperationException("The original legacy read lifetime has ended.");
        }
        void EnterCallback(bool cleanup)
        {
            lock (custody) { DemandLive(cleanup); ++activeCallbacks; }
        }
        void ExitCallback()
        {
            lock (custody)
            {
                if (--activeCallbacks == 0)
                {
                    var settled = callbacksSettled;
                    callbacksSettled = null;
                    settled?.TrySetResult();
                }
            }
        }
        T Finite<T>(Func<T> body, bool cleanup = false)
        {
            EnterCallback(cleanup);
            var gate = new object();
            bool open = true, invoked = false;
            int thread = Environment.CurrentManagedThreadId;
            T result = default!;
            Exception? callbackFailure = null;
            void Run()
            {
                lock (gate)
                {
                    if (!open || invoked || thread != Environment.CurrentManagedThreadId)
                    {
                        var error = new InvalidOperationException("The original scope requires one synchronous finite callback.");
                        callbackFailure ??= error; originals.Retain(error); throw error;
                    }
                    invoked = true;
                }
                try
                {
                    DemandLive(cleanup);
                    result = CloudflareOriginalExecutionGuard.InvokeOriginal(this, body);
                }
                catch (Exception error) { callbackFailure ??= error; originals.Retain(error); throw; }
            }
            try
            {
                DemandLive(cleanup);
                originalScope(Run);
                if (!invoked) throw new InvalidOperationException("The original scope did not run its finite callback.");
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                return result;
            }
            catch (OperationCanceledException error)
            {
                // A synchronous OCE is a fault. Only the actual canceled Task below
                // may establish cancellation; opaque aggregates are never flattened.
                originals.Retain(error);
                var fault = new AggregateException("Synchronous original legacy read callback failed.", error);
                originals.Retain(fault); // Preserve the SAME semantic fault even if a producer swallows it.
                throw fault;
            }
            finally
            {
                lock (gate) open = false;
                ExitCallback();
            }
        }
        void Within(Action body) => Finite(() => { body(); return true; });
        void WithinClose(Action body) => Finite(() => { body(); return true; }, cleanup: true);
        void Retain(Task raw)
        {
            ArgumentNullException.ThrowIfNull(raw);
            lock (custody)
            {
                // This gate is shared with the final closure check. Cancellation
                // cannot forbid original cleanup, but settlement expires the callback
                // before it can change custody or enter the external host again.
                DemandLive(cleanup: true);
                originals.Track(raw); // Actual custody precedes the external callback.
                ++activeCallbacks;
            }
            try { retainOriginal(raw); }
            catch (OperationCanceledException error)
            {
                originals.Retain(error);
                var fault = new AggregateException("Synchronous original task-retention callback failed.", error);
                originals.Retain(fault);
                throw fault;
            }
            catch (Exception error) { originals.Retain(error); throw; }
            finally { ExitCallback(); }
        }
        async Task DrainOriginalClosure()
        {
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                Task[] pending;
                Task? waitForCallbacks = null;
                lock (custody)
                {
                    pending = originals.OriginalTasks.Where(raw => !joined.Contains(raw)).ToArray();
                    if (pending.Length == 0)
                    {
                        if (activeCallbacks == 0)
                        {
                            // No retainer or finite body can enter between this stable
                            // check and expiry. All admitted raw tasks are terminal.
                            Volatile.Write(ref live, 0);
                            return;
                        }
                        callbacksSettled ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                        waitForCallbacks = callbacksSettled.Task;
                    }
                }
                if (waitForCallbacks is not null)
                {
                    await waitForCallbacks.ConfigureAwait(false);
                    continue;
                }
                foreach (var raw in pending)
                {
                    joined.Add(raw);
                    try { await originals.AwaitAsync(raw).ConfigureAwait(false); }
                    catch (Exception error) { originals.Capture(raw, error); }
                }
            }
        }
        async Task<T> Capture<T>(Func<Task<T>> start, Action<T>? capture = null)
        {
            Task<T>? raw = null; Exception? failure = null; T value = default!;
            try { Finite(() => { raw = start(); Retain(raw); return true; }); }
            catch (Exception error) { failure = error; originals.Retain(error); }
            if (raw is not null)
            {
                try { value = await originals.AwaitAsync(raw).ConfigureAwait(false); capture?.Invoke(value); }
                catch (Exception error) { failure ??= error; originals.Retain(error); }
            }
            if (failure is not null)
            {
                if (failure is OperationCanceledException && raw?.IsCanceled == true &&
                    originals.OriginalErrors.All(error => error is OperationCanceledException))
                    ExceptionDispatchInfo.Capture(failure).Throw();
                throw new AggregateException("The original read acquisition or finite scope failed.", originals.OriginalErrors);
            }
            return raw is null ? throw new InvalidOperationException("No original read task was returned.") : value;
        }
        async Task Close()
        {
            Task? raw = null; Exception? failure = null;
            try { Finite(() => { raw = acquired!.CloseAsync(WithinClose, Retain); Retain(raw); return true; }, cleanup: true); }
            catch (Exception error) { failure = error; originals.Retain(error); }
            if (raw is not null)
            {
                try { await originals.AwaitAsync(raw).ConfigureAwait(false); }
                catch (Exception error) { failure ??= error; originals.Retain(error); }
            }
            if (failure is not null)
            {
                if (failure is OperationCanceledException && raw?.IsCanceled == true &&
                    originals.OriginalErrors.All(error => error is OperationCanceledException))
                    ExceptionDispatchInfo.Capture(failure).Throw();
                throw new AggregateException("The original read cleanup or finite scope failed.", originals.OriginalErrors);
            }
            if (raw is null) throw new InvalidOperationException("No original read cleanup task was returned.");
        }

        try
        {
            BoardsLegacyFileInspectionResult? result = null;
            Exception? primary = null;
            try
            {
                result = await originals.RunToOriginalSettlementAsync(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sourceLifetime = Finite(() => source.OriginalLifetime);
                        if (!sourceLifetime.CanBeCanceled)
                            return BoardsLegacyFileInspectionResult.Refused("DependencyUnavailable", "The original Files source has no actual lifetime.");
                        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(actualAppLifetime, sourceLifetime, cancellationToken);
                        var token = lifetime.Token;
                        token.ThrowIfCancellationRequested();
                        if (!Finite(() => source.IsIssuedObservation(observation)))
                            return BoardsLegacyFileInspectionResult.Refused("PermissionDenied", "The observation was not issued by this original Files source.");
                        var identity = Finite(() => observation.Source);
                        if (identity is null || identity.StoreId == Guid.Empty || identity.FileId == Guid.Empty || string.IsNullOrWhiteSpace(identity.Revision))
                            return BoardsLegacyFileInspectionResult.Refused("MissingIdentity", "The original observation has no stable store/file/revision identity.");
                        var length = Finite(() => observation.ByteLength);
                        var digest = Finite(() => observation.Sha256);
                        if (length < 0 || digest is null || digest.Length != 64 ||
                            digest.Any(value => (value < '0' || value > '9') && (value < 'a' || value > 'f')))
                            return BoardsLegacyFileInspectionResult.Refused("MissingIdentity", "The original observation has no exact content length and SHA256 binding.");
                        if (length > _maximumReadBytes)
                            return BoardsLegacyFileInspectionResult.Refused("ReadBudgetExceeded", "The original document exceeds this host's admitted read budget.");
                        if (!await Capture(() => source.IsObservationCurrentAsync(observation, Within, Retain, token)).ConfigureAwait(false))
                            return BoardsLegacyFileInspectionResult.Refused("SourceChanged", "The original Files observation is no longer current.");
                        _ = await Capture(() => source.OpenReadAsync(observation, _maximumReadBytes, Within, Retain, token), value => acquired = value).ConfigureAwait(false);
                        if (acquired is null || !Finite(() => source.IsIssuedLease(acquired, observation)) || Finite(() => acquired.Source) != identity)
                            return BoardsLegacyFileInspectionResult.Refused("PermissionDenied", "The acquired read is not the exact issued original source.");
                        var memory = await Capture(() => acquired.ReadAsync(_maximumReadBytes, Within, Retain, token)).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (memory.Length > _maximumReadBytes)
                            return BoardsLegacyFileInspectionResult.Refused("ReadBudgetExceeded", "The original read returned content beyond this host's admitted budget.");
                        if (memory.Length != length)
                            return BoardsLegacyFileInspectionResult.Refused("SourceChanged", "The original read length does not match the issued observation.");
                        var detached = Finite(() => memory.ToArray());
                        token.ThrowIfCancellationRequested();
                        if (detached.LongLength != length ||
                            !string.Equals(Convert.ToHexString(SHA256.HashData(detached)).ToLowerInvariant(), digest, StringComparison.Ordinal))
                            return BoardsLegacyFileInspectionResult.Refused("SourceChanged", "The original read bytes do not match the issued content observation.");
                        token.ThrowIfCancellationRequested();
                        if (!await Capture(() => source.IsObservationCurrentAsync(observation, Within, Retain, token)).ConfigureAwait(false) ||
                            !Finite(() => source.IsIssuedObservation(observation) && source.IsIssuedLease(acquired, observation) &&
                                observation.Source == identity && observation.ByteLength == length && observation.Sha256 == digest &&
                                acquired.Source == identity && source.OriginalLifetime == sourceLifetime))
                            return BoardsLegacyFileInspectionResult.Refused("SourceChanged", "The original source changed during its read; inspect it again.");
                        token.ThrowIfCancellationRequested();
                        return BoardsLegacyFileInspectionResult.Observed(Finite(() => codec.Inspect(detached, identity)));
                    }
                    catch (Exception error) { originals.Retain(error); throw; }
                    finally { if (acquired is not null) await Close().ConfigureAwait(false); }
                }).ConfigureAwait(false);
            }
            catch (Exception error) { primary = error; originals.Retain(error); }

            // The shared ledger observes a task-list snapshot. A child can retain a
            // grandchild while that snapshot is joining it, so settle the local
            // transitive closure before publishing either the result or the failure.
            await DrainOriginalClosure().ConfigureAwait(false);
            if (primary is OperationCanceledException && originals.OriginalTasks.All(raw => !raw.IsFaulted) &&
                originals.OriginalErrors.All(error => error is OperationCanceledException))
                ExceptionDispatchInfo.Capture(primary).Throw();
            if (originals.OriginalErrors.Count != 0)
                throw new AggregateException("The original legacy read task closure failed.", originals.OriginalErrors);
            return result ?? throw new InvalidOperationException("The original legacy read produced no inspection result.");
        }
        finally { lock (custody) Volatile.Write(ref live, 0); }
    }
}
