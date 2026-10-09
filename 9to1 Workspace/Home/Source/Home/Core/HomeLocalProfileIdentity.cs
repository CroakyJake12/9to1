using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public interface ITrustedHostPrincipalSource
{
    ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken);
}

/// <summary>Reads the actual process authority; environment usernames and request claims are never identity evidence.</summary>
public sealed class OperatingSystemPrincipalSource : ITrustedHostPrincipalSource
{
    public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return ValueTask.FromResult(identity.User?.Value is { } sid ? "windows-sid:" + sid : null);
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            return ValueTask.FromResult<string?>("unix-euid:" + GetEffectiveUserId());
        return ValueTask.FromResult<string?>(null);
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}

public sealed record HomeLocalProfile(Guid ProfileId, string PrincipalDigest, DateTimeOffset CreatedAtUtc);

/// <summary>Local OS profile identity is independent of CAKE AccountID and OrganisationID. This creates no artifact ownership grants.</summary>
public sealed partial class HomeLocalProfileIdentity(IHomeCoreStateStore store, ITrustedHostPrincipalSource principals)
    : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
{
    internal bool IsBoundToStore(IHomeCoreStateStore candidate) => ReferenceEquals(store, candidate);

    private IHomeCoreStateStore OriginalStore => store;
    private ITrustedHostPrincipalSource OriginalPrincipals => principals;

    private const string RecordId = "home.local-profile";
    private const int SchemaVersion = 1;
    private readonly string _sessionRevision = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
        HomeStateCommitPhase phase, CancellationToken cancellationToken)
    {
        var principal = await principals.GetPrincipalAsync(cancellationToken).ConfigureAwait(false);
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
            return actor == expectedActor && await principals.GetPrincipalAsync(cancellationToken).ConfigureAwait(false) == principal;
        }
        catch (JsonException) { return false; }
    }

    private sealed class InitialProfileGuard(ITrustedHostPrincipalSource source, string expectedPrincipal) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, CancellationToken cancellationToken) =>
            !lockedState.Records.Any(item => item.RecordId == RecordId) &&
            await source.GetPrincipalAsync(cancellationToken).ConfigureAwait(false) == expectedPrincipal;
    }

    public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var principal = await principals.GetPrincipalAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(principal)) return null;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal)));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // CAS plus process locks in the existing state store make concurrent first startup converge.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var read = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!read.IsSuccess) throw new InvalidDataException("Local profile state is unavailable; recovery is required without resetting it.");
                var record = read.State!.Records.SingleOrDefault(item => item.RecordId == RecordId);
                HomeLocalProfile profile;
                if (record is null)
                {
                    profile = new(Guid.NewGuid(), digest, DateTimeOffset.UtcNow);
                    var initialActor = new AuthenticatedResourceActor("local-profile:" + profile.ProfileId.ToString("D"),
                        profile.ProfileId.ToString("D"), null, null, _sessionRevision);
                    var write = await store.WriteGuardedAsync(new(RecordId, RecordId, SchemaVersion, HomeDataScope.DeviceLocal,
                        HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(profile)), 0, initialActor,
                        new InitialProfileGuard(principals, principal), cancellationToken).ConfigureAwait(false);
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
                if (await principals.GetPrincipalAsync(cancellationToken).ConfigureAwait(false) != principal) return null;
                return new("local-profile:" + profile.ProfileId.ToString("D"), profile.ProfileId.ToString("D"),
                    null, null, _sessionRevision);
            }
            throw new InvalidOperationException("Local profile creation conflicted repeatedly; retry after the other host finishes.");
        }
        finally { _gate.Release(); }
    }
}
