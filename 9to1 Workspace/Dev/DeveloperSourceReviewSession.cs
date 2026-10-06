using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace HavenOS.Apps.Dev;

public enum DeveloperSourceChangeState { Prepared, Reviewed, Applied, Rejected, Unresolved }

/// <summary>An exact Dev pass, distinct from the repository's pre-existing working-tree changes.
/// IDs, hashes and this display record never issue a filesystem or Task permission.</summary>
public sealed record DeveloperSourceChangePass(
    Guid ChangeSetId, Guid EditorId, DeveloperProjectReference Project, DeveloperCodeDocument Document,
    Guid TaskId, Guid ExecutionId, Guid ContextId, Guid OriginalReadActionId,
    long DraftRevision, string BeforeText, string AfterText, string BeforeSha256, string AfterSha256,
    DeveloperSourceChangeState State, DeveloperActionObservation? OriginalObservation,
    DateTimeOffset CreatedAt, Guid? RevertsChangeSetId = null);

/// <summary>Complete text observed through the genuine tool result, never its truncated output prose.</summary>
public sealed record DeveloperEditorSnapshot(
    Guid EditorId, DeveloperProjectReference Project, DeveloperCodeDocument Document,
    string OriginalText, string DraftText, string OriginalSha256, long DraftRevision,
    Guid? PreparedChangeSetId, Guid? LastAppliedChangeSetId, DeveloperActionObservation OriginalObservation)
{
    public bool IsDirty => !StringComparer.Ordinal.Equals(OriginalText, DraftText);
}

/// <summary>
/// Platform-neutral source editor/review owner. All reads, previews and writes use the SAME
/// canonical Task service. View retirement seals presentation admission without cancelling or
/// retiring that global business service. Pass-specific revert uses an exact shared hash check.
/// </summary>
public sealed class DeveloperSourceReviewSession(DeveloperTaskWorkspaceService development) : IAsyncDisposable
{
    public const int MaximumOpenDocuments = 8;
    public const int MaximumRetainedPasses = 32;
    public const int MaximumTextCharacters = 2_000_000;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _editors = [];
    private readonly Dictionary<Guid, DeveloperSourceChangePass> _passes = [];
    private readonly Dictionary<Guid, Original> _originals = [];
    private readonly AsyncLocal<Original?> _executing = new();
    private bool _retiring;
    private Task? _close;
    private int _opening;
    private sealed class Original(string digest)
    {
        public string Digest { get; } = digest;
        public Task Task = null!;
        public Original? Parent { get; init; }
        public bool Live;
        public readonly List<Task> Sources = [];
    }
    private sealed class Entry(DeveloperEditorSnapshot snapshot)
    {
        public DeveloperEditorSnapshot Snapshot = snapshot;
        public Guid? RevertsChangeSetId;
        public Guid? PendingApply;
        public readonly Guid OriginalReadActionId = snapshot.OriginalObservation.OriginalContext.ActionId;
    }

