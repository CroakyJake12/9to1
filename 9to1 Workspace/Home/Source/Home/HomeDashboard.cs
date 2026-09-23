namespace HavenOS.Home;

public enum HomeSectionState
{
    Available,
    Unavailable,
    Error,
}

public enum HomeOperationState
{
    Idle,
    Succeeded,
    Unavailable,
    Failed,
    NoWork,
}

public enum HomeCapabilityState
{
    Ready,
    Starting,
    Unavailable,
    Error,
}

public sealed record HomeSectionStatus(HomeSectionState State, string Message)
{
    public bool IsAvailable => State == HomeSectionState.Available;

    public static HomeSectionStatus Available(string message) => new(HomeSectionState.Available, message);
    public static HomeSectionStatus Unavailable(string message) => new(HomeSectionState.Unavailable, message);
    public static HomeSectionStatus Error(string message) => new(HomeSectionState.Error, message);
}

public sealed record HomeOperationStatus(HomeOperationState State, string Message)
{
    public static HomeOperationStatus Idle(string message) => new(HomeOperationState.Idle, message);
}

public sealed record HomeCatalogApp(string PackageId, string Name, string Version, string Description);

public sealed record HomeInstalledApp(string PackageId, string Name, string Version);

public sealed record HomeUpdate(string PackageId, string Name, string InstalledVersion, string AvailableVersion);

public sealed record HomeCatalogSection(IReadOnlyList<HomeCatalogApp> Apps, HomeSectionStatus Status);

public sealed record HomeInstalledAppsSection(IReadOnlyList<HomeInstalledApp> Apps, HomeSectionStatus Status);

public sealed record HomeUpdatesSection(IReadOnlyList<HomeUpdate> Items, HomeSectionStatus Status);

public sealed record HomeSettingsSection(
    HomeSectionStatus Status,
    bool? AutomaticUpdates,
    string? UpdateChannel)
{
    public static HomeSettingsSection Unavailable { get; } = new(
        HomeSectionStatus.Unavailable("A settings provider is not configured."),
        null,
        null);
}

public sealed record HomeCapabilityStatus(HomeCapabilityState State, string Message)
{
    public static HomeCapabilityStatus Unavailable(string message) => new(HomeCapabilityState.Unavailable, message);
}

public sealed record HomeRuntimeSection(
    HomeCapabilityStatus Runtime,
    HomeCapabilityStatus Model,
    HomeCapabilityStatus Voice)
{
    public static HomeRuntimeSection Unavailable { get; } = new(
        HomeCapabilityStatus.Unavailable("A runtime status provider is not configured."),
        HomeCapabilityStatus.Unavailable("A model status provider is not configured."),
        HomeCapabilityStatus.Unavailable("A voice status provider is not configured."));
}

public sealed record HomePackageInventory(
    HomeCatalogSection Catalog,
    HomeInstalledAppsSection InstalledApps,
    HomeUpdatesSection Updates)
{
    public static HomePackageInventory Unavailable(string message) => new(
        new HomeCatalogSection([], HomeSectionStatus.Unavailable(message)),
        new HomeInstalledAppsSection([], HomeSectionStatus.Unavailable(message)),
        new HomeUpdatesSection([], HomeSectionStatus.Unavailable(message)));

    public static HomePackageInventory Error(string message) => new(
        new HomeCatalogSection([], HomeSectionStatus.Error(message)),
        new HomeInstalledAppsSection([], HomeSectionStatus.Error(message)),
        new HomeUpdatesSection([], HomeSectionStatus.Error(message)));
}

public sealed record HomePackageOperationResult(
    HomeOperationStatus Status,
    HomePackageInventory? UpdatedInventory = null);

/// <summary>
/// The sole boundary allowed to enumerate or mutate packages for Home. Implementations must
/// return observed backend outcomes; callers never infer that an installation succeeded.
/// </summary>
public interface IHomePackageBackend
{
    Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken);

    Task<HomePackageOperationResult> InstallAllAsync(
        IReadOnlyList<string> packageIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Safe default while Home has no package-manager integration. It deliberately performs no
/// package operation and returns an observable unavailable result instead.
/// </summary>
public sealed class UnavailableHomePackageBackend : IHomePackageBackend
{
    public const string Message = "A package backend is not configured.";

    public Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HomePackageInventory.Unavailable(Message));
    }

    public Task<HomePackageOperationResult> InstallAllAsync(
        IReadOnlyList<string> packageIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageIds);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HomePackageOperationResult(
            new HomeOperationStatus(HomeOperationState.Unavailable, Message)));
    }
}

public sealed record HomeDashboardSnapshot(
    HomeCatalogSection Catalog,
    HomeInstalledAppsSection InstalledApps,
    HomeUpdatesSection Updates,
    HomeSettingsSection Settings,
    HomeRuntimeSection Runtime,
    HomeOperationStatus LastOperation)
{
    public bool CanInstallAll => Updates.Status.IsAvailable && Updates.Items.Count > 0;
}

/// <summary>
/// Owns Home's package-facing state. Settings and runtime state are injected snapshots so their
/// platform providers can remain outside this app slice.
/// </summary>
public sealed class HomeDashboard : IDisposable
{
    private readonly IHomePackageBackend _packages;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private HomeDashboardSnapshot _current;
    private bool _disposed;

