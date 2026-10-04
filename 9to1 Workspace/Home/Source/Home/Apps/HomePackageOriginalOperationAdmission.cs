using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Home.Apps
{
    internal interface IHomePackageOriginalConnection
    {
        AuthenticatedResourceActor Actor { get; }
        HomeNativeInstalledPeer InstalledCaller { get; }
        // Optional only for inherited isolated fakes. Genuine root effects require the SAME observed socket peer.
        HomeNativeObservedPeer? ObservedPeer => null;
        string SessionId { get; }
        HomeNativeCoreApiSessions.Session.OriginalPackageCaller CaptureOriginalCallerInsideGate();
        CancellationToken OriginalLifetime { get; }
        Task<bool> IsCurrentInsideOriginalGateAsync(CancellationToken cancellationToken);
        Task<T> RunOwnedAsync<T>(Func<CancellationToken, Task<T>> original,
            CancellationToken cancellationToken);
    }

    internal enum HomePackageOriginalSettlement { None, RejectedBegin, UnclaimedAbort, RejectedClaim, Claimed }

    /// <summary>Exact local prepared handle; request IDs and detached observations cannot recreate it.</summary>
    internal sealed class HomePackageOriginalReview
    {
        internal readonly HomePackageOriginalOperationAdmission Owner;
        internal readonly HomePackageActionRequest Request;
        internal readonly HomePackageArtifactSelection Artifact;
        internal readonly HomeResourcePreparedReview Prepared;
        internal readonly HomePackageOriginalActionBinding Binding;
        internal readonly JsonElement Arguments;
        internal readonly long RegistryRevision;
        internal readonly long OriginalPackageEntryRevision;
        internal long CurrentRegistryRevision;
        internal readonly HomePermissionActionPolicy Policy;
        internal readonly string OperationId = Guid.NewGuid().ToString("N");
        internal HomeResourceExecutionCapability? Capability;
        internal int DispatchAttempted;
        internal Task? OriginalDispatchTask;
        internal HomePackageOriginalSettlement Settlement;
        internal HomeExecutionOutcome? OriginalOutcome;
        internal HomePackageActionResult? OriginalResult;
        internal Exception? OriginalFailure;
        internal HomePackageOriginalReview(HomePackageOriginalOperationAdmission owner,
            HomePackageActionRequest request, HomePackageArtifactSelection artifact,
            HomeResourcePreparedReview prepared, HomePackageOriginalActionBinding binding,
            JsonElement arguments, long registryRevision, long originalPackageEntryRevision, HomePermissionActionPolicy policy)
        {
            Owner = owner; Request = request; Artifact = artifact; Prepared = prepared;
            Binding = binding; Arguments = arguments.Clone(); RegistryRevision = CurrentRegistryRevision = registryRevision; Policy = policy;
            OriginalPackageEntryRevision = originalPackageEntryRevision;
        }
    }

    /// <summary>
    /// Supplied only to the registered original platform owner. Public fields describe the
    /// admitted tuple; only the SAME authenticated root endpoint can authorize a privileged effect.
    /// DemandCurrentBeforeEffectAsync must run BEFORE taking the Home state writer lease.
    /// </summary>
    internal sealed class HomePackageOriginalInvocation
    {
        private readonly Func<CancellationToken, Task> _current;
        internal HomePackageOriginalReview Review { get; }
        internal AuthenticatedResourceActor OriginalActor { get; }
        internal HomeNativeInstalledPeer OriginalCaller { get; }
        internal HomeNativeObservedPeer? OriginalObservedPeer { get; }
        internal string OriginalSessionId { get; }
        internal HomePackageOriginalInvocation(HomePackageOriginalReview review,
            IHomePackageOriginalConnection connection, Func<CancellationToken, Task> current)
        {
            Review = review; OriginalActor = connection.Actor; OriginalCaller = connection.InstalledCaller;
            OriginalSessionId = connection.SessionId; OriginalObservedPeer = connection.ObservedPeer; _current = current;
        }
        internal Task DemandCurrentBeforeEffectAsync(CancellationToken cancellationToken) =>
            _current(cancellationToken);
        internal ValueTask<IAsyncDisposable?> AcquireOriginalCompletionLeaseAsync(CancellationToken token) =>
            Review.Owner.AcquireOriginalCompletionLeaseAsync(Review, token);
        internal void RetainOriginalRegistryTransition(IHomePackageOriginalPlatformOwner sameOwner,
            long before, HomePackageDatabaseSnapshot confirmed)
        {
            if (!Review.Artifact.IssuedBy(sameOwner) || before != Review.CurrentRegistryRevision ||
                before == long.MaxValue || confirmed.Revision != before + 1 ||
                !confirmed.RecentOperations.Any(item => item.OperationId == Review.OperationId &&
                    item.IdempotencyKey == Review.Request.IdempotencyKey &&
                    item.PackageId == Review.Request.PackageId &&
                    item.Action == Review.Request.Action.ToString()))
                throw new UnauthorizedAccessException("SAME confirmed original journal transition required.");
            Review.CurrentRegistryRevision = confirmed.Revision;
        }
        internal void RetainOriginalKnownResult(IHomePackageOriginalPlatformOwner sameOwner, HomePackageActionResult result)
        {
            if (!Review.Artifact.IssuedBy(sameOwner))
                throw new UnauthorizedAccessException("SAME original root-result owner required.");
            var captured = HomePackageOriginalOperationAdmission.CaptureOriginalResult(Review, result);
            if (Review.OriginalResult is not null &&
                HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(Review.OriginalResult)) !=
                    HomePackageArtifactSelection.Digest(JsonSerializer.SerializeToUtf8Bytes(captured)))
                throw new InvalidDataException("SAME original root result cannot be replaced.");
            Review.OriginalResult ??= captured;
        }
    }

    /// <summary>
    /// Original-session package adapter, deliberately unregistered. It reuses the canonical
    /// database and actual Home resource/permission broker rather than maintaining another registry.
    /// A supplied port with no real root/transaction authority must refuse before effects.
    /// </summary>
    internal sealed class HomePackageOriginalOperationAdmission : IAsyncDisposable
    {
        private const int MaximumOriginalTasks = 64;
        private const int MaximumOriginalReviews = 32;
        private readonly IHomePackageOriginalConnection _connection;
        private readonly IHomePackageOriginalPlatformOwner _platform;
        private readonly HomeResourceOperationBroker _broker;
        private readonly HomePermissionTrustService _permissions;
        private readonly IHomeCoreStateStore _deviceStore;
        private readonly HomePackageDatabase _database;
        private readonly CancellationTokenSource _lifetime;
        private readonly object _gate = new();
        private readonly List<Task> _originalTasks = [];
        // Access only while the SAME connection Gate is held, or after all originals have drained.
        private readonly Dictionary<string, HomePackageOriginalReview> _reviews = new(StringComparer.Ordinal);
        private bool _closing;
        private Task? _close;

        internal HomePackageOriginalOperationAdmission(IHomePackageOriginalConnection originalConnection,
            IHomePackageOriginalPlatformOwner originalPlatform, HomeResourceOperationBroker broker,
            HomePermissionTrustService permissions)
        {
            _connection = originalConnection ?? throw new ArgumentNullException(nameof(originalConnection));
            _platform = originalPlatform ?? throw new ArgumentNullException(nameof(originalPlatform));
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
            _deviceStore = _platform.DeviceStore ?? throw new ArgumentException("Genuinely supplied device store missing.", nameof(originalPlatform));
            _database = _platform.Database ?? throw new ArgumentException("Canonical device database missing.", nameof(originalPlatform));
            if (!_database.IsBoundToStore(_deviceStore))
                throw new UnauthorizedAccessException("The canonical database must use the SAME supplied device store.");
            if (!_broker.IsBoundToPermissions(_permissions))
                throw new UnauthorizedAccessException("The SAME original Home resource/permission issuer is required.");
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(_connection.OriginalLifetime);
        }

        // Reference binding only; the lifecycle runner cannot mint installed or root authority.
        internal bool IsBoundToOriginalPlatform(IHomePackageOriginalPlatformOwner original) =>
            ReferenceEquals(_platform, original);

        // Observational reuse still requires the SAME live installed session, protected
        // original artifact/channel, and exact canonical snapshot. It grants no graph readiness.
        internal Task DemandOriginalReuseAsync(HomePackageArtifactSelection sameOriginalSelection,
            HomePackageDatabaseSnapshot originalRegistry, CancellationToken caller = default)
        {
            ArgumentNullException.ThrowIfNull(sameOriginalSelection);
            if (!sameOriginalSelection.IssuedBy(_platform))
                throw new UnauthorizedAccessException("SAME protected original selection is required.");
            var captured = HomePackageDatabase.CaptureGuardedSnapshot(originalRegistry);
            return RetainAsync(async token =>
            {
                var current = await ReadRegistryAsync(token).ConfigureAwait(false);
                if (!JsonSerializer.SerializeToUtf8Bytes(current).AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(captured)))
                    throw new UnauthorizedAccessException("The original canonical reuse snapshot changed.");
                await _platform.DemandOriginalArtifactAndChannelAsync(sameOriginalSelection,
                    _connection.Actor, _connection.InstalledCaller, _connection.SessionId, token).ConfigureAwait(false);
                if (!await _connection.IsCurrentInsideOriginalGateAsync(token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original installed session retired during reuse observation.");
                current = await ReadRegistryAsync(token).ConfigureAwait(false);
                if (!JsonSerializer.SerializeToUtf8Bytes(current).AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(captured)))
                    throw new UnauthorizedAccessException("The original canonical reuse snapshot changed.");
                return true;
            }, caller);
        }

        internal Task<HomePackageOriginalReview> RequestAsync(HomePackageActionRequest originalRequest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(originalRequest);
            var request = originalRequest with { };
            if (!HomePackageArtifactSelection.Identifier(request.PackageId) ||
                !Enum.IsDefined(request.Action) || !HomePackageArtifactSelection.Text(request.IdempotencyKey, 256) ||
                request.RequestedVersion is not null && !HomePackageArtifactSelection.Text(request.RequestedVersion, 256) ||
                request.RequestedChannel is not null && !HomePackageArtifactSelection.Text(request.RequestedChannel, 128) ||
                request.ExpectedRevision is not null && !HomePackageArtifactSelection.Text(request.ExpectedRevision, 1024))
                throw new ArgumentException("Exact bounded original package request required.", nameof(originalRequest));
            return RetainAsync(RequestOriginalAsync, cancellationToken);

            async Task<HomePackageOriginalReview> RequestOriginalAsync(CancellationToken token)
            {
                HomePackageOriginalReview? prior;
                lock (_gate) _reviews.TryGetValue(request.IdempotencyKey, out prior);
                if (prior is not null)
                {
                    if (prior.Request != request)
                        throw new UnauthorizedAccessException("The original package idempotency key cannot name a different request.");
                    await DemandCurrentAsync(prior, token).ConfigureAwait(false);
                    return prior;
                }
                lock (_gate)
                    if (_reviews.Count >= MaximumOriginalReviews)
                        throw new InvalidOperationException("Original package review capacity reached; close this original adapter.");
                var registry = await ReadRegistryAsync(token).ConfigureAwait(false);
                if (registry.RecentOperations.Any(item => item.IdempotencyKey == request.IdempotencyKey))
                    throw new InvalidOperationException("A canonical recorded operation exists; observe/recover that same effect rather than dispatch again.");
                if (registry.RecentOperations.Any(item => item.PackageId == request.PackageId &&
                    item.State is HomePackageJournalState.Pending or HomePackageJournalState.OutcomeUnknown))
                    throw new InvalidOperationException("The original package has an unresolved canonical effect; recover it before admitting another mutation.");
                var originalEntry = registry.Packages.SingleOrDefault(item => item.PackageId == request.PackageId);
                var originalEntryRevision = originalEntry?.Revision ?? 0;
                if (originalEntry is not null && originalEntryRevision < 1 ||
                    originalEntryRevision == long.MaxValue ||
                    request.Action == HomePackageAction.Install && originalEntry is not null &&
                        (originalEntry.InstalledVersion is not null ||
                         originalEntry.InstallationState is HomePackageInstallState.Installed or
                             HomePackageInstallState.Updating or HomePackageInstallState.Removing))
                    throw new UnauthorizedAccessException("Original first-install entry/revision is unavailable or incompatible.");
                var material = await _platform.ResolveOriginalArtifactAsync(request, registry, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Original trusted package artifact selection is unavailable.");
                var artifact = HomePackageArtifactSelection.Capture(_platform, material, request);
                var mapped = _platform.ResolveOriginalAction(request.Action, artifact.Descriptor)
                    ?? throw new InvalidOperationException("No trusted owner package action mapping is registered.");
                if (!HomePackageArtifactSelection.Text(mapped.TargetAppId, 128) ||
                    !HomePackageArtifactSelection.Text(mapped.ActionId, 256) ||
                    !HomePackageArtifactSelection.Identifier(mapped.RequiredInstalledServiceId) ||
                    !_connection.InstalledCaller.AllowedServiceIds.Contains(mapped.RequiredInstalledServiceId) ||
                    mapped.Scopes is null)
                    throw new InvalidDataException("Trusted package action mapping is invalid.");
                var scopes = mapped.Scopes.Take(1001).ToArray();
                if (scopes.Length is < 1 or > 1000 || scopes.Any(item => item is null ||
                        !HomePackageArtifactSelection.Text(item.Kind, 256) ||
                        !HomePackageArtifactSelection.Text(item.Id, 1024) ||
                        !HomePackageArtifactSelection.Text(item.Revision, 1024) || !Enum.IsDefined(item.Access)))
                    throw new InvalidDataException("Explicit bounded canonical package resource scopes required.");
                var binding = mapped with { Scopes = Array.AsReadOnly(scopes) };
                var policy = _permissions.ResolveTrustedActionPolicy(binding.TargetAppId, binding.ActionId)
                    ?? throw new UnauthorizedAccessException("The original Home trusted catalogue has no package action policy.");
                // Consequential package mutations cannot enter through a routine read catalogue.
                if (request.Action != HomePackageAction.Launch &&
                    (policy.Risk is not (PermissionRisk.Elevated or PermissionRisk.High) || !policy.RequiresPerActionApproval))
                    throw new UnauthorizedAccessException("Consequential package policy requires actual elevated per-action approval.");
                await DemandCurrentSelectionAsync(artifact, registry.Revision, token).ConfigureAwait(false);
                var arguments = JsonSerializer.SerializeToElement(new {
                    schemaVersion = 1, request, registryRevision = registry.Revision,
                    originalPackageEntryRevision = originalEntryRevision,
                    requiredInstalledServiceId = binding.RequiredInstalledServiceId,
                    catalogueRevision = artifact.CatalogueRevision,
                    descriptorSha256 = artifact.SignedDescriptorSha256,
                    descriptorPayloadSha256 = artifact.DescriptorPayloadSha256,
                    artifact.Descriptor, originalActor = _connection.Actor,
                    originalCaller = new {
                        _connection.InstalledCaller.AppId, _connection.InstalledCaller.InstalledApplicationId,
                        _connection.InstalledCaller.InstallationRevision, _connection.InstalledCaller.ExecutableIdentity
                    }, originalSessionId = _connection.SessionId
                });
                if (JsonSerializer.SerializeToUtf8Bytes(arguments).Length > 128 * 1024)
                    throw new InvalidDataException("Original package review arguments exceed the supported bound.");
                var nativeCaller = _connection.CaptureOriginalCallerInsideGate();
                if (nativeCaller.Actor != _connection.Actor || nativeCaller.SessionId != _connection.SessionId)
                    throw new UnauthorizedAccessException("The SAME original native package caller proof is required.");
                var prepared = _broker.PrepareReviewForOriginalInstalledCaller(nativeCaller, binding.TargetAppId,
                    binding.ActionId, binding.Scopes, arguments, "Exact original package operation",
                    registry.Packages.SingleOrDefault(item => item.PackageId == request.PackageId)?.LastKnownGoodVersion);
                // Retain original prepared custody BEFORE durable authorization/possible lost acknowledgement.
                var review = new HomePackageOriginalReview(this, request, artifact, prepared, binding,
                    arguments, registry.Revision, originalEntryRevision, policy);
                lock (_gate) _reviews.Add(request.IdempotencyKey, review);
                await DemandCurrentAsync(review, token).ConfigureAwait(false);
                await _broker.AuthorizePreparedReviewAsync(prepared, token).ConfigureAwait(false);
                await DemandCurrentAsync(review, token).ConfigureAwait(false);
                return review;
            }
        }

        internal Task<HomePackageActionResult> ExecuteAsync(HomePackageOriginalReview originalReview,
            CancellationToken cancellationToken = default)
        {
            RequireOwned(originalReview);
            var run = new OriginalExecutionRun();
            return RetainAsync(token => ExecuteOriginalAsync(originalReview, run, token), cancellationToken,
                original => run.Task = original);
        }

        private sealed class OriginalExecutionRun { internal Task? Task; }
        private async Task<HomePackageActionResult> ExecuteOriginalAsync(HomePackageOriginalReview review,
            OriginalExecutionRun run, CancellationToken token)
        {
            await DemandCurrentAsync(review, token).ConfigureAwait(false);
            var observation = await _broker.ObservePreparedReviewAsync(review.Prepared, token).ConfigureAwait(false);
            if (observation.Request?.State == HomePermissionRequestState.PendingApproval)
                return Result(review, HomePackageOperationState.Pending, "HomePackages.PermissionRequired");
            if (observation.State != HomePreparedReviewState.RequestObserved || !observation.BindingRecovered ||
                observation.Request is not { State: HomePermissionRequestState.Approved } approved ||
                approved.Policy != review.Policy ||
                _permissions.ResolveTrustedActionPolicy(review.Binding.TargetAppId, review.Binding.ActionId) != review.Policy)
                return Result(review, HomePackageOperationState.Rejected, "HomePackages.PermissionDenied");
            if (Interlocked.CompareExchange(ref review.DispatchAttempted, 1, 0) != 0)
                throw new InvalidOperationException("Observe the SAME attempted package operation; it cannot dispatch twice.");
            review.OriginalDispatchTask = run.Task ?? throw new InvalidOperationException("Original dispatch task was not published.");

            HomePackageActionResult? result = null;
            Exception? primary = null;
            List<Exception> failures = [];
            bool ownerEntered = false;
            try
            {
                // A begin may consume its original intent even if its acknowledgement is lost.
                // This retained route permits audit-only recovery, never another dispatch.
                review.Settlement = HomePackageOriginalSettlement.RejectedBegin;
                review.Capability = await _broker.BeginExecutionCapabilityAsync(review.Prepared.RequestId,
                    review.Arguments, token).ConfigureAwait(false);
                if (review.Capability is null)
                    throw new UnauthorizedAccessException("The original package begin was refused; its consumed/audit state must be observed.");
                review.Settlement = HomePackageOriginalSettlement.UnclaimedAbort;
                await DemandCurrentAsync(review, token).ConfigureAwait(false);
                // Any failure after entering the actual claim is a retained rejection/unknown route.
                // Its exact per-invocation result is the only basis for changing that route.
                review.Settlement = HomePackageOriginalSettlement.RejectedClaim;
                var claim = await _broker.ClaimExecutionObservedAsync(review.Capability,
                    review.Binding.TargetAppId, review.Binding.ActionId, review.Binding.Scopes,
                    review.Arguments, token).ConfigureAwait(false);
                if (claim.Disposition == HomeResourceClaimDisposition.InputNotConsumed)
                    review.Settlement = HomePackageOriginalSettlement.UnclaimedAbort;
                if (claim.Disposition == HomeResourceClaimDisposition.Claimed)
                {
                    review.Settlement = HomePackageOriginalSettlement.Claimed;
                    if (claim.Actor != _connection.Actor)
                        throw new UnauthorizedAccessException("The actual claimed package actor differs from the original session.");
                }
                else
                    throw new UnauthorizedAccessException("The original package resource claim was refused.");
                await DemandBeforeEffectAsync(review, token).ConfigureAwait(false);
                var invocation = new HomePackageOriginalInvocation(review, _connection,
                    ct => DemandBeforeEffectAsync(review, ct));
                ownerEntered = true;
                result = await _platform.ExecuteAndCommitOriginalAsync(invocation, token).ConfigureAwait(false);
                result = CaptureOriginalResult(review, result);
                review.OriginalResult = result;
            }
            catch (Exception error) { primary = review.OriginalFailure = error; }
            finally
            {
                if (review.Settlement == HomePackageOriginalSettlement.Claimed)
                {
                    result ??= review.OriginalResult; // A lost root acknowledgement may still have a retained known effect.
                    var state = result?.State switch {
                        HomePackageOperationState.Succeeded => HomePermissionRequestState.Succeeded,
                        HomePackageOperationState.Failed or HomePackageOperationState.Rejected =>
                            HomePermissionRequestState.Failed,
                        HomePackageOperationState.Cancelled => HomePermissionRequestState.Cancelled,
                        _ => ownerEntered ? HomePermissionRequestState.PartiallyCompleted :
                            HomePermissionRequestState.Failed
                    };
                    review.OriginalOutcome = new HomeExecutionOutcome(state,
                        result?.Code ?? (ownerEntered ? "HOME_PACKAGE_OUTCOME_UNCONFIRMED" : "HOME_PACKAGE_NOT_DISPATCHED"),
                        result?.Message ?? "The SAME original owner result or failure is retained; no repeated dispatch is allowed.",
                        Array.AsReadOnly(review.Binding.Scopes.Select(scope =>
                            new HomeObjectReference(scope.Kind, scope.Id)).ToArray()));
                }
                // Settlement is independent of retired execution permission. It retains the SAME
                // capability/result/failure and cannot mint another effect or replace its outcome.
                try { await SettleOriginalAsync(review, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error, primary); }
            }
            Throw(primary, failures);
            return result ?? throw new InvalidOperationException("The original package operation returned no known result.");
        }

        internal Task RetryOriginalSettlementAsync(HomePackageOriginalReview originalReview,
            CancellationToken cancellationToken = default)
        {
            RequireOwned(originalReview);
            if (Volatile.Read(ref originalReview.OriginalDispatchTask) is not { IsCompleted: true })
                throw new InvalidOperationException("The SAME original dispatch task must settle before audit-only retry.");
            return RetainSettlementAsync(originalReview, cancellationToken);
        }
        private Task RetainSettlementAsync(HomePackageOriginalReview review, CancellationToken caller)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task original;
            lock (_gate)
            {
                if (_closing) throw new ObjectDisposedException(nameof(HomePackageOriginalOperationAdmission));
                if (_originalTasks.Count >= MaximumOriginalTasks)
                    throw new InvalidOperationException("Original package task capacity reached; do not drop audit causes.");
                original = RetryAfterStartAsync(start.Task, review, caller);
                _originalTasks.Add(original);
            }
            start.SetResult();
            return original;
        }
        private async Task RetryAfterStartAsync(Task start, HomePackageOriginalReview review, CancellationToken token)
        {
            await start.ConfigureAwait(false);
            // The retry observes only privately retained settlement. No current actor/owner lookup,
            // original connection Gate, artifact resolution, root channel or dispatch is acquired.
            await SettleOriginalAsync(review, token).ConfigureAwait(false);
        }
        private async Task SettleOriginalAsync(HomePackageOriginalReview review, CancellationToken token)
        {
            RequireOwned(review);
            if (Volatile.Read(ref review.DispatchAttempted) == 0 ||
                review.Settlement == HomePackageOriginalSettlement.None)
                throw new InvalidOperationException("No exact original dispatch admission has settled for audit recovery.");
            HomePermissionOperationResult audit;
            switch (review.Settlement)
            {
                case HomePackageOriginalSettlement.RejectedBegin:
                    audit = await _broker.RetryRejectedBeginAuditAsync(review.Prepared.RequestId, token).ConfigureAwait(false);
                    break;
                case HomePackageOriginalSettlement.UnclaimedAbort:
                    audit = await _broker.AbortUnclaimedExecutionAsync(
                        review.Capability ?? throw new InvalidOperationException("Original abort capability missing."), token).ConfigureAwait(false);
                    break;
                case HomePackageOriginalSettlement.RejectedClaim:
                    audit = await _broker.RetryRejectedClaimAuditAsync(
                        review.Capability ?? throw new InvalidOperationException("Original rejected claim capability missing."), token).ConfigureAwait(false);
                    break;
                case HomePackageOriginalSettlement.Claimed:
                    audit = await _broker.CompleteExecutionAsync(
                        review.Capability ?? throw new InvalidOperationException("Original claimed capability missing."),
                        review.OriginalOutcome ?? throw new InvalidOperationException("SAME original outcome is not yet settled."),
                        token).ConfigureAwait(false);
                    break;
                default: throw new InvalidOperationException("Unsupported original settlement route.");
            }
            if (!audit.Succeeded)
                throw new InvalidOperationException("The original package audit remains unconfirmed: " + audit.Code);
        }

        internal ValueTask<IAsyncDisposable?> AcquireOriginalCompletionLeaseAsync(
            HomePackageOriginalReview review, CancellationToken token)
        {
            RequireOwned(review);
            if (review.Settlement != HomePackageOriginalSettlement.Claimed || review.Capability is null)
                return ValueTask.FromResult<IAsyncDisposable?>(null);
            return review.Capability.AcquireCommitCompletionLeaseAsync(_broker, token);
        }
        private async Task DemandBeforeEffectAsync(HomePackageOriginalReview review, CancellationToken token)
        {
            await DemandCurrentAsync(review, token).ConfigureAwait(false);
            if (_permissions.ResolveTrustedActionPolicy(review.Binding.TargetAppId, review.Binding.ActionId) != review.Policy)
                throw new UnauthorizedAccessException("The exact trusted package action policy changed before an effect.");
            var mapped = _platform.ResolveOriginalAction(review.Request.Action, review.Artifact.Descriptor);
            if (mapped is null || mapped.TargetAppId != review.Binding.TargetAppId ||
                mapped.ActionId != review.Binding.ActionId ||
                mapped.RequiredInstalledServiceId != review.Binding.RequiredInstalledServiceId ||
                !_connection.InstalledCaller.AllowedServiceIds.Contains(mapped.RequiredInstalledServiceId) ||
                mapped.Scopes is null ||
                !mapped.Scopes.Take(1001).SequenceEqual(review.Binding.Scopes))
                throw new UnauthorizedAccessException("The exact trusted package resource/action mapping changed.");
            var observed = await _permissions.ReadRequestObservationAsync(review.Prepared.RequestId, token).ConfigureAwait(false);
            if (observed is null || observed.Policy != review.Policy)
                throw new UnauthorizedAccessException("The original broker package policy observation changed.");
            var decision = review.Capability is null ? null :
                await _broker.GetExecutionDecisionAsync(review.Capability, token).ConfigureAwait(false);
            if (decision is null || !decision.IsAllowed || decision.State != HomePermissionRequestState.Executing)
                throw new UnauthorizedAccessException("Original package execution permission is no longer current.");
            await DemandCurrentAsync(review, token).ConfigureAwait(false);
        }
        private Task DemandCurrentAsync(HomePackageOriginalReview review, CancellationToken token)
        {
            RequireOwned(review);
            return DemandCurrentSelectionAsync(review.Artifact, review.CurrentRegistryRevision, token);
        }
        private async Task DemandCurrentSelectionAsync(HomePackageArtifactSelection artifact,
            long registryRevision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!artifact.IssuedBy(_platform) ||
                !await _connection.IsCurrentInsideOriginalGateAsync(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The SAME original installed package session has retired.");
            await _platform.DemandOriginalArtifactAndChannelAsync(artifact, _connection.Actor,
                _connection.InstalledCaller, _connection.SessionId, token).ConfigureAwait(false);
            if ((await ReadRegistryAsync(token).ConfigureAwait(false)).Revision != registryRevision)
                throw new UnauthorizedAccessException("The original device package registry revision changed.");
            token.ThrowIfCancellationRequested();
            if (!await _connection.IsCurrentInsideOriginalGateAsync(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Original package session changed during admission.");
        }
        private async Task<HomePackageDatabaseSnapshot> ReadRegistryAsync(CancellationToken token)
        {
            if (!ReferenceEquals(_platform.DeviceStore, _deviceStore) ||
                !ReferenceEquals(_platform.Database, _database) || !_database.IsBoundToStore(_deviceStore))
                throw new UnauthorizedAccessException("The SAME registered device package owner/store was replaced.");
            var read = await _database.ReadAsync(token).ConfigureAwait(false);
            return read.Succeeded ? read.Snapshot! : throw new HomePackageDatabaseException(read.Failure!);
        }
        private void RequireOwned(HomePackageOriginalReview review)
        {
            ArgumentNullException.ThrowIfNull(review);
            lock (_gate)
                if (!ReferenceEquals(review.Owner, this) ||
                    !_reviews.TryGetValue(review.Request.IdempotencyKey, out var retained) ||
                    !ReferenceEquals(review, retained))
                    throw new UnauthorizedAccessException("The SAME retained opaque original package review is required.");
        }
        private Task<T> RetainAsync<T>(Func<CancellationToken, Task<T>> original, CancellationToken caller,
            Action<Task<T>>? publishInsideGate = null)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<T> work;
            lock (_gate)
            {
                if (_closing) throw new ObjectDisposedException(nameof(HomePackageOriginalOperationAdmission));
                if (_originalTasks.Count >= MaximumOriginalTasks)
                    throw new InvalidOperationException("Original package task capacity reached; close this adapter without dropping causes.");
                work = RunOriginalAsync(start.Task, original, caller);
                _originalTasks.Add(work); // SAME original task published BEFORE callbacks/Cancel reentry.
                publishInsideGate?.Invoke(work); // Private task-field assignment only, before start release.
            }
            start.SetResult();
            return work;
        }
        private async Task<T> RunOriginalAsync<T>(Task start, Func<CancellationToken, Task<T>> original,
            CancellationToken caller)
        {
            await start.ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
            return await _connection.RunOwnedAsync(original, linked.Token).ConfigureAwait(false);
        }
        internal Task CloseAndDrainAsync()
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task result;
            lock (_gate)
            {
                if (_close is not null) return _close;
                _closing = true;
                result = _close = CloseOriginalAsync(start.Task, _originalTasks.ToArray());
            }
            start.SetResult(); // SAME close exists before original cancellation callback reentry.
            return result;
        }
        private async Task CloseOriginalAsync(Task start, Task[] originals)
        {
            await start.ConfigureAwait(false);
            List<Exception> failures = [];
            try { _lifetime.Cancel(); } catch (Exception error) { Add(failures, error, null); }
            foreach (var original in originals)
                try { await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error, null); }
            foreach (var review in _reviews.Values)
            {
                if (Volatile.Read(ref review.DispatchAttempted) != 0)
                {
                    try { await SettleOriginalAsync(review, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception error) { Add(failures, error, null); }
                }
                try
                {
                    if (!await _broker.RetirePreparedReviewAsync(review.Prepared, CancellationToken.None).ConfigureAwait(false))
                        throw new InvalidOperationException("The exact original pending/unknown package review remains retained for owning recovery.");
                }
                catch (Exception error) { Add(failures, error, null); }
            }
            try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error, null); }
            Throw(null, failures);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

        private static HomePackageActionResult Result(HomePackageOriginalReview review,
            HomePackageOperationState state, string code) => new(review.OperationId, review.Request.PackageId,
            review.Request.Action, state, code, "The original package operation has not dispatched.",
            false, true, false, [], [], [], []);

        internal static HomePackageActionResult CaptureOriginalResult(HomePackageOriginalReview review,
            HomePackageActionResult value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.OperationId != review.OperationId || value.PackageId != review.Request.PackageId ||
                value.Action != review.Request.Action || !Enum.IsDefined(value.State) ||
                value.State == HomePackageOperationState.Pending ||
                !HomePackageArtifactSelection.Text(value.Code, 128) ||
                !HomePackageArtifactSelection.Text(value.Message, 4096))
                throw new InvalidDataException("The original platform result does not bind the exact package operation.");
            string[] Capture(IReadOnlyList<string>? source)
            {
                if (source is null) throw new InvalidDataException("Original package step evidence missing.");
                var steps = source.Take(1001).ToArray();
                if (steps.Length > 1000 || steps.Any(item => !HomePackageArtifactSelection.Text(item, 512)))
                    throw new InvalidDataException("Original package step evidence exceeds its bound.");
                return steps;
            }
            return value with {
                SucceededSteps = Array.AsReadOnly(Capture(value.SucceededSteps)),
                FailedSteps = Array.AsReadOnly(Capture(value.FailedSteps)),
                SkippedSteps = Array.AsReadOnly(Capture(value.SkippedSteps)),
                RolledBackSteps = Array.AsReadOnly(Capture(value.RolledBackSteps))
            };
        }
        private static void Add(List<Exception> failures, Exception error, Exception? primary)
        {
            if (!ReferenceEquals(error, primary) && !failures.Any(item => ReferenceEquals(item, error)))
                failures.Add(error);
        }
        private static void Throw(Exception? primary, List<Exception> failures)
        {
            if (primary is null && failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count != 0)
                throw new AggregateException("Original package body/settlement/drain failures retained.",
                    primary is null ? failures : new[] { primary }.Concat(failures));
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }
}

