using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>
/// Genuine manual (no-AI) source-edit owner over a resolved saved project. It composes the REAL
/// configured Files selection/capture, the genuine Home READ admission, the genuine Home WRITE
/// admission (broker-approved, private-issuer-recognized) and the kernel owner. Applying a
/// reviewed edit:
/// <list type="number">
/// <item>selects the SAME existing project (strictly beneath the configured Files root) and
/// captures it under the kernel owner;</item>
/// <item>acquires the actual Home READ admission (used to read the exact current file revision)
/// and the actual Home WRITE admission for that exact file/revision;</item>
/// <item>calls
/// <see cref="IDeveloperProjectOriginalPhysicalCaptureSource.ApplyOriginalCapturedFileEditAsync"/>,
/// which revalidates BOTH admissions, refuses unless the current captured hash equals the reviewed
/// expected revision, applies the reviewed replacement through the SHARED atomic engine under the
/// WRITE admission, then re-reads the committed inode and verifies the after hash.</item>
/// </list>
/// No process execution, restore/build, terminal or inference occurs here; a READ admission alone
/// is never treated as WRITE permission.
/// </summary>
public sealed class DeveloperManualSourceEditOwner(
    NativeFilesWorkspaceService files,
    FilesDeveloperOriginalSourceSelection selections,
    IDeveloperProjectOriginalReadAdmissionSource reads,
    IDeveloperProjectOriginalEditAdmissionSource edits,
    IDeveloperProjectOriginalPhysicalCaptureSource capture)
{
    /// <summary>Reads one exact captured source file's complete text through the genuine Home/Files/
    /// kernel owner (never raw path IO) and returns it with its current SHA-256 revision. The captured
    /// read itself refuses any stale/mutated source, so the returned hash is the exact current revision.</summary>
    public async Task<(string Text, string Sha256)> ReadAsync(DeveloperResolvedProject project, string relativePath, CancellationToken token)
    {
        DemandSafeProjectRelativePath(relativePath);
        await using var lease = await AcquireReadAsync(project, token).ConfigureAwait(false);
        var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.ReadAdmission, relativePath, token).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The requested source file is not part of the genuine saved-project capture.", relativePath);
        return (text, Sha256(text));
    }

    /// <summary>Applies one exactly reviewed source replacement. It selects/captures the SAME project,
    /// acquires genuine READ + WRITE admissions over the exact file/revision, and routes the reviewed
    /// mutation through the kernel's shared atomic engine. It returns the post-write SHA-256 after the
    /// kernel re-reads the committed inode and verifies it. READ approval never substitutes for the
    /// required WRITE approval.</summary>
    public async Task<string> ApplyAsync(DeveloperResolvedProject project, string relativePath,
        string expectedSha256, string newText, CancellationToken token)
    {
        DemandSafeProjectRelativePath(relativePath);
        if (!IsHash(expectedSha256)) throw new ArgumentException("The exact reviewed current SHA-256 revision is required.", nameof(expectedSha256));
        var configuration = await files.GetConfigurationAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
        var selection = await selections.SelectOriginalRegisteredProjectAsync(configuration.StoreId, project, token).ConfigureAwait(false);
        var readAdmission = await reads.AcquireOriginalAsync(selection, token).ConfigureAwait(false);
        try
        {
            var captured = await selections.CaptureOriginalAsync(selection, readAdmission, token).ConfigureAwait(false);
            var edit = await edits.AcquireOriginalEditAsync(selection, captured, relativePath, expectedSha256, newText, token).ConfigureAwait(false);
            try
            {
                return await capture.ApplyOriginalCapturedFileEditAsync(captured, readAdmission, edit,
                    relativePath, expectedSha256, newText, token).ConfigureAwait(false);
            }
            finally { await edit.DisposeAsync().ConfigureAwait(false); }
        }
        finally { await readAdmission.DisposeAsync().ConfigureAwait(false); }
    }

    // Inputs are private editor snapshots, never Files identities or authority.
    // Every eligible file still enters this SAME owner's individual READ/WRITE
    // producer and shared atomic engine. No project-wide write grant is created.
    internal Task<IReadOnlyList<DeveloperManualSaveOutcome>> SaveReviewedAsync(DeveloperResolvedProject project,
        IReadOnlyList<DeveloperManualSaveRequest> requests, Action<Action> sourceScope, CancellationToken businessToken)
        => ExecuteReviewedBatchAsync(requests,
            (request, token) => ApplyAsync(project, request.RelativePath, request.BeforeSha256, request.DraftText, token),
            (request, token) => ReadAsync(project, request.RelativePath, token), sourceScope, businessToken);

    // An internal orchestration seam for focused unit fixtures. Production always
    // supplies the genuine methods above, not a replacement mutation/read owner.
    internal static async Task<IReadOnlyList<DeveloperManualSaveOutcome>> ExecuteReviewedBatchAsync(
        IReadOnlyList<DeveloperManualSaveRequest> requests,
        Func<DeveloperManualSaveRequest, CancellationToken, Task<string>> applyOriginal,
        Func<DeveloperManualSaveRequest, CancellationToken, Task<(string Text, string Sha256)>> readOriginal,
        Action<Action> sourceScope, CancellationToken businessToken)
    {
        ArgumentNullException.ThrowIfNull(requests); ArgumentNullException.ThrowIfNull(applyOriginal);
        ArgumentNullException.ThrowIfNull(readOriginal); ArgumentNullException.ThrowIfNull(sourceScope);
        var captured = requests.ToArray();
        if (captured.Length > DeveloperSourceReviewSession.MaximumOpenDocuments || captured.Any(value => value is null) ||
            captured.Any(value => value.TabId == Guid.Empty) || captured.Select(value => value.TabId).Distinct().Count() != captured.Length ||
            captured.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() != captured.Length)
            throw new ArgumentException("Save reviewed requires a bounded unambiguous snapshot of the actual open tabs.", nameof(requests));
        foreach (var request in captured) DemandSafeProjectRelativePath(request.RelativePath);
        var outcomes = new List<DeveloperManualSaveOutcome>();
        foreach (var request in captured)
        {
            if (!request.Reviewed || request.ReviewRevision != request.DraftRevision ||
                !IsHash(request.BeforeSha256) || !StringComparer.Ordinal.Equals(Sha256(request.DraftText), request.AfterSha256))
            {
                outcomes.Add(new(request, DeveloperManualSaveState.NeedsReview, "Review the current draft first; no write was requested.", null, false, [], []));
                continue;
            }
            if (businessToken.IsCancellationRequested)
            {
                outcomes.Add(new(request, DeveloperManualSaveState.Cancelled, "Cancelled before file admission; the draft was retained.", null, false, [], []));
                continue;
            }
            var originals = new CloudflareOriginalTaskLedger();
            originals.BindOriginalOwner(originals); originals.BindOriginalCallerCallback(sourceScope);
            var state = DeveloperManualSaveState.Failed;
            var detail = "No verified write result is available; inspect this original operation before retrying.";
            string? savedSha256 = null; var writeStarted = false;
            try
            {
                await originals.RunToOriginalSettlementAsync(async () =>
                {
                    var current = await originals.AwaitAsync(Start(() => readOriginal(request, businessToken))).ConfigureAwait(false);
                    if (!StringComparer.Ordinal.Equals(current.Sha256, request.BeforeSha256) ||
                        !StringComparer.Ordinal.Equals(current.Text, request.BaselineText))
                    {
                        state = DeveloperManualSaveState.Conflict;
                        detail = "Source changed outside this draft; no write was requested. Resolve the conflict and review again.";
                        return true;
                    }
                    savedSha256 = await originals.AwaitAsync(Start(() =>
                    {
                        writeStarted = true; return applyOriginal(request, businessToken);
                    })).ConfigureAwait(false);
                    if (!StringComparer.Ordinal.Equals(savedSha256, request.AfterSha256))
                        throw new InvalidDataException("The original write acknowledged a different source revision.");
                    var reopened = await originals.AwaitAsync(Start(() => readOriginal(request, businessToken))).ConfigureAwait(false);
                    if (!StringComparer.Ordinal.Equals(reopened.Text, request.DraftText) ||
                        !StringComparer.Ordinal.Equals(reopened.Sha256, savedSha256))
                        throw new InvalidDataException("The original applied edit did not reopen at its exact reviewed revision.");
                    state = DeveloperManualSaveState.Saved;
                    detail = "Saved through individual Home WRITE and verified by reopening the exact source revision.";
                    return true;
                }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // The ledger has independently joined every raw child, including
                // one issued before a finite callback refused. Keep direct causes.
                originals.Retain(error);
                var canceled = businessToken.IsCancellationRequested && originals.OriginalTasks.Any(actual => actual.IsCanceled) &&
                    originals.OriginalTasks.All(actual => !actual.IsFaulted);
                state = canceled ? DeveloperManualSaveState.Cancelled : DeveloperManualSaveState.Failed;
                var cause = originals.OriginalErrors.FirstOrDefault(value => value is not AggregateException) ?? error;
                var message = cause.Message.Length > 240 ? cause.Message[..240] + "…" : cause.Message;
                detail = (writeStarted ? "Write outcome needs inspection; draft retained. " : "Save was not admitted; draft retained. ") + message;
            }
            outcomes.Add(new(request, state, detail, state == DeveloperManualSaveState.Saved ? savedSha256 : null,
                writeStarted, originals.OriginalTasks, originals.OriginalErrors));

            Task<T> Start<T>(Func<Task<T>> factory) => originals.Invoke(() =>
            {
                if (originals.OriginalErrors.Count != 0)
                    throw new InvalidOperationException("A prior original source refusal prevents further file stages.");
                businessToken.ThrowIfCancellationRequested();
                return originals.Track(factory());
            });
        }
        return Array.AsReadOnly(outcomes.ToArray());
    }

    // Private view-state reconciliation only; a result here cannot mutate Files.
    // Callers retain an unchanged live buffer when another baseline superseded
    // this snapshot, or retain newer typing above the genuinely saved baseline.
    internal static DeveloperManualDraftReconciliation? ReconcileSavedDraft(DeveloperManualSaveOutcome outcome,
        string liveBaseline, string liveBeforeSha256, string liveDraft, long liveRevision)
    {
        var request = outcome.Request;
        if (outcome.State != DeveloperManualSaveState.Saved || outcome.SavedSha256 is not { } actualHash ||
            !StringComparer.Ordinal.Equals(actualHash, request.AfterSha256) ||
            !StringComparer.Ordinal.Equals(Sha256(request.DraftText), actualHash) ||
            !StringComparer.Ordinal.Equals(liveBaseline, request.BaselineText) ||
            !StringComparer.Ordinal.Equals(liveBeforeSha256, request.BeforeSha256) ||
            liveRevision < request.DraftRevision || liveRevision == long.MaxValue) return null;
        var newer = liveRevision != request.DraftRevision || !StringComparer.Ordinal.Equals(liveDraft, request.DraftText);
        return new(request.DraftText, actualHash, newer ? liveDraft : request.DraftText, liveRevision + 1);
    }

    private async Task<ReadLease> AcquireReadAsync(DeveloperResolvedProject project, CancellationToken token)
    {
        var configuration = await files.GetConfigurationAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
        var selection = await selections.SelectOriginalRegisteredProjectAsync(configuration.StoreId, project, token).ConfigureAwait(false);
        var readAdmission = await reads.AcquireOriginalAsync(selection, token).ConfigureAwait(false);
        try
        {
            var captureValue = await selections.CaptureOriginalAsync(selection, readAdmission, token).ConfigureAwait(false);
            return new(selection, readAdmission, captureValue);
        }
        catch (Exception error)
        {
            try { await readAdmission.DisposeAsync().ConfigureAwait(false); }
            catch (Exception disposeError) { throw new AggregateException("Manual source read acquisition failed and its genuine READ admission did not close cleanly.", error, disposeError); }
            throw;
        }
    }

    private sealed record ReadLease(IDeveloperProjectOriginalReadSelection Selection,
        IDeveloperProjectOriginalReadAdmission ReadAdmission, IDeveloperProjectOriginalExistingSourceCapture Capture)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ReadAdmission.DisposeAsync();
    }

    internal static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void DemandSafeProjectRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Length > 2048 || path.IndexOf('\0') >= 0 ||
            path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(value => value is "." or ".."))
            throw new ArgumentException("A safe project-relative source path is required.", nameof(path));
    }
}

// Immutable manual draft/review snapshots. TabId selects the already-owned view
// buffer only; it is never converted into a Files ID or a permission receipt.
internal sealed record DeveloperManualSaveRequest(Guid TabId, string RelativePath, string BaselineText,
    string BeforeSha256, string DraftText, string AfterSha256, long DraftRevision, long ReviewRevision, bool Reviewed);
internal enum DeveloperManualSaveState { Saved, NeedsReview, Conflict, Failed, Cancelled }
internal sealed record DeveloperManualSaveOutcome(DeveloperManualSaveRequest Request, DeveloperManualSaveState State,
    string Detail, string? SavedSha256, bool WriteStarted, IReadOnlyList<Task> OriginalSources,
    IReadOnlyList<Exception> OriginalFailures);
internal sealed record DeveloperManualDraftReconciliation(string Baseline, string BeforeSha256, string Draft, long DraftRevision);