    public HomeDashboard(
        IHomePackageBackend? packages = null,
        HomeSettingsSection? settings = null,
        HomeRuntimeSection? runtime = null)
    {
        var hasConfiguredBackend = packages is not null;
        _packages = packages ?? new UnavailableHomePackageBackend();
        var inventory = HomePackageInventory.Unavailable(UnavailableHomePackageBackend.Message);
        _current = CreateSnapshot(
            inventory,
            settings ?? HomeSettingsSection.Unavailable,
            runtime ?? HomeRuntimeSection.Unavailable,
            hasConfiguredBackend
                ? HomeOperationStatus.Idle("Package inventory has not been queried.")
                : new HomeOperationStatus(HomeOperationState.Unavailable, UnavailableHomePackageBackend.Message));
    }

    public HomeDashboardSnapshot Current => Volatile.Read(ref _current);

    public async Task<HomeDashboardSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var inventory = await _packages.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
            inventory = ValidateInventory(inventory);
            var current = Current;
            var operation = StatusForInventory(inventory);
            var snapshot = CreateSnapshot(inventory, current.Settings, current.Runtime, operation);
            Publish(snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = $"Package backend refresh failed: {exception.Message} Last known package data may be stale.";
            var current = Current;
            // Keep the last observed rows visible for recovery and context, but mark
            // every package section as failed so callers cannot treat it as fresh.
            var inventory = new HomePackageInventory(
                current.Catalog with { Status = HomeSectionStatus.Error(message) },
                current.InstalledApps with { Status = HomeSectionStatus.Error(message) },
                current.Updates with { Status = HomeSectionStatus.Error(message) });
            var snapshot = CreateSnapshot(
                inventory,
                current.Settings,
                current.Runtime,
                new HomeOperationStatus(HomeOperationState.Failed, message));
            Publish(snapshot);
            return snapshot;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<HomeDashboardSnapshot> InstallAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Current;
            if (!current.Updates.Status.IsAvailable)
            {
                var unavailable = new HomeOperationStatus(
                    HomeOperationState.Unavailable,
                    $"Install all unavailable: {current.Updates.Status.Message}");
                var snapshot = current with { LastOperation = unavailable };
                Publish(snapshot);
                return snapshot;
            }

            var packageIds = current.Updates.Items.Select(update => update.PackageId).ToArray();
            if (packageIds.Length == 0)
            {
                var snapshot = current with
                {
                    LastOperation = new HomeOperationStatus(HomeOperationState.NoWork, "No updates are available to install."),
                };
                Publish(snapshot);
                return snapshot;
            }

            var result = await _packages.InstallAllAsync(packageIds, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Package backend returned no install result.");
            ArgumentNullException.ThrowIfNull(result.Status);
            var inventory = result.UpdatedInventory is null
                ? new HomePackageInventory(current.Catalog, current.InstalledApps, current.Updates)
                : ValidateInventory(result.UpdatedInventory);
            var updated = CreateSnapshot(inventory, current.Settings, current.Runtime, result.Status);
            Publish(updated);
            return updated;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var current = Current;
            var message = $"Install all failed: {exception.Message} Package state may be stale; refresh before retrying.";
            var inventory = new HomePackageInventory(
                current.Catalog,
                current.InstalledApps with { Status = HomeSectionStatus.Error(message) },
                current.Updates with { Status = HomeSectionStatus.Error(message) });
            var failed = CreateSnapshot(
                inventory,
                current.Settings,
                current.Runtime,
                new HomeOperationStatus(HomeOperationState.Failed, message));
            Publish(failed);
            return failed;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static HomeDashboardSnapshot CreateSnapshot(
        HomePackageInventory inventory,
        HomeSettingsSection settings,
        HomeRuntimeSection runtime,
        HomeOperationStatus operation) => new(
            inventory.Catalog,
            inventory.InstalledApps,
            inventory.Updates,
            settings,
            runtime,
            operation);

    private static HomeOperationStatus StatusForInventory(HomePackageInventory inventory)
    {
        var unavailable = new[] { inventory.Catalog.Status, inventory.InstalledApps.Status, inventory.Updates.Status }
            .FirstOrDefault(status => !status.IsAvailable);
        if (unavailable is null)
            return HomeOperationStatus.Idle("Package inventory refreshed.");

        return unavailable.State == HomeSectionState.Error
            ? new HomeOperationStatus(HomeOperationState.Failed, unavailable.Message)
            : new HomeOperationStatus(HomeOperationState.Unavailable, unavailable.Message);
    }

    private static HomePackageInventory ValidateInventory(HomePackageInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(inventory.Catalog);
        ArgumentNullException.ThrowIfNull(inventory.InstalledApps);
        ArgumentNullException.ThrowIfNull(inventory.Updates);
        ArgumentNullException.ThrowIfNull(inventory.Catalog.Apps);
        ArgumentNullException.ThrowIfNull(inventory.InstalledApps.Apps);
        ArgumentNullException.ThrowIfNull(inventory.Updates.Items);

        ValidatePackageIds(inventory.Catalog.Apps.Select(app => app.PackageId), "catalog");
        ValidatePackageIds(inventory.InstalledApps.Apps.Select(app => app.PackageId), "installed apps");
        ValidatePackageIds(inventory.Updates.Items.Select(update => update.PackageId), "updates");
        return inventory;
    }

    private static void ValidatePackageIds(IEnumerable<string> packageIds, string section)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packageId in packageIds)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new InvalidDataException($"Package backend returned an empty package ID in {section}.");
            if (!seen.Add(packageId))
                throw new InvalidDataException($"Package backend returned duplicate package ID '{packageId}' in {section}.");
        }
    }

    private void Publish(HomeDashboardSnapshot snapshot) => Volatile.Write(ref _current, snapshot);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operationGate.Dispose();
    }
}