namespace HavenOS.Home.Core
{
    using HavenOS.Home.Apps;

    public sealed partial class HomeNativeCoreApiSessions
    {
        public sealed partial class Session
        {
            // Existing outer/session/private Context declarations and their entire bodies stay exact.
            // Only the registered genuine package owner may use this internal composition seam.
            internal HomePackageOriginalOperationAdmission OpenOriginalPackageOperations(
                IHomePackageOriginalPlatformOwner originalPlatform, HomeResourceOperationBroker originalBroker)
                => new(new OriginalPackageConnection(this), originalPlatform, originalBroker, _issuer._permissions);

            /// <summary>Opaque SAME-session issuer proof; not a serialized caller identity.</summary>
            internal sealed class OriginalPackageCaller
            {
                private readonly Session _owner;
                private OriginalPackageCaller(Session owner) { _owner = owner; }
                internal static OriginalPackageCaller Capture(Session original)
                {
                    ArgumentNullException.ThrowIfNull(original);
                    var proof = new OriginalPackageCaller(original);
                    proof.RequireRetained();
                    return proof;
                }
                internal AuthenticatedResourceActor Actor => _owner._context.Actor;
                internal HomePermissionCallerIdentity PermissionCaller => _owner._context.PermissionCaller;
                internal string SessionId => _owner._context.SessionId;
                internal void RequireRetained()
                {
                    var context = _owner._context;
                    if (!_owner._issuer._sessions.TryGetValue(context.Id, out var retained) ||
                        !ReferenceEquals(retained, context) || context.Closed ||
                        context.Lifetime.IsCancellationRequested || !context.Lease.IsHeld)
                        throw new UnauthorizedAccessException("SAME retained original installed caller proof required.");
                }
            }
            // Called only inside this session's original Gate, after full CurrentAsync observation.
            internal OriginalPackageCaller CaptureOriginalPackageCallerInsideGate()
            {
                return OriginalPackageCaller.Capture(this);
            }

