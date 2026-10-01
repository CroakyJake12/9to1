using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Launcher;

public sealed class HomeLauncherLayoutStore(IHomeCoreStateStore home, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService authorization, IInstalledApplicationRegistry applications)
{
    public const string RecordType = "launcher.layout";
    public static string RecordId(string profile) => RecordType + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
    private sealed record Payload(string ProfileId, LauncherLayout Current, LauncherLayout? Previous);
    public async Task<LauncherStoredLayout?> ReadExistingAsync(CancellationToken ct = default)
    {
        var actor = await Actor(ct); var read = await home.ReadAsync(ct);
        if (!read.IsSuccess) throw new IOException("Home launcher state requires recovery.");
        var record = read.State!.Records.SingleOrDefault(r => r.RecordId == RecordId(actor.ProfileId));
        if (record is null) { await SameActor(actor, ct); return null; }
        var value = Decode(record, actor.ProfileId); await Authorize(actor, value.Revision, ResourceAccess.Read, ct);
        return value;
    }

    public async Task<LauncherStoredLayout> GetAsync(IReadOnlyList<Guid>? legacyOrder = null, int rows = 5, int columns = 4, CancellationToken ct = default)
    {
        var actor = await Actor(ct);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var read = await home.ReadAsync(ct);
            if (!read.IsSuccess) throw new IOException("Home launcher state requires recovery.");
            var record = read.State!.Records.SingleOrDefault(r => r.RecordId == RecordId(actor.ProfileId));
            if (record is not null)
            {
                var value = Decode(record, actor.ProfileId);
                await Authorize(actor, value.Revision, ResourceAccess.Read, ct);
                return value;
            }
            var available = (await applications.RefreshAsync(ct)).Where(a => a.HomeProfileId == actor.ProfileId && a.Enabled && a.ProfileAccessible).OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ApplicationId).ToArray();
            var availableIds = available.Select(a => a.ApplicationId).ToHashSet();
            var ordered = (legacyOrder ?? []).Where(availableIds.Contains).Concat(available.Select(a => a.ApplicationId)).Distinct();
            var initial = LauncherLayoutEdits.Seed(LauncherLayout.Empty(rows, columns), ordered);
            await SameActor(actor, ct);
            var write = await home.WriteGuardedAsync(new(RecordId(actor.ProfileId), RecordType, 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                1, JsonSerializer.SerializeToElement(new Payload(actor.ProfileId, initial, null))), 0, actor, CommitGuard(), ct);
            if (write.Failure?.Code == HomeCoreErrorCode.PermissionDenied) throw new UnauthorizedAccessException("Home did not authorize launcher persistence.");
            if (!write.IsSuccess && write.Failure?.Code != HomeCoreErrorCode.HomeStateConflict) throw new IOException("Home could not initialize launcher layout safely.");
            await SameActor(actor, ct);
        }
        throw new IOException("Launcher initialization conflicted with another session. Retry without resetting data.");
    }
    public Task<LauncherStoredLayout> EditAsync(LauncherStoredLayout expected, Func<LauncherLayout, LauncherLayout> edit, CancellationToken ct = default)
        => EditCoreAsync(expected, edit, null, ct);
    internal Task<LauncherStoredLayout> EditAsActorAsync(LauncherStoredLayout expected, AuthenticatedResourceActor actor,
        Func<LauncherLayout, LauncherLayout> edit, CancellationToken ct)
        => EditCoreAsync(expected, edit, actor, ct);
    private async Task<LauncherStoredLayout> EditCoreAsync(LauncherStoredLayout expected, Func<LauncherLayout, LauncherLayout> edit,
        AuthenticatedResourceActor? expectedActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var actor = expectedActor ?? await Actor(ct);
        await SameActor(actor, ct);
        var current = await ReadExistingAsync(ct) ?? throw new IOException("The current Home launcher layout is unavailable. Refresh before editing.");
        await SameActor(actor, ct);
        if (current.AuthorityId != expected.AuthorityId || current.Revision != expected.Revision)
            throw new IOException("Launcher layout or profile changed. Refresh before editing.");
        var candidate = LauncherLayoutEdits.Clone(edit(LauncherLayoutEdits.Clone(current.Current))); candidate.Validate();
        await SameActor(actor, ct);
        if (current.AuthorityId != RecordId(actor.ProfileId)) throw new UnauthorizedAccessException("Launcher layout belongs to another profile.");
        var existingIds = LauncherLayoutEdits.Placements(current.Current).Where(i => i.FolderId is null).Select(i => i.ApplicationId).Concat(current.Current.HiddenApplications).Concat(current.Current.Drawer?.Categories.SelectMany(c => c.Applications) ?? []).ToHashSet();
        var newIds = LauncherLayoutEdits.Placements(candidate).Where(i => i.FolderId is null).Select(i => i.ApplicationId).Concat(candidate.HiddenApplications).Concat(candidate.Drawer?.Categories.SelectMany(c => c.Applications) ?? []).Where(id => !existingIds.Contains(id)).ToHashSet();
        if (newIds.Count != 0)
        {
            var allowed = (await applications.RefreshAsync(ct)).Where(a => a.HomeProfileId == actor.ProfileId && a.Enabled && a.ProfileAccessible).Select(a => a.ApplicationId).ToHashSet();
            if (!newIds.IsSubsetOf(allowed)) throw new UnauthorizedAccessException("A shortcut is unavailable to the current Home or Android profile.");
        }
        await Authorize(actor, current.Revision, ResourceAccess.Write, ct);
        var revision = checked(current.Revision + 1);
        var write = await home.WriteGuardedAsync(new(current.AuthorityId, RecordType, 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
            revision, JsonSerializer.SerializeToElement(new Payload(actor.ProfileId, candidate, current.Current))), current.Revision, actor, CommitGuard(), ct);
        if (write.Failure?.Code == HomeCoreErrorCode.PermissionDenied) throw new UnauthorizedAccessException("Home did not authorize launcher persistence.");
        await SameActor(actor, ct);
        if (!write.IsSuccess) throw new IOException(write.Failure?.Code == HomeCoreErrorCode.HomeStateConflict
            ? "Launcher layout changed concurrently. Refresh; the winning layout was preserved." : "Home could not persist launcher layout safely.");
        return new(revision, current.AuthorityId, candidate, LauncherLayoutEdits.Clone(current.Current));
    }
    private IHomeStateCommitActorGuard CommitGuard() => actors as IHomeStateCommitActorGuard
        ?? throw new UnauthorizedAccessException("The current Home identity cannot guard launcher persistence.");
    private async Task<AuthenticatedResourceActor> Actor(CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct);
        return actor is { OrganisationId: null } && !string.IsNullOrWhiteSpace(actor.ProfileId) && !string.IsNullOrWhiteSpace(actor.ActorId) && !string.IsNullOrWhiteSpace(actor.AuthenticationRevision)
            ? actor : throw new UnauthorizedAccessException("A current personal Home profile is required.");
    }
    private async Task SameActor(AuthenticatedResourceActor actor, CancellationToken ct)
    { if (await actors.GetCurrentAsync(ct) != actor) throw new UnauthorizedAccessException("Home profile changed during launcher access."); }
    private async Task Authorize(AuthenticatedResourceActor actor, long revision, ResourceAccess access, CancellationToken ct)
    {
        var action = access == ResourceAccess.Read ? "launcher.layout.read" : "launcher.layout.edit";
        if (await authorization.AuthorizeAsync(action, [new(RecordType, RecordId(actor.ProfileId), revision.ToString(CultureInfo.InvariantCulture), access)], ct) != actor)
            throw new UnauthorizedAccessException("The current Home launcher layout is not authorized.");
    }
    internal static LauncherStoredLayout Decode(HomeCoreStateRecord record, string profile)
    {
        if (record.RecordId != RecordId(profile) || record.RecordType != RecordType || record.SchemaVersion != 1 || record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical || record.Revision < 1 || record.Revision == long.MaxValue)
            throw new InvalidDataException("Unsupported launcher record. Preserve it for recovery.");
        Payload value;
        try { value = record.Payload.Deserialize<Payload>() ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InvalidDataException("Launcher state is malformed and was preserved.", ex); }
        if (value.ProfileId != profile || value.Current is null) throw new InvalidDataException("Launcher state has different profile ownership.");
        value = value with { Current = LauncherLayout.UpgradeKnownSchema(value.Current), Previous = value.Previous is null ? null : LauncherLayout.UpgradeKnownSchema(value.Previous) };
        return new(record.Revision, record.RecordId, LauncherLayoutEdits.Clone(value.Current), value.Previous is null ? null : LauncherLayoutEdits.Clone(value.Previous));
    }
}

public sealed class LauncherLayoutResourceResolver(IHomeCoreStateStore home) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => HomeLauncherLayoutStore.RecordType;
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
    {
        var denied = new ResourceAccessDecision(false, "LauncherOwnershipUnavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (actor.OrganisationId is not null || scope.Id != HomeLauncherLayoutStore.RecordId(actor.ProfileId) ||
            !((actionId == "launcher.layout.read" && scope.Access == ResourceAccess.Read) || (actionId == "launcher.layout.edit" && scope.Access == ResourceAccess.Write))) return denied;
        var read = await home.ReadAsync(ct); var record = read.State?.Records.SingleOrDefault(r => r.RecordId == scope.Id);
        if (!read.IsSuccess || record is null || record.Revision.ToString(CultureInfo.InvariantCulture) != scope.Revision) return denied;
        try { HomeLauncherLayoutStore.Decode(record, actor.ProfileId); } catch (InvalidDataException) { return denied; }
        return denied with { Allowed = true, Code = "OwnedHomeLauncherLayout" };
    }
}
