using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Finite, READ-only views of the maintained automation rows. The SAME
/// protected SQLite/current actor/current canonical.sqlite Home receipt precedes
/// metadata SQL. A library observation never admits a writer or scheduler.</summary>
public sealed partial class CanonicalAutomationLibraryOriginalReadOwner
{
    public enum LibraryState { Available, SetupRequired }
    public interface IOriginalLibraryObservation
    {
        AuthenticatedResourceActor Actor { get; }
        ResourceStoreIdentity? OriginalStoreIdentity { get; }
        LibraryState State { get; }
        string Detail { get; }
        IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions { get; }
        IOriginalLibraryContinuation? NextContinuation { get; }
    }
    public interface IOriginalLibraryContinuation { }

    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly AutomationRepository _repository;
    private readonly IAppPaths _paths;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly ConditionalWeakTable<IOriginalLibraryObservation, Observation> _observations = new();
    private readonly ConditionalWeakTable<IOriginalLibraryContinuation, Continuation> _continuations = new();

    private sealed record Query(bool IncludeDisabled, bool IncludeArchived, string Search, int Limit);
    private sealed class Continuation(CanonicalAutomationLibraryOriginalReadOwner owner,
        AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        VerifiedResourceStoreOwnership receipt, Query query, string updatedAt, string id)
        : IOriginalLibraryContinuation
    {
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Owner = owner;
        internal readonly AuthenticatedResourceActor Actor = actor;
        internal readonly ResourceStoreIdentity Identity = identity;
        internal readonly VerifiedResourceStoreOwnership Receipt = receipt;
        internal readonly Query Query = query;
        internal readonly string UpdatedAt = updatedAt, Id = id;
    }
    private sealed class Observation(CanonicalAutomationLibraryOriginalReadOwner owner,
        AuthenticatedResourceActor actor, Query query, Continuation? originalWindow,
        LibraryState state, string detail, IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> definitions,
        ResourceStoreIdentity? identity = null, VerifiedResourceStoreOwnership? receipt = null,
        Continuation? next = null) : IOriginalLibraryObservation
    {
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Owner = owner;
        internal readonly Query Query = query;
        internal readonly Continuation? OriginalWindow = originalWindow;
        internal readonly VerifiedResourceStoreOwnership? Receipt = receipt;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public ResourceStoreIdentity? OriginalStoreIdentity { get; } = identity;
        public LibraryState State { get; } = state;
        public string Detail { get; } = detail;
        public IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions { get; } = definitions;
        public IOriginalLibraryContinuation? NextContinuation { get; } = next;
    }

    public CanonicalAutomationLibraryOriginalReadOwner(CanonicalSqliteOriginalStoreOwner sameStore,
        SqliteDatabase sameDatabase, IAppPaths samePaths, AutomationRepository sameRepository,
        HomeResourceStoreOwnershipAuthority sameOwnership)
    {
        if (!sameStore.HasOriginalComposition(sameDatabase, samePaths, sameStore.OriginalProfiles) ||
            !sameRepository.HasOriginalSqliteFactory(sameDatabase))
            throw new UnauthorizedAccessException("Use the SAME actual canonical store/database/paths/automation repository.");
        _store = sameStore; _repository = sameRepository; _paths = samePaths; _ownership = sameOwnership;
    }
    public CanonicalSqliteOriginalStoreOwner OriginalStore => _store;
    public AutomationRepository OriginalRepository => _repository;
    public HomeResourceStoreOwnershipAuthority OriginalOwnership => _ownership;
    public bool IsIssuedOriginalObservation(IOriginalLibraryObservation actual) =>
        actual is Observation observed && ReferenceEquals(observed.Owner, this) &&
        _observations.TryGetValue(actual, out var same) && ReferenceEquals(same, observed);
    public bool IsIssuedOriginalContinuation(IOriginalLibraryContinuation actual) =>
        actual is Continuation cursor && ReferenceEquals(cursor.Owner, this) &&
        _continuations.TryGetValue(actual, out var same) && ReferenceEquals(same, cursor);

