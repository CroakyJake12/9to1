using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using HomeAppsSnapshot = HavenOS.Home.Apps.HomeAppsSnapshot;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Home Apps uses the SAME installed Root client and canonical Home index.
/// Only actual selected native launch is supported here; package installation and
/// publisher enrollment remain the genuine single-installer flow.</summary>
public sealed partial class NativeWindowsHomeRootClient : IHomeAppsFeatureSource
{
    private sealed record AppsCursor(string Revision, HomeAppsQuery Query, int Offset);
    private sealed record AppsView(string Revision, IReadOnlyList<InstalledApplicationReference> Rows);
    private readonly Dictionary<string, AppsCursor> _appsCursors = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<HomeAppsSnapshot, AppsView> _appsSnapshots = new();
    private HomeAppsSnapshot? _currentAppsSnapshot;
    private sealed record AppsOperation(HomePackageActionRequest OriginalRequest, Guid OriginalOperation,
        Task<HomePackageActionResult> OriginalTask);
    private readonly Dictionary<string, AppsOperation> _appsOperations = new(StringComparer.Ordinal);
    public Task<HomeAppsSnapshot> GetSnapshotAsync(HomeAppsQuery query, CancellationToken token) => Admit(body => body(), _ => { }, async source =>
    {
        var actualQuery = query.Validate();
        var rows = await Take(source, () => ObserveOriginalApplicationLaunchChoicesWithinSourceAsync(
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
        if (_publishedListening is null || _launchHome is null || _connect?.IsCompletedSuccessfully != true)
            return new(string.Empty, HomeAppsDataState.Unavailable, [], null, null, false,
                new("HomeApps.InstalledRootRequired", "The installed enrolled Root and actual Home listening session are required.", "HomeApps.Catalogue", true, true));
        var revision = source.Invoke(() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { Listening = _publishedListening, Rows = rows.Select(row => new { row, Package = _launchChoices.GetValue(row, _ => throw new UnauthorizedAccessException("The actual installed choice is unavailable.")).Package.Fingerprint }).ToArray() }))));
        var offset = 0;
        if (actualQuery.PageToken is { } cursorToken)
        {
            AppsCursor? cursor; lock (_gate) _appsCursors.TryGetValue(cursorToken, out cursor);
            if (cursor is null || cursor.Revision != revision || cursor.Query != (actualQuery with { PageToken = null }))
                return new(revision, HomeAppsDataState.Unavailable, [], null, null, false,
                    new("HomeApps.PageExpired", "Refresh the current installed catalogue before continuing this page.", "HomeApps.Catalogue", true, true));
            offset = cursor.Offset;
        }
        var filtered = rows.Where(row => (actualQuery.InstallationState is null or HomePackageInstallationState.Installed) &&
                actualQuery.UpdatesOnly != true && (actualQuery.SearchText is null || row.Label.Contains(actualQuery.SearchText, StringComparison.OrdinalIgnoreCase) ||
                    row.StableLaunchIdentity!.Contains(actualQuery.SearchText, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.Label, StringComparer.Ordinal).ThenBy(row => row.ApplicationId).ToArray();
        var page = filtered.Skip(offset).Take(actualQuery.PageSize).ToArray();
        string? next = null;
        if (offset + page.Length < filtered.Length)
        {
            lock (_gate)
            {
                // Detached read cursor metadata owns no resources or operation grant.
                // Expiry gives a healthy explicit page-refresh outcome above.
                if (_appsCursors.Count >= 128) _appsCursors.Remove(_appsCursors.Keys.First());
                next = Guid.NewGuid().ToString("N"); _appsCursors.Add(next, new(revision, actualQuery with { PageToken = null }, offset + page.Length));
            }
        }
        var packages = source.Invoke(() => page.Select(row =>
        {
            if (!_launchChoices.TryGetValue(row, out var choice)) throw new UnauthorizedAccessException("The SAME installed row/source choice is required.");
            return new HomePackageEntry(choice.Package.PackageId, choice.Package.AppId, row.Label, null, row.Version, null, null,
                HomePackageInstallationState.Installed, HomePackageCompatibilityState.Unknown,
                "Launch revalidates the actual protected runtime and requests individual Home approval.", null, null, null,
                new[] { HomePackageAction.Launch }.ToFrozenSet(), "home");
        }).ToArray());
        var snapshot = new HomeAppsSnapshot(revision, packages.Length == 0 ? HomeAppsDataState.Empty : HomeAppsDataState.Available,
            Array.AsReadOnly(packages), filtered.Length, next, false);
        lock (_gate) { _appsSnapshots.Add(snapshot, new(revision, rows)); _currentAppsSnapshot = snapshot; }
        return snapshot;
    });
    public Task<HomePackageActionResult> ExecuteAsync(HomePackageActionRequest request, CancellationToken token)
    {
        if (request.IdempotencyKey is not { Length: > 0 and <= 1024 })
            return Task.FromResult(Decision(request, HomePackageOperationState.Rejected, "HomeApps.OperationRequired", "A bounded original operation identity is required."));
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<HomePackageActionResult> original;
        lock (_gate)
        {
            if (_appsOperations.TryGetValue(request.IdempotencyKey, out var known))
            {
                if (known.OriginalRequest != request) throw new UnauthorizedAccessException("The original launch operation cannot be rebound to another request.");
                return known.OriginalTask;
            }
            if (_retiring || _launchWithdrawalRequested) throw new ObjectDisposedException(nameof(NativeWindowsHomeRootClient));
            token.ThrowIfCancellationRequested();
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count > 126)
                throw new InvalidOperationException("Retained Root originals leave no capacity for the actual Apps command and its launch parent.");
            var operation = Guid.NewGuid();
            // Publish the actual native-launch parent at the product admission
            // boundary. Retirement therefore sees its finite Home review-publication
            // child even if this outer Apps command has not started its async body.
            // This private intent is an observation only: the Home acquisition and
            // Root preparation still independently revalidate current authority.
            var launch = PublishOriginalAppsLaunchParent(request, operation);
            original = Admit(body => body(), _ => { }, async source =>
            {
                await begin.Task.ConfigureAwait(false);
                return await ExecuteOriginalAppsRequest(request, launch, source).ConfigureAwait(false);
            });
            _appsOperations.Add(request.IdempotencyKey, new(request, operation, original));
        }
        begin.SetResult(); return original;
    }
    private Task<ICanonicalInstalledApplicationLaunchAcknowledgment>? PublishOriginalAppsLaunchParent(
        HomePackageActionRequest request, Guid operation)
    {
        // Called only while the SAME owner's admission gate remains held. Metadata
        // cannot allocate another issuer: both view and row must be actual originals.
        var snapshot = _currentAppsSnapshot;
        if (request.Action != HomePackageAction.Launch || snapshot is null ||
            !_appsSnapshots.TryGetValue(snapshot, out var view) || request.ExpectedRevision != view.Revision ||
            request.RequestedVersion is not null || request.RequestedChannel is not null) return null;
        var row = view.Rows.SingleOrDefault(candidate => _launchChoices.TryGetValue(candidate, out var known) &&
            known.Package.PackageId == request.PackageId);
        if (row is null || !_launchChoices.TryGetValue(row, out var choice) || !ReferenceEquals(choice.Row, row)) return null;
        var intent = new LaunchIntent(this, choice, operation); var work = new LaunchInvocation(intent, this);
        _launchIntents.Add(intent, work);
        return ExecuteOriginalApplicationLaunchWithinSourceAsync(intent, body => body(), _ => { }, CancellationToken.None);
    }
    private async Task<HomePackageActionResult> ExecuteOriginalAppsRequest(HomePackageActionRequest request,
        Task<ICanonicalInstalledApplicationLaunchAcknowledgment>? originalLaunch, CloudflareOriginalTaskLedger source)
    {
        if (request.Action != HomePackageAction.Launch)
            return Decision(request, HomePackageOperationState.Rejected, "HomeApps.SingleInstallerRequired", "Use the actual single installer for package changes and publisher enrollment.");
        if (originalLaunch is null)
            return Decision(request, HomePackageOperationState.Rejected, "HomeApps.CurrentChoiceRequired", "Refresh the current source-issued installed app catalogue before choosing Launch.");
        _ = source.Track(originalLaunch);
        var acknowledgment = await source.AwaitAsync(originalLaunch).ConfigureAwait(false);
        if (acknowledgment.Applied)
            return Decision(request, HomePackageOperationState.Succeeded, "HomeApps.ControlledChildLaunched", "The actual installed Root-controlled native app started.", ["IndividualHomeApproval", "CurrentProtectedActivation", "ActualControlledChildResume"]);
        return Decision(request, HomePackageOperationState.Rejected, "HomeApps.IndividualReviewDeclined", acknowledgment.Reason);
    }
    private static HomePackageActionResult Decision(HomePackageActionRequest request, HomePackageOperationState state,
        string code, string message, IReadOnlyList<string>? succeeded = null) => new(request.IdempotencyKey, request.PackageId, request.Action,
            state, code, message, false, true, true, succeeded ?? [], [], [], []);
}
