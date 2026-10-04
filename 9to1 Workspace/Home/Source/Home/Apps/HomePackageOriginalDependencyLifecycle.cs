using System.Collections.ObjectModel;

namespace HavenOS.Home.Apps;

// Classification is supplied for the exact original signed selection by its registered
// installer owner. Neither these labels nor a lifecycle plan grant permission or root access.
internal enum HomePackageComponentClass
{
    MandatorySharedCore,
    OptionalApp,
    AppRequiredDependency,
    OptionalFeature,
}

internal interface IHomePackageOriginalLifecyclePolicy
{
    string OriginalHomePackageId { get; }
    string Platform { get; }
    string Abi { get; }
    HomePackageComponentClass? ClassifyOriginal(HomePackageArtifactSelection originalSelection);
    bool TrySatisfyOriginalVersion(string actualVersion, HomePackageDependency requiredRange, out bool satisfies);
    bool SupportsOriginalAction(HomePackageArtifactSelection originalSelection, HomePackageAction action);
}

internal sealed record HomePackageOriginalLifecycleStep(
    HomePackageArtifactSelection OriginalSelection, HomePackageComponentClass Classification,
    HomePackageAction Action, long OriginalEntryRevision, string? OriginalInstalledVersion,
    string? OriginalKnownGoodVersion, bool ReuseCompatibleInstallation);

internal sealed class HomePackageOriginalLifecyclePlan
{
    private readonly HomePackageOriginalDependencyLifecycle _issuer;
    private readonly HomePackageDatabaseSnapshot _originalRegistry;
    internal HomePackageActionRequest OriginalRequest { get; }
    internal long OriginalRegistryRevision => _originalRegistry.Revision;
    internal IReadOnlyList<HomePackageOriginalLifecycleStep> Steps { get; }
    internal IReadOnlyList<string> AffectedPackageIds { get; }
    internal IReadOnlyList<string> RetainedSharedPackageIds { get; }
    internal bool RequiresOptionalFeatureConsent { get; }
    internal HomePackageOriginalLifecyclePlan(HomePackageOriginalDependencyLifecycle issuer,
        HomePackageActionRequest request, HomePackageDatabaseSnapshot registry,
        IReadOnlyList<HomePackageOriginalLifecycleStep> steps, IReadOnlyList<string> affected,
        IReadOnlyList<string> retained, bool requiresFeatureConsent)
    {
        _issuer = issuer;
        _originalRegistry = HomePackageDatabase.CaptureGuardedSnapshot(registry);
        OriginalRequest = request with { };
        Steps = Array.AsReadOnly(steps.ToArray());
        AffectedPackageIds = Array.AsReadOnly(affected.ToArray());
        RetainedSharedPackageIds = Array.AsReadOnly(retained.ToArray());
        RequiresOptionalFeatureConsent = requiresFeatureConsent;
    }
    internal bool IssuedBy(HomePackageOriginalDependencyLifecycle original) => ReferenceEquals(_issuer, original);
    internal HomePackageDatabaseSnapshot CopyOriginalRegistry() => HomePackageDatabase.CaptureGuardedSnapshot(_originalRegistry);
}

internal sealed record HomePackageOriginalLifecyclePlanningResult(
    HomePackageOriginalLifecyclePlan? Plan, HomeAppsError? Error);

// This is the dependency and impact part of the SAME installer, over its signed
// selections and canonical Home database. It creates no store, approval, journal,
// path, default Home identity, or platform effect. Every consequential step still
// needs the original installed-session review and genuine root lifecycle adapter.
internal sealed class HomePackageOriginalDependencyLifecycle
{
    private const int MaximumPackages = 256;
    private readonly IHomePackageOriginalPlatformOwner _owner;
    private readonly IHomePackageOriginalLifecyclePolicy _policy;
    private readonly IReadOnlyDictionary<string, HomePackageArtifactSelection> _selected;
    private readonly IReadOnlyDictionary<string, HomePackageComponentClass> _classes;
    private readonly string _homePackageId, _platform, _abi;

