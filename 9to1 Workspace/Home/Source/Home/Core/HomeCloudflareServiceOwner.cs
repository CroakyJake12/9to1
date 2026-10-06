using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
namespace HavenOS.Home.Core;

/// <summary>Actual DeviceLocal Home app owner. Configuration is a reviewed delegation to an
/// existing saved OAuth connection, not a claim that its credential or CAKE role owns an account.</summary>
public sealed partial class HomeCloudflareServiceOwner : ICanonicalResourceAccessResolver, ICloudflareCallerScopedSavedServiceSource,
    ICloudflareProductionSetupSource, ICloudflareSavedServiceSource,
    ICloudflareOriginalPermissionSource, ICloudflareOriginalNamespaceOwner, ICloudflareOriginalPermissionRetirement
{
    private const string ConfigurationId = "home.cloudflare.connection";
    private const string SetupAction = "cloudflare.connection.configure";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly IExternalConnectionRepository _connections;
    private readonly ITaskRunOriginalActionAdmissionSource _actions;
    private readonly ICloudflareMcpInvocationClient _mcp;
    private readonly object _sync = new();
    private readonly ConditionalWeakTable<CloudflareSavedService, Capture> _services = new();
    private readonly Dictionary<CloudflareCompiledInvocation, Permission> _originals = new(ReferenceEqualityComparer.Instance);
    private sealed record Configuration(string ProfileId, CloudflareSetupSelection Selection, string ConnectionFingerprint);
    private sealed record Namespace(string ProfileId, Guid ConnectionId, string AccountId, string NamespaceId,
        Guid TaskId, Guid ExecutionId, string CreationOperationKey, string Title, bool Deleted);
    private sealed record Capture(AuthenticatedResourceActor Actor, HomeCoreStateRecord Record, Configuration Configuration, ExternalConnection Connection, CloudflareOriginalTaskLedger Stages);
    public HomeCloudflareServiceOwner(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        IExternalConnectionRepository connections, ITaskRunOriginalActionAdmissionSource actions, ICloudflareMcpInvocationClient mcp)
    {
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new InvalidOperationException("SAME actual Home store/profile/broker/permissions required.");
        _store = store; _profiles = profiles; _broker = broker; _permissions = permissions; _connections = connections; _actions = actions; _mcp = mcp;
    }
    public string ResourceKind => "cloudflare.resource";
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static string NamespaceRecordId(string account, string ns) => "home.cloudflare.namespace:" + account + ":" + ns;
    private static bool RecordShape(HomeCoreStateRecord record, string type) => record.RecordType == type && record.SchemaVersion == 1 &&
        record.Scope == HomeDataScope.DeviceLocal && record.Authority == HomeRecordAuthority.LocalCanonical && record.Revision > 0;
    private static HomeCoreStateRecord? Single(HomeCoreStoredState state, string id)
    { var found = state.Records.Where(x => x.RecordId == id).Take(2).ToArray(); return found.Length == 1 ? found[0] : null; }
    private static string SetupScopeId(AuthenticatedResourceActor actor) => "cloudflare:profile:" + actor.ProfileId;
    private async Task<AuthenticatedResourceActor> ActorAsync(CloudflareOriginalTaskLedger stages, CancellationToken token) =>
        await stages.AwaitAsync(stages.Invoke(() => _profiles.GetCurrentAsync(token))).ConfigureAwait(false)
            ?? throw new CloudflareSetupRequiredException(CloudflareSetupStage.HomeProfileRequired, "CF_HOME_PROFILE_REQUIRED", "Open the installed local Home profile before configuring Cloudflare.");
    private async Task<HomeCoreStoredState> StateAsync(CloudflareOriginalTaskLedger stages, CancellationToken token)
    {
        var read = await stages.AwaitAsync(stages.Invoke(() => _store.ReadAsync(token))).ConfigureAwait(false);
        return read.State ?? throw new InvalidDataException("Preserve and recover unavailable Home state before Cloudflare setup.");
    }
    private static void DemandSelection(CloudflareSetupSelection selection)
    {
        if (selection.ConnectionId == Guid.Empty || !CloudflareTypedToolCatalogue.IsHexId(selection.AccountId) ||
            !Uri.TryCreate(selection.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            string.IsNullOrWhiteSpace(selection.ExecuteTool) || selection.ExecuteTool.Length > 200)
            throw new CloudflareSetupRequiredException(CloudflareSetupStage.AccountSelectionRequired, "CF_EXPLICIT_SELECTION_REQUIRED", "Select the saved HTTPS OAuth service, exact Cloudflare account and fixed API tool in Home.");
    }
    private static void DemandConnection(ExternalConnection? connection, CloudflareSetupSelection selection)
    {
        DemandSelection(selection);
        var config = connection is null ? null : JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson);
        if (connection is null || connection.Id != selection.ConnectionId || connection.Kind != ExternalConnectionKind.Mcp ||
            !connection.IsEnabled || connection.State != ExternalConnectionState.Ready || config is null || !config.UseOAuth || config.LocalOnly ||
            config.Transport != McpTransportKind.StreamableHttp || !Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpoint) || endpoint != new Uri(selection.Endpoint))
            throw new CloudflareSetupRequiredException(CloudflareSetupStage.SavedOAuthConnectionRequired, "CF_SAVED_OAUTH_REQUIRED", "Connect and authorize the selected OAuth MCP service through the existing Connections settings.");
    }
    private async Task<Capture> ReadCaptureAsync(CloudflareOriginalTaskLedger stages, CancellationToken token)
    {
        var actor = await ActorAsync(stages, token).ConfigureAwait(false);
        var state = await StateAsync(stages, token).ConfigureAwait(false);
        var record = Single(state, ConfigurationId);
        if (record is null) throw new CloudflareSetupRequiredException(CloudflareSetupStage.AccountSelectionRequired, "CF_SETUP_REQUIRED", "Review and save a Cloudflare account connection in Home.");
        if (!RecordShape(record, ConfigurationId)) throw new InvalidDataException("Unsupported Cloudflare setup record; preserve it for recovery.");
        var config = record.Payload.Deserialize<Configuration>() ?? throw new InvalidDataException("Invalid Cloudflare setup record.");
        if (config.ProfileId != actor.ProfileId) throw new UnauthorizedAccessException("Cloudflare setup belongs to a different Home profile.");
        var connection = await stages.AwaitAsync(stages.Invoke(() => _connections.GetAsync(config.Selection.ConnectionId, token))).ConfigureAwait(false);
        DemandConnection(connection, config.Selection);
        if (Fingerprint(connection) != config.ConnectionFingerprint) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ConfigurationConflict, "CF_CONNECTION_CHANGED", "Review the changed saved connection in Home before using it.");
        if (await ActorAsync(stages, token).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("Home actor changed after the saved connection read.");
        var last = Single(await StateAsync(stages, token).ConfigureAwait(false), ConfigurationId);
        if (last is null || Fingerprint(last) != Fingerprint(record)) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ConfigurationConflict, "CF_CONFIGURATION_CHANGED", "Reload the current Home connection selection.");
        if (await ActorAsync(stages, token).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("Home actor changed after the final configuration read.");
        return new(actor, record, config, connection!, stages);
    }
    public async Task<CloudflareSetupObservation> GetSetupAsync(CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger();
        try { var actual = await ReadCaptureAsync(stages, token).ConfigureAwait(false); return new(true, "CF_CONFIGURED_CREDENTIAL_AVAILABILITY_UNOBSERVED", null, actual.Record.Revision); }
        catch (CloudflareSetupRequiredException missing) { return new(false, missing.Code, missing.ReviewRequestId, 0); }
    }
    public async Task<ICloudflareOriginalSetupReview> PrepareSetupAsync(CloudflareSetupSelection explicitSelection, long expectedRevision, CancellationToken token)
    {
        DemandSelection(explicitSelection); if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var stages = new CloudflareOriginalTaskLedger(); var actor = await ActorAsync(stages, token).ConfigureAwait(false);
        var state = await StateAsync(stages, token).ConfigureAwait(false); var prior = Single(state, ConfigurationId);
        if ((prior?.Revision ?? 0) != expectedRevision || prior is not null && (!RecordShape(prior, ConfigurationId) || prior.Payload.Deserialize<Configuration>()?.ProfileId != actor.ProfileId))
            throw new CloudflareSetupRequiredException(CloudflareSetupStage.ConfigurationConflict, "CF_CONFIGURATION_CONFLICT", "Reload Cloudflare setup before saving.");
        var connection = await stages.AwaitAsync(stages.Invoke(() => _connections.GetAsync(explicitSelection.ConnectionId, token))).ConfigureAwait(false);
        DemandConnection(connection, explicitSelection);
        if (await ActorAsync(stages, token).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("Home actor changed during setup capture.");
        var candidate = new Configuration(actor.ProfileId, explicitSelection, Fingerprint(connection));
        var arguments = JsonSerializer.SerializeToElement(new { expectedRevision, configuration = candidate });
        var scope = new ResourceScope(ResourceKind, SetupScopeId(actor), expectedRevision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write);
        var review = _broker.PrepareReviewForActor(actor, "cloudflare", SetupAction, [scope], arguments,
            "Use saved OAuth connection " + explicitSelection.ConnectionId + " for account " + explicitSelection.AccountId + ". Each remote operation requires its own Home review.", null, "cloudflare:setup:" + actor.AuthenticationRevision);
        return new SetupReview(this, actor, prior, candidate, connection!, arguments, review, stages);
    }
    private sealed class SetupReview(HomeCloudflareServiceOwner owner, AuthenticatedResourceActor actor,
        HomeCoreStateRecord? prior, Configuration configuration, ExternalConnection connection,
        JsonElement arguments, HomeResourcePreparedReview prepared, CloudflareOriginalTaskLedger stages) : ICloudflareOriginalSetupReview
    {
        private readonly object _sync = new(); private Task<CloudflareSetupObservation>? _submit; private Task<CloudflareSetupObservation>? _commit;
        public string RequestId => prepared.RequestId;
        public Task<CloudflareSetupObservation> SubmitOriginalAsync(CancellationToken token)
        {
            lock (_sync)
            {
                if (_submit is not null) return _submit;
                stages.BindOriginalOwner(this);
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _submit = SubmitPublishedAsync(begin.Task, token); begin.SetResult(); return _submit;
            }
        }
        private async Task<CloudflareSetupObservation> SubmitPublishedAsync(Task begin, CancellationToken token)
        { await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); return await SubmitAsync(token).ConfigureAwait(false); }
        private async Task<CloudflareSetupObservation> SubmitAsync(CancellationToken token)
        {
            var observed = await stages.AwaitAsync(stages.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(prepared, token))).ConfigureAwait(false);
            if (observed.State != HomePreparedReviewState.RequestObserved || observed.Request is null) throw new UnauthorizedAccessException(observed.Code);
            return new(false, observed.Request.State == HomePermissionRequestState.Approved ? "CF_SETUP_APPROVED_NOT_SAVED" : "CF_SETUP_APPROVAL_REQUIRED", RequestId, prior?.Revision ?? 0);
        }
        public Task<CloudflareSetupObservation> CommitOriginalAsync(CancellationToken token)
        {
            lock (_sync)
            {
                if (_commit is not null) return _commit;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _commit = CommitPublishedAsync(begin.Task, token); begin.SetResult(); return _commit;
            }
        }
        private async Task<CloudflareSetupObservation> CommitPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); await stages.AwaitAsync(SubmitOriginalAsync(token)).ConfigureAwait(false);
            var observed = await stages.AwaitAsync(stages.Invoke(() => owner._broker.ObservePreparedReviewAsync(prepared, token))).ConfigureAwait(false);
            if (observed.Request?.State != HomePermissionRequestState.Approved) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ApprovalRequired, "CF_SETUP_APPROVAL_REQUIRED", "Accept the exact connection selection in Home before saving.", RequestId);
            var latest = await stages.AwaitAsync(stages.Invoke(() => owner._connections.GetAsync(connection.Id, token))).ConfigureAwait(false);
            if (latest != connection || await owner.ActorAsync(stages, token).ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("Saved connection or Home actor changed before setup commit.");
            var capability = await stages.AwaitAsync(stages.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(RequestId, arguments, token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Home setup capability unavailable.");
            var claim = await stages.AwaitAsync(stages.Invoke(() => owner._broker.ClaimExecutionObservedAsync(capability, "cloudflare", SetupAction, prepared.Scopes, arguments, token))).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != actor) throw new UnauthorizedAccessException("Home setup claim rejected.");
            var attestation = owner._broker.CaptureClaimedAttestation(capability) ?? throw new UnauthorizedAccessException("Individual Home Accept is required for this setup.");
            var guard = new ClaimedStateGuard(owner, actor, attestation, prior is null ? [] : [prior], ConfigurationId, prior?.Revision ?? 0);
            var record = new HomeCoreStateRecord(ConfigurationId, ConfigurationId, 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                checked((prior?.Revision ?? 0) + 1), JsonSerializer.SerializeToElement(configuration));
            var written = await stages.AwaitAsync(stages.Invoke(() => owner._store.WriteGuardedAsync(record, prior?.Revision ?? 0, actor, guard, token))).ConfigureAwait(false);
            if (!written.IsSuccess) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ConfigurationConflict, "CF_SETUP_CAS_UNCONFIRMED", "The setup save was not acknowledged. Reload its canonical Home state before further changes.", RequestId);
            var audit = await stages.AwaitAsync(stages.Invoke(() => owner._broker.CompleteExecutionAsync(capability,
                new(HomePermissionRequestState.Succeeded, "CF_SETUP_SAVED", "Saved nonsecret account/connection selection; no remote effect was dispatched.", []), CancellationToken.None))).ConfigureAwait(false);
            if (!audit.Succeeded) throw new InvalidOperationException("Setup was saved but Home audit is unfinished: " + audit.Code);
            return new(true, "CF_SETUP_SAVED", RequestId, record.Revision);
        }
    }
    public Task<CloudflareSavedService> AcquireOriginalAsync(CancellationToken token) => AcquireCallerScopedOriginalAsync(null, token);
    public async Task<CloudflareSavedService> AcquireCallerScopedOriginalAsync(Action<Action>? callback, CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(callback);
        return await stages.RunToOriginalSettlementAsync(async () =>
        {

        var captured = await ReadCaptureAsync(stages, token).ConfigureAwait(false);
        var service = CloudflareSavedService.ObserveSavedConfiguration(captured.Connection, captured.Configuration.Selection.AccountId, captured.Configuration.Selection.ExecuteTool);
        _services.Add(service, captured); return service;

        }).ConfigureAwait(false);
    }
    public Task RevalidateOriginalAsync(CloudflareSavedService service, CancellationToken token) => RevalidateCallerScopedOriginalAsync(service, null, token);
    public async Task RevalidateCallerScopedOriginalAsync(CloudflareSavedService service, Action<Action>? callback, CancellationToken token)
    {
        if (!_services.TryGetValue(service, out var original)) throw new UnauthorizedAccessException("SAME Home-issued saved Cloudflare service required.");
        original.Stages.BindOriginalCallerCallback(callback);
        await original.Stages.RunToOriginalSettlementAsync(async () =>
        {
            var current = await ReadCaptureAsync(original.Stages, token).ConfigureAwait(false);
            if (current.Actor != original.Actor || Fingerprint(current.Record) != Fingerprint(original.Record) || current.Connection != original.Connection)
                throw new UnauthorizedAccessException("Original Home service selection changed.");
        }).ConfigureAwait(false);
    }
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger(); var actualActor = await ActorAsync(stages, token).ConfigureAwait(false);
        var state = await StateAsync(stages, token).ConfigureAwait(false); var configRecord = Single(state, ConfigurationId);
        var actualRevision = configRecord?.Revision ?? 0; bool allowed = false;
        if (actionId == SetupAction && scope.Id == SetupScopeId(actor) && scope.Access == ResourceAccess.Write)
            allowed = actualActor == actor && (configRecord is null || RecordShape(configRecord, ConfigurationId) && configRecord.Payload.Deserialize<Configuration>()?.ProfileId == actor.ProfileId);
        else if (configRecord is not null && RecordShape(configRecord, ConfigurationId) && configRecord.Payload.Deserialize<Configuration>() is { } config && config.ProfileId == actor.ProfileId)
        {
            // Recovery is local custody of a retained successful original response. The
            // account write scope does not authorize another remote create or adoption.
            if ((actionId == ReconcileAction || actionId == StagingAction) && scope.Id == "cloudflare:account:" + config.Selection.AccountId && scope.Access == ResourceAccess.Write)
                allowed = actualActor == actor;
            var descriptor = CloudflareTypedToolCatalogue.Descriptors.SingleOrDefault(x => x.ActionId == actionId && x.IsImplemented);
            if (descriptor?.Kind is CloudflareOperationKind.KvMarkerPut or CloudflareOperationKind.KvMarkerGet or CloudflareOperationKind.KvMarkerDelete && scope.Access == ResourceAccess.Read)
            {
                var bound = state.Records.SingleOrDefault(x => RecordShape(x, "home.cloudflare.staging") && x.Payload.Deserialize<BorrowedNamespace>() is { } ns &&
                    WorkerScopeId(ns.AccountId, ns.Selection.WorkerName, ns.Selection.BindingName) == scope.Id && ns.ProfileId == actor.ProfileId &&
                    ns.AccountId == config.Selection.AccountId && ns.ConnectionId == config.Selection.ConnectionId);
                actualRevision = bound?.Revision ?? -1; allowed = actualActor == actor && bound is not null;
            }
            if (descriptor is not null && !scope.Id.StartsWith("cloudflare:worker:", StringComparison.Ordinal) && scope.Access == (descriptor.IsReadOnly ? ResourceAccess.Read : ResourceAccess.Execute))
            {
                if (scope.Id == "cloudflare:account:" + config.Selection.AccountId) allowed = actualActor == actor;
                else
                {
                    var record = state.Records.SingleOrDefault(x => RecordShape(x, "home.cloudflare.namespace") &&
                        x.Payload.Deserialize<Namespace>() is { } ns && "cloudflare:kv:" + ns.AccountId + "/" + ns.NamespaceId == scope.Id);
                    if (record is null && descriptor.Kind is CloudflareOperationKind.KvMarkerPut or CloudflareOperationKind.KvMarkerGet or CloudflareOperationKind.KvMarkerDelete)
                        record = state.Records.SingleOrDefault(x => RecordShape(x, "home.cloudflare.staging") && x.Payload.Deserialize<BorrowedNamespace>() is { } ns &&
                            "cloudflare:kv:" + ns.AccountId + "/" + ns.NamespaceId == scope.Id && ns.ProfileId == actor.ProfileId &&
                            ns.AccountId == config.Selection.AccountId && ns.ConnectionId == config.Selection.ConnectionId);
                    var borrowed = record is not null && RecordShape(record, "home.cloudflare.staging") ? record.Payload.Deserialize<BorrowedNamespace>() : null;
                    var ns = record is not null && RecordShape(record, "home.cloudflare.namespace") ? record.Payload.Deserialize<Namespace>() : null; actualRevision = record?.Revision ?? -1;
                    if (borrowed is not null) allowed = actualActor == actor;
                    allowed |= actualActor == actor && ns is not null && !ns.Deleted && ns.ProfileId == actor.ProfileId &&
                        ns.AccountId == config.Selection.AccountId && ns.ConnectionId == config.Selection.ConnectionId;
                }
            }
        }
        if (await ActorAsync(stages, token).ConfigureAwait(false) != actor) allowed = false;
        return new(allowed && scope.Revision == actualRevision.ToString(CultureInfo.InvariantCulture), allowed ? "CF_LOCAL_BINDING" : "CF_OWNER_DENIED",
            actor.ActorId, actualRevision.ToString(CultureInfo.InvariantCulture), actor.OrganisationId);
    }
    private async Task<HomeCoreStateRecord?> NamespaceRecordAsync(CloudflareCompiledInvocation original, CloudflareOriginalTaskLedger stages, CancellationToken token)
    {
        if (original.NamespaceId is null) return null;
        var state = await StateAsync(stages, token).ConfigureAwait(false);
        var record = Single(state, NamespaceRecordId(original.Service.AccountId, original.NamespaceId));
        if (record is null && FindBorrowedNamespace(state, original) is { } borrowed)
        {
            var value = borrowed.Payload.Deserialize<BorrowedNamespace>()!;
            if (!_services.TryGetValue(original.Service, out var service) || value.ProfileId != service.Actor.ProfileId)
                throw new UnauthorizedAccessException("Borrowed marker delegation belongs to another actual Home profile.");
            return borrowed; // Preflight only; normal canonical/action/Home admission and a fresh actual Worker read remain required.
        }
        var ns = record is not null && RecordShape(record, "home.cloudflare.namespace") ? record.Payload.Deserialize<Namespace>() : null;
        if (ns is null || ns.Deleted || ns.TaskId != original.TaskId || ns.ExecutionId != original.ExecutionId ||
            ns.AccountId != original.Service.AccountId || ns.ConnectionId != original.Service.Connection.Id)
            throw new CloudflareSetupRequiredException(CloudflareSetupStage.NamespaceRecoveryRequired, "CF_TASK_NAMESPACE_NOT_OWNED", "This namespace is not an acknowledged isolated resource of the SAME Task/Run. Inspect or reconcile its original create first.");
        return record;
    }
    public async ValueTask DemandOriginalNamespaceAsync(CloudflareCompiledInvocation original, CancellationToken token)
    { await RevalidateOriginalAsync(original.Service, token).ConfigureAwait(false); await NamespaceRecordAsync(original, _services.TryGetValue(original.Service, out var captured) ? captured.Stages : throw new UnauthorizedAccessException("Original Home service unavailable."), token).ConfigureAwait(false); }
    public Task<ICloudflareOriginalPermission> AcquireOriginalAsync(CloudflareOriginalTaskBinding binding, CancellationToken token)
    {
        Permission original;
        lock (_sync)
        {
            if (!_originals.TryGetValue(binding.Invocation, out original!))
            {
                if (_originals.Count >= 128) throw new InvalidOperationException("Cloudflare Home original custody is full.");
                original = new(this, binding); _originals.Add(binding.Invocation, original);
            }
            else if (!ReferenceEquals(original.Binding.OriginalPreparation, binding.OriginalPreparation)) throw new UnauthorizedAccessException("Original Home review cannot move to a different preparation.");
        }
        return original.AcquireAsync(token);
    }
    public ValueTask ValidateOriginalAsync(CloudflareOriginalTaskBinding binding, ICloudflareOriginalPermission permission, CancellationToken token)
    {
        lock (_sync)
            if (permission is not Permission actual || !_originals.TryGetValue(binding.Invocation, out var registered) || !ReferenceEquals(actual, registered) ||
                !ReferenceEquals(actual.Binding.OriginalPreparation, binding.OriginalPreparation)) throw new UnauthorizedAccessException("SAME Home Cloudflare permission issuer required.");
        return permission.RevalidateOriginalAsync(binding, token);
    }
    public Task RetireAcknowledgedOriginalAsync(CloudflareCompiledInvocation invocation, ICloudflareOriginalPermission samePermission, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (samePermission is not Permission original || !_originals.TryGetValue(invocation, out var issued) || !ReferenceEquals(original, issued) || !original.IsHealthyClosed)
                throw new UnauthorizedAccessException("Only the SAME healthy audited and closed Home original can release custody.");
            _originals.Remove(invocation); return Task.CompletedTask;
        }
    }
    private sealed class ClaimedStateGuard(HomeCloudflareServiceOwner owner, AuthenticatedResourceActor actor,
        HomeClaimedResourceAttestation admission, HomeCoreStateRecord[] expectedRecords, string? expectedAbsentId = null, long expectedRevision = -1) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken token)
        {
            if (expected != actor || !owner._broker.IsClaimedAttestationCurrentInState(admission, actor, state)) return false;
            foreach (var original in expectedRecords) if (Single(state, original.RecordId) is not { } current || Fingerprint(current) != Fingerprint(original)) return false;
            if (expectedAbsentId is not null && (Single(state, expectedAbsentId)?.Revision ?? 0) != expectedRevision) return false;
            return await owner._profiles.CheckAsync(state, actor, phase, token).ConfigureAwait(false);
        }
    }
}
