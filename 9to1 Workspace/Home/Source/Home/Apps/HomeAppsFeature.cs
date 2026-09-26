namespace HavenOS.Home.Apps;

/// <summary>The state of one catalogue read, kept separate from an empty result.</summary>
public enum HomeAppsDataState
{
    Loading,
    Available,
    Partial,
    Empty,
    Unavailable,
    Failed,
}

public enum HomePackageInstallationState
{
    Unknown,
    Available,
    Installed,
    Installing,
    Updating,
    Repairing,
    Removing,
    Failed,
}

public enum HomePackageCompatibilityState
{
    Unknown,
    Checking,
    Compatible,
    Incompatible,
    MissingDependency,
    HomeServiceUnavailable,
}

public enum HomePackageAction
{
    Install,
    Uninstall,
    Update,
    Repair,
    Launch,
    SelectChannel,
    SelectVersion,
    Rollback,
}

public enum HomePackageOperationState
{
    Succeeded,
    PartiallySucceeded,
    Rejected,
    Failed,
    Cancelled,
    Pending,
    Unknown,
}

/// <summary>
/// A package catalogue row. Nullable metadata means unknown or unavailable; it must not be
/// rendered as a guessed value. PackageId and AppId are stable identities, never display names.
/// </summary>
public sealed record HomePackageEntry(
    string PackageId,
    string? AppId,
    string Name,
    Uri? IconUri,
    string? InstalledVersion,
    string? AvailableVersion,
    string? UpdateChannel,
    HomePackageInstallationState InstallationState,
    HomePackageCompatibilityState CompatibilityState,
    string? CompatibilityMessage,
    bool? UpdateAvailable,
    bool? RepairAvailable,
    bool? RollbackAvailable,
    IReadOnlySet<HomePackageAction> SupportedActions,
    string? OwnerAppId = null);

/// <summary>Bounded, resumable catalogue query. A null filter means no constraint.</summary>
public sealed record HomeAppsQuery(
    string? SearchText = null,
    HomePackageInstallationState? InstallationState = null,
    bool? UpdatesOnly = null,
    string? PageToken = null,
    int PageSize = 50)
{
    public HomeAppsQuery Validate()
    {
        if (PageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(PageSize), "Page size must be between 1 and 200.");
        if (PageToken is { Length: > 1024 })
            throw new ArgumentException("Page token exceeds the 1024 character limit.", nameof(PageToken));
        return this with { SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim() };
    }
}

public sealed record HomeAppsError(
    string Code,
    string Message,
    string Target,
    bool Retryable,
    bool Recoverable);

public sealed record HomeAppsSnapshot(
    string Revision,
    HomeAppsDataState State,
    IReadOnlyList<HomePackageEntry> Packages,
    int? TotalCount,
    string? NextPageToken,
    bool IsStale,
    HomeAppsError? Error = null);

public sealed record HomePackageActionRequest(
    string PackageId,
    HomePackageAction Action,
    string IdempotencyKey,
    string? RequestedVersion = null,
    string? RequestedChannel = null,
    string? ExpectedRevision = null);

public sealed record HomePackageActionResult(
    string OperationId,
    string PackageId,
    HomePackageAction Action,
    HomePackageOperationState State,
    string Code,
    string Message,
    bool Retryable,
    bool Recoverable,
    bool PreviousKnownGoodVersionRetained,
    IReadOnlyList<string> SucceededSteps,
    IReadOnlyList<string> FailedSteps,
    IReadOnlyList<string> SkippedSteps,
    IReadOnlyList<string> RolledBackSteps,
    HomeAppsError? Error = null);

/// <summary>
/// Feature boundary consumed by Home's shell and API host. Implementations must use the
/// canonical Home package service and permission broker; UI code must not mutate package state.
/// </summary>
public interface IHomeAppsFeatureProvider
{
    Task<HomeAppsSnapshot> GetSnapshotAsync(HomeAppsQuery query, CancellationToken cancellationToken);

