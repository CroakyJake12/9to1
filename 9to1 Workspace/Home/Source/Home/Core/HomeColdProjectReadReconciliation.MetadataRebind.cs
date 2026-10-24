using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Original source transition only. A new selection requires its own real Home READ
/// approval; an old grant is never attached to that new selection. Task/capsule migration is
/// intentionally separate. Original owners retain every old READ and permission lease.</summary>
public sealed partial class HomeColdProjectReadReconciliation : IDeveloperOriginalProjectMetadataRebindSource
{
    private sealed class MetadataRebindOperation { internal Task? Driver; }
    private sealed class MetadataRebindPreparation(HomeColdProjectReadReconciliation owner,
        MetadataRebindOperation operation, CommandRead oldRead, TaskRunColdProjectIdentity before,
        IDeveloperProjectOriginalMetadataTransitionStore store, string beforeDocument)
        : IDeveloperOriginalProjectMetadataRebindPreparation
    {
        internal HomeColdProjectReadReconciliation Owner => owner;
        internal MetadataRebindOperation Operation => operation;
        internal CommandRead OldRead => oldRead;
        internal IDeveloperProjectOriginalMetadataTransitionStore Store => store;
        internal string BeforeDocument => beforeDocument;
        public IDeveloperOriginalProjectCommandRead OriginalCommandRead => oldRead;
        public IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore => store;
        public TaskRunColdProjectIdentity BeforeIdentity => before;
    }
    private sealed class MetadataRebindResult(HomeColdProjectReadReconciliation owner,
        MetadataRebindOperation operation, MetadataRebindPreparation preparation,
        IDeveloperProjectOriginalMetadataTransition transition, IDeveloperProjectOriginalMetadataCommitValue committedValue,
        CommandRead newRead)
        : IDeveloperOriginalProjectMetadataRebindResult
    {
        internal HomeColdProjectReadReconciliation Owner => owner;
        internal MetadataRebindOperation Operation => operation;
        internal CommandRead NewRead => newRead;
        public IDeveloperOriginalProjectMetadataRebindPreparation OriginalPreparation => preparation;
        public IDeveloperProjectOriginalMetadataTransition OriginalTransition => transition;
        public IDeveloperProjectOriginalMetadataCommitValue OriginalCommittedValue => committedValue;
        public IDeveloperOriginalProjectCommandRead NewOriginalCommandRead => newRead;
        public TaskRunColdProjectIdentity AfterIdentity => newRead.OriginalIdentity;
    }
    private readonly ConditionalWeakTable<MetadataRebindPreparation, MetadataRebindOperation> _metadataRebindPreparations = new();
    private readonly ConditionalWeakTable<MetadataRebindResult, MetadataRebindOperation> _metadataRebindResults = new();

    public bool IsIssuedOriginalMetadataRebindPreparation(IDeveloperOriginalProjectMetadataRebindPreparation value)
    {
        lock (_gate)
            return !_retiring && value is MetadataRebindPreparation actual && ReferenceEquals(actual.Owner, this) &&
                _metadataRebindPreparations.TryGetValue(actual, out var operation) && ReferenceEquals(operation, actual.Operation) &&
                operation.Driver?.IsCompletedSuccessfully == true && IsIssuedOriginalCommandRead(actual.OldRead);
    }
    private MetadataRebindPreparation RequireMetadataRebindPreparation(IDeveloperOriginalProjectMetadataRebindPreparation value)
        => IsIssuedOriginalMetadataRebindPreparation(value) && value is MetadataRebindPreparation actual ? actual
            : throw new UnauthorizedAccessException("SAME successful live Home before-native metadata preparation required.");