    public Task<IOriginalLibraryObservation> ReadOriginalLibraryWithinSourceAsync(
        AuthenticatedResourceActor actualActor, AutomationLibraryQuery query,
        Action<Action> scope, Action<Task> retain, CancellationToken token,
        IOriginalLibraryContinuation? continuation = null)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader<IOriginalLibraryObservation>(source, async () =>
        {
            var actualQuery = source.Invoke(() => CaptureQuery(query));
            var window = source.Invoke(() => continuation is null ? null :
                IsIssuedOriginalContinuation(continuation) && ((Continuation)continuation).Actor == actualActor &&
                ((Continuation)continuation).Query == actualQuery ? (Continuation)continuation :
                    throw new UnauthorizedAccessException("Use the SAME source-issued actor/query/page continuation."));
            await DemandActor(actualActor, source, token).ConfigureAwait(false);
            var exists = source.Invoke(() =>
            {
                try { _ = File.GetAttributes(_paths.DatabasePath); return true; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
            });
            if (!exists) return await Setup("The existing canonical SQLite store is unavailable.").ConfigureAwait(false);
            var identity = await source.Read(() => _store.ReadExistingStoreIdentityWithinOriginalSourceAsync(
                actualActor, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (identity is null) return await Setup("The actual canonical SQLite identity requires its owning setup lifecycle.").ConfigureAwait(false);
            var receipt = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync("canonical.sqlite",
                identity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
            if (receipt is null) return await Setup("Import this actual canonical SQLite store through Home before reading automation metadata.").ConfigureAwait(false);
            await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
            // A swallowed callback protocol/body failure remains sticky in this SAME
            // source. Independently join its originals before acquiring metadata custody.
            await source.JoinAllAsync().ConfigureAwait(false);
            if (window is not null && (window.Identity != identity || window.Receipt != receipt))
                throw new UnauthorizedAccessException("The actual store/Home READ receipt changed. Start a fresh library page.");

            CanonicalSqliteOriginalStoreLease? lease = null; Page? page = null;
            var failures = new List<Exception>(); bool schemaPresent = false;
            try
            {
                await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actualActor, false,
                    source.Run, source.Retain, token), actual => lease = actual).ConfigureAwait(false);
                source.Run(() =>
                {
                    if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                        throw new UnauthorizedAccessException("The SAME protected store lease/identity is required.");
                });
                await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
                // The actual held lease stays retained; joining finite source children
                // does not close it or lend a permission from a previous observation.
                await source.JoinAllAsync().ConfigureAwait(false);
                schemaPresent = await HasExistingTable(lease!, token).ConfigureAwait(false);
                if (schemaPresent) page = await ReadPage(lease!, actualQuery, window, token).ConfigureAwait(false);
                await source.Read(() => lease!.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
            }
            catch (Exception cause) { failures.Add(cause); }
            // Capture and independently join the SAME raw close before publication,
            // including a late lease returned after callback/retainer rejection.
            Task? close = null;
            if (lease is not null)
                try { close = lease.CloseAndDrainAsync(); source.Retain(close); await close.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, close, cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(cause); }
            CanonicalSqliteOriginalStoreOwner.Throw(failures);
            await DemandActor(actualActor, source, token).ConfigureAwait(false);
            var current = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actualActor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (current != identity) throw new UnauthorizedAccessException("The canonical store changed before library publication.");
            await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            Continuation? next = null;
            if (page is { HasMore: true, Last: { } last })
                next = new(this, actualActor, identity, receipt, actualQuery, last.UpdatedAt, last.Id);
            var result = new Observation(this, actualActor, actualQuery, window,
                schemaPresent ? LibraryState.Available : LibraryState.SetupRequired,
                schemaPresent ? "Actual maintained automation rows are READ-only. Original owner review/recovery is required before protected editing or execution."
                    : "The actual automation table is unavailable. READ does not initialize it.",
                page?.Definitions ?? [], identity, receipt, next);
            source.Run(() =>
            {
                if (next is not null) _continuations.Add(next, next);
                _observations.Add(result, result);
            });
            return result;

            async Task<IOriginalLibraryObservation> Setup(string detail)
            {
                await DemandActor(actualActor, source, token).ConfigureAwait(false);
                await source.JoinAllAsync().ConfigureAwait(false);
                var unavailable = new Observation(this, actualActor, actualQuery, window, LibraryState.SetupRequired, detail, []);
                source.Run(() => _observations.Add(unavailable, unavailable)); return unavailable;
            }
        });
    }

    public Task RevalidateOriginalObservationWithinSourceAsync(IOriginalLibraryObservation sameActual,
        AuthenticatedResourceActor actualActor, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader(source, async () =>
        {
            var original = source.Invoke(() => IsIssuedOriginalObservation(sameActual) && sameActual.Actor == actualActor
                ? (Observation)sameActual : throw new UnauthorizedAccessException("The SAME library source/actor must issue this observation."));
            var query = original.Query;
            var current = (Observation)await source.Read(() => ReadOriginalLibraryWithinSourceAsync(actualActor,
                new(query.IncludeDisabled, query.IncludeArchived, query.Search, query.Limit), source.Run, source.Retain,
                token, original.OriginalWindow)).ConfigureAwait(false);
            source.Run(() =>
            {
                if (original.State != current.State || original.OriginalStoreIdentity != current.OriginalStoreIdentity ||
                    original.Receipt != current.Receipt || !SameRows(original.Definitions, current.Definitions) ||
                    !SameNext(original.NextContinuation, current.NextContinuation))
                    throw new UnauthorizedAccessException("The actual original library page changed. Refresh that page before using its rows.");
            });
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        });
    }
    private static Query CaptureQuery(AutomationLibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cursor is not null || query.Limit is < 1 or > 64 || query.Search is null || query.Search.Length > 256)
            throw new ArgumentException("Use a bounded literal search and the actual opaque continuation; serialized cursor strings are not selections.");
        return new(query.IncludeDisabled, query.IncludeArchived, query.Search, query.Limit);
    }
    private static bool SameNext(IOriginalLibraryContinuation? first, IOriginalLibraryContinuation? second) =>
        first is null ? second is null : first is Continuation a && second is Continuation b &&
        a.Actor == b.Actor && a.Identity == b.Identity && a.Receipt == b.Receipt && a.Query == b.Query &&
        a.UpdatedAt == b.UpdatedAt && a.Id == b.Id;
    private async Task DemandActor(AuthenticatedResourceActor actor, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        var actual = await source.Read(() => _store.OriginalProfiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (actual != actor) throw new UnauthorizedAccessException("The actual current canonical store actor changed before metadata READ.");
    }
    private async Task DemandReceipt(VerifiedResourceStoreOwnership receipt, ResourceStoreIdentity identity,
        AuthenticatedResourceActor actor, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        if (receipt.ResourceKind != "canonical.sqlite" || receipt.ProfileId != actor.ProfileId || receipt.Receipt is null ||
            receipt.StoreId != identity.StoreId.ToString("D") || !await source.Read(() =>
                _ownership.IsCurrentWithinOriginalSourceAsync(receipt, actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("A current original canonical.sqlite Home READ receipt is required.");
    }
}