            private sealed class OriginalPackageConnection(Session original) : IHomePackageOriginalConnection
            {
                private readonly Session _original = original;
                public AuthenticatedResourceActor Actor => _original._context.Actor;
                public HomeNativeInstalledPeer InstalledCaller => _original._context.Peer;
                public HomeNativeObservedPeer? ObservedPeer => _original._context.Observed;
                public string SessionId => _original._context.SessionId;
                public OriginalPackageCaller CaptureOriginalCallerInsideGate() =>
                    _original.CaptureOriginalPackageCallerInsideGate();
                public CancellationToken OriginalLifetime => _original._context.Lifetime;
                public Task<bool> IsCurrentInsideOriginalGateAsync(CancellationToken token) =>
                    _original._issuer.CurrentAsync(_original._context, token).AsTask();
                public async Task<T> RunOwnedAsync<T>(Func<CancellationToken, Task<T>> originalWork,
                    CancellationToken cancellationToken)
                {
                    var context = _original._context;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.Lifetime);
                    await context.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
                    try
                    {
                        if (!_original._issuer._sessions.TryGetValue(context.Id, out var issued) ||
                            !ReferenceEquals(issued, context) ||
                            !await _original._issuer.CurrentAsync(context, linked.Token).ConfigureAwait(false))
                            throw new UnauthorizedAccessException("Original installed package connection is unavailable.");
                        return await originalWork(linked.Token).ConfigureAwait(false);
                    }
                    finally { context.Gate.Release(); }
                }
            }
        }
    }
}
