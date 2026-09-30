using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell;

/// <summary>Device-local shell state belongs to the authenticated Home profile, never an app-private settings file.</summary>
public sealed class HomeShellConfigurationStore(IHomeCoreStateStore home, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService authorization) : IShellConfigurationStore
{
    public const string RecordType = "os.shell.configuration";
    public static string RecordId(string profileId) => RecordType + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
    private sealed record Payload(string ProfileId, ShellConfiguration Current, ShellConfiguration? Previous);
    public async Task<ShellStoredConfiguration> ReadAsync(CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var read = await home.ReadAsync(ct);
            if (!read.IsSuccess) throw new InvalidDataException("Home shell state requires recovery.");
            var record = read.State!.Records.SingleOrDefault(r => r.RecordId == RecordId(actor.ProfileId));
            if (record is not null) { var result = Decode(record, actor.ProfileId); await AuthorizeReadAsync(actor, result.Revision, ct); await SameActorAsync(actor, ct); return result; }
            // Persist the initial stable identities atomically so reopening cannot silently replace them.
            var initial = new Payload(actor.ProfileId, ShellConfiguration.Default(), null);
            await SameActorAsync(actor, ct);
            var write = await home.WriteGuardedAsync(new(RecordId(actor.ProfileId), RecordType, 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(initial)), 0, actor, CommitGuard(), ct);
            await SameActorAsync(actor, ct);
            if (write.IsSuccess) { await AuthorizeReadAsync(actor, 1, ct); return new(1, initial.Current, null, RecordId(actor.ProfileId)); }
            if (write.Failure?.Code != HomeCoreErrorCode.HomeStateConflict) throw new IOException("Home could not initialize shell state safely.");
        }
        throw new ShellConfigurationConflictException();
    }
    public async Task<bool> TryWriteAsync(long expectedRevision, ShellStoredConfiguration next, CancellationToken ct)
    {
        next.Current.Validate(); next.Previous?.Validate();
        if (expectedRevision < 1 || next.Revision != checked(expectedRevision + 1)) throw new InvalidDataException("Invalid shell transaction revision.");
        var actor = await ActorAsync(ct);
        if (next.AuthorityId != RecordId(actor.ProfileId)) throw new UnauthorizedAccessException("The shell transaction belongs to another Home profile.");
        var authorized = await authorization.AuthorizeAsync("os.shell.configuration.keep",
            [new(RecordType, RecordId(actor.ProfileId), expectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write)], ct);
        if (authorized != actor) throw new UnauthorizedAccessException("Current Home shell ownership could not be verified.");
        var write = await home.WriteGuardedAsync(new(RecordId(actor.ProfileId), RecordType, 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, next.Revision, JsonSerializer.SerializeToElement(new Payload(actor.ProfileId, next.Current, next.Previous))), expectedRevision, actor, CommitGuard(), ct);
        await SameActorAsync(actor, ct);
        if (!write.IsSuccess && write.Failure?.Code != HomeCoreErrorCode.HomeStateConflict) throw new IOException("Home could not save the shell transaction safely.");
        return write.IsSuccess;
    }
    private IHomeStateCommitActorGuard CommitGuard() => actors as IHomeStateCommitActorGuard
        ?? throw new UnauthorizedAccessException("The current Home identity cannot guard shell persistence.");
    private async Task AuthorizeReadAsync(AuthenticatedResourceActor actor, long revision, CancellationToken ct)
    {
        if (await authorization.AuthorizeAsync("os.shell.configuration.read", [new(RecordType, RecordId(actor.ProfileId),
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Read)], ct) != actor)
            throw new UnauthorizedAccessException("Current Home shell read access could not be verified.");
    }
    private async Task<AuthenticatedResourceActor> ActorAsync(CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct);
        if (actor is null || string.IsNullOrWhiteSpace(actor.ProfileId) || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || actor.AccountId == Guid.Empty || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("A verified personal Home profile is required.");
        return actor;
    }
    private async Task SameActorAsync(AuthenticatedResourceActor actor, CancellationToken ct)
    { if (await actors.GetCurrentAsync(ct) != actor) throw new UnauthorizedAccessException("Home profile changed during shell configuration access."); }
    internal static ShellStoredConfiguration Decode(HomeCoreStateRecord record, string profile)
    {
        if (record.RecordType != RecordType || record.SchemaVersion != 1 || record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical || record.Revision < 1 || record.Revision == long.MaxValue)
            throw new InvalidDataException("Unsupported Home shell state. Preserve it for recovery.");
        Payload payload;
        try { payload = record.Payload.Deserialize<Payload>() ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InvalidDataException("Corrupt Home shell state. Preserve it for recovery.", ex); }
        if (payload.ProfileId != profile || payload.Current is null) throw new InvalidDataException("Shell state has different profile ownership.");
        var current = payload.Current.UpgradeSupportedLegacy(); var previous = payload.Previous?.UpgradeSupportedLegacy();
        return new(record.Revision, current, previous, record.RecordId);
    }
}

public sealed class ShellConfigurationResourceResolver(IHomeCoreStateStore home) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => HomeShellConfigurationStore.RecordType;
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
    {
        var denied = new ResourceAccessDecision(false, "ShellOwnershipUnavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (actor.OrganisationId is not null || scope.Id != HomeShellConfigurationStore.RecordId(actor.ProfileId) ||
            !((actionId == "os.shell.configuration.keep" && scope.Access == ResourceAccess.Write) ||
                (actionId == "os.shell.configuration.read" && scope.Access == ResourceAccess.Read))) return denied;
        var read = await home.ReadAsync(ct);
        var record = read.State?.Records.SingleOrDefault(r => r.RecordId == scope.Id);
        if (!read.IsSuccess || record is null || record.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) != scope.Revision) return denied;
        try { HomeShellConfigurationStore.Decode(record, actor.ProfileId); }
        catch (InvalidDataException) { return denied; }
        return denied with { Allowed = true, Code = "OwnedProfileShellConfiguration" };
    }
}
