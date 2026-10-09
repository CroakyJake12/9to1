using System.Text.Json;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceService
{
    internal async ValueTask<HomeClaimedResourceCommitFence> CaptureBrowserCommitFenceAsync(
        NativeFilesWorkspace original, HomeResourceOperationBroker broker,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceExecutionCapability capability,
        Func<bool> lifetime, CancellationToken token)
    {
        if (home is not FileHomeCoreStateStore store || !IsOriginalReadComposition(profiles, ownership))
            throw new UnauthorizedAccessException("The actual Files/Home commit composition is unavailable.");
        var read = await home.ReadAsync(token).ConfigureAwait(false);
        var record = read.State?.Records.SingleOrDefault(item => item.RecordId == RecordId(original.Actor.ProfileId));
        if (!read.IsSuccess || record is null || !lifetime()
            || !_cache.TryGetValue((original.Actor.ProfileId, record.Revision), out var cached)
            || !ReferenceEquals(cached.Provider, original.Provider)
            || !ReferenceEquals(cached.Directories, original.Directories)
            || JsonSerializer.Serialize(record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>())
                != JsonSerializer.Serialize(original.Configuration))
            throw new UnauthorizedAccessException("The original Files workspace configuration changed.");
        return await HomeClaimedResourceCommitFence.CaptureAsync(broker, store, profiles, ownership,
            "files", original.Configuration.StoreId.ToString("D"), capability, original.Actor,
            [record], lifetime, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The SAME claimed Files operation has no current Home commit fence.");
    }
}

public sealed partial class NativeFilesWorkspaceAuthority
{
    internal async ValueTask<HomeClaimedResourceCommitFence> CaptureBrowserCommitFenceAsync(
        NativeFilesWorkspace original, HomeResourceOperationBroker broker,
        HomeResourceExecutionCapability capability, Func<bool> lifetime, CancellationToken token)
    {
        if (ownership is not HomeResourceStoreOwnershipAuthority actualOwnership || !lifetime())
            throw new UnauthorizedAccessException("The actual Home ownership source is unavailable.");
        var current = await GetCurrentAsync(original.Configuration.StoreId, token).ConfigureAwait(false);
        if (current?.Actor != original.Actor || !ReferenceEquals(current.Provider, original.Provider)
            || !ReferenceEquals(current.Directories, original.Directories) || !lifetime())
            throw new UnauthorizedAccessException("The selected Files workspace was replaced.");
        return await workspaces.CaptureBrowserCommitFenceAsync(original, broker, actualOwnership,
            capability, lifetime, token).ConfigureAwait(false);
    }
}
