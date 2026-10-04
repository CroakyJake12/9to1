using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.Apps;

/// <summary>
/// Configuration-bound artifact/action provider. Detached bytes and mapping records are observations;
/// the authenticated root endpoint must verify protected publisher policy and exact payload itself.
/// No default provider, catalogue registry, machine path, publisher key or release declaration exists here.
/// </summary>
public interface IHomePackageOriginalArtifactProvider
{
    ValueTask<HomePackageOriginalArtifactObservation?> ResolveAsync(HomePackageActionRequest request,
        HomePackageDatabaseSnapshot originalDeviceRegistry, CancellationToken cancellationToken);
    HomePackageOriginalActionMap? ResolveAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor);
    Task DemandOriginalCurrentAsync(HomePackageOriginalArtifactObservation originalArtifact,
        AuthenticatedResourceActor originalActor, HomeNativeInstalledPeer originalCaller,
        string originalSessionId, CancellationToken cancellationToken);
}

public sealed record HomePackageOriginalArtifactObservation(ReadOnlyMemory<byte> SignedDescriptorBytes,
    ReadOnlyMemory<byte> DescriptorPayloadBytes, string CatalogueRevision, object OriginalProviderEvidence);
public sealed record HomePackageOriginalActionMap(string TargetAppId, string ActionId,
    string RequiredInstalledServiceId, IReadOnlyList<ResourceScope> Scopes);

/// <summary>
/// SAME configured original root channel. Open authenticates the actual existing root peer/control
/// session, original installed caller/actor/approval tuple and protected artifact; it performs no effect.
/// Implementations must refuse unsupported actions or unavailable enrollment, policy, payload or privilege.
/// Implementing this interface or supplying a descriptor alone is not privileged authority.
/// </summary>
public interface IHomePackageOriginalRootMutationPort
{
    Task<IHomePackageOriginalRootMutation> OpenOriginalAsync(HomePackageOriginalRootRequest originalRequest,
        CancellationToken cancellationToken);
}

/// <summary>
/// Privately constructed original request; metadata cannot recreate this instance. Currentness checks
/// run outside Home's writer. The genuine root endpoint independently reauthenticates its original channel
/// and installed/approval/payload tuple immediately before each privileged effect.
/// </summary>
public sealed class HomePackageOriginalRootRequest
{
    private readonly HomePackageOriginalInvocation _original;
    internal HomePackageOriginalRootRequest(HomePackageOriginalInvocation original) { _original = original; }
    public string OperationId => _original.Review.OperationId;
    public string OriginalApprovalRequestId => _original.Review.Prepared.RequestId;
    public HavenOS.Home.PermissionsTrustNotifications.HomePermissionActionPolicy OriginalActionPolicy => _original.Review.Policy;
    public string ReviewedArgumentsSha256 => HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(_original.Review.Arguments));
    public HomePackageActionRequest Action => _original.Review.Request with { };
    public AuthenticatedResourceActor Actor => _original.OriginalActor;
    public HomeNativeInstalledPeer InstalledCaller => _original.OriginalCaller;
    // Observation from the SAME privately retained accepted Context; serialization creates no authority.
    public HomeNativeObservedPeer? OriginalObservedPeer => _original.OriginalObservedPeer;
    public string SessionId => _original.OriginalSessionId;
    public HomePackageArtifactDescriptor Artifact => _original.Review.Artifact.Descriptor;
    public long OriginalRegistryRevision => _original.Review.RegistryRevision;
    public long OriginalPackageEntryRevision => _original.Review.OriginalPackageEntryRevision;
    public string CatalogueRevision => _original.Review.Artifact.CatalogueRevision;
    public string SignedDescriptorSha256 => _original.Review.Artifact.SignedDescriptorSha256;
    public ReadOnlyMemory<byte> CopySignedDescriptor() => _original.Review.Artifact.CopySignedDescriptor();
    public ReadOnlyMemory<byte> CopyDescriptorPayload() => _original.Review.Artifact.CopyDescriptorPayload();
    public Task DemandOriginalCurrentBeforeEffectAsync(CancellationToken cancellationToken) =>
        _original.DemandCurrentBeforeEffectAsync(cancellationToken);
}

/// <summary>
/// One authenticated root operation, retained through its original execute/query/close tasks. The
/// mutation may return the SAME known outcome after acknowledgement loss, but may never dispatch twice.
/// Guards must use genuinely bound raw-current owner state only; no broker/profile/store reentry.
/// Audit-only settlement can preserve an already admitted known effect after execution authority retires.
/// Cross-store checks are sequential observations unless the supplied original owner closes that race.
/// </summary>
public interface IHomePackageOriginalRootMutation : IAsyncDisposable
{
    HomePackageOriginalRootRequest OriginalRequest { get; }
    IHomeStateCommitActorGuard OriginalAdmissionGuard { get; }
    Task DemandOriginalChannelCurrentAsync(CancellationToken cancellationToken);
    Task<HomePackageOriginalRootOutcome> ExecuteOriginalAsync(CancellationToken cancellationToken);
    Task<HomePackageOriginalRootOutcome?> ObserveOriginalOutcomeAsync(CancellationToken settlementCancellationToken);
    IHomeStateCommitActorGuard OriginalSettlementGuard(HomePackageOriginalRootOutcome sameKnownOutcome);
}