    public Task<IDeveloperOriginalProjectMetadataRebindPreparation> PrepareOriginalMetadataRebindWithinSourceAsync(
        IDeveloperOriginalProjectCommandRead sameOldRead, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var old = RequireCommandRead(sameOldRead, true);
        var operation = new MetadataRebindOperation();
        var driver = old.Original.StartCommandOperation<IDeveloperOriginalProjectMetadataRebindPreparation>(scope, retain, async sources =>
        {
            var store = old.OriginalDescriptor.OriginalStore as IDeveloperProjectOriginalMetadataTransitionStore
                ?? throw new InvalidOperationException("The original canonical store has no metadata-transition provenance source.");
            await old.Original.ValidateReadAsync(sources, token).ConfigureAwait(false);
            CommandNativeRead? capture = null; Exception? primary = null;
            string? beforeDocument = null; var before = old.OriginalIdentity;
            try
            {
                capture = await old.Original.CaptureCommandNativeAsync(old, sources, token).ConfigureAwait(false);
                await ValidateCommandNativeAsync(old, capture, sources, token).ConfigureAwait(false);
                sources.Invoke(() =>
                {
                    if (!StringComparer.Ordinal.Equals(capture.Read.OriginalWorkspaceDocumentSha256, before.SavedWorkspaceDocumentSha256) ||
                        !StringComparer.Ordinal.Equals(capture.Read.OriginalRegisteredRootFingerprint, before.RegisteredRootFingerprint))
                        throw new UnauthorizedAccessException("The original command identity changed before metadata transition preparation.");
                    capture.Read.DemandOriginalExecutionBinding();
                    beforeDocument = capture.Read.OriginalWorkspaceDocument;
                    return true;
                });
            }
            catch (Exception cause) { primary = cause; }
            if (capture is not null)
            {
                try { await CloseMetadataBeforeCaptureAsync(old, capture, sources).ConfigureAwait(false); }
                catch (Exception cleanup)
                { throw new AggregateException("Original before-native read and its exact healthy close did not settle.", primary is null ? [cleanup] : [primary, cleanup]); }
            }
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            if (beforeDocument is null) throw new InvalidOperationException("No original before-native document was retained.");
            await old.Original.ValidateReadAsync(sources, token).ConfigureAwait(false);
            return sources.Invoke(() =>
            {
                _ = RequireCommandRead(old, true);
                var preparation = new MetadataRebindPreparation(this, operation, old, before, store, beforeDocument);
                lock (_gate) _metadataRebindPreparations.Add(preparation, operation);
                return (IDeveloperOriginalProjectMetadataRebindPreparation)preparation;
            });
        });
        operation.Driver = driver;
        return driver;
    }

    private async Task CloseMetadataBeforeCaptureAsync(CommandRead old, CommandNativeRead product, Context sources)
    {
        Task? close = null; var closes = new List<Task>();
        sources.AcquireClose(() => { var actual = product.Read.DisposeAsync().AsTask(); close = actual; return actual; }, closes);
        foreach (var actual in closes)
            try { await sources.Await(actual).ConfigureAwait(false); }
            catch (Exception cause) { sources.Errors.Retain(cause); }
        if (close is null || !sources.Physical(() => _actualNative!.IsClosedOriginalRead(old.OriginalSelection,
            product.Capture, product.Read, close)))
            throw new UnauthorizedAccessException("The SAME before-native capture has no healthy original close proof.");
        if (!close.IsCompletedSuccessfully) throw new InvalidOperationException("The original before-native close failed.");
    }