    internal HomePackageOriginalDependencyLifecycle(
        IHomePackageOriginalPlatformOwner sameOriginalOwner,
        IHomePackageOriginalLifecyclePolicy sameOriginalPolicy,
        IEnumerable<HomePackageArtifactSelection> originalConfiguredSelections)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalOwner);
        ArgumentNullException.ThrowIfNull(sameOriginalPolicy);
        ArgumentNullException.ThrowIfNull(originalConfiguredSelections);
        _owner = sameOriginalOwner; _policy = sameOriginalPolicy;
        _homePackageId = sameOriginalPolicy.OriginalHomePackageId;
        _platform = sameOriginalPolicy.Platform; _abi = sameOriginalPolicy.Abi;
        if (!HomePackageArtifactSelection.Identifier(_homePackageId) ||
            !HomePackageArtifactSelection.Identifier(_platform) ||
            !HomePackageArtifactSelection.Identifier(_abi))
            throw new InvalidDataException("Genuine original Home/platform/ABI classification is required.");
        var selections = new Dictionary<string, HomePackageArtifactSelection>(StringComparer.Ordinal);
        var classes = new Dictionary<string, HomePackageComponentClass>(StringComparer.Ordinal);
        foreach (var selection in originalConfiguredSelections)
        {
            if (selections.Count == MaximumPackages || selection is null ||
                !selection.IssuedBy(_owner) || selection.Descriptor.Platform != _platform ||
                selection.Descriptor.Abi != _abi)
                throw new InvalidDataException("Bounded SAME-owner platform package selections are required.");
            var classification = _policy.ClassifyOriginal(selection);
            if (classification is null || !Enum.IsDefined(classification.Value) ||
                !selections.TryAdd(selection.Descriptor.PackageId, selection))
                throw new InvalidDataException("One explicitly classified original version per PackageID is required.");
            classes.Add(selection.Descriptor.PackageId, classification.Value);
        }
        if (!classes.TryGetValue(_homePackageId, out var homeClass) ||
            homeClass != HomePackageComponentClass.MandatorySharedCore)
            throw new InvalidDataException("The supplied Home package must be classified as mandatory shared core.");
        _selected = new ReadOnlyDictionary<string, HomePackageArtifactSelection>(selections);
        _classes = new ReadOnlyDictionary<string, HomePackageComponentClass>(classes);
    }

    internal bool IssuedBy(IHomePackageOriginalPlatformOwner owner) => ReferenceEquals(_owner, owner);

    internal HomePackageOriginalLifecyclePlanningResult Plan(
        HomePackageActionRequest originalRequest,
        HomePackageDatabaseSnapshot originalRegistry, bool optionalFeatureAccepted)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        // Capture complete canonical intent synchronously before any caller can publish
        // an effect task or mutate nested package/dependency collections.
        var registry = HomePackageDatabase.CaptureGuardedSnapshot(originalRegistry);
        var request = originalRequest with { };
        if (registry.Revision is < 0 or long.MaxValue ||
            registry.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion ||
            !HomePackageArtifactSelection.Identifier(request.PackageId) ||
            !HomePackageArtifactSelection.Text(request.IdempotencyKey, 128))
            return Refuse("PackageSelectionChanged", "The original package selection is invalid.", request.PackageId);
        if (!_selected.TryGetValue(request.PackageId, out var selected))
            return Refuse("DependencyRequired", "The exact trusted package selection is unavailable.", request.PackageId);
        if (request.ExpectedRevision is not null &&
            request.ExpectedRevision != selected.CatalogueRevision ||
            request.RequestedVersion is not null &&
            request.RequestedVersion != selected.Descriptor.Version ||
            request.RequestedChannel is not null &&
            request.RequestedChannel != selected.Descriptor.Channel)
            return Refuse("PackageSelectionChanged", "The original version/channel/catalogue selection changed.", request.PackageId);
        var entries = new Dictionary<string, HomePackageDatabaseEntry>(StringComparer.Ordinal);
        foreach (var entry in registry.Packages)
        {
            if (entry is null || entry.Revision is <= 0 or long.MaxValue ||
                !Enum.IsDefined(entry.InstallationState) || !Enum.IsDefined(entry.Compatibility) ||
                !entries.TryAdd(entry.PackageId, entry))
                return Refuse("PackageSelectionChanged", "Canonical package identity or revision is invalid.", request.PackageId);
        }
        var classification = _classes[request.PackageId];
        var feature = classification == HomePackageComponentClass.OptionalFeature;
        if (feature && request.Action == HomePackageAction.Install && !optionalFeatureAccepted)
            return Refuse("DependencyRequired", "Install this optional feature to continue; the original work remains unchanged.", request.PackageId);
        if (request.Action == HomePackageAction.Uninstall)
            return PlanRemoval(request, registry, entries, selected);
        if (request.Action is not (HomePackageAction.Install or HomePackageAction.Update or
            HomePackageAction.Repair or HomePackageAction.Rollback))
            return Refuse("HomeApps.ActionUnavailable", "This action is not a dependency lifecycle operation.", request.PackageId);

        var steps = new List<HomePackageOriginalLifecycleStep>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        HomeAppsError? error = null;
        // Home and every configured mandatory core are roots of the same install
        // graph. Shared classification takes precedence over optional dependency use.
        foreach (var core in _classes.Where(item => item.Value == HomePackageComponentClass.MandatorySharedCore)
            .Select(item => item.Key).Order(StringComparer.Ordinal))
            Visit(core, null, false);
        Visit(request.PackageId, null, true);
        if (error is not null) return new(null, error);
        return new(new(this, request, registry, Array.AsReadOnly(steps.ToArray()),
            Array.AsReadOnly(steps.Where(step => !step.ReuseCompatibleInstallation)
                .Select(step => step.OriginalSelection.Descriptor.PackageId).ToArray()),
            Array.AsReadOnly(steps.Where(step =>
                step.Classification == HomePackageComponentClass.MandatorySharedCore &&
                step.ReuseCompatibleInstallation).Select(step => step.OriginalSelection.Descriptor.PackageId).ToArray()),
            feature), null);

        void Visit(string packageId, HomePackageDependency? requiredRange, bool selectedTarget)
        {
            if (error is not null) return;
            selectedTarget |= packageId == request.PackageId;
            if (!_selected.TryGetValue(packageId, out var original))
            { Fail("DependencyRequired", "A required trusted package/version is unavailable.", packageId); return; }
            if (_policy.ClassifyOriginal(original) != _classes[packageId] ||
                _policy.OriginalHomePackageId != _homePackageId ||
                _policy.Platform != _platform || _policy.Abi != _abi)
            { Fail("PackageSelectionChanged", "The original package classification/platform policy changed.", packageId); return; }
            if (requiredRange is not null)
            {
                if (!_policy.TrySatisfyOriginalVersion(original.Descriptor.Version, requiredRange, out var satisfies))
                { Fail("DependencyRequired", "The original dependency version syntax/policy is unavailable.", packageId); return; }
                if (!satisfies)
                { Fail("PackageDependencyConflict", "The configured dependency version does not satisfy its original range.", packageId); return; }
            }
            if (active.Contains(packageId))
            { Fail("PackageDependencyConflict", "The original dependency graph contains a cycle.", packageId); return; }
            // Check each incoming range even when a shared dependency was already visited.
            if (visited.Contains(packageId)) return;
            active.Add(packageId);
            foreach (var dependency in original.Descriptor.Dependencies.OrderBy(item => item.PackageId, StringComparer.Ordinal))
                Visit(dependency.PackageId, dependency, false);
            active.Remove(packageId);
            if (error is not null) return;
            entries.TryGetValue(packageId, out var entry);
            if (entry is not null && entry.InstallationState is
                HomePackageInstallState.Unknown or HomePackageInstallState.Updating or
                HomePackageInstallState.Repairing or HomePackageInstallState.Removing)
            { Fail("PackageDependencyConflict", "Reconcile the original pending package operation before another lifecycle effect.", packageId); return; }
            var exactInstalled = entry is not null &&
                entry.InstallationState == HomePackageInstallState.Installed &&
                entry.Compatibility == HomePackageCompatibility.Compatible &&
                entry.InstalledVersion == original.Descriptor.Version &&
                entry.IntegrityEvidence.Any(item => item.State == HomePackageIntegrityState.Verified &&
                    item.Version == original.Descriptor.Version &&
                    string.Equals(item.ArtifactSha256, original.Descriptor.PayloadSha256, StringComparison.OrdinalIgnoreCase));
            if (_classes[packageId] == HomePackageComponentClass.OptionalFeature && !exactInstalled &&
                (!selectedTarget || !optionalFeatureAccepted))
            { Fail("DependencyRequired", "Install/cancel consent for the exact optional feature is required.", packageId); return; }
            if (selectedTarget && request.Action == HomePackageAction.Install &&
                entry?.InstalledVersion is not null && !exactInstalled)
            { Fail("PackageDependencyConflict", "Use an explicitly reviewed update or repair for an existing installation.", packageId); return; }
            var action = selectedTarget ? request.Action :
                entry?.InstalledVersion is null ? HomePackageAction.Install : HomePackageAction.Update;
            var reuse = exactInstalled && (!selectedTarget || request.Action == HomePackageAction.Install);
            if (!reuse && !_policy.SupportsOriginalAction(original, action))
            { Fail("DependencyRequired", "The genuine platform lifecycle adapter for this step is unavailable.", packageId); return; }
            if (!reuse && action is HomePackageAction.Update or HomePackageAction.Repair or HomePackageAction.Rollback)
            {
                if (entry?.InstalledVersion is null || entry.LastKnownGoodVersion is null)
                { Fail("PackageDependencyConflict", "Retain the genuine previous valid installation before replacement.", packageId); return; }
                if (action == HomePackageAction.Rollback &&
                    !entry.RetainedRollbackVersions.Contains(original.Descriptor.Version, StringComparer.Ordinal))
                { Fail("PackageDependencyConflict", "The selected rollback version is not retained by the canonical owner.", packageId); return; }
            }
            if (registry.RecentOperations.Any(item => item.PackageId == packageId &&
                item.State is HomePackageJournalState.Pending or HomePackageJournalState.Staged or
                    HomePackageJournalState.Validated or HomePackageJournalState.Activating or
                    HomePackageJournalState.OutcomeUnknown))
            { Fail("PackageDependencyConflict", "The original package outcome requires audit-only reconciliation.", packageId); return; }
            visited.Add(packageId);
            steps.Add(new(original, _classes[packageId], action, entry?.Revision ?? 0,
                entry?.InstalledVersion, entry?.LastKnownGoodVersion, reuse));
        }

        void Fail(string code, string message, string target) =>
            error ??= new(code, message, target, false, true);
    }

    private HomePackageOriginalLifecyclePlanningResult PlanRemoval(
        HomePackageActionRequest request, HomePackageDatabaseSnapshot registry,
        Dictionary<string, HomePackageDatabaseEntry> entries, HomePackageArtifactSelection selected)
    {
        if (_policy.ClassifyOriginal(selected) != _classes[request.PackageId] ||
            _policy.OriginalHomePackageId != _homePackageId ||
            _policy.Platform != _platform || _policy.Abi != _abi)
            return Refuse("PackageSelectionChanged", "The original package classification/platform policy changed.", request.PackageId);
        if (_classes[request.PackageId] == HomePackageComponentClass.MandatorySharedCore)
            return Refuse("PackageDependencyConflict", "Mandatory Home/shared core cannot be removed.", request.PackageId);
        var dependents = entries.Values.Where(entry => entry.PackageId != request.PackageId &&
            (entry.InstalledVersion is not null || entry.InstallationState is
                HomePackageInstallState.Unknown or HomePackageInstallState.Staged or
                HomePackageInstallState.Updating or HomePackageInstallState.Repairing or HomePackageInstallState.Removing) &&
            entry.Dependencies.Any(dependency => dependency.PackageId == request.PackageId))
            .Select(entry => entry.PackageId).Order(StringComparer.Ordinal).ToArray();
        if (dependents.Length != 0)
            return Refuse("PackageDependencyConflict", "Other installed or pending packages still require this component.", request.PackageId);
        if (!entries.TryGetValue(request.PackageId, out var originalEntry) ||
            originalEntry.InstalledVersion is null || !originalEntry.PreservesOwnedArtifactsOnUninstall ||
            originalEntry.InstallationState != HomePackageInstallState.Installed ||
            registry.RecentOperations.Any(item => item.PackageId == request.PackageId &&
                item.State is HomePackageJournalState.Pending or HomePackageJournalState.Staged or
                    HomePackageJournalState.Validated or HomePackageJournalState.Activating or HomePackageJournalState.OutcomeUnknown) ||
            !_policy.SupportsOriginalAction(selected, HomePackageAction.Uninstall))
            return Refuse("PackageDependencyConflict", "A genuine artifact-preserving removal with a known original state is required.", request.PackageId);
        var retained = entries.Values.Where(entry => entry.PackageId != request.PackageId &&
            entry.InstalledVersion is not null && _classes.TryGetValue(entry.PackageId, out var value) &&
            value is HomePackageComponentClass.MandatorySharedCore or HomePackageComponentClass.AppRequiredDependency)
            .Select(entry => entry.PackageId).Order(StringComparer.Ordinal).ToArray();
        return new(new(this, request, registry, Array.AsReadOnly(new[] {
            new HomePackageOriginalLifecycleStep(selected, _classes[request.PackageId], HomePackageAction.Uninstall,
                originalEntry.Revision, originalEntry.InstalledVersion, originalEntry.LastKnownGoodVersion, false)
        }), Array.AsReadOnly(new[] { request.PackageId }), Array.AsReadOnly(retained), false), null);
    }

    private static HomePackageOriginalLifecyclePlanningResult Refuse(string code, string message, string target) =>
        new(null, new(code, message, target, false, true));
}
