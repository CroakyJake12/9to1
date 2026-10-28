using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Local, profile-owned Data authority. Home supplies the actual actor source and paths.
/// Account/cloud/external artifacts must use their own broker authority, never this local owner.</summary>
public sealed class DataLocalWorkbookAuthority : IDataWorkbookCommitAuthority
{
    private sealed record Owner(Guid StoreID, string ProfileID, string Root);
    private readonly DataWorkbookRepository _repository;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly Owner _owner;
    private DataLocalWorkbookAuthority(DataWorkbookRepository repository, IAuthenticatedResourceActorSource actors, Owner owner)
    { _repository = repository; _actors = actors; _owner = owner; }

    public static async Task<DataLocalWorkbookAuthority> BindAsync(DataWorkbookRepository repository,
        IAuthenticatedResourceActorSource actors, string expectedProfile, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(repository); ArgumentNullException.ThrowIfNull(actors);
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (actor is null || actor.ProfileId != expectedProfile || string.IsNullOrWhiteSpace(expectedProfile))
            throw new UnauthorizedAccessException("The current local Data profile must be issued by Home.");
        var store = await repository.GetStoreIdentityAsync(token).ConfigureAwait(false);
        var owner = await repository.WithRootLeaseAsync(async () =>
        {
            var path = Path.Combine(repository.CanonicalRoot, ".owner-profile.json"); DataWorkbookRepository.DemandOrdinaryFile(path);
            if (File.Exists(path))
            {
                var held = JsonSerializer.Deserialize<Owner>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                if (held is null || held.StoreID != store.StoreId || held.ProfileID != expectedProfile || held.Root != repository.CanonicalRoot)
                    throw new UnauthorizedAccessException("This canonical Data root belongs to a different profile.");
                return held;
            }
            if (Directory.EnumerateDirectories(repository.CanonicalRoot).Any(d => Guid.TryParse(Path.GetFileName(d), out _)))
                throw new UnauthorizedAccessException("Existing Data files need explicit Home-owned legacy enrollment before profile binding.");
            var created = new Owner(store.StoreId, expectedProfile, repository.CanonicalRoot);
            await DataWorkbookRepository.WriteNewJsonAsync(path, created, token).ConfigureAwait(false); return created;
        }, token).ConfigureAwait(false);
        return new(repository, actors, owner);
    }

    public async ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(Guid storeID, Guid workbookID, int expectedVersion,
        Guid expectedRevisionID, string actionID, AuthenticatedResourceActor expectedActor, CancellationToken token)
    {
        if (storeID != _owner.StoreID || workbookID == Guid.Empty || expectedVersion < 0 ||
            expectedActor.ProfileId != _owner.ProfileID || actionID is not
            ("data.workbooks.read" or "data.workbooks.create" or "data.records.update" or "data.records.create" or "data.views.update" or "data.records.undo")) return null;
        if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != expectedActor) return null;
        var path = Path.Combine(_repository.CanonicalRoot, ".owner-profile.json");
        DataWorkbookRepository.DemandOrdinaryFile(path);
        var ownerText = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        if (JsonSerializer.Deserialize<Owner>(ownerText) != _owner) return null;
        var hash = await _repository.FingerprintAsync(workbookID, token).ConfigureAwait(false);
        if ((expectedVersion == 0) != (hash is null)) return null;
        return new Admission(this, expectedActor, workbookID, expectedVersion, expectedRevisionID, hash, ownerText);
    }
    public async Task<DataArtifactReference> ResolveLocalAsync(Guid workbookID, CancellationToken token = default)
    {
        var actor = await _actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (actor is null || actor.ProfileId != _owner.ProfileID) throw new UnauthorizedAccessException("Data profile changed.");
        var ownerPath = Path.Combine(_repository.CanonicalRoot, ".owner-profile.json");
        DataWorkbookRepository.DemandOrdinaryFile(ownerPath);
        if (JsonSerializer.Deserialize<Owner>(await File.ReadAllTextAsync(ownerPath, token).ConfigureAwait(false)) != _owner)
            throw new UnauthorizedAccessException("The Data root owner changed.");
        var identityPath = Path.Combine(_repository.CanonicalRoot, ".store.json");
        DataWorkbookRepository.DemandOrdinaryFile(identityPath);
        if (JsonSerializer.Deserialize<ResourceStoreIdentity>(await File.ReadAllTextAsync(identityPath, token).ConfigureAwait(false))?.StoreId != _owner.StoreID)
            throw new UnauthorizedAccessException("The Data store identity changed.");
        var before = await _repository.FingerprintAsync(workbookID, token).ConfigureAwait(false);
        var loaded = await _repository.LoadAsync(workbookID, token).ConfigureAwait(false) ?? throw new FileNotFoundException("Data artifact unavailable.");
        if (before != await _repository.FingerprintAsync(workbookID, token).ConfigureAwait(false) ||
            await _actors.GetCurrentAsync(token).ConfigureAwait(false) != actor) throw new InvalidOperationException("Data read changed; retry from the current library.");
        return new(_owner.StoreID, loaded.Id, loaded.Version, loaded.RevisionId);
    }

    private sealed class Admission(DataLocalWorkbookAuthority owner, AuthenticatedResourceActor actor, Guid workbook,
        int version, Guid revision, string? hash, string ownerText) : IDataWorkbookCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken token)
        {
            if (context.StoreID != owner._owner.StoreID || context.WorkbookID != workbook || context.ExpectedVersion != version || context.ExpectedRevisionID != revision ||
                await owner._actors.GetCurrentAsync(token).ConfigureAwait(false) != actor) return false;
            DataWorkbookRepository.DemandOrdinaryDirectory(owner._repository.CanonicalRoot);
            var path = Path.Combine(owner._repository.CanonicalRoot, ".owner-profile.json"); DataWorkbookRepository.DemandOrdinaryFile(path);
            var storePath = Path.Combine(owner._repository.CanonicalRoot, ".store.json");
            DataWorkbookRepository.DemandOrdinaryFile(storePath);
            var store = JsonSerializer.Deserialize<ResourceStoreIdentity>(await File.ReadAllTextAsync(storePath, token).ConfigureAwait(false));
            return store?.StoreId == owner._owner.StoreID && ownerText == await File.ReadAllTextAsync(path, token).ConfigureAwait(false) &&
                hash == await owner._repository.FingerprintAsync(workbook, token).ConfigureAwait(false);
        }
    }
}