/// <summary>Observed original result/evidence; it grants neither a new mutation nor a replacement root session.</summary>
public sealed record HomePackageOriginalRootOutcome(HomePackageActionResult Result,
    HomePackageDatabaseEntry? ObservedPackage, object OriginalRootOutcomeEvidence);

/// <summary>
/// ONE device-wide registered owner, explicitly constructed over the supplied canonical store and
/// original provider/root port. It is deliberately unregistered. It does not own the borrowed root
/// channel; that platform must wait for this owner's SAME close proof and every original session
/// adapter's close proof before releasing channel/device-store resources.
/// Missing WriteGuarded support refuses reservation before the actual root effect.
/// </summary>
public sealed class HomePackageOriginalDeviceOwner : IAsyncDisposable, IHomePackageOriginalPlatformOwner
{
    private const int MaximumOriginalTasks = 512;
    private readonly IHomeCoreStateStore _store;
    private readonly HomePackageDatabase _database;
    private readonly IHomePackageOriginalArtifactProvider _artifacts;
    private readonly IHomePackageOriginalRootMutationPort _root;
    private readonly object _gate = new();
    private readonly List<Task> _originalTasks = [];
    private readonly Dictionary<string, OriginalOperation> _operations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _originalMutationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime;
    private bool _closing;
    private Task? _close;

