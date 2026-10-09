using System.Text.Json;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceService
{
    internal async ValueTask<HomeClaimedResourceCommitFence> CaptureBrowserCommitFenceAsync(
        NativeFilesWorkspace original, HomeResourceOperationBroker broker,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceExecutionCapability capability,
        Func<bool> lifetime, CancellationToken token, FilesOriginalReadSourceScope? source = null,
        Action<HomeClaimedResourceCommitFence>? captureActualFence = null)
    {
        if (home is not FileHomeCoreStateStore store || !IsOriginalReadComposition(profiles, ownership))
            throw new UnauthorizedAccessException("The actual Files/Home commit composition is unavailable.");
        var read = source is null ? await home.ReadAsync(token).ConfigureAwait(false)
            : await source.Observe(() => home.ReadAsync(token)).ConfigureAwait(false);
        var record = read.State?.Records.SingleOrDefault(item => item.RecordId == RecordId(original.Actor.ProfileId));
        if (!read.IsSuccess || record is null || !lifetime()
            || !_cache.TryGetValue((original.Actor.ProfileId, record.Revision), out var cached)
            || !ReferenceEquals(cached.Provider, original.Provider)
            || !ReferenceEquals(cached.Directories, original.Directories)
            || JsonSerializer.Serialize(record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>())
                != JsonSerializer.Serialize(original.Configuration))
            throw new UnauthorizedAccessException("The original Files workspace configuration changed.");
        var fence = source is null
            ? await HomeClaimedResourceCommitFence.CaptureAsync(broker, store, profiles, ownership,
                "files", original.Configuration.StoreId.ToString("D"), capability, original.Actor,
                [record], lifetime, token).ConfigureAwait(false)
            : await ObserveOriginalBrowserFenceProduct(source, () => HomeClaimedResourceCommitFence.CaptureWithinOriginalSourceAsync(broker, store, profiles, ownership,
                "files", original.Configuration.StoreId.ToString("D"), capability, original.Actor,
                [record], lifetime, source.OriginalSynchronousScope, source.RetainOriginalTask, token).AsTask(), value => value, captureActualFence).ConfigureAwait(false);
        return fence ?? throw new UnauthorizedAccessException("The SAME claimed Files operation has no current Home commit fence.");
    }

    // A successful actual acquisition may be followed by a wrapper callback failure.
    // Independently await that SAME task and close the SAME unpublished product.
    internal static async Task<T> ObserveOriginalBrowserFenceProduct<T>(FilesOriginalReadSourceScope source,
        Func<Task<T>> factory, Func<T, HomeClaimedResourceCommitFence?> fenceOf,
        Action<HomeClaimedResourceCommitFence>? captureActualFence)
    {
        Task<T>? actual = null; T value = default!;
        bool captured = false;
        void Capture(T actualValue)
        {
            var actualFence = fenceOf(actualValue);
            if (actualFence is not null && !captured) { captured = true; captureActualFence?.Invoke(actualFence); }
        }
        try
        {
            value = await source.Observe(() => actual = factory()).ConfigureAwait(false);
            Capture(value); return value;
        }
        catch (Exception primary)
        {
            List<Exception> errors = [primary]; HomeClaimedResourceCommitFence? fence = null;
            if (actual is not null)
            {
                try
                {
                    value = await actual.ConfigureAwait(false); fence = fenceOf(value);
                    Capture(value);
                }
                catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
            }
            if (fence is not null)
            {
                // Cleanup belongs to this physical acquisition owner; a caller's refused
                // publication cannot suppress actual release or reconstruct another fence.
                Task? close = null;
                try { close = fence.DisposeWithinOriginalSourceAsync(body => body(), source.RetainOriginalTask).AsTask(); }
                catch (Exception cause) { errors.Add(cause); }
                if (close is not null)
                    try { await close.ConfigureAwait(false); } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
            }
            throw new AggregateException("The original Files fence publication and independent late-product cleanup failed.", errors);
        }
    }
}

public sealed partial class NativeFilesWorkspaceAuthority
{
    internal ValueTask<HomeClaimedResourceCommitFence> CaptureBrowserCommitFenceAsync(
        NativeFilesWorkspace original, HomeResourceOperationBroker broker,
        HomeResourceExecutionCapability capability, Func<bool> lifetime, CancellationToken token) =>
        CaptureBrowserCommitFenceCoreAsync(original, broker, capability, lifetime, token, null);

    internal ValueTask<HomeClaimedResourceCommitFence> CaptureBrowserCommitFenceWithinOriginalSourceAsync(
        NativeFilesWorkspace original, HomeResourceOperationBroker broker, HomeResourceExecutionCapability capability,
        Func<bool> lifetime, Action<Action> scope, Action<Task> retain, CancellationToken token,
        Action<HomeClaimedResourceCommitFence>? captureActualFence = null) =>
        CaptureBrowserCommitFenceCoreAsync(original, broker, capability, lifetime, token,
            FilesOriginalParentSourceCallbacks.Create(scope, retain), captureActualFence);

    private async ValueTask<HomeClaimedResourceCommitFence> CaptureBrowserCommitFenceCoreAsync(
        NativeFilesWorkspace original, HomeResourceOperationBroker broker, HomeResourceExecutionCapability capability,
        Func<bool> lifetime, CancellationToken token, FilesOriginalReadSourceScope? source,
        Action<HomeClaimedResourceCommitFence>? captureActualFence = null)
    {
        if (ownership is not HomeResourceStoreOwnershipAuthority actualOwnership || !lifetime())
            throw new UnauthorizedAccessException("The actual Home ownership source is unavailable.");
        var current = source is null ? await GetCurrentAsync(original.Configuration.StoreId, token).ConfigureAwait(false)
            : await source.Observe(() => GetOriginalCurrentWithinSourceAsync(original.Configuration.StoreId, source, token)).ConfigureAwait(false);
        if (current?.Actor != original.Actor || !ReferenceEquals(current.Provider, original.Provider)
            || !ReferenceEquals(current.Directories, original.Directories) || !lifetime())
            throw new UnauthorizedAccessException("The selected Files workspace was replaced.");
        return source is null
            ? await workspaces.CaptureBrowserCommitFenceAsync(original, broker, actualOwnership, capability, lifetime, token).ConfigureAwait(false)
            : await NativeFilesWorkspaceService.ObserveOriginalBrowserFenceProduct(source,
                () => workspaces.CaptureBrowserCommitFenceAsync(original, broker, actualOwnership,
                    capability, lifetime, token, source, captureActualFence).AsTask(), value => value, captureActualFence).ConfigureAwait(false);
    }
}