    public void RequestRetirement() { lock (_gate) _retiring = true; }
    public void DemandExternalOriginalRetirementJoin()
    {
        development.DemandExternalOriginalRetirementJoin();
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (Volatile.Read(ref current.Live)) throw new InvalidOperationException("A source review original cannot join its own presentation retirement.");
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource start; Original[] originals; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true;
            originals = _originals.Values.ToArray();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DrainAsync(start.Task, originals); _close = actual;
        }
        start.TrySetResult(); return actual;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task DrainAsync(Task start, Original[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var original in originals)
        {
            try { await original.Task.ConfigureAwait(false); }
            catch (Exception error) { CaptureCauses(errors, original.Task, error); }
            Task[] sources; lock (_gate) sources = original.Sources.ToArray();
            foreach (var source in sources)
                try { await source.ConfigureAwait(false); }
                catch (Exception error) { CaptureCauses(errors, source, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Source review originals did not drain cleanly.", errors);
    }
    private static void CaptureCauses(List<Exception> errors, Task actual, Exception observed)
    {
        foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable())
            if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
    }

    public DeveloperEditorSnapshot GetSnapshot(Guid editorId)
    { lock (_gate) { DemandAdmission(); return DemandEntry(editorId).Snapshot; } }
    public IReadOnlyList<DeveloperSourceChangePass> GetPasses()
    { lock (_gate) { DemandAdmission(); return _passes.Values.OrderBy(value => value.CreatedAt).ThenBy(value => value.ChangeSetId).ToArray(); } }

    public Task<DeveloperOperationResult<DeveloperEditorSnapshot>> OpenAsync(
        DeveloperProjectReference project, DeveloperCanonicalActionContext context, DeveloperCodeDocument document,
        CancellationToken businessCancellationToken = default) => AdmitAsync(context.ActionId,
            Digest(new { operation = "open", project, context, document }), async () =>
            {
                lock (_gate)
                {
                    var existing = _editors.Values.SingleOrDefault(value => value.Snapshot.Project == project && value.Snapshot.Document == document);
                    if (existing is not null)
                    {
                        DemandSameTask(existing.Snapshot.OriginalObservation.OriginalContext, context);
                        // Even a cached editor performs a fresh genuine read below before disclosure.
                    }
                    if (_editors.Count + _opening >= MaximumOpenDocuments)
                        throw new InvalidOperationException("Source editor document custody is full. This is a local runtime limit.");
                    _opening++;
                }
                try
                {
                    var read = await ObserveOwnedAsync(() => development.ReadFileAsync(project, context, document, businessCancellationToken)).ConfigureAwait(false);
                    if (!read.Succeeded || read.Value is null) return new(read.RequestId, null, read.Error);
                    DemandCompletedObservation(read.Value, "read_file");
                    // OriginalReadText is issued by the maintained runtime after the actual read
                    // and before output caps; never substitute Output or parse display prose.
                    var text = read.Value.OriginalToolResult?.OriginalReadText;
                    if (text is null) return Fail<DeveloperEditorSnapshot>(DeveloperOperationErrorCode.CapabilityUnavailable,
                        "The host did not return the complete original source text; no truncated editor was opened.", document.FileId);
                    DemandText(text);
                    var snapshot = new DeveloperEditorSnapshot(Guid.NewGuid(), project, document, text, text,
                        Sha256(text), 1, null, null, read.Value);
                    lock (_gate)
                    {
                        var existing = _editors.Values.SingleOrDefault(value => value.Snapshot.Project == project && value.Snapshot.Document == document);
                        if (existing is not null)
                        {
                            DemandSameTask(existing.Snapshot.OriginalObservation.OriginalContext, context);
                            if (existing.Snapshot.OriginalSha256 != snapshot.OriginalSha256)
                                return Fail<DeveloperEditorSnapshot>(DeveloperOperationErrorCode.RevisionConflict, "The actual source changed outside this editor; its retained draft was preserved.", document.FileId);
                            existing.Snapshot = existing.Snapshot with { OriginalObservation = read.Value };
                            return DeveloperOperationResult<DeveloperEditorSnapshot>.Success(existing.Snapshot, context.ActionId);
                        }
                        _editors.Add(snapshot.EditorId, new(snapshot));
                    }
                    return DeveloperOperationResult<DeveloperEditorSnapshot>.Success(snapshot, context.ActionId);
                }
                finally { lock (_gate) _opening--; }
            });

    public DeveloperEditorSnapshot UpdateDraft(Guid editorId, long expectedDraftRevision, string completeText)
    {
        DemandText(completeText);
        lock (_gate)
        {
            DemandAdmission(); var entry = DemandEntry(editorId);
            DemandRevision(entry, expectedDraftRevision);
            entry.Snapshot = entry.Snapshot with { DraftText = completeText, DraftRevision = checked(expectedDraftRevision + 1), PreparedChangeSetId = null };
            entry.RevertsChangeSetId = null;
            return entry.Snapshot;
        }
    }

    public Task<DeveloperOperationResult<DeveloperSourceChangePass>> PreviewAsync(
        Guid editorId, long expectedDraftRevision, DeveloperCanonicalActionContext context,
        CancellationToken businessCancellationToken = default)
    {
        DeveloperEditorSnapshot snapshot; Guid? reverts; Guid originalReadActionId;
        lock (_gate)
        {
            DemandAdmission(); var entry = DemandEntry(editorId); DemandRevision(entry, expectedDraftRevision);
            DemandSameTask(entry.Snapshot.OriginalObservation.OriginalContext, context);
            snapshot = entry.Snapshot; reverts = entry.RevertsChangeSetId; originalReadActionId = entry.OriginalReadActionId;
        }
        return AdmitAsync(context.ActionId, Digest(new { operation = "preview", editorId, expectedDraftRevision, context, snapshot.DraftText }), async () =>
        {
            var candidate = new DeveloperSourceChangePass(Guid.NewGuid(), editorId, snapshot.Project, snapshot.Document,
                context.TaskId, context.ExecutionId, context.ContextId, originalReadActionId,
                expectedDraftRevision, snapshot.OriginalText, snapshot.DraftText, snapshot.OriginalSha256, Sha256(snapshot.DraftText),
                DeveloperSourceChangeState.Prepared, null, DateTimeOffset.UtcNow, reverts);
            lock (_gate)
            {
                if (_passes.Count >= MaximumRetainedPasses) throw new InvalidOperationException("Source pass custody is full. Inspect or checkpoint the retained passes before another preview.");
                _passes.Add(candidate.ChangeSetId, candidate); // retained BEFORE provider callbacks
            }
            var preview = await ObserveOwnedAsync(() => development.PreviewEditAsync(snapshot.Project, context,
                new(snapshot.Document, candidate.BeforeSha256, candidate.AfterText), businessCancellationToken)).ConfigureAwait(false);
            if (!preview.Succeeded || preview.Value is null)
            {
                lock (_gate) _passes[candidate.ChangeSetId] = candidate with { State = DeveloperSourceChangeState.Rejected };
                return new(preview.RequestId, null, preview.Error);
            }
            DemandCompletedObservation(preview.Value, "preview_change_set");
            var reviewed = candidate with { State = DeveloperSourceChangeState.Reviewed, OriginalObservation = preview.Value };
            lock (_gate)
            {
                _passes[candidate.ChangeSetId] = reviewed;
                var entry = DemandEntry(editorId);
                // A held preview may finish after a new draft was typed. Preserve that newer draft.
                if (entry.Snapshot.DraftRevision == expectedDraftRevision && entry.Snapshot.DraftText == candidate.AfterText)
                    entry.Snapshot = entry.Snapshot with { PreparedChangeSetId = candidate.ChangeSetId, OriginalObservation = preview.Value };
            }
            return DeveloperOperationResult<DeveloperSourceChangePass>.Success(reviewed, context.ActionId);
        });
    }

    public Task<DeveloperOperationResult<DeveloperSourceChangePass>> ApplyAsync(
        Guid editorId, Guid changeSetId, long expectedDraftRevision, DeveloperCanonicalActionContext context,
        CancellationToken businessCancellationToken = default)
    {
        DeveloperSourceChangePass candidate; Entry entry;
        lock (_gate)
        {
            DemandAdmission(); entry = DemandEntry(editorId); DemandRevision(entry, expectedDraftRevision);
            if (!_passes.TryGetValue(changeSetId, out candidate!) || candidate.EditorId != editorId ||
                candidate.State != DeveloperSourceChangeState.Reviewed || entry.Snapshot.PreparedChangeSetId != changeSetId ||
                candidate.DraftRevision != expectedDraftRevision || candidate.AfterText != entry.Snapshot.DraftText ||
                candidate.BeforeSha256 != entry.Snapshot.OriginalSha256)
                throw new InvalidOperationException("Apply requires this session's exact current reviewed pass.");
            DemandSameTask(entry.Snapshot.OriginalObservation.OriginalContext, context);
            if (entry.PendingApply is { } admitted && admitted != context.ActionId)
                throw new InvalidOperationException("An original source apply is still pending. Its outcome must settle before another apply.");
        }
        return AdmitAsync(context.ActionId, Digest(new { operation = "apply", editorId, changeSetId, expectedDraftRevision, context }), async () =>
        {
            lock (_gate)
            {
                if (entry.PendingApply is { } admitted && admitted != context.ActionId)
                    throw new InvalidOperationException("Another original source apply won admission.");
                entry.PendingApply = context.ActionId;
            }
            DeveloperOperationResult<DeveloperActionObservation> apply;
            try
            {
                apply = await ObserveOwnedAsync(() => development.ApplyEditAsync(candidate.Project, context,
                    new(candidate.Document, candidate.BeforeSha256, candidate.AfterText), businessCancellationToken)).ConfigureAwait(false);
            }
            catch
            {
                lock (_gate) _passes[changeSetId] = candidate with { State = DeveloperSourceChangeState.Unresolved };
                // PendingApply stays retained. A fault cannot prove physical absence or authorize replay.
                throw;
            }
            if (!apply.Succeeded || apply.Value is null)
            {
                lock (_gate) { _passes[changeSetId] = candidate with { State = DeveloperSourceChangeState.Rejected }; entry.PendingApply = null; }
                return new(apply.RequestId, null, apply.Error);
            }
            var observation = apply.Value;
            var applied = candidate with
            {
                OriginalObservation = observation,
                State = observation.Action?.State == TaskPlanNodeState.Completed && !string.IsNullOrWhiteSpace(observation.OwnerReceiptReference)
                    ? DeveloperSourceChangeState.Applied : DeveloperSourceChangeState.Unresolved
            };
            lock (_gate)
            {
                _passes[changeSetId] = applied;
                if (applied.State == DeveloperSourceChangeState.Applied)
                {
                    // A newer typed draft is never overwritten by completion of an older apply.
                    entry.Snapshot = entry.Snapshot with
                    {
                        OriginalText = candidate.AfterText, OriginalSha256 = candidate.AfterSha256,
                        DraftText = entry.Snapshot.DraftRevision == expectedDraftRevision ? candidate.AfterText : entry.Snapshot.DraftText,
                        PreparedChangeSetId = null, LastAppliedChangeSetId = changeSetId, OriginalObservation = observation
                    };
                    entry.PendingApply = null;
                }
            }
            if (applied.State == DeveloperSourceChangeState.Unresolved)
                throw new DeveloperSourceOutcomeUnresolvedException(applied);
            return DeveloperOperationResult<DeveloperSourceChangePass>.Success(applied, context.ActionId);
        });
    }

    /// <summary>Stages only this pass's inverse. Later source edits are preserved by the shared
    /// expected-after hash check at preview/apply; a dirty editor is never silently replaced.</summary>
    public DeveloperEditorSnapshot PrepareRevert(Guid editorId, Guid appliedChangeSetId, long expectedDraftRevision)
    {
        lock (_gate)
        {
            DemandAdmission(); var entry = DemandEntry(editorId); DemandRevision(entry, expectedDraftRevision);
            if (!_passes.TryGetValue(appliedChangeSetId, out var pass) || pass.EditorId != editorId ||
                pass.State != DeveloperSourceChangeState.Applied || entry.Snapshot.IsDirty ||
                entry.Snapshot.OriginalSha256 != pass.AfterSha256)
                throw new InvalidOperationException("Revert requires this session's exact applied pass and a clean editor at its after revision.");
            entry.Snapshot = entry.Snapshot with { DraftText = pass.BeforeText, DraftRevision = checked(expectedDraftRevision + 1), PreparedChangeSetId = null };
            entry.RevertsChangeSetId = pass.ChangeSetId;
            return entry.Snapshot;
        }
    }

    private Task<T> AdmitAsync<T>(Guid actionId, string digest, Func<Task<T>> source)
    {
        if (actionId == Guid.Empty) throw new ArgumentException("The canonical action ID is required.", nameof(actionId));
        TaskCompletionSource start; Original original; Task<T> actual;
        lock (_gate)
        {
            DemandAdmission();
            foreach (var healthy in _originals.Where(value => value.Value.Task.IsCompletedSuccessfully).Select(value => value.Key).ToArray()) _originals.Remove(healthy);
            if (_originals.TryGetValue(actionId, out var existing))
            {
                // This presentation session has no private issuer port for copied result retrieval.
                // Do not disclose a retained result from value-equal IDs after actor retirement.
                throw new InvalidOperationException("This source review action was already admitted. Inspect its original reply; use a fresh current context for another operation.");
            }
            if (_originals.Count >= DeveloperTaskWorkspaceService.MaximumRetainedInvocations) throw new InvalidOperationException("Unresolved source review original custody is full.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = new(digest) { Parent = _executing.Value };
            actual = DriveAsync(start.Task, original, source); original.Task = actual; _originals.Add(actionId, original);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<T> DriveAsync<T>(Task start, Original original, Func<Task<T>> source)
    {
        await start.ConfigureAwait(false);
        var previous = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
        try { return await ObserveOwnedAsync(source).ConfigureAwait(false); }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
    }
    private async Task<T> ObserveOwnedAsync<T>(Func<Task<T>> source)
    {
        Task<T> actual;
        try { actual = source(); }
        catch (OperationCanceledException cause) { throw new AggregateException("A synchronous original source fault is not a canceled Task.", cause); }
        if (_executing.Value is { } original)
            lock (_gate) if (!original.Sources.Any(value => ReferenceEquals(value, actual))) original.Sources.Add(actual);
        try { return await actual.ConfigureAwait(false); } catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private Entry DemandEntry(Guid id) => _editors.TryGetValue(id, out var entry) ? entry : throw new KeyNotFoundException("The source editor is not owned by this session.");
    private void DemandAdmission() { if (_retiring) throw new ObjectDisposedException(nameof(DeveloperSourceReviewSession)); }
    private static void DemandRevision(Entry entry, long expected)
    { if (entry.Snapshot.DraftRevision != expected) throw new InvalidOperationException("The draft changed; review its current revision."); }
    private static void DemandSameTask(DeveloperCanonicalActionContext actual, DeveloperCanonicalActionContext requested)
    { if (actual.TaskId != requested.TaskId || actual.ExecutionId != requested.ExecutionId || actual.ContextId != requested.ContextId) throw new UnauthorizedAccessException("Source review must remain in the same original Task/Run/context."); }
    private static void DemandCompletedObservation(DeveloperActionObservation observation, string toolName)
    {
        if (observation.ToolName != toolName || observation.Action?.State != TaskPlanNodeState.Completed ||
            observation.OriginalToolResult is not { OriginalEffectBodyCompleted: true, OriginalRuntimeError: null })
            throw new InvalidOperationException("The original source operation did not return a complete current observation; no review acknowledgment was inferred.");
    }
    private static void DemandText(string text)
    { ArgumentNullException.ThrowIfNull(text); if (text.Length > MaximumTextCharacters || text.IndexOf('\0') >= 0) throw new ArgumentException("A complete bounded text document is required; binary or oversized content is not truncated.", nameof(text)); }
    private static string Digest<T>(T request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static DeveloperOperationResult<T> Fail<T>(DeveloperOperationErrorCode code, string message, Guid target) =>
        DeveloperOperationResult<T>.Failure(code, message, target.ToString("D"), retryable: false);
}

public sealed class DeveloperSourceOutcomeUnresolvedException(DeveloperSourceChangePass originalPass)
    : InvalidOperationException("The source action has an unresolved original owner outcome. Inspect that original; do not replay it.")
{
    public DeveloperSourceChangePass OriginalPass { get; } = originalPass;
}