    public Task<IDeveloperOriginalProjectMetadataRebindResult> CompleteOriginalMetadataRebindWithinSourceAsync(
        IDeveloperOriginalProjectMetadataRebindPreparation samePreparation,
        IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameCommittedValue,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var preparation = RequireMetadataRebindPreparation(samePreparation);
        var operation = new MetadataRebindOperation();
        var driver = StartOperation<IDeveloperOriginalProjectMetadataRebindResult>(scope, retain, async sources =>
        {
            sources.Invoke(() =>
            {
                _ = RequireMetadataRebindPreparation(preparation);
                if (!preparation.Store.IsIssuedOriginalMetadataTransition(sameTransition, sameCommittedValue))
                    throw new UnauthorizedAccessException("The SAME canonical store's original metadata receipt and committed value are required.");
                if (!ReferenceEquals(sameTransition.OriginalStore, preparation.Store) ||
                    sameTransition.WorkspaceId != preparation.BeforeIdentity.WorkspaceId ||
                    !StringComparer.Ordinal.Equals(sameTransition.BeforeDocumentSha256, preparation.BeforeIdentity.SavedWorkspaceDocumentSha256))
                    throw new UnauthorizedAccessException("The actual metadata transition does not begin at this exact native command observation.");
                return true;
            });
            await sources.Await(preparation.OldRead.Original.StartCommandOperation(sources.Scope, sources.Retain, async beforeSources =>
            { await preparation.OldRead.Original.ValidateReadAsync(beforeSources, token).ConfigureAwait(false); return true; })).ConfigureAwait(false);
            CommandRead? fresh = null; var published = false;
            try
            {
                var read = await sources.Capture(() =>
                {
                    // Internal child admission, matching the existing closed-restoration
                    // path: public Reserve would demand an external join from this live owner.
                    var child = ReserveAdmitted(null);
                    var command = new CommandRead(this, child);
                    lock (_gate) child._command = command;
                    return child.StartCommandRead(command,
                        preparation.OldRead.OriginalDescriptor.OriginalConversation,
                        preparation.OldRead.OriginalDescriptor.OriginalContainer,
                        preparation.OldRead.OriginalDescriptor.ExactProjectReferenceJson,
                        sources.Scope, sources.Retain, token);
                }, actual =>
                    {
                        // Historical ownership is recorded before any live-use/publication check.
                        if (actual is CommandRead owned && ReferenceEquals(owned.Owner, this)) fresh = owned;
                    }).ConfigureAwait(false);
                if (fresh is null || !ReferenceEquals(fresh, read) || !IsOwnedOriginalCommandRead(fresh))
                    throw new UnauthorizedAccessException("The newly approved READ lacks SAME original Home ownership.");
                CommandRead actualFresh = fresh;
                sources.Invoke(() =>
                {
                    _ = RequireMetadataRebindPreparation(preparation);
                    _ = RequireCommandRead(actualFresh, true);
                    var after = actualFresh.OriginalIdentity; var before = preparation.BeforeIdentity;
                    // Only metadata document bytes may change. Actor, root, container, complete
                    // project tuple and permissions remain the SAME; after has its own new READ.
                    if (before with { SavedWorkspaceDocumentSha256 = after.SavedWorkspaceDocumentSha256 } != after ||
                        !StringComparer.Ordinal.Equals(after.SavedWorkspaceDocumentSha256, sameTransition.AfterDocumentSha256) ||
                        !ReferenceEquals(actualFresh.OriginalDescriptor.OriginalStore, preparation.Store))
                        throw new UnauthorizedAccessException("The newly approved after-native project/root/actor identity differs.");
                    return true;
                });
                // StartCommandRead already acquired/validated the genuine after-native read and
                // joined its short Home/completion/native borrowers before publishing Identity.
                // Re-observe after-native bytes under the NEW read and close this exact borrower.
                await sources.Await(actualFresh.Original.StartCommandOperation(sources.Scope, sources.Retain, async afterSources =>
                {
                    var currentCapture = await actualFresh.Original.CaptureCommandNativeAsync(actualFresh, afterSources, token).ConfigureAwait(false);
                    Exception? validation = null;
                    try
                    {
                        await ValidateCommandNativeAsync(actualFresh, currentCapture, afterSources, token).ConfigureAwait(false);
                        afterSources.Invoke(() =>
                        {
                            if (!preparation.Store.VerifyOriginalMetadataTransitionDocuments(sameTransition, sameCommittedValue,
                                preparation.BeforeDocument, currentCapture.Read.OriginalWorkspaceDocument) ||
                                !StringComparer.Ordinal.Equals(currentCapture.Read.OriginalWorkspaceDocumentSha256, sameTransition.AfterDocumentSha256) ||
                                !StringComparer.Ordinal.Equals(currentCapture.Read.OriginalRegisteredRootFingerprint, preparation.BeforeIdentity.RegisteredRootFingerprint))
                                throw new UnauthorizedAccessException("The actual native document pair/root does not match the original unchanged-authority transition.");
                            currentCapture.Read.DemandOriginalExecutionBinding();
                            return true;
                        });
                    }
                    catch (Exception cause) { validation = cause; }
                    try { await CloseMetadataBeforeCaptureAsync(actualFresh, currentCapture, afterSources).ConfigureAwait(false); }
                    catch (Exception cleanup)
                    { throw new AggregateException("After-native verification and exact close did not settle.", validation is null ? [cleanup] : [validation, cleanup]); }
                    if (validation is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(validation).Throw();
                    return true;
                })).ConfigureAwait(false);
                await sources.Await(actualFresh.Original.StartCommandOperation(sources.Scope, sources.Retain, async afterSources =>
                { await actualFresh.Original.ValidateReadAsync(afterSources, token).ConfigureAwait(false); return true; })).ConfigureAwait(false);
                var result = sources.Invoke(() =>
                {
                    _ = RequireMetadataRebindPreparation(preparation); _ = RequireCommandRead(actualFresh, true);
                    var product = new MetadataRebindResult(this, operation, preparation, sameTransition, sameCommittedValue, actualFresh);
                    lock (_gate) _metadataRebindResults.Add(product, operation);
                    return (IDeveloperOriginalProjectMetadataRebindResult)product;
                });
                published = true;
                return result;
            }
            catch (Exception cause) { sources.Errors.Retain(cause); throw; }
            finally
            {
                if (!published && fresh is not null)
                {
                    // Only this newly acquired child is ours to close. The original old READ
                    // and every other caller's leases remain untouched.
                    Task? close = null; var closes = new List<Task>();
                    sources.AcquireClose(() => { var actual = fresh.Original.CloseOwned(); close = actual; return actual; }, closes);
                    foreach (var actual in closes) await sources.Await(actual).ConfigureAwait(false);
                }
            }
        });
        operation.Driver = driver; return driver;
    }

    public bool IsOwnedOriginalMetadataRebindResult(IDeveloperOriginalProjectMetadataRebindResult value)
    {
        lock (_gate)
            return value is MetadataRebindResult actual && ReferenceEquals(actual.Owner, this) &&
                _metadataRebindResults.TryGetValue(actual, out var operation) && ReferenceEquals(actual.Operation, operation);
    }
    public bool IsIssuedOriginalMetadataRebindResult(IDeveloperOriginalProjectMetadataRebindResult value)
    {
        lock (_gate)
            return !_retiring && value is MetadataRebindResult actual && IsOwnedOriginalMetadataRebindResult(actual) &&
                actual.Operation.Driver?.IsCompletedSuccessfully == true && IsIssuedOriginalCommandRead(actual.NewRead);
    }
}
