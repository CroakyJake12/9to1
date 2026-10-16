using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    internal Task<bool> RecordOriginalUcmlAccessWithinSourceAsync(HomeUcmlAccessAuditOwner owner,
        IChatOriginalPersistentMemorySource memory, IChatOriginalPersistentMemoryInput? input,
        AuthenticatedResourceActor? expectedActor, Guid requestId, HomeUcmlAccessOutcome outcome,
        IReadOnlyList<Guid> ids, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            source.Run(() =>
            {
                if (!owner.HasOriginalPermissionOwner(this)) throw new UnauthorizedAccessException("Use the original Home UCML audit owner.");
                owner.DemandOriginal(memory, input, outcome, ids);
            });
            var actor = await source.ReadAsync(() => owner.Profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            if (actor is null || expectedActor is not null && actor != expectedActor)
                throw new UnauthorizedAccessException("The current Home actor changed before UCML access audit.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var auditId = "ucml:" + requestId.ToString("N") + ":" + (int)outcome;
                var objects = ids.Select(id => new HomeObjectReference("ucml-record", id.ToString("D"))).ToArray();
                var code = outcome switch
                {
                    HomeUcmlAccessOutcome.BrokerAllowed => "HOME_UCML_BROKER_ALLOWED",
                    HomeUcmlAccessOutcome.DispatchValidated => "HOME_UCML_LOCAL_DISPATCH_VALIDATED",
                    HomeUcmlAccessOutcome.Exported => "HOME_UCML_OWNER_EXPORTED",
                    HomeUcmlAccessOutcome.Denied => "HOME_UCML_ACCESS_DENIED",
                    _ => throw new UnauthorizedAccessException("Unsupported UCML audit outcome.")
                };
                // A model runtime may validate the SAME selection repeatedly. Idempotence
                // is bound to the source-issued request and exact actor/record IDs.
                if (state.Audit.SingleOrDefault(item => item.AuditId == auditId) is { } existing)
                {
                    if (existing.CallerId != actor.ActorId || existing.ResultCode != code || !existing.AffectedObjects.SequenceEqual(objects))
                        throw new UnauthorizedAccessException("The original UCML audit request cannot be substituted.");
                    return true;
                }
                var action = outcome == HomeUcmlAccessOutcome.Exported ? "ucml.owner.export" : "ucml.context.local-personalization";
                state.Audit.Add(new HomePermissionAuditEvent(auditId, requestId.ToString("N"), actor.ActorId,
                    "9to1.assistants", action, new("9to1.assistants", action, objects), null, null,
                    HomePermissionAuditKind.ExecutionStarted, null, _timeProvider.GetUtcNow(), objects, code,
                    "Content-free UCML access observation; no query, title, body, provenance or key is retained."));
                var record = new HomeCoreStateRecord(StateRecordId, StateRecordType, StateSchemaVersion,
                    HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, state.RecordRevision, JsonSerializer.SerializeToElement(state));
                var write = await source.ReadAsync(() => _stateStore.WriteGuardedAsync(record, state.RecordRevision, actor,
                    new UcmlAuditProfileGuard(owner.Profiles, actor, source), token)).ConfigureAwait(false);
                if (!write.IsSuccess) throw new HomeFeatureStoreException(write.Failure!.Code.ToString(), write.Failure.Message);
                if (await source.ReadAsync(() => owner.Profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("The Home actor changed while recording UCML access.");
                return true;
            }
            finally { source.Run(() => _gate.Release()); }
        });

    private sealed class UcmlAuditProfileGuard(HomeLocalProfileIdentity profiles, AuthenticatedResourceActor originalActor,
        HomeOwnershipOriginalSourceCallbacks captured) : IHomeOriginalScopedStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, CancellationToken token) => CheckCoreAsync(state, actor, phase, captured.Run, captured.Retain, token);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var supplied = new HomeOwnershipOriginalSourceCallbacks(scope, retain);
            return CheckCoreAsync(state, actor, phase, body => captured.Run(() => supplied.Run(body)),
                actual => { captured.Retain(actual); supplied.Retain(actual); }, token);
        }
        private async ValueTask<bool> CheckCoreAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            if (actor != originalActor) return false;
            Task<bool>? actual = null;
            scope(() => { actual = profiles.CheckAsync(state, actor, phase, scope, retain, token).AsTask(); retain(actual); });
            if (actual is null) throw new InvalidOperationException("The original UCML audit profile guard was not invoked.");
            try { return await actual.ConfigureAwait(false); }
            catch when (actual.Exception is { InnerExceptions.Count: > 1 }) { throw actual.Exception!; }
        }
    }
}
