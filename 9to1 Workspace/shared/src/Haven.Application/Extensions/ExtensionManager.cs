using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Repository-backed package discovery and atomic installation coordinator.</summary>
public sealed class ExtensionManager(
    IExtensionRepository repository,
    IExtensionSourceTransport transport,
    ExtensionManifestValidator validator,
    NativePluginRuntime runtime,
    IAppPaths paths)
{
    private static readonly string[] ManifestLocations = ["haven.repository.json", ".haven/repository.json"];

    public Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken cancellationToken) => repository.GetSourcesAsync(cancellationToken);
    public Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken cancellationToken) => repository.GetInstalledAsync(cancellationToken);

    public async Task<InstalledExtensionPackage?> GetPackageAsync(string packageId, CancellationToken cancellationToken) =>
        (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Manifest.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<InstalledExtensionPackage>> ListPluginsAsync(CancellationToken cancellationToken) =>
        (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.Manifest.PackageType is ExtensionPackageType.Plugin or ExtensionPackageType.PluginAndSkills)
            .OrderBy(item => item.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<IReadOnlyList<ExtensionCapabilityManifest>> GetPluginCapabilitiesAsync(string packageId, CancellationToken cancellationToken)
    {
        var package = await GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Plugin package was not found.");
        if (package.Manifest.PackageType == ExtensionPackageType.Skill) throw new NotSupportedException("A Skill does not expose executable Plugin capabilities.");
        return package.Manifest.Capabilities.ToArray();
    }

    public async Task<string> InvokePluginAsync(
        string packageId,
        string capabilityId,
        string argumentsJson,
        ExtensionPermission authorisedPermissions,
        Guid executionId,
        Guid? parentActionId,
        CancellationToken cancellationToken)
    {
        var package = await GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Plugin package was not found.");
        if (!package.IsEnabled || package.State != ExtensionInstallState.Enabled)
            throw new InvalidOperationException("Plugin is not enabled in an available scope.");
        return await runtime.InvokeAsync(packageId, capabilityId, argumentsJson, authorisedPermissions, executionId, parentActionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstalledExtensionPackage> UpdatePackageAsync(string packageId, string? version, CancellationToken cancellationToken)
    {
        var package = await GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        var source = (await repository.GetSourcesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == package.SourceId)
            ?? throw new InvalidOperationException("The package source is no longer registered.");
        var candidates = await RefreshAsync(source.Id, cancellationToken).ConfigureAwait(false);
        var candidate = candidates.FirstOrDefault(item => item.Manifest.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
            (version is null || item.Manifest.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            ?? throw new KeyNotFoundException("The requested package update was not found in its source.");
        return await InstallAsync(candidate, package.GrantedPermissions, cancellationToken).ConfigureAwait(false);
    }

    public Task UninstallPackageAsync(string packageId, CancellationToken cancellationToken) => UninstallPackageByIdAsync(packageId, cancellationToken);

    private async Task UninstallPackageByIdAsync(string packageId, CancellationToken cancellationToken)
    {
        var package = await GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        await UninstallAsync(package.Id, cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveSourceAsync(Guid sourceId, CancellationToken cancellationToken) => repository.DeleteSourceAsync(sourceId, cancellationToken);

    public async Task AddSourceAsync(ExtensionSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Type == ExtensionSourceType.GitHubRepository && !IsGitHubUri(source.RepositoryUri))
            throw new ArgumentException("GitHub sources must use an HTTPS or SSH github.com repository URL.", nameof(source));
        if (source.IsPrivate && string.IsNullOrWhiteSpace(source.ConnectedAccountId))
            throw new InvalidOperationException("Private repositories require an authorised connected GitHub account reference.");
        await repository.UpsertSourceAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DiscoveredExtensionPackage>> RefreshAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var source = (await repository.GetSourcesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == sourceId)
            ?? throw new KeyNotFoundException("Extension source was not found.");
        var refreshRoot = Path.Combine(paths.DataDirectory, "extension-sources", source.Id.ToString("N"), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(refreshRoot);
        var materialized = await transport.MaterializeAsync(source, refreshRoot, cancellationToken).ConfigureAwait(false);
        var manifestPath = ManifestLocations.Select(location => Path.Combine(materialized, location.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists) ?? throw new InvalidDataException("Repository does not contain haven.repository.json or .haven/repository.json.");
        var document = JsonSerializer.Deserialize<ExtensionManifestDocument>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Repository manifest was empty.");
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
        var installed = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var dependencyErrors = ExtensionDependencyResolver.ValidateGraph(document.Packages, installed);
        var discovered = new List<DiscoveredExtensionPackage>(document.Packages.Count);
        foreach (var manifestPackage in document.Packages)
        {
            var package = manifestPackage;
            var packageRoot = ResolveInside(materialized, package.PackagePath);
            if (!Directory.Exists(packageRoot)) throw new InvalidDataException($"Package directory '{package.PackagePath}' does not exist.");
            var hash = await ExtensionPackageIntegrity.ComputeHashAsync(packageRoot, cancellationToken).ConfigureAwait(false);
            var provenance = package.Provenance ?? source.RepositoryUri;
            package = package with { Provenance = provenance };
            var current = installed.FirstOrDefault(item => item.Manifest.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
            var state = current is null ? ExtensionInstallState.Available
                : Version.TryParse(package.Version, out var available) && Version.TryParse(current.Manifest.Version, out var present) && available > present
                    ? ExtensionInstallState.UpdateAvailable : current.State;
            var packageErrors = dependencyErrors.GetValueOrDefault(package.PackageId) ?? [];
            var integrityMismatch = package.DeclaredContentHash is not null &&
                !package.DeclaredContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase);
            var unsignedVerifierUnavailable = package.Signature is not null;
            var safeError = integrityMismatch ? "Declared package hash does not match the materialized content." :
                unsignedVerifierUnavailable ? "Package declares a signature, but no trusted signature verifier is configured for this source." :
                packageErrors.Count > 0 ? string.Join(" ", packageErrors) : null;
            discovered.Add(new DiscoveredExtensionPackage(source.Id, package, materialized, hash,
                integrityMismatch || unsignedVerifierUnavailable ? ExtensionInstallState.Quarantined :
                    packageErrors.Count == 0 ? state : ExtensionInstallState.Incompatible,
                safeError));
        }
        await repository.UpsertSourceAsync(source with { LastRefreshedAt = DateTimeOffset.UtcNow, SafeLastError = null }, cancellationToken).ConfigureAwait(false);
        return discovered;
    }

    /// <summary>Stages package files and metadata without enabling code or granting any requested permissions.</summary>
    public Task<InstalledExtensionPackage> InstallAsync(DiscoveredExtensionPackage discovered, CancellationToken cancellationToken) =>
        InstallCoreAsync(discovered, ExtensionPermission.None, cancellationToken);

    /// <summary>Stages a package after a distinct caller permission review; execution remains disabled until SetEnabledAsync.</summary>
    public Task<InstalledExtensionPackage> InstallAsync(
        DiscoveredExtensionPackage discovered,
        ExtensionPermission explicitlyGrantedPermissions,
        CancellationToken cancellationToken) => InstallCoreAsync(discovered, explicitlyGrantedPermissions, cancellationToken);

    public Task<InstalledExtensionPackage> InstallSkillPackageAsync(DiscoveredExtensionPackage discovered, CancellationToken cancellationToken)
    {
        if (discovered.Manifest.PackageType != ExtensionPackageType.Skill) throw new ArgumentException("The selected package is not Skill-only.", nameof(discovered));
        return InstallAsync(discovered, cancellationToken);
    }

    public Task<InstalledExtensionPackage> InstallPluginPackageAsync(
        DiscoveredExtensionPackage discovered,
        ExtensionPermission explicitlyGrantedPermissions,
        CancellationToken cancellationToken)
    {
        if (discovered.Manifest.PackageType == ExtensionPackageType.Skill) throw new ArgumentException("A Skill package does not expose Plugin actions.", nameof(discovered));
        return InstallAsync(discovered, explicitlyGrantedPermissions, cancellationToken);
    }

    private async Task<InstalledExtensionPackage> InstallCoreAsync(
        DiscoveredExtensionPackage discovered,
        ExtensionPermission explicitlyGrantedPermissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        if (discovered.State is ExtensionInstallState.Incompatible or ExtensionInstallState.Quarantined)
            throw new InvalidDataException(discovered.SafeError ?? "Package dependencies or compatibility requirements are not satisfied.");
        if (explicitlyGrantedPermissions != discovered.Manifest.RequestedPermissions)
            throw new UnauthorizedAccessException("All requested package permissions must be reviewed and granted explicitly.");
        var installed = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var existing = installed.FirstOrDefault(item => item.Manifest.PackageId.Equals(discovered.Manifest.PackageId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!Version.TryParse(discovered.Manifest.Version, out var offeredVersion) || !Version.TryParse(existing.Manifest.Version, out var currentVersion) || offeredVersion <= currentVersion)
                throw new InvalidOperationException("Package updates must advance to a valid newer version.");
        }
        if (existing is not null)
        {
            var currentHash = Directory.Exists(existing.InstallPath)
                ? await ExtensionPackageIntegrity.ComputeHashAsync(existing.InstallPath, cancellationToken).ConfigureAwait(false) : string.Empty;
            if (existing.HasLocalModifications || !string.Equals(currentHash, existing.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installed package has local modifications and will not be overwritten.");
            var newScopes = discovered.Manifest.RequestedPermissions & ~existing.GrantedPermissions;
            if ((newScopes & ~explicitlyGrantedPermissions) != ExtensionPermission.None)
                throw new UnauthorizedAccessException("The update requests broader permissions and requires a separate permission review.");
            await runtime.UnloadAsync(existing.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        }
        var packageSource = ResolveInside(discovered.MaterializedRepositoryPath, discovered.Manifest.PackagePath);
        var destination = Path.Combine(paths.DataDirectory, "extensions", discovered.Manifest.PackageId, discovered.Manifest.Version);
        var staging = destination + ".installing-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        var movedIntoStore = false;
        var loadedNewPackage = false;
        try
        {
            CopyDirectory(packageSource, staging);
            var stagedHash = await ExtensionPackageIntegrity.ComputeHashAsync(staging, cancellationToken).ConfigureAwait(false);
            if (!stagedHash.Equals(discovered.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package contents changed during installation.");
            if (Directory.Exists(destination)) throw new IOException("This package version is already installed.");
            Directory.Move(staging, destination);
            movedIntoStore = true;
            var now = DateTimeOffset.UtcNow;
            var isEnabled = existing?.IsEnabled ?? false;
            var effectiveGrants = existing is null ? explicitlyGrantedPermissions : existing.GrantedPermissions | (explicitlyGrantedPermissions & discovered.Manifest.RequestedPermissions);
            var package = new InstalledExtensionPackage(
                existing?.Id ?? Guid.NewGuid(), discovered.SourceId, discovered.Manifest, destination,
                effectiveGrants, isEnabled ? ExtensionInstallState.Enabled : ExtensionInstallState.Installed, isEnabled, false,
                discovered.ContentHash, existing?.InstalledAt ?? now, now);
            if (package.IsEnabled)
            {
                await runtime.LoadAsync(package, cancellationToken).ConfigureAwait(false);
                loadedNewPackage = true;
            }
            await repository.UpsertInstalledAsync(package, cancellationToken).ConfigureAwait(false);
            return package;
        }
        catch
        {
            if (loadedNewPackage)
                try { await runtime.UnloadAsync(discovered.Manifest.PackageId, CancellationToken.None).ConfigureAwait(false); } catch { }
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (movedIntoStore && Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            if (existing is { IsEnabled: true })
                try { await runtime.LoadAsync(existing, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public Task SetEnabledAsync(Guid packageId, bool enabled, CancellationToken cancellationToken) =>
        SetEnabledAsync(packageId, enabled, "device", cancellationToken);

    public async Task SetEnabledAsync(Guid packageId, bool enabled, string scope, CancellationToken cancellationToken)
    {
        scope = NormalizeScope(scope);
        var package = (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == packageId)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        var enabledScopes = (package.EnabledScopes ?? (package.IsEnabled ? [package.EnablementScope] : []))
            .Where(item => !string.Equals(item, scope, StringComparison.OrdinalIgnoreCase)).ToList();
        if (enabled) enabledScopes.Add(scope);
        enabledScopes.Sort(StringComparer.Ordinal);
        var updated = package with
        {
            IsEnabled = enabledScopes.Count > 0,
            EnabledScopes = enabledScopes,
            EnablementScope = scope,
            State = enabledScopes.Count > 0 ? ExtensionInstallState.Enabled : ExtensionInstallState.Disabled,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (updated.IsEnabled)
        {
            var installed = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
            var dependencyErrors = ExtensionDependencyResolver.ValidateGraph(
                [package.Manifest], installed.Where(item => item.Id != package.Id).ToArray());
            if (dependencyErrors.TryGetValue(package.Manifest.PackageId, out var errors))
                throw new InvalidOperationException("Package cannot be enabled: " + string.Join(" ", errors));
            if (!package.IsEnabled) await runtime.LoadAsync(updated, cancellationToken).ConfigureAwait(false);
            try { await repository.UpsertInstalledAsync(updated, cancellationToken).ConfigureAwait(false); }
            catch
            {
                if (!package.IsEnabled) try { await runtime.UnloadAsync(package.Manifest.PackageId, CancellationToken.None).ConfigureAwait(false); } catch { }
                throw;
            }
            return;
        }

        await runtime.UnloadAsync(package.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        try { await repository.UpsertInstalledAsync(updated, cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (package.IsEnabled)
                try { await runtime.LoadAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public async Task SetGrantedPermissionsAsync(Guid packageId, ExtensionPermission grantedPermissions, CancellationToken cancellationToken)
    {
        var package = (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == packageId)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        if ((grantedPermissions & ~package.Manifest.RequestedPermissions) != ExtensionPermission.None)
            throw new UnauthorizedAccessException("A Plugin cannot receive permissions it did not declare.");
        if (grantedPermissions == package.GrantedPermissions) return;
        var updated = package with { GrantedPermissions = grantedPermissions, UpdatedAt = DateTimeOffset.UtcNow };
        if (package.IsEnabled) await runtime.UnloadAsync(package.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.UpsertInstalledAsync(updated, cancellationToken).ConfigureAwait(false);
            if (updated.IsEnabled) await runtime.LoadAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (updated.IsEnabled) try { await runtime.UnloadAsync(updated.Manifest.PackageId, CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await repository.UpsertInstalledAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            if (package.IsEnabled) try { await runtime.LoadAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public async Task<IReadOnlyList<ExtensionSkillCatalogEntry>> ListSkillsAsync(CancellationToken cancellationToken)
    {
        var packages = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        return packages.SelectMany(package => package.Manifest.Skills.Select(skill => new ExtensionSkillCatalogEntry(
                StableSkillId(package.Manifest.PackageId, skill.Id), package.Manifest.PackageId, package.Manifest.Version,
                skill.DisplayName, skill.Description, package.State,
                package.IsEnabled && (package.EnabledScopes ?? [package.EnablementScope]).Count > 0,
                package.SkillEnablementScopes?.GetValueOrDefault(skill.Id) ?? [])))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SkillId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ExtensionSkillCatalogEntry?> GetSkillAsync(string skillId, CancellationToken cancellationToken) =>
        (await ListSkillsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.SkillId.Equals(skillId, StringComparison.OrdinalIgnoreCase));

    public Task EnableSkillAsync(string skillId, string scope, CancellationToken cancellationToken) =>
        SetSkillEnabledAsync(skillId, scope, true, cancellationToken);

    public Task DisableSkillAsync(string skillId, string scope, CancellationToken cancellationToken) =>
        SetSkillEnabledAsync(skillId, scope, false, cancellationToken);

    public async Task<InstalledExtensionPackage> UpdateSkillAsync(string skillId, string? version, CancellationToken cancellationToken)
    {
        var skill = await GetSkillAsync(skillId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Installed Skill was not found.");
        return await UpdatePackageAsync(skill.PackageId, version, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSkillEnabledAsync(string skillId, string scope, bool enabled, CancellationToken cancellationToken)
    {
        scope = NormalizeScope(scope);
        var packages = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var package = packages.FirstOrDefault(item => item.Manifest.Skills.Any(skill => StableSkillId(item.Manifest.PackageId, skill.Id).Equals(skillId, StringComparison.OrdinalIgnoreCase)))
            ?? throw new KeyNotFoundException("Skill was not found.");
        var skill = package.Manifest.Skills.First(item => StableSkillId(package.Manifest.PackageId, item.Id).Equals(skillId, StringComparison.OrdinalIgnoreCase));
        var scopeMap = package.SkillEnablementScopes?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var scopes = (scopeMap.GetValueOrDefault(skill.Id) ?? []).Where(item => !item.Equals(scope, StringComparison.OrdinalIgnoreCase)).ToList();
        if (enabled) scopes.Add(scope);
        scopes.Sort(StringComparer.Ordinal);
        if (scopes.Count == 0) scopeMap.Remove(skill.Id); else scopeMap[skill.Id] = scopes;
        await repository.UpsertInstalledAsync(package with { SkillEnablementScopes = scopeMap, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UninstallSkillAsync(string skillId, CancellationToken cancellationToken)
    {
        var packages = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var package = packages.FirstOrDefault(item => item.Manifest.Skills.Any(skill => StableSkillId(item.Manifest.PackageId, skill.Id).Equals(skillId, StringComparison.OrdinalIgnoreCase)))
            ?? throw new KeyNotFoundException("Skill was not found.");
        if (package.Manifest.PackageType == ExtensionPackageType.Skill && package.Manifest.Skills.Count == 1)
        {
            await UninstallAsync(package.Id, cancellationToken).ConfigureAwait(false);
            return;
        }
        var remaining = package.Manifest.Skills.Where(skill => !StableSkillId(package.Manifest.PackageId, skill.Id).Equals(skillId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var type = remaining.Length == 0 ? ExtensionPackageType.Plugin : package.Manifest.PackageType;
        var updated = package with
        {
            Manifest = package.Manifest with { Skills = remaining, PackageType = type },
            SkillEnablementScopes = package.SkillEnablementScopes?.Where(pair => remaining.Any(skill => skill.Id.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (package.IsEnabled) await runtime.UnloadAsync(package.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.UpsertInstalledAsync(updated, cancellationToken).ConfigureAwait(false);
            if (updated.IsEnabled) await runtime.LoadAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await repository.UpsertInstalledAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            if (package.IsEnabled) try { await runtime.LoadAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static string StableSkillId(string packageId, string skillId) => packageId + "/" + skillId;

    private static string NormalizeScope(string scope)
    {
        var value = scope?.Trim() ?? string.Empty;
        if (value.Equals("device", StringComparison.OrdinalIgnoreCase) || value.Equals("user", StringComparison.OrdinalIgnoreCase))
            return value.ToLowerInvariant();
        foreach (var prefix in new[] { "space:", "agent:" })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value[prefix.Length..], out var id))
                return prefix + id.ToString("N");
        throw new ArgumentException("Scope must be device, user, space:<stable-id>, or agent:<stable-id>.", nameof(scope));
    }

    public async Task UninstallAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var package = (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == packageId)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        var extensionRoot = Path.GetFullPath(Path.Combine(paths.DataDirectory, "extensions")) + Path.DirectorySeparatorChar;
        var installPath = Path.GetFullPath(package.InstallPath);
        if (!installPath.StartsWith(extensionRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installed package path is outside Haven's extension store.");
        var quarantine = installPath + ".uninstalling-" + Guid.NewGuid().ToString("N");
        var moved = false;
        await runtime.UnloadAsync(package.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(installPath))
            {
                Directory.Move(installPath, quarantine);
                moved = true;
            }
            await repository.DeleteInstalledAsync(package.Id, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (moved && Directory.Exists(quarantine) && !Directory.Exists(installPath)) Directory.Move(quarantine, installPath);
            if (package.IsEnabled)
                try { await runtime.LoadAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
        if (moved && Directory.Exists(quarantine))
            try { Directory.Delete(quarantine, recursive: true); } catch { /* Renamed package remains inert and can be cleaned on maintenance. */ }
    }

    private static bool IsGitHubUri(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        ? uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Scheme == Uri.UriSchemeHttps
        : value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase);

    private static string ResolveInside(string root, string relative)
    {
        var resolvedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escaped the repository.");
        return resolved;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked directories.");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked files.");
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

}
