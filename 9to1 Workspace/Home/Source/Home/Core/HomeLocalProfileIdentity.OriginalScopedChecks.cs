using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
namespace HavenOS.Home.Core;

public sealed partial class HomeLocalProfileIdentity
{
    public async ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
        HomeStateCommitPhase phase, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var principal = await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => OriginalPrincipals.GetPrincipalAsync(cancellationToken).AsTask(), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(principal) || lockedState.SchemaVersion != FileHomeCoreStateStore.CurrentSchemaVersion) return false;
        var records = lockedState.Records.Where(item => item.RecordId == RecordId).ToArray();
        if (records.Length != 1) return false;
        var record = records[0];
        if (record.RecordType != RecordId || record.SchemaVersion != SchemaVersion || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical) return false;
        try
        {
            var profile = record.Payload.Deserialize<HomeLocalProfile>();
            if (profile is null || profile.ProfileId == Guid.Empty || profile.CreatedAtUtc == default ||
                profile.PrincipalDigest != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal)))) return false;
            var actor = new AuthenticatedResourceActor("local-profile:" + profile.ProfileId.ToString("D"),
                profile.ProfileId.ToString("D"), null, null, _sessionRevision);
            return actor == expectedActor && await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => OriginalPrincipals.GetPrincipalAsync(cancellationToken).AsTask(), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false) == principal;
        }
        catch (JsonException) { return false; }
    }

    private sealed class InitialScopedProfileGuard(ITrustedHostPrincipalSource source, string expectedPrincipal,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, CancellationToken cancellationToken) =>
            !lockedState.Records.Any(item => item.RecordId == RecordId) &&
            await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => source.GetPrincipalAsync(cancellationToken).AsTask(),
                originalSynchronousScope, retainOriginalTask).ConfigureAwait(false) == expectedPrincipal;
    }

    public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var principal = await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => OriginalPrincipals.GetPrincipalAsync(cancellationToken).AsTask(), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(principal)) return null;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal)));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // CAS plus process locks in the existing state store make concurrent first startup converge.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var read = await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => OriginalStore.ReadAsync(cancellationToken), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
                if (!read.IsSuccess) throw new InvalidDataException("Local profile state is unavailable; recovery is required without resetting it.");
                var record = read.State!.Records.SingleOrDefault(item => item.RecordId == RecordId);
                HomeLocalProfile profile;
                if (record is null)
                {
                    profile = new(Guid.NewGuid(), digest, DateTimeOffset.UtcNow);
                    var initialActor = new AuthenticatedResourceActor("local-profile:" + profile.ProfileId.ToString("D"),
                        profile.ProfileId.ToString("D"), null, null, _sessionRevision);
                    var write = await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => OriginalStore.WriteGuardedAsync(new(RecordId, RecordId, SchemaVersion, HomeDataScope.DeviceLocal,
                        HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(profile)), 0, initialActor,
                        new InitialScopedProfileGuard(OriginalPrincipals, principal, originalSynchronousScope, retainOriginalTask), cancellationToken), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
                    if (!write.IsSuccess)
                    {
                        if (write.Failure?.Code == HomeCoreErrorCode.HomeStateConflict) continue;
                        throw new InvalidDataException("Local profile identity could not be stored safely.");
                    }
                }
                else
                {
                    if (record.RecordType != RecordId || record.SchemaVersion != SchemaVersion ||
                        record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
                        throw new InvalidDataException("Unsupported local profile identity schema; preserve it for recovery.");
                    try { profile = record.Payload.Deserialize<HomeLocalProfile>() ?? throw new JsonException(); }
                    catch (JsonException exception) { throw new InvalidDataException("Corrupt local profile identity; preserve it for recovery.", exception); }
                }
                if (profile.ProfileId == Guid.Empty || profile.PrincipalDigest != digest || profile.CreatedAtUtc == default)
                    throw new UnauthorizedAccessException("The local profile belongs to a different operating-system principal or requires explicit ownership recovery.");
                if (await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => OriginalPrincipals.GetPrincipalAsync(cancellationToken).AsTask(), originalSynchronousScope, retainOriginalTask).ConfigureAwait(false) != principal) return null;
                return new("local-profile:" + profile.ProfileId.ToString("D"), profile.ProfileId.ToString("D"),
                    null, null, _sessionRevision);
            }
            throw new InvalidOperationException("Local profile creation conflicted repeatedly; retry after the other host finishes.");
        }
        finally { _gate.Release(); }
    }
}