    Task<HomePackageActionResult> ExecuteAsync(
        HomePackageActionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates catalogue reads and package actions for the Apps surface. It never retries a
/// consequential action automatically: an ambiguous result must be refreshed before retry.
/// </summary>
public sealed class HomeAppsFeature(IHomeAppsFeatureProvider provider)
{
    private readonly IHomeAppsFeatureProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HomeAppsSnapshot _current = new(
        string.Empty,
        HomeAppsDataState.Loading,
        Array.Empty<HomePackageEntry>(),
        null,
        null,
        false);

    public HomeAppsSnapshot Current => Volatile.Read(ref _current);

    public async Task<HomeAppsSnapshot> RefreshAsync(
        HomeAppsQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        var validated = (query ?? new HomeAppsQuery()).Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _provider.GetSnapshotAsync(validated, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The Home package service returned no catalogue result.");
            ValidateSnapshot(snapshot);
            Publish(snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var current = Current;
            var error = new HomeAppsError(
                "HomeApps.CatalogueUnavailable",
                "The app catalogue could not be refreshed. Refresh to try again.",
                "HomeApps.GetSnapshot",
                Retryable: true,
                Recoverable: true);
            var failed = current with
            {
                State = current.Packages.Count == 0 ? HomeAppsDataState.Failed : HomeAppsDataState.Partial,
                IsStale = current.Packages.Count > 0,
                Error = error with { Message = exception is InvalidDataException ? exception.Message : error.Message },
            };
            Publish(failed);
            return failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HomePackageActionResult> ExecuteAsync(
        HomePackageActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PackageId))
            return Rejected(request, "HomeApps.PackageIdRequired", "Select an app before performing this action.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Rejected(request, "HomeApps.IdempotencyKeyRequired", "The action could not be safely identified. Refresh and try again.");

        var current = Current;
        var entry = current.Packages.FirstOrDefault(item => item.PackageId == request.PackageId);
        if (entry is null || !entry.SupportedActions.Contains(request.Action))
            return Rejected(request, "HomeApps.ActionUnavailable", "This action is not available for the selected app.");
        if (current.IsStale || current.State is HomeAppsDataState.Failed or HomeAppsDataState.Unavailable)
            return Rejected(request, "HomeApps.RefreshRequired", "Refresh the app catalogue before performing this action.");
        if (request.ExpectedRevision is not null && !StringComparer.Ordinal.Equals(request.ExpectedRevision, current.Revision))
            return Rejected(request, "HomeApps.RevisionChanged", "The app catalogue changed. Refresh it before continuing.");

        if ((request.Action is HomePackageAction.SelectVersion && string.IsNullOrWhiteSpace(request.RequestedVersion)) ||
            (request.Action is HomePackageAction.SelectChannel && string.IsNullOrWhiteSpace(request.RequestedChannel)))
            return Rejected(request, "HomeApps.SelectionRequired", "Choose a version or update channel first.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Recheck after entering the gate: another refresh or action may have changed state.
            current = Current;
            entry = current.Packages.FirstOrDefault(item => item.PackageId == request.PackageId);
            if (entry is null || current.IsStale || !entry.SupportedActions.Contains(request.Action))
                return Rejected(request, "HomeApps.StateChanged", "The app state changed. Refresh and try again.");

            var result = await _provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The Home package service returned no operation result.");
            ValidateActionResult(request, result);
            if (result.State is HomePackageOperationState.Succeeded or HomePackageOperationState.PartiallySucceeded)
                await RefreshAsync(new HomeAppsQuery(), cancellationToken).ConfigureAwait(false);
            else if (result.State is HomePackageOperationState.Unknown or HomePackageOperationState.Pending)
                Publish(current with { IsStale = true, State = HomeAppsDataState.Partial });
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var result = new HomePackageActionResult(
                Guid.NewGuid().ToString("N"),
                request.PackageId,
                request.Action,
                HomePackageOperationState.Unknown,
                "HomeApps.OperationOutcomeUnknown",
                "The app action outcome is unknown. Refresh before trying again.",
                Retryable: false,
                Recoverable: true,
                PreviousKnownGoodVersionRetained: false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new HomeAppsError("HomeApps.OperationOutcomeUnknown", "Refresh to check the current app state.", "HomeApps.Execute", false, true));
            Publish(Current with { IsStale = true, State = HomeAppsDataState.Partial });
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateSnapshot(HomeAppsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Packages);
        if (snapshot.State == HomeAppsDataState.Available && snapshot.Packages.Count == 0)
            throw new InvalidDataException("An empty catalogue must use the Empty state.");
        if (snapshot.State == HomeAppsDataState.Empty && snapshot.Packages.Count != 0)
            throw new InvalidDataException("An empty catalogue cannot contain package rows.");

        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in snapshot.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageId) || string.IsNullOrWhiteSpace(package.Name))
                throw new InvalidDataException("The Home package service returned an app without a stable package ID or name.");
            if (!packageIds.Add(package.PackageId))
                throw new InvalidDataException($"The Home package service returned duplicate package ID '{package.PackageId}'.");
            if (package.SupportedActions is null)
                throw new InvalidDataException($"The Home package service returned no action set for '{package.PackageId}'.");
        }
    }

    private static void ValidateActionResult(HomePackageActionRequest request, HomePackageActionResult result)
    {
        if (result.PackageId != request.PackageId || result.Action != request.Action ||
            string.IsNullOrWhiteSpace(result.OperationId) || string.IsNullOrWhiteSpace(result.Code) ||
            string.IsNullOrWhiteSpace(result.Message))
            throw new InvalidDataException("The Home package service returned an operation result for a different action or without a stable result identity.");
        ArgumentNullException.ThrowIfNull(result.SucceededSteps);
        ArgumentNullException.ThrowIfNull(result.FailedSteps);
        ArgumentNullException.ThrowIfNull(result.SkippedSteps);
        ArgumentNullException.ThrowIfNull(result.RolledBackSteps);
    }

    private static HomePackageActionResult Rejected(HomePackageActionRequest request, string code, string message) => new(
        Guid.NewGuid().ToString("N"),
        request.PackageId ?? string.Empty,
        request.Action,
        HomePackageOperationState.Rejected,
        code,
        message,
        Retryable: false,
        Recoverable: true,
        PreviousKnownGoodVersionRetained: false,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new HomeAppsError(code, message, "HomeApps.Execute", false, true));

    private void Publish(HomeAppsSnapshot snapshot) => Volatile.Write(ref _current, snapshot);
}
