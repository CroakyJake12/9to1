using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Sockets;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Native.Linux;

public sealed partial class LinuxHomeRootClient
{
    private sealed record AppsRemoval(HomePackageActionRequest OriginalRequest, Task<HomePackageActionResult> OriginalTask);
    private readonly Dictionary<string, AppsRemoval> _appsRemovals = new(StringComparer.Ordinal);
    private Task<HomePackageActionResult> ExecuteOriginalOptionalRemoval(HomePackageActionRequest request, CancellationToken token)
    {
        lock (_gate)
        {
            DemandActive(); token.ThrowIfCancellationRequested();
            if (_appsRemovals.TryGetValue(request.IdempotencyKey, out var prior))
                return prior.OriginalRequest == request ? prior.OriginalTask : throw new UnauthorizedAccessException("An original removal cannot be rebound.");
            var snapshot = _currentAppsSnapshot;
            if (_launchRetiring || _appsRemovals.Count + _appsOperations.Count >= 64 || _appsOperations.ContainsKey(request.IdempotencyKey) ||
                _packageService?.IsOriginalAvailable != true || _packageLifecycleVersion != 1 || request.RequestedVersion is not null || request.RequestedChannel is not null ||
                request.IdempotencyKey is not { Length: > 0 and <= 128 } || snapshot is null || request.ExpectedRevision != snapshot.Revision ||
                !_appsSnapshots.TryGetValue(snapshot, out var view) || view.OriginalPackages is null)
                return Task.FromResult(AppsDecision(request, HomePackageOperationState.Rejected, "HomeApps.CurrentRemovalChoiceRequired", "Refresh Apps before removing an app."));
            var row = view.Rows.SingleOrDefault(value => _choices.TryGetValue(value, out var choice) && choice.Package.PackageId == request.PackageId);
            if (row is null || !_choices.TryGetValue(row, out var selected) ||
                HomePackageOriginalRemovalChecks.Evaluate(view.OriginalPackages, ParseDescriptor(selected.Package), _homePackageId!) is not null)
                return Task.FromResult(AppsDecision(request, HomePackageOperationState.Rejected, "HomeApps.RemovalUnavailable", "This app cannot be removed."));
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = Admit(body => body(), _ => { }, async (source, caller) => {
                await begin.Task.ConfigureAwait(false);
                await DemandChoice(source, selected, caller).ConfigureAwait(false);
                var read = await source.Read(() => new HomePackageDatabase(_packageDeviceStore!).ReadAsync(caller)).ConfigureAwait(false);
                if (!read.Succeeded || LinuxHomePackageRemovalWire.Digest(read.Snapshot) != LinuxHomePackageRemovalWire.Digest(view.OriginalPackages))
                    return AppsDecision(request, HomePackageOperationState.Rejected, "HomeApps.PackageSelectionChanged", "Refresh Apps before removing an app.");
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                HomeUnixCoreClient? client = null; var failures = new List<Exception>(); HomePackageActionResult? result = null;
                try
                {
                    await source.Read(() => socket.ConnectAsync(new UnixDomainSocketEndPoint(selected.Listening.PipeName), caller).AsTask()).ConfigureAwait(false);
                    client = await source.Read(() => HomeUnixCoreClient.AttachAsync(socket, this,
                        new("home", "9to1.package:" + _homePackageId), _lifetime.Token, caller).AsTask()).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("The actual SAME original Home session could not be authenticated.");
                    // Translate only this privately retained source selection. The
                    // frontend snapshot digest is never an arbitrary catalogue ID.
                    var removal = request with { ExpectedRevision = selected.Package.CatalogueRevision };
                    var reply = await source.Read(() => client.BeginPackageRemovalAsync(removal, caller)).ConfigureAwait(false);
                    var observation = reply.Operation.Value ?? throw new InvalidDataException("The original package operation was not observed.");
                    while (observation.State == HomePackageOperationState.Pending && !observation.AuditOnlyAfterAttempt)
                    {
                        lock (_gate)
                            if (_launchRetiring)
                            { result = AppsDecision(request, HomePackageOperationState.Cancelled, "HomePackages.OriginalReviewRetired", "Removal cancelled."); break; }
                        // This repeats only a privately retained pre-effect review
                        // continuation. The original runner forbids redispatch once attempted.
                        await source.Read(() => Task.Delay(TimeSpan.FromMilliseconds(500), caller)).ConfigureAwait(false);
                        reply = await source.Read(() => client.ContinuePackageRemovalAsync(observation.OperationId, caller)).ConfigureAwait(false);
                        observation = reply.Operation.Value ?? throw new InvalidDataException("The same original package continuation has no observation.");
                    }
                    if (result is null)
                    {
                        var known = observation.KnownResults.LastOrDefault();
                        result = known is not null && known.State == observation.State && observation.State is
                            HomePackageOperationState.Succeeded or HomePackageOperationState.Rejected or HomePackageOperationState.Failed or HomePackageOperationState.Cancelled
                            ? known with { OperationId = request.IdempotencyKey } : AppsDecision(request, observation.AuditOnlyAfterAttempt && observation.State is not (HomePackageOperationState.Rejected or HomePackageOperationState.Failed)
                                ? HomePackageOperationState.Unknown : observation.State,
                                observation.Code, observation.AuditOnlyAfterAttempt ? "Check the original removal before trying again." : "Removal was declined.");
                    }
                }
                catch (Exception cause) { failures.Add(cause); }
                finally
                {
                    if (client is not null) try { await source.CloseAsync(client).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                    try { socket.Dispose(); } catch (Exception cause) { failures.Add(cause); }
                }
                if (failures.Count != 0) throw new AggregateException("The original optional removal and native session retain their actual outcomes/failures.", failures);
                return result ?? throw new InvalidDataException("The original removal has no known observation.");
            }, CancellationToken.None);
            _appsRemovals.Add(request.IdempotencyKey, new(request with { }, actual)); begin.SetResult(); return actual;
        }
    }
    private OriginalPackageDeviceStore? _packageDeviceStore;
    private OriginalPackageRootPort? _packageRootPort;
    private OriginalPackageMutation? _currentPackageMutation;
    private HomeNativePackageOperationsService? _packageService;
    public IHomeCoreService CreateOriginalPackageOperationsService(HomeNativeWindowsOwnerComponents owners, HomeNativeCoreApiSessions originalSessions)
    {
        lock (_gate)
        {
            DemandActive();
            if (_packageService is not null) throw new InvalidOperationException("Compose the SAME Linux package service once.");
            var store = _packageDeviceStore = new(this);
            var port = _packageRootPort = new(this);
            var artifacts = new OriginalPackageArtifacts(this, store, originalSessions);
            var device = new HomePackageOriginalDeviceOwner(store, artifacts, port, _lifetime.Token);
            _packageService = new(device, originalSessions, owners.Broker, async token => {
                if (_connect?.IsCompletedSuccessfully != true || _packageLifecycleVersion != 1)
                    throw new PlatformNotSupportedException("The actual registered Root has no original package lifecycle endpoint.");
                var read = await store.ReadAsync(token).ConfigureAwait(false);
                if (!read.IsSuccess) throw new UnauthorizedAccessException("The actual canonical Root package registry is unavailable.");
            }, () => _homePackageId ?? throw new UnauthorizedAccessException("The actual Root Home package identity is unavailable."),
                () => _homeActivation?.Abi ?? throw new UnauthorizedAccessException("The actual Root package ABI is unavailable."));
            return _packageService;
        }
    }
    public IOriginalScopedCanonicalResourceAccessResolver OriginalPackageResourceResolver =>
        new OriginalPackageResourceResolver(this);
    private sealed class OriginalPackageResourceResolver(LinuxHomeRootClient owner) : IOriginalScopedCanonicalResourceAccessResolver
    {
        public string ResourceKind => HomeNativePackageOperationsService.ResourceKind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource, CancellationToken token)
            => new(EvaluateWithinOriginalSourceAsync(actor, action, resource, body => body(), _ => { }, token));
        public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
            => (owner._packageService ?? throw new UnauthorizedAccessException("The same original package service is unconfigured."))
                .EvaluateWithinOriginalSourceAsync(actor, action, resource, scope, retain, token);
    }
    private sealed class OriginalPackageRootPort(LinuxHomeRootClient owner) : IHomePackageOriginalRootMutationPort
    {
        internal readonly ConditionalWeakTable<HomePackageOriginalRootRequest, OriginalPackageMutation> Original = new();
        public Task<IHomePackageOriginalRootMutation> OpenOriginalAsync(HomePackageOriginalRootRequest request, CancellationToken token)
            => owner.Admit<IHomePackageOriginalRootMutation>(body => body(), _ => { }, async (source, caller) => {
                if (owner._packageService?.IsOriginalAvailable != true || request.Action.Action != HomePackageAction.Uninstall || request.Artifact.ComponentClass != "optional-app" ||
                    owner._listening is null || request.Actor != owner._listening.Actor || request.OriginalObservedPeer is not { } observed ||
                    observed.ProcessId != Environment.ProcessId || request.InstalledCaller.AppId != "home" ||
                    request.InstalledCaller.InstalledApplicationId != owner._listening.InstalledApplicationId ||
                    !request.InstalledCaller.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole))
                    throw new UnauthorizedAccessException("The same actual original installed Home removal request is required.");
                await request.DemandOriginalCurrentBeforeEffectAsync(caller).ConfigureAwait(false);
                OriginalPackageMutation mutation;
                lock (owner._gate)
                {
                    if (owner._currentPackageMutation is { IsClosed: false }) throw new InvalidOperationException("Settle the same original package mutation first.");
                    if (Original.TryGetValue(request, out _)) throw new UnauthorizedAccessException("An original Root operation cannot be opened twice.");
                    mutation = new(owner, request); Original.Add(request, mutation); owner._currentPackageMutation = mutation;
                }
                var wire = new LinuxHomePackageRemovalWire.Open(request.OperationId, request.Action, request.Actor, request.InstalledCaller,
                    observed, request.SessionId, request.OriginalApprovalRequestId, request.ReviewedArgumentsSha256,
                    request.OriginalRegistryRevision, request.OriginalPackageEntryRevision, request.CatalogueRevision, request.SignedDescriptorSha256);
                var response = await owner.Exchange(source, new(1, Guid.NewGuid(), "open-package-removal") { PackageRemoval = wire }, caller).ConfigureAwait(false);
                if (response.PackageOperationId != request.OperationId) throw new UnauthorizedAccessException("The actual Root did not retain the same original package operation.");
                mutation.Opened = true;
                // This finite source owns the wire tasks only. The original
                // DeviceOwner retains and drains the returned mutation itself.
                return mutation;
            }, token);
    }
    private sealed class OriginalPackageMutation(LinuxHomeRootClient owner, HomePackageOriginalRootRequest original) : IHomePackageOriginalRootMutation
    {
        public HomePackageOriginalRootRequest OriginalRequest => original;
        internal bool Opened, IsClosed;
        private readonly object _sync = new();
        private Task<HomePackageOriginalRootOutcome>? _execute;
        private HomePackageOriginalRootOutcome? _known;
        private Task? _close;
        private sealed class Evidence(OriginalPackageMutation mutation, LinuxHomePackageRemovalWire.Outcome actual)
        { internal readonly OriginalPackageMutation Owner = mutation; internal readonly LinuxHomePackageRemovalWire.Outcome Actual = actual; }
        public IHomeStateCommitActorGuard OriginalAdmissionGuard => new Guard(this, null);
        internal void DemandOriginalForWrite(AuthenticatedResourceActor actor)
        {
            owner.DemandActive(); owner.DemandRoot();
            lock (owner._gate)
            {
                var port = owner._packageRootPort;
                if (owner._packageService?.HasOriginalCallbackFailure == true || !Opened || IsClosed || !ReferenceEquals(owner._currentPackageMutation, this) || actor != original.Actor ||
                    port is null || !port.Original.TryGetValue(original, out var issued) || !ReferenceEquals(issued, this))
                    throw new UnauthorizedAccessException("The same actual original Root operation guard is required.");
            }
        }
        public Task DemandOriginalChannelCurrentAsync(CancellationToken token)
            => owner.Admit(body => body(), _ => { }, async (source, caller) => {
                DemandOriginalForWrite(original.Actor);
                var current = await owner.Exchange(source, new(1, Guid.NewGuid(), "observe-package-removal") { PackageOperationId = original.OperationId }, caller).ConfigureAwait(false);
                if (current.PackageOperationId != original.OperationId) throw new UnauthorizedAccessException("The original retained Root package channel changed.");
                return true;
            }, token);
        public Task<HomePackageOriginalRootOutcome> ExecuteOriginalAsync(CancellationToken token)
        {
            lock (_sync) return _execute ??= owner.Admit(body => body(), _ => { }, async (source, caller) => {
                DemandOriginalForWrite(original.Actor); await original.DemandOriginalCurrentBeforeEffectAsync(caller).ConfigureAwait(false);
                var response = await owner.Exchange(source, new(1, Guid.NewGuid(), "execute-package-removal") { PackageOperationId = original.OperationId }, caller).ConfigureAwait(false);
                return Capture(response);
            }, token);
        }
        private HomePackageOriginalRootOutcome Capture(LinuxHomeRootControlWire.Response response)
        {
            if (response.PackageOperationId != original.OperationId || response.PackageRemovalOutcome is not { } outcome)
                throw new UnauthorizedAccessException("The actual original Root package result remains unknown.");
            if (outcome.Result.PackageId != original.Action.PackageId || outcome.Result.OperationId != original.OperationId || outcome.Result.Action != HomePackageAction.Uninstall)
                throw new UnauthorizedAccessException("The original package result names another request.");
            lock (_sync)
            {
                if (_known is { } known)
                {
                    if (LinuxHomePackageRemovalWire.Digest(known.Result) != LinuxHomePackageRemovalWire.Digest(outcome.Result) ||
                        LinuxHomePackageRemovalWire.Digest(known.ObservedPackage) != LinuxHomePackageRemovalWire.Digest(outcome.Package))
                        throw new UnauthorizedAccessException("The same known original Root outcome cannot be replaced.");
                    return known;
                }
                return _known = new(outcome.Result, outcome.Package, new Evidence(this, outcome));
            }
        }
        public Task<HomePackageOriginalRootOutcome?> ObserveOriginalOutcomeAsync(CancellationToken token)
            => owner.Admit<HomePackageOriginalRootOutcome?>(body => body(), _ => { }, async (source, caller) => {
                DemandOriginalForWrite(original.Actor);
                var response = await owner.Exchange(source, new(1, Guid.NewGuid(), "observe-package-removal") { PackageOperationId = original.OperationId }, caller).ConfigureAwait(false);
                return response.PackageRemovalOutcome is null ? null : Capture(response);
            }, token);
        public IHomeStateCommitActorGuard OriginalSettlementGuard(HomePackageOriginalRootOutcome same)
        {
            lock (_sync) if (!ReferenceEquals(_known, same) || same.OriginalRootOutcomeEvidence is not Evidence evidence || !ReferenceEquals(evidence.Owner, this))
                throw new UnauthorizedAccessException("Only the same original known Root effect may settle its canonical result.");
            return new Guard(this, same);
        }
        private sealed class Guard(OriginalPackageMutation mutation, HomePackageOriginalRootOutcome? known) : IHomeStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor, HomeStateCommitPhase phase, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); mutation.DemandOriginalForWrite(actor);
                lock (mutation._sync) return ValueTask.FromResult(known is null ? mutation._known is null : ReferenceEquals(known, mutation._known));
            }
        }
        public ValueTask DisposeAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            lock (_sync) return new(_close ??= Close());
            async Task Close()
            {
                var failures = new List<Exception>();
                if (_execute is { } execute)
                    try { await execute.ConfigureAwait(false); } catch (Exception cause) { failures.Add(execute.Exception ?? cause); }
                if (Opened)
                    try
                    {
                        await owner.Admit(body => body(), _ => { }, async (source, caller) => {
                            var response = await owner.Exchange(source, new(1, Guid.NewGuid(), "close-package-removal") { PackageOperationId = original.OperationId }, caller).ConfigureAwait(false);
                            if (response.PackageOperationId != original.OperationId) throw new UnauthorizedAccessException("The original Root package close was not acknowledged."); return true;
                        }, CancellationToken.None).ConfigureAwait(false);
                        IsClosed = true;
                    }
                    catch (Exception cause) { failures.Add(cause); }
                else IsClosed = true;
                if (failures.Count != 0) throw new AggregateException("The original Root package effect/close retained actual failures.", failures);
            }
        }
    }
    private sealed class OriginalPackageArtifacts(LinuxHomeRootClient owner, OriginalPackageDeviceStore store,
        HomeNativeCoreApiSessions sessions) : IHomePackageOriginalArtifactProvider
    {
        private sealed record Evidence(OriginalPackageArtifacts Owner, HomePackageDatabaseSnapshot Before,
            string CatalogueRevision, string EnrollmentSha256, string SignedSha256, string PayloadSha256);
        private readonly ConditionalWeakTable<HomePackageOriginalArtifactObservation, Evidence> _issued = new();
        private async Task<HomePackageDatabaseSnapshot> Read(CancellationToken token)
        {
            var read = await new HomePackageDatabase(store).ReadAsync(token).ConfigureAwait(false);
            return read.Succeeded && read.Snapshot is { } snapshot ? snapshot
                : throw new InvalidDataException("The actual canonical package state requires recovery.");
        }
        private static HomePackageOriginalPublisherEnrollmentRecord Enrollment(HomePackageDatabaseSnapshot snapshot)
            => snapshot.UnknownFields is { } fields && fields.TryGetValue(HomePackageOriginalPublisherEnrollmentRecord.Field, out var retained)
                ? retained.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidDataException("The actual retained publisher enrollment is empty.")
                : throw new UnauthorizedAccessException("The actual retained publisher enrollment is unavailable.");
        public async ValueTask<HomePackageOriginalArtifactObservation?> ResolveAsync(HomePackageActionRequest request,
            HomePackageDatabaseSnapshot originalDeviceRegistry, CancellationToken token)
        {
            var current = await Read(token).ConfigureAwait(false);
            if (LinuxHomePackageRemovalWire.Digest(current) != LinuxHomePackageRemovalWire.Digest(originalDeviceRegistry))
                throw new UnauthorizedAccessException("The SAME canonical Root package selection is required.");
            var enrollment = Enrollment(current);
            var rows = LinuxHomePackageOriginalArtifactProvider.ReadCatalogue(enrollment,
                owner._homeActivation?.Abi ?? throw new UnauthorizedAccessException("The original Root ABI is unavailable."), token);
            if (!rows.TryGetValue(request.PackageId, out var row)) return null;
            var parsed = HomePackageOriginalArtifactDescriptorParser.Parse(row.Signed, row.Payload, enrollment.CatalogueRevision, request);
            var proof = new Evidence(this, current, enrollment.CatalogueRevision, LinuxHomePackageRemovalWire.Digest(enrollment),
                Convert.ToHexString(SHA256.HashData(parsed.SignedDescriptor)), Convert.ToHexString(SHA256.HashData(parsed.Payload)));
            var observation = new HomePackageOriginalArtifactObservation(parsed.SignedDescriptor, parsed.Payload, enrollment.CatalogueRevision, proof);
            _issued.Add(observation, proof); return observation;
        }
        public HomePackageOriginalActionMap? ResolveAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor)
            => action == HomePackageAction.Uninstall && descriptor.ComponentClass == "optional-app"
                ? new("home", HomeNativePackageOperationsService.RemoveActionId, "home.core",
                    [new(HomeNativePackageOperationsService.ResourceKind, descriptor.PackageId, descriptor.Version, ResourceAccess.Write)]) : null;
        public async Task DemandOriginalCurrentAsync(HomePackageOriginalArtifactObservation observation, AuthenticatedResourceActor actor,
            HomeNativeInstalledPeer caller, string sessionId, CancellationToken token)
        {
            if (owner._packageService?.IsOriginalAvailable != true || !_issued.TryGetValue(observation, out var evidence) || !ReferenceEquals(evidence.Owner, this) ||
                !ReferenceEquals(observation.OriginalProviderEvidence, evidence) || observation.CatalogueRevision != evidence.CatalogueRevision ||
                Convert.ToHexString(SHA256.HashData(observation.SignedDescriptorBytes.Span)) != evidence.SignedSha256 ||
                Convert.ToHexString(SHA256.HashData(observation.DescriptorPayloadBytes.Span)) != evidence.PayloadSha256)
                throw new UnauthorizedAccessException("The same privately issued original package artifact is required.");
            await sessions.DemandOriginalPackageArtifactSessionCurrentAsync(actor, caller, sessionId, token).ConfigureAwait(false);
            var current = await Read(token).ConfigureAwait(false);
            if (LinuxHomePackageRemovalWire.Digest(Enrollment(current)) != evidence.EnrollmentSha256)
                throw new UnauthorizedAccessException("The original publisher enrollment changed.");
            if (LinuxHomePackageRemovalWire.Digest(current) != LinuxHomePackageRemovalWire.Digest(evidence.Before))
            {
                OriginalPackageMutation? mutation; lock (owner._gate) mutation = owner._currentPackageMutation;
                if (mutation is null || mutation.IsClosed || !mutation.Opened || mutation.OriginalRequest.Actor != actor ||
                    mutation.OriginalRequest.SessionId != sessionId || mutation.OriginalRequest.OriginalRegistryRevision != evidence.Before.Revision ||
                    mutation.OriginalRequest.SignedDescriptorSha256 != evidence.SignedSha256 || current.Revision != evidence.Before.Revision + 1 ||
                    LinuxHomePackageRemovalWire.Digest(current.Packages) != LinuxHomePackageRemovalWire.Digest(evidence.Before.Packages) ||
                    LinuxHomePackageRemovalWire.Digest(current.UnknownFields) != LinuxHomePackageRemovalWire.Digest(evidence.Before.UnknownFields) ||
                    current.RecentOperations.Count != evidence.Before.RecentOperations.Count + 1 ||
                    LinuxHomePackageRemovalWire.Digest(current.RecentOperations.Take(evidence.Before.RecentOperations.Count).ToArray()) !=
                        LinuxHomePackageRemovalWire.Digest(evidence.Before.RecentOperations))
                    throw new UnauthorizedAccessException("Only the SAME original operation's confirmed reservation may advance its artifact observation.");
                var journal = current.RecentOperations[^1]; var request = mutation.OriginalRequest;
                if (journal.OperationId != request.OperationId || journal.IdempotencyKey != request.Action.IdempotencyKey ||
                    journal.PackageId != request.Action.PackageId || journal.Action != HomePackageAction.Uninstall.ToString() ||
                    journal.State != HomePackageJournalState.Pending || journal.ResultCode != "HomePackages.OriginalReserved" || journal.CompletedAtUtc is not null)
                    throw new UnauthorizedAccessException("The exact original pending reservation changed.");
            }
            await sessions.DemandOriginalPackageArtifactSessionCurrentAsync(actor, caller, sessionId, token).ConfigureAwait(false);
        }
    }
}