    public HomePackageOriginalDeviceOwner(IHomeCoreStateStore originalDeviceStore,
        IHomePackageOriginalArtifactProvider originalArtifacts,
        IHomePackageOriginalRootMutationPort originalRootMutationPort,
        CancellationToken originalOwnerLifetime)
    {
        _store = originalDeviceStore ?? throw new ArgumentNullException(nameof(originalDeviceStore));
        _artifacts = originalArtifacts ?? throw new ArgumentNullException(nameof(originalArtifacts));
        _root = originalRootMutationPort ?? throw new ArgumentNullException(nameof(originalRootMutationPort));
        if (!originalOwnerLifetime.CanBeCanceled)
            throw new ArgumentException("Retained original device-owner lifetime is required.", nameof(originalOwnerLifetime));
        _database = new(_store);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalOwnerLifetime);
    }

    IHomeCoreStateStore IHomePackageOriginalPlatformOwner.DeviceStore => _store;
    HomePackageDatabase IHomePackageOriginalPlatformOwner.Database => _database;

    async ValueTask<HomePackageOriginalArtifactMaterial?> IHomePackageOriginalPlatformOwner.ResolveOriginalArtifactAsync(
        HomePackageActionRequest request, HomePackageDatabaseSnapshot registry, CancellationToken token)
    {
        return await RetainProviderAsync<HomePackageOriginalArtifactMaterial?>(async linked =>
        {
            var original = await _artifacts.ResolveAsync(request, registry, linked).ConfigureAwait(false);
            if (original is null) return null;
            if (original.SignedDescriptorBytes.Length is < 1 or > 1024 * 1024 ||
                original.DescriptorPayloadBytes.Length is < 1 or > 64 * 1024 ||
                !HomePackageArtifactSelection.Text(original.CatalogueRevision, 1024))
                throw new InvalidDataException("Original provider descriptor exceeds its bound.");
            var signed = original.SignedDescriptorBytes.ToArray();
            var payload = original.DescriptorPayloadBytes.ToArray();
            return new HomePackageOriginalArtifactMaterial(signed, payload, original.CatalogueRevision,
                new OriginalArtifactEvidence(this, original, HomePackageArtifactSelection.Digest(signed),
                    HomePackageArtifactSelection.Digest(payload)));
        }, token).ConfigureAwait(false);
    }

    HomePackageOriginalActionBinding? IHomePackageOriginalPlatformOwner.ResolveOriginalAction(
        HomePackageAction action, HomePackageArtifactDescriptor descriptor)
    {
        lock (_gate) if (_closing) throw new ObjectDisposedException(nameof(HomePackageOriginalDeviceOwner));
        // Configuration-only lookup; this callback must not use root/store/IO or perform an effect.
        var mapped = _artifacts.ResolveAction(action, descriptor);
        lock (_gate) if (_closing) throw new ObjectDisposedException(nameof(HomePackageOriginalDeviceOwner));
        return mapped is null ? null : new(mapped.TargetAppId, mapped.ActionId, mapped.RequiredInstalledServiceId, mapped.Scopes);
    }

    Task IHomePackageOriginalPlatformOwner.DemandOriginalArtifactAndChannelAsync(HomePackageArtifactSelection selection,
        AuthenticatedResourceActor actor, HomeNativeInstalledPeer caller, string session, CancellationToken token)
    {
        if (!selection.IssuedBy(this) || selection.OriginalEvidence is not OriginalArtifactEvidence retained ||
            !ReferenceEquals(retained.Owner, this))
            throw new UnauthorizedAccessException("SAME original device-owner artifact evidence required.");
        return RetainProviderAsync(async linked =>
        {
            await _artifacts.DemandOriginalCurrentAsync(retained.Observation, actor, caller, session, linked).ConfigureAwait(false);
            linked.ThrowIfCancellationRequested();
            if (HomePackageArtifactSelection.Digest(retained.Observation.SignedDescriptorBytes.Span) != retained.SignedSha256 ||
                HomePackageArtifactSelection.Digest(retained.Observation.DescriptorPayloadBytes.Span) != retained.PayloadSha256 ||
                retained.SignedSha256 != selection.SignedDescriptorSha256 ||
                retained.PayloadSha256 != selection.DescriptorPayloadSha256 ||
                retained.Observation.CatalogueRevision != selection.CatalogueRevision)
                throw new UnauthorizedAccessException("Original artifact provider bytes/catalogue changed during currentness observation.");
            return true;
        }, token);
    }

    private sealed record OriginalArtifactEvidence(HomePackageOriginalDeviceOwner Owner,
        HomePackageOriginalArtifactObservation Observation, string SignedSha256, string PayloadSha256);

    private Task<T> RetainProviderAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken caller)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> original;
        lock (_gate)
        {
            RequireAdmission();
            original = ProviderAfterStartAsync(start.Task, body, caller); _originalTasks.Add(original);
        }
        start.SetResult();
        return original;
    }
    private async Task<T> ProviderAfterStartAsync<T>(Task start,
        Func<CancellationToken, Task<T>> body, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        return await body(linked.Token).ConfigureAwait(false);
    }

    // Retained privately before the original task is released. A public operation ID cannot create
    // this record, original request, actual root mutation or its already admitted audit authority.
    private sealed class OriginalOperation
    {
        internal readonly HomePackageOriginalInvocation Invocation;
        internal readonly HomePackageOriginalRootRequest Request;
        internal Task<HomePackageActionResult>? OriginalTask;
        internal Task<HomePackageOriginalOperationObservation>? RecoveryTask;
        internal IHomePackageOriginalRootMutation? Mutation;
        internal Task<IHomePackageOriginalRootMutation>? OriginalOpen;
        internal Task<HomePackageOriginalRootOutcome>? OriginalEffect;
        internal Task<HomePackageOriginalRootOutcome?>? OriginalQuery;
        internal Task? OriginalRootClose;
        internal HomePackageDatabaseSnapshot? Reserved;
        internal HomePackageDatabaseSnapshot? OriginalReservationIntent;
        internal Task<HomePackageDatabaseSnapshot>? OriginalReservation;
        internal bool ReservationAttempted;
        internal HomePackageOriginalRootOutcome? RootOutcome;
        internal HomePackageActionResult? CapturedRootResult;
        internal HomePackageDatabaseEntry? CapturedPackage;
        internal bool PackageCaptured;
        internal bool EffectAttempted;
        internal bool CanonicalConfirmed;
        internal long? ConfirmedRegistryRevision;
        internal OriginalOperation(HomePackageOriginalInvocation invocation)
        {
            Invocation = invocation; Request = new(invocation);
        }
    }

    Task<HomePackageActionResult> IHomePackageOriginalPlatformOwner.ExecuteAndCommitOriginalAsync(
        HomePackageOriginalInvocation invocation, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var record = new OriginalOperation(invocation);
        Task<HomePackageActionResult> original;
        lock (_gate)
        {
            RequireAdmission();
            if (!invocation.Review.Artifact.IssuedBy(this) || _operations.ContainsKey(record.Request.OperationId))
                throw new UnauthorizedAccessException("SAME original package effect can be dispatched only once.");
            original = ExecuteAfterStartAsync(start.Task, record, caller);
            record.OriginalTask = original;
            _operations.Add(record.Request.OperationId, record);
            _originalTasks.Add(original);
        }
        start.SetResult();
        return original;
    }

    private void RequireAdmission()
    {
        if (_closing) throw new ObjectDisposedException(nameof(HomePackageOriginalDeviceOwner));
        if (_originalTasks.Count >= MaximumOriginalTasks)
            throw new InvalidOperationException("Device-owner original task capacity reached; retain all causes through close.");
    }

    private async Task<HomePackageActionResult> ExecuteAfterStartAsync(Task start,
        OriginalOperation record, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        IAsyncDisposable? completionLease = null;
        bool mutationGateHeld = false;
        Exception? primary = null; List<Exception> failures = [];
        try
        {
            await _originalMutationGate.WaitAsync(linked.Token).ConfigureAwait(false);
            mutationGateHeld = true;
            var invocation = record.Invocation;
            var request = record.Request;
            await invocation.DemandCurrentBeforeEffectAsync(linked.Token).ConfigureAwait(false);
            completionLease = await invocation.AcquireOriginalCompletionLeaseAsync(linked.Token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("SAME uncompleted claimed package capability required.");
            // Authentication/acquisition only. The trusted root port must not reacquire the native
            // Context Gate that owns this operation, or call Home/broker while under a device writer.
            record.OriginalOpen = _root.OpenOriginalAsync(request, linked.Token);
            record.Mutation = await record.OriginalOpen.ConfigureAwait(false);
            if (record.Mutation is null || !ReferenceEquals(record.Mutation.OriginalRequest, request) ||
                record.Mutation.OriginalAdmissionGuard is null)
                throw new UnauthorizedAccessException("SAME original root request and non-reentrant admission guard required.");
            await record.Mutation.DemandOriginalChannelCurrentAsync(linked.Token).ConfigureAwait(false);
            await request.DemandOriginalCurrentBeforeEffectAsync(linked.Token).ConfigureAwait(false);

            var current = await ReadAsync(linked.Token).ConfigureAwait(false);
            if (current.Revision != invocation.Review.RegistryRevision ||
                current.RecentOperations.Any(item => item.IdempotencyKey == request.Action.IdempotencyKey ||
                    item.PackageId == request.Action.PackageId &&
                    item.State is HomePackageJournalState.Pending or HomePackageJournalState.OutcomeUnknown))
                throw new UnauthorizedAccessException("The original device revision or idempotency tuple changed.");
            if (current.RecentOperations.Count >= HomePackageDatabase.MaximumJournalEntries)
                throw new InvalidOperationException("Canonical journal capacity reached; no pending cause is discarded.");
            var pending = new HomePackageJournalEntry(request.Action.IdempotencyKey, request.OperationId,
                request.Action.PackageId, request.Action.Action.ToString(), HomePackageJournalState.Pending,
                DateTimeOffset.UtcNow, null, "HomePackages.OriginalReserved", false,
                current.Packages.SingleOrDefault(item => item.PackageId == request.Action.PackageId)?.LastKnownGoodVersion is not null,
                [], [], [], []);
            var next = current with { RecentOperations = Array.AsReadOnly(current.RecentOperations.Append(pending).ToArray()) };
            // Retain the complete detached intent and SAME original writer task before awaiting
            // its acknowledgement. A postcommit observer may throw after durable Pending exists.
            record.OriginalReservationIntent = HomePackageDatabase.CaptureGuardedSnapshot(next);
            record.ReservationAttempted = true;
            record.OriginalReservation = SaveGuardedAsync(record.OriginalReservationIntent,
                current.Revision, request.Actor, record.Mutation.OriginalAdmissionGuard, linked.Token);
            record.Reserved = await record.OriginalReservation.ConfigureAwait(false);
            invocation.RetainOriginalRegistryTransition(this, current.Revision, record.Reserved);

            // These observations are outside the writer; the genuine root endpoint must make its
            // own original-channel/approval/publisher/payload check immediately before its effect.
            await request.DemandOriginalCurrentBeforeEffectAsync(linked.Token).ConfigureAwait(false);
            await record.Mutation.DemandOriginalChannelCurrentAsync(linked.Token).ConfigureAwait(false);
            record.EffectAttempted = true;
            record.OriginalEffect = record.Mutation.ExecuteOriginalAsync(linked.Token);
            var outcome = await record.OriginalEffect.ConfigureAwait(false);
            CaptureRootOutcome(record, outcome);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (record.ReservationAttempted && record.Reserved is null)
                try { await ConfirmSameOriginalReservationAsync(record, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            if ((record.EffectAttempted || record.ReservationAttempted) &&
                record.RootOutcome is null && record.Mutation is not null)
                try { await ObserveSameOutcomeAsync(record, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            if (record.Reserved is not null && record.Mutation is not null)
                try { await SettleSameKnownOutcomeAsync(record, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            if (record.CapturedRootResult is not null)
                try { record.Invocation.RetainOriginalKnownResult(this, ResultForOriginalCall(record)); }
                catch (Exception error) { Add(failures, error, primary); }
            // Device writer and actual root effect/settlement have settled before broker audit can
            // acquire this capability again. Root cleanup remains independent of that lease.
            if (completionLease is not null)
                try { await completionLease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            if (!record.ReservationAttempted || record.CanonicalConfirmed)
                try { await CloseSameRootAsync(record).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            // An unconfirmed reservation, a confirmed reservation without dispatched effect,
            // an ambiguous effect or failed known-effect settlement retains the SAME root audit
            // route until canonical confirmation or final owner close. Missing ACK is not no-effect.
            if (mutationGateHeld)
                try { _originalMutationGate.Release(); }
                catch (Exception error) { Add(failures, error, primary); }
        }
        Throw(primary, failures);
        return ResultForOriginalCall(record);
    }

    private HomePackageActionResult ResultForOriginalCall(OriginalOperation record)
    {
        var known = record.CapturedRootResult ?? throw new InvalidOperationException("Original root outcome remains unknown.");
        return !record.CanonicalConfirmed && known.State == HomePackageOperationState.Succeeded
            ? known with { State = HomePackageOperationState.PartiallySucceeded,
                Code = "HomePackages.CanonicalOutcomeUnconfirmed",
                Message = "The original root effect is known; canonical package settlement is unconfirmed.",
                Retryable = false, Recoverable = true }
            : known;
    }

    private static void CaptureRootOutcome(OriginalOperation record, HomePackageOriginalRootOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome); ArgumentNullException.ThrowIfNull(outcome.OriginalRootOutcomeEvidence);
        var known = HomePackageOriginalOperationAdmission.CaptureOriginalResult(record.Invocation.Review, outcome.Result);
        if (record.RootOutcome is not null &&
            !ReferenceEquals(record.RootOutcome.OriginalRootOutcomeEvidence, outcome.OriginalRootOutcomeEvidence))
            throw new UnauthorizedAccessException("Original root outcome evidence cannot be replaced.");
        if (record.CapturedRootResult is not null &&
            HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(record.CapturedRootResult)) !=
            HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(known)))
            throw new InvalidDataException("SAME original root result cannot change.");
        // Capture result before package validation, so a malformed package observation never
        // erases an already known actual root effect or becomes a fabricated no-effect failure.
        record.CapturedRootResult ??= known;
        record.RootOutcome ??= outcome;
        record.CapturedPackage = outcome.ObservedPackage is null ? null :
            CapturePackage(record, outcome.ObservedPackage, known);
        if (known.State == HomePackageOperationState.Succeeded &&
            record.Request.Action.Action != HomePackageAction.Launch && record.CapturedPackage is null)
            throw new InvalidDataException("Known successful package mutation requires its original canonical package observation.");
        record.PackageCaptured = true;
    }

    private static HomePackageDatabaseEntry CapturePackage(OriginalOperation record,
        HomePackageDatabaseEntry original, HomePackageActionResult result)
    {
        // Synchronously detach ALL nested mutable fields before any further await.
        var captured = HomePackageDatabase.CaptureGuardedSnapshot(
            HomePackageDatabaseSnapshot.Empty with { Packages = Array.AsReadOnly(new[] { original }) }).Packages.Single();
        var descriptor = record.Request.Artifact;
        if (captured.PackageId != descriptor.PackageId || captured.AppId != descriptor.AppId ||
            !HomePackageArtifactSelection.Text(captured.Name, 256) || captured.Revision < 0 ||
            !Enum.IsDefined(captured.InstallationState) || !Enum.IsDefined(captured.Compatibility) ||
            captured.Dependencies.Any(item => item is null || !HomePackageArtifactSelection.Identifier(item.PackageId)) ||
            captured.IntegrityEvidence.Any(item => item is null || !Enum.IsDefined(item.State)) ||
            captured.RetainedRollbackVersions.Any(item => !HomePackageArtifactSelection.Text(item, 256)))
            throw new InvalidDataException("Original root package observation does not bind its bounded stable identities.");
        if (result.State == HomePackageOperationState.Succeeded)
        {
            var action = record.Request.Action.Action;
            if (action is HomePackageAction.Install or HomePackageAction.Update or HomePackageAction.Repair or
                HomePackageAction.SelectVersion or HomePackageAction.Rollback)
            {
                if (captured.InstallationState != HomePackageInstallState.Installed ||
                    captured.InstalledVersion != descriptor.Version ||
                    !captured.IntegrityEvidence.Any(item => item.Version == descriptor.Version &&
                        item.ArtifactSha256 == descriptor.PayloadSha256 && item.State == HomePackageIntegrityState.Verified))
                    throw new InvalidDataException("Known installed outcome lacks the exact original version/payload observation.");
            }
            if (action == HomePackageAction.Uninstall &&
                (captured.InstalledVersion is not null || captured.InstallationState == HomePackageInstallState.Installed))
                throw new InvalidDataException("Known uninstall outcome still observes an installed original package.");
            if (action == HomePackageAction.SelectChannel && captured.UpdateChannel != descriptor.Channel)
                throw new InvalidDataException("Known channel outcome differs from the original selected channel.");
        }
        return captured;
    }

    private async Task ConfirmSameOriginalReservationAsync(OriginalOperation record, CancellationToken token)
    {
        if (record.Reserved is not null) return;
        var intent = record.OriginalReservationIntent;
        if (!record.ReservationAttempted || intent is null || record.OriginalReservation is not { IsCompleted: true })
            throw new InvalidOperationException("SAME original reservation writer must settle before audit reconciliation.");
        // No approval/profile/root-current callback and no new writer/effect occurs here. This
        // read can only recover the exact already attempted original Pending intent after ACK loss.
        // Concurrent changes remain unconfirmed rather than creating a replacement reservation.
        var current = await ReadAsync(token).ConfigureAwait(false);
        // SaveCore owns the timestamp and ordinal package ordering; neither is a fresh grant.
        var expected = intent with {
            SchemaVersion = HomePackageDatabase.CurrentSchemaVersion,
            Revision = checked(intent.Revision + 1), UpdatedAtUtc = current.UpdatedAtUtc,
            Packages = intent.Packages.OrderBy(item => item.PackageId, StringComparer.Ordinal).ToArray()
        };
        if (current.Revision != expected.Revision ||
            HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(current)) !=
            HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(expected)))
            throw new UnauthorizedAccessException("Exact original Pending reservation is unconfirmed; retain the same root audit route.");
        var pending = current.RecentOperations.SingleOrDefault(item => item.OperationId == record.Request.OperationId);
        if (pending is null || pending.State != HomePackageJournalState.Pending ||
            pending.IdempotencyKey != record.Request.Action.IdempotencyKey ||
            pending.PackageId != record.Request.Action.PackageId || pending.Action != record.Request.Action.Action.ToString())
            throw new UnauthorizedAccessException("SAME original reservation tuple is unavailable for audit reconciliation.");
        record.Reserved = HomePackageDatabase.CaptureGuardedSnapshot(current);
        // Deliberately do not RetainOriginalRegistryTransition: reconciliation grants no execution
        // continuation and never refreshes the original review's permission/current revision.
    }

    private async Task ObserveSameOutcomeAsync(OriginalOperation record, CancellationToken token)
    {
        var mutation = record.Mutation ?? throw new InvalidOperationException("SAME original root mutation is unavailable.");
        if (record.OriginalRootClose is not null)
            throw new InvalidOperationException("The original root mutation is already closing; no replacement channel is acquired.");
        record.OriginalQuery = mutation.ObserveOriginalOutcomeAsync(token);
        var known = await record.OriginalQuery.ConfigureAwait(false);
        if (known is not null) CaptureRootOutcome(record, known);
    }

    private async Task SettleSameKnownOutcomeAsync(OriginalOperation record, CancellationToken token)
    {
        if (record.CanonicalConfirmed) return;
        var outcome = record.RootOutcome ?? throw new InvalidOperationException(
            "SAME root outcome remains unknown; canonical Pending is retained for owning recovery.");
        var result = record.CapturedRootResult ?? throw new InvalidDataException("Original root result capture unavailable.");
        if (!record.PackageCaptured)
            throw new InvalidDataException("Original package observation capture unavailable; canonical Pending is retained.");
        var mutation = record.Mutation ?? throw new InvalidOperationException("SAME root settlement owner is unavailable.");
        var current = await ReadAsync(token).ConfigureAwait(false);
        var journal = current.RecentOperations.SingleOrDefault(item => item.OperationId == record.Request.OperationId);
        if (record.Reserved is null || journal is null ||
            journal.IdempotencyKey != record.Request.Action.IdempotencyKey ||
            journal.PackageId != record.Request.Action.PackageId ||
            journal.Action != record.Request.Action.Action.ToString())
            throw new UnauthorizedAccessException("Exact original reservation is not current; no replacement operation is created.");
        var expectedState = result.State == HomePackageOperationState.Succeeded ? HomePackageJournalState.Succeeded :
            result.State is HomePackageOperationState.Failed or HomePackageOperationState.Rejected or
                HomePackageOperationState.Cancelled ? HomePackageJournalState.Failed : HomePackageJournalState.OutcomeUnknown;
        if (journal.State != HomePackageJournalState.Pending)
        {
            // The prior original write can persist and then lose its acknowledgement. A complete
            // exact terminal read is revalidated under the SAME audit-only guard; this performs
            // another canonical CAS, never another package effect or cached-authority acceptance.
            var terminalPackage = current.Packages.SingleOrDefault(item => item.PackageId == record.Request.Action.PackageId);
            if (journal.State != expectedState || journal.CompletedAtUtc is null ||
                journal.ResultCode != result.Code || journal.Retryable ||
                journal.PreviousKnownGoodVersionRetained != result.PreviousKnownGoodVersionRetained ||
                !journal.SucceededSteps.SequenceEqual(result.SucceededSteps) ||
                !journal.FailedSteps.SequenceEqual(result.FailedSteps) ||
                !journal.SkippedSteps.SequenceEqual(result.SkippedSteps) ||
                !journal.RolledBackSteps.SequenceEqual(result.RolledBackSteps) ||
                record.CapturedPackage is not null && (terminalPackage is null ||
                    HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(terminalPackage)) !=
                    HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(record.CapturedPackage))))
                throw new UnauthorizedAccessException("Current terminal canonical observation does not match the original known root outcome.");
            var recheck = mutation.OriginalSettlementGuard(outcome)
                ?? throw new UnauthorizedAccessException("Original raw-current audit settlement guard unavailable.");
            var confirmed = await SaveGuardedAsync(current, current.Revision, record.Request.Actor, recheck, token).ConfigureAwait(false);
            record.ConfirmedRegistryRevision = confirmed.Revision;
            record.CanonicalConfirmed = true;
            return;
        }
        var packages = current.Packages.ToList();
        if (record.CapturedPackage is { } package)
        {
            packages.RemoveAll(item => item.PackageId == package.PackageId);
            packages.Add(package);
        }
        var terminal = journal with {
            State = result.State == HomePackageOperationState.Succeeded ? HomePackageJournalState.Succeeded :
                result.State is HomePackageOperationState.Failed or HomePackageOperationState.Rejected or
                    HomePackageOperationState.Cancelled ? HomePackageJournalState.Failed : HomePackageJournalState.OutcomeUnknown,
            CompletedAtUtc = DateTimeOffset.UtcNow, ResultCode = result.Code, Retryable = false,
            PreviousKnownGoodVersionRetained = result.PreviousKnownGoodVersionRetained,
            SucceededSteps = result.SucceededSteps, FailedSteps = result.FailedSteps,
            SkippedSteps = result.SkippedSteps, RolledBackSteps = result.RolledBackSteps
        };
        var next = current with {
            Packages = Array.AsReadOnly(packages.ToArray()),
            RecentOperations = Array.AsReadOnly(current.RecentOperations
                .Select(item => item.OperationId == record.Request.OperationId ? terminal : item).ToArray())
        };
        var guard = mutation.OriginalSettlementGuard(outcome)
            ?? throw new UnauthorizedAccessException("Genuine raw-current audit-only settlement guard is unavailable.");
        var saved = await SaveGuardedAsync(next, current.Revision, record.Request.Actor, guard, token).ConfigureAwait(false);
        record.ConfirmedRegistryRevision = saved.Revision;
        record.CanonicalConfirmed = true;
        // This is audit of the already admitted root effect. It never refreshes the review's
        // permission or authorizes another effect, even if other device records changed.
    }

    /// <summary>
    /// Audit-only recovery of an actual privately retained operation. It waits for the SAME original
    /// task to settle, never calls Execute/Open/artifact/current approval, and retains its exact root
    /// outcome and original failure. Cross-process recovery requires the configured root ledger and
    /// genuine enrolled input; absence remains Pending, not a fabricated failed/no-effect outcome.
    /// </summary>
    public Task<HomePackageOriginalOperationObservation> RecoverOriginalSettlementAsync(
        string originalOperationId, CancellationToken settlementCancellationToken = default)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HomePackageOriginalOperationObservation> original;
        lock (_gate)
        {
            RequireAdmission();
            if (!_operations.TryGetValue(originalOperationId, out var record) ||
                record.OriginalTask is not { IsCompleted: true })
                throw new InvalidOperationException("SAME original admitted task must settle before audit-only recovery.");
            if (record.RecoveryTask is { IsCompleted: false } pending) return pending;
            original = RecoverAfterStartAsync(start.Task, record, settlementCancellationToken);
            record.RecoveryTask = original; _originalTasks.Add(original);
        }
        start.SetResult();
        return original;
    }

    private async Task<HomePackageOriginalOperationObservation> RecoverAfterStartAsync(Task start,
        OriginalOperation record, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        bool held = false; Exception? primary = null; List<Exception> failures = [];
        try
        {
            await _originalMutationGate.WaitAsync(token).ConfigureAwait(false); held = true;
            if (!record.CanonicalConfirmed)
            {
                if (record.Reserved is null) await ConfirmSameOriginalReservationAsync(record, token).ConfigureAwait(false);
                if (record.RootOutcome is null) await ObserveSameOutcomeAsync(record, token).ConfigureAwait(false);
                await SettleSameKnownOutcomeAsync(record, token).ConfigureAwait(false);
            }
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (record.CanonicalConfirmed)
                try { await CloseSameRootAsync(record).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            if (held)
                try { _originalMutationGate.Release(); }
                catch (Exception error) { Add(failures, error, primary); }
        }
        Throw(primary, failures);
        return Observation(record);
    }

    // Historic private ledger observation only. Missing means the original claim never
    // reached this owner; it is not evidence of no effect and never grants new work.
    internal HomePackageOriginalOperationObservation? TryObserveOriginalOperation(string originalOperationId)
    {
        lock (_gate)
        {
            if (!_operations.TryGetValue(originalOperationId, out var record)) return null;
            if (record.OriginalTask is not { IsCompleted: true } ||
                record.RecoveryTask is { IsCompleted: false })
                throw new InvalidOperationException("SAME original operation and recovery task must settle before observation.");
            return Observation(record);
        }
    }

    public HomePackageOriginalOperationObservation ObserveOriginalOperation(string originalOperationId)
    {
        lock (_gate)
        {
            if (!_operations.TryGetValue(originalOperationId, out var record) ||
                record.OriginalTask is not { IsCompleted: true } ||
                record.RecoveryTask is { IsCompleted: false })
                throw new InvalidOperationException("SAME original operation and recovery task must settle before observation.");
            return Observation(record);
        }
    }

    private static HomePackageOriginalOperationObservation Observation(OriginalOperation record) => new(
        record.Request.OperationId, record.Request.Action.PackageId, record.Request.Action.Action,
        record.EffectAttempted, record.CapturedRootResult is not null, record.CanonicalConfirmed,
        record.ConfirmedRegistryRevision, record.CapturedRootResult, record.OriginalTask!);

    private static async Task CloseSameRootAsync(OriginalOperation record)
    {
        if (record.Mutation is null) return;
        record.OriginalRootClose ??= record.Mutation.DisposeAsync().AsTask();
        await record.OriginalRootClose.ConfigureAwait(false);
    }

    private async Task<HomePackageDatabaseSnapshot> ReadAsync(CancellationToken token)
    {
        if (!_database.IsBoundToStore(_store)) throw new UnauthorizedAccessException("SAME canonical device database/store required.");
        var result = await _database.ReadAsync(token).ConfigureAwait(false);
        return result.Succeeded ? result.Snapshot! : throw new HomePackageDatabaseException(result.Failure!);
    }

    private async Task<HomePackageDatabaseSnapshot> SaveGuardedAsync(HomePackageDatabaseSnapshot snapshot,
        long expected, AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken token)
    {
        var result = await _database.SaveGuardedAsync(snapshot, expected, actor, guard, token).ConfigureAwait(false);
        return result.Succeeded ? result.Snapshot! : throw new HomePackageDatabaseException(result.Failure!);
    }

    public Task CloseAndDrainAsync()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task close;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true;
            close = _close = CloseOriginalAsync(start.Task, _originalTasks.ToArray(), _operations.Values.ToArray());
        }
        start.SetResult();
        return close;
    }

    private async Task CloseOriginalAsync(Task start, Task[] originals, OriginalOperation[] operations)
    {
        await start.ConfigureAwait(false); List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { Add(failures, error, null); }
        foreach (var original in originals)
            try { await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error, null); }
        foreach (var record in operations)
        {
            if (record.ReservationAttempted && !record.CanonicalConfirmed)
                try
                {
                    if (record.Reserved is null) await ConfirmSameOriginalReservationAsync(record, CancellationToken.None).ConfigureAwait(false);
                    if (record.RootOutcome is null) await ObserveSameOutcomeAsync(record, CancellationToken.None).ConfigureAwait(false);
                    await SettleSameKnownOutcomeAsync(record, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error) { Add(failures, error, null); }
            try { await CloseSameRootAsync(record).ConfigureAwait(false); }
            catch (Exception error) { Add(failures, error, null); }
        }
        try { _originalMutationGate.Dispose(); } catch (Exception error) { Add(failures, error, null); }
        try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error, null); }
        Throw(null, failures);
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static void Add(List<Exception> failures, Exception error, Exception? primary)
    {
        if (!ReferenceEquals(error, primary) && !failures.Any(item => ReferenceEquals(item, error))) failures.Add(error);
    }
    private static void Throw(Exception? primary, List<Exception> failures)
    {
        if (primary is null && failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Original package owner body/settlement/close failures retained.",
            primary is null ? failures : new[] { primary }.Concat(failures));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
}

/// <summary>Observation of the retained original operation; never an execution or installation grant.</summary>
public sealed record HomePackageOriginalOperationObservation(string OperationId, string PackageId,
    HomePackageAction Action, bool OriginalEffectAttempted, bool RootOutcomeKnown,
    bool CanonicalSettlementConfirmed, long? ConfirmedRegistryRevision,
    HomePackageActionResult? ObservedRootResult, Task<HomePackageActionResult> OriginalTask);
