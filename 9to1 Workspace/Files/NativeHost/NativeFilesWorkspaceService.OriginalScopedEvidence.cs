using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceService : IHomeOriginalScopedLocalStoreEvidenceProvider
{
    internal async Task<NativeFilesWorkspace?> GetOriginalConfiguredWithinSourceAsync(Guid? expectedStoreId, FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        var actor = await original.Observe(() => profiles.GetCurrentAsync(original.OriginalSynchronousScope, original.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null) return null;
        var read = await original.Observe(() => home.ReadAsync(cancellationToken)).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidDataException("Home Files configuration needs recovery; it was preserved.");
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == RecordId(actor.ProfileId));
        if (record is null) return null;
        if (record.SchemaVersion != 1 || record.RecordType != "files.native-workspace" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Unsupported Files workspace configuration; it was preserved.");
        NativeFilesWorkspaceConfiguration configuration;
        try { configuration = record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>() ?? throw new JsonException(); }
        catch (JsonException exception) { throw new InvalidDataException("Files workspace configuration is corrupt; it was preserved.", exception); }
        if (configuration.ProfileId != actor.ProfileId || configuration.StoreId == Guid.Empty ||
            configuration.LocationId.Value == Guid.Empty || string.IsNullOrWhiteSpace(configuration.RootDirectory) ||
            configuration.RootDirectory.Contains('\0') || !Path.IsPathFullyQualified(configuration.RootDirectory) ||
            configuration.AppFolders is null || configuration.AppFolders.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value.Value == Guid.Empty))
            throw new UnauthorizedAccessException("Files workspace identity does not match the current verified profile.");
        if (expectedStoreId is { } originalStore && (originalStore == Guid.Empty || configuration.StoreId != originalStore))
            throw new UnauthorizedAccessException("The configured Files store differs from the original selection.");
        RequireDirectDirectory(configuration.RootDirectory);
        RequireDirectDirectory(MetadataDirectory(configuration.RootDirectory));
        var statePath = Path.Combine(MetadataDirectory(configuration.RootDirectory), "drive.json");
        if (!File.Exists(statePath) || (File.GetAttributes(statePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The configured Files state is unavailable or redirected.");
        var workspace = original.Invoke(() => _cache.GetOrAdd((actor.ProfileId, record.Revision), _ => Create(actor, configuration)));
        var evidence = await original.Observe(() => workspace.Provider.GetStoreEvidenceAsync(configuration.StoreId, original.OriginalSynchronousScope, original.RetainOriginalTask, cancellationToken)).ConfigureAwait(false);
        if (evidence.StoreId != configuration.StoreId || await original.Observe(() => profiles.GetCurrentAsync(original.OriginalSynchronousScope, original.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Files workspace or profile identity changed.");
        return workspace with { Actor = actor };
    }

    public async ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(storeId, out var id) || id == Guid.Empty) return null;
        var source = FilesOriginalParentSourceCallbacks.Create(originalSynchronousScope, retainOriginalTask);
        var actor = await source.Observe(() => profiles.GetCurrentAsync(source.OriginalSynchronousScope,
            source.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false);
        NativeFilesWorkspace? workspace;
        try
        {
            var pending = source.Invoke(() => _creating.TryGetValue(id, out var actual) ? actual : null);
            workspace = pending ?? await source.Observe(() => GetOriginalConfiguredWithinSourceAsync(id, source, cancellationToken)).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return null; }
        if (actor is null || workspace is null || workspace.Actor != actor) return null;
        var evidence = await source.Observe(() => workspace.Provider.GetStoreEvidenceAsync(id,
            source.OriginalSynchronousScope, source.RetainOriginalTask, cancellationToken)).ConfigureAwait(false);
        var current = await source.Observe(() => profiles.GetCurrentAsync(source.OriginalSynchronousScope,
            source.RetainOriginalTask, cancellationToken).AsTask()).ConfigureAwait(false);
        return source.Invoke(() => current == actor && evidence.StoreId == id
            ? new HomeLocalStoreEvidence(ResourceKind, evidence.StoreId.ToString("D"), evidence.Revision,
                evidence.NewlyCreated, evidence.IsEmpty, true) : null);
    }
}
