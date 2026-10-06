using System.Text.Json;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceService
{
    internal async Task<NativeFilesWorkspace?> GetOriginalConfiguredAsync(Guid? expectedStoreId, FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        var actor = await original.Observe(() => profiles.GetCurrentAsync(cancellationToken).AsTask()).ConfigureAwait(false);
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
        var evidence = await original.Observe(() => workspace.Provider.GetStoreEvidenceAsync(configuration.StoreId, cancellationToken)).ConfigureAwait(false);
        if (evidence.StoreId != configuration.StoreId || await original.Observe(() => profiles.GetCurrentAsync(cancellationToken).AsTask()).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Files workspace or profile identity changed.");
        return workspace with { Actor = actor };
    }

    internal async ValueTask<Func<CancellationToken, ValueTask<bool>>> CaptureOriginalConfigurationCheckAsync(
        NativeFilesWorkspace expected, FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        // Home-only observation: this callback must remain safe under the Files metadata lease.
        var read = await original.Observe(() => home.ReadAsync(cancellationToken)).ConfigureAwait(false);
        var record = read.State?.Records.SingleOrDefault(item => item.RecordId == RecordId(expected.Actor.ProfileId));
        if (!read.IsSuccess || record is null ||
            !_cache.TryGetValue((expected.Actor.ProfileId, record.Revision), out var cached) ||
            !ReferenceEquals(cached.Provider, expected.Provider) ||
            JsonSerializer.Serialize(record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>()) != JsonSerializer.Serialize(expected.Configuration))
            throw new UnauthorizedAccessException("The selected Files workspace configuration changed.");
        var captured = JsonSerializer.Serialize(record);
        return async token =>
        {
            var current = await original.Observe(() => home.ReadAsync(token)).ConfigureAwait(false);
            var candidate = current.State?.Records.SingleOrDefault(item => item.RecordId == record.RecordId);
            return current.IsSuccess && candidate is not null && JsonSerializer.Serialize(candidate) == captured;
        };
    }

    internal ValueTask<Func<CancellationToken, ValueTask<bool>>> CaptureOriginalReadConfigurationCheckAsync(
        NativeFilesWorkspace originalWorkspace, FilesOriginalReadSourceScope original, CancellationToken cancellationToken)
    {
        // Provider alone cannot lend provenance to a caller's foreign directory/materialization services.
        if (!_cache.Values.Any(cached => ReferenceEquals(cached.Provider, originalWorkspace.Provider)
            && ReferenceEquals(cached.Directories, originalWorkspace.Directories)
            && ReferenceEquals(cached.Materializations, originalWorkspace.Materializations)
            && cached.Actor == originalWorkspace.Actor
            && JsonSerializer.Serialize(cached.Configuration) == JsonSerializer.Serialize(originalWorkspace.Configuration)))
            throw new UnauthorizedAccessException("Original Files workspace composition is unavailable.");
        return CaptureOriginalConfigurationCheckAsync(originalWorkspace, original, cancellationToken);
    }

}
