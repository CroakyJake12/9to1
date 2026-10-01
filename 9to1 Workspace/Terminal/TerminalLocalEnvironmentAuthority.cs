using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Terminal;

/// <summary>A locator issued from the owning durable Home record, bound to the captured live actor.
/// It grants no shell permission and contains no invented process/session.</summary>
public sealed class TerminalLocalEnvironmentLease
{
    internal TerminalLocalEnvironmentLease(string recordId, long revision, string environmentId, AuthenticatedResourceActor actor)
    { RecordId = recordId; RecordRevision = revision; EnvironmentId = new(environmentId); Actor = actor; }
    internal string RecordId { get; }
    internal long RecordRevision { get; }
    public TerminalEnvironmentId EnvironmentId { get; }
    public AuthenticatedResourceActor Actor { get; }
}

public sealed class TerminalLocalEnvironmentAuthority(IHomeCoreStateStore home,
    IAuthenticatedResourceActorSource actors, IHomeStateCommitActorGuard commitGuard)
{
    private const string RecordType = "terminal.local-environment";
    private sealed record Binding(string EnvironmentId, string ProfileId);

    public async Task<TerminalLocalEnvironmentLease> GetOrCreateAsync(CancellationToken ct = default)
    {
        var actor = await CurrentActor(ct).ConfigureAwait(false);
        var recordId = RecordType + ":" + actor.ProfileId;
        var record = await ReadRecord(recordId, ct).ConfigureAwait(false);
        if (record is null)
        {
            var payload = new Binding(Guid.NewGuid().ToString("D"), actor.ProfileId);
            var create = new HomeCoreStateRecord(recordId, RecordType, 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, JsonSerializer.SerializeToElement(payload));
            var saved = await home.WriteGuardedAsync(create, 0, actor, commitGuard, ct).ConfigureAwait(false);
            if (!saved.IsSuccess && saved.Failure?.Code != HomeCoreErrorCode.HomeStateConflict)
                throw new InvalidOperationException(saved.Failure?.Message ?? "Home did not persist the Terminal environment.");
            // A concurrent creator wins one identity. Never return the losing generated locator.
            record = await ReadRecord(recordId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("The canonical Terminal environment record is missing after creation.");
        }
        var binding = Validate(record, actor);
        if (await CurrentActor(ct).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The Home actor changed while opening Terminal.");
        return new(recordId, record.Revision, binding.EnvironmentId, actor);
    }

    public async ValueTask RequireCurrentAsync(TerminalLocalEnvironmentLease lease, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (await CurrentActor(ct).ConfigureAwait(false) != lease.Actor)
            throw new UnauthorizedAccessException("The Terminal session belongs to a previous Home actor.");
        var record = await ReadRecord(lease.RecordId, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The Terminal environment was removed.");
        var binding = Validate(record, lease.Actor);
        if (record.Revision != lease.RecordRevision || binding.EnvironmentId != lease.EnvironmentId.Value ||
            await CurrentActor(ct).ConfigureAwait(false) != lease.Actor)
            throw new UnauthorizedAccessException("The Terminal environment or actor changed.");
    }

    private async Task<AuthenticatedResourceActor> CurrentActor(CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("A current Home profile is required for a local Terminal.");
        if (actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("A local Terminal cannot infer account or organisation process authority.");
        return actor;
    }

    private async Task<HomeCoreStateRecord?> ReadRecord(string id, CancellationToken ct)
    {
        var state = await home.ReadAsync(ct).ConfigureAwait(false);
        if (!state.IsSuccess) throw new InvalidDataException("Home state requires recovery; Terminal did not replace it.");
        return state.State!.Records.SingleOrDefault(record => record.RecordId == id);
    }

    private static Binding Validate(HomeCoreStateRecord record, AuthenticatedResourceActor actor)
    {
        if (record.RecordType != RecordType || record.SchemaVersion != 1 || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical || record.Revision < 1)
            throw new InvalidDataException("Unsupported Terminal environment record; preserve it for recovery.");
        Binding? binding;
        try { binding = record.Payload.Deserialize<Binding>(); }
        catch (JsonException error) { throw new InvalidDataException("Invalid Terminal environment record.", error); }
        if (binding is null || binding.ProfileId != actor.ProfileId || !Guid.TryParseExact(binding.EnvironmentId, "D", out var id) || id == Guid.Empty)
            throw new UnauthorizedAccessException("The Terminal environment does not belong to this Home profile.");
        return binding;
    }
}
