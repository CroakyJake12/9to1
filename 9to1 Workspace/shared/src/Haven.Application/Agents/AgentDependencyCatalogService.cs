using System.Collections.Frozen;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Exact saved dependency declarations and the current host's extension enablement scope.
/// Neither these strings nor a successful lookup grant invocation or resource authority.</summary>
public sealed record AgentDependencyRequest(IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> SkillIds, IReadOnlyList<string> PluginIds,
    IReadOnlyList<string> McpCapabilityIds, string Scope);

public sealed record AgentDependencyDiagnostic(string Code, string Target, string Message);
public sealed record AgentPackageObservation(string PackageId, string Version, string ContentHash,
    DateTimeOffset UpdatedAt, string Scope);

/// <summary>Current registry observations only. Home must independently authorize the actual
/// actor, canonical Agent/run/step, model and each original tool call immediately before work.</summary>
public sealed record AgentDependencyLookup(AgentDependencyRequest Request,
    IReadOnlyList<CapabilityDefinition> Capabilities, IReadOnlyList<ResolvedSkill> Skills,
    IReadOnlyList<AgentPackageObservation> Packages, IReadOnlyList<AgentDependencyDiagnostic> Diagnostics)
{
    public bool DependenciesResolved => Diagnostics.Count == 0;
}

/// <summary>Uses the existing platform capability catalogue, installed extension store and
/// maintained Skill resolver. It does not create a registry, activate plugins or mint permissions.</summary>
public sealed class AgentDependencyCatalogService(CapabilityRegistryService capabilities,
    IExtensionRepository? extensions = null, NativePluginRuntime? plugins = null)
{
    public async Task<AgentDependencyLookup> ResolveCurrentAsync(AgentDependencyRequest request,
        CapabilityPlatform platform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<AgentDependencyDiagnostic>();
        var captured = Capture(request, diagnostics);
        if (platform is not (CapabilityPlatform.Windows or CapabilityPlatform.Android or
            CapabilityPlatform.Linux or CapabilityPlatform.MacOS or CapabilityPlatform.iOS))
            diagnostics.Add(new("UnsupportedConcretePlatform", "platform",
                "The existing registry has no supported concrete value for this host platform."));
        if (diagnostics.Count != 0) return Result(captured, [], [], [], diagnostics);

        var current = await capabilities.DiscoverAsync(platform, cancellationToken).ConfigureAwait(false);
        var selected = new Dictionary<Guid, CapabilityDefinition>();
        foreach (var id in captured.ToolIds) ResolveCapability(id, false, current, selected, diagnostics);
        foreach (var id in captured.McpCapabilityIds) ResolveCapability(id, true, current, selected, diagnostics);

        IReadOnlyList<InstalledExtensionPackage> installed = extensions is null ? [] :
            await extensions.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var packageInputs = new Dictionary<string, InstalledExtensionPackage>(StringComparer.Ordinal);
        foreach (var id in captured.PluginIds)
        {
            var package = FindPackage(id, captured.Scope, installed, diagnostics);
            if (package is not null)
            {
                if (package.Manifest.PackageType == ExtensionPackageType.Skill)
                    diagnostics.Add(new("PluginPackageRequired", id, "The selected package defines Skills without a plugin."));
                else packageInputs.TryAdd(package.Manifest.PackageId, package);
            }
        }
        foreach (var id in captured.SkillIds)
        {
            var separator = id.IndexOf('/');
            if (separator <= 0 || separator == id.Length - 1)
            { diagnostics.Add(new("ExactSkillIdentityRequired", id, "A Skill requires its canonical PackageID/SkillID.")); continue; }
            var package = FindPackage(id[..separator], captured.Scope, installed, diagnostics);
            if (package is null) continue;
            var key = id[(separator + 1)..];
            if (package.Manifest.Skills.Count(skill => skill.Id == key) != 1)
                diagnostics.Add(new("SkillNotFound", id, "The exact canonical Skill is not present in this installed package."));
            else packageInputs.TryAdd(package.Manifest.PackageId, package);
        }

        var packageFingerprints = packageInputs.ToDictionary(value => value.Key,
            value => PackageFingerprint(value.Value), StringComparer.Ordinal);
        foreach (var package in packageInputs.Values)
        {
            try { await ExtensionPackageIntegrity.VerifyInstalledAsync(package, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { diagnostics.Add(new("PackageIntegrityUnavailable", package.Manifest.PackageId,
                "The installed package's current content integrity could not be verified.")); }
        }
        IReadOnlyList<ResolvedSkill> skills = [];
        if (captured.SkillIds.Count != 0)
        {
            if (plugins is null)
                diagnostics.Add(new("SkillRuntimeUnavailable", "skills", "The maintained current Skill resolver is unavailable."));
            else if (diagnostics.Count == 0)
            {
                // These are canonical capability keys from discovery; they are not permission keys.
                var resolution = await plugins.ResolveSkillsForAsync(new(captured.SkillIds, captured.Scope,
                    selected.Values.Select(value => value.Key).ToArray()), cancellationToken).ConfigureAwait(false);
                if (!resolution.Succeeded)
                    diagnostics.Add(new(resolution.ErrorCode ?? "SkillResolutionUnavailable", "skills",
                        "The maintained resolver could not resolve the selected Skills in this current scope."));
                else if (resolution.Skills.Count != captured.SkillIds.Count ||
                    !resolution.Skills.Select(value => value.SkillId).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(captured.SkillIds))
                    diagnostics.Add(new("SkillIdentityChanged", "skills", "The current Skill result differs from the exact saved identities."));
                else if (resolution.Skills.Any(value =>
                    !packageInputs.TryGetValue(value.PackageId, out var originalPackage) ||
                    value.PackageVersion != originalPackage.Manifest.Version ||
                    !value.SkillId.StartsWith(value.PackageId + "/", StringComparison.Ordinal) ||
                    value.OriginalPackageFingerprint != packageFingerprints[value.PackageId]))
                    diagnostics.Add(new("SkillPackageChanged", "skills",
                        "The maintained Skill resolver selected a different original package source, content, version or scope."));
                else skills = Array.AsReadOnly(resolution.Skills.Select(value => value with
                {
                    CapabilityIds = Array.AsReadOnly(value.CapabilityIds.ToArray()),
                    Resources = value.Resources.ToFrozenDictionary(StringComparer.Ordinal)
                }).ToArray());
            }
        }

        // Discovery/installation can change during package I/O. A successful lookup never
        // returns stale metadata merely because it was valid before those awaits.
        var final = await capabilities.DiscoverAsync(platform, cancellationToken).ConfigureAwait(false);
        var finalSelected = new Dictionary<Guid, CapabilityDefinition>();
        foreach (var id in captured.ToolIds) ResolveCapability(id, false, final, finalSelected, diagnostics);
        foreach (var id in captured.McpCapabilityIds) ResolveCapability(id, true, final, finalSelected, diagnostics);
        foreach (var value in selected.Values)
            if (!finalSelected.TryGetValue(value.Id, out var observed) || observed != value)
                diagnostics.Add(new("CapabilityChanged", value.Key,
                    "A selected canonical capability changed or disappeared during lookup."));
        if (packageInputs.Count != 0)
        {
            var finalPackages = await extensions!.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
            foreach (var package in packageInputs.Values)
            {
                var matches = finalPackages.Where(value => value.Manifest.PackageId == package.Manifest.PackageId).ToArray();
                if (matches.Length != 1 || PackageFingerprint(matches[0]) != packageFingerprints[package.Manifest.PackageId])
                    diagnostics.Add(new("PackageChanged", package.Manifest.PackageId,
                        "The installed package or its current enablement changed during lookup."));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Result(captured, selected.Values.OrderBy(value => value.Id).ToArray(), skills,
            packageInputs.Values.OrderBy(value => value.Manifest.PackageId, StringComparer.Ordinal)
                .Select(value => new AgentPackageObservation(value.Manifest.PackageId, value.Manifest.Version,
                    value.ContentHash, value.UpdatedAt, captured.Scope)).ToArray(), diagnostics);
    }

    private static AgentDependencyRequest Capture(AgentDependencyRequest request,
        List<AgentDependencyDiagnostic> diagnostics)
    {
        IReadOnlyList<string> Copy(IReadOnlyList<string>? ids, string target)
        {
            var values = ids?.ToArray() ?? [];
            if (ids is null || values.Length > 128 || values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
                values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || value != value.Trim() || value.Any(char.IsControl)))
                diagnostics.Add(new("InvalidDependencyDeclaration", target, "Dependency IDs must be exact, unique and bounded canonical identities."));
            return Array.AsReadOnly(values);
        }
        if (!ValidScope(request.Scope)) diagnostics.Add(new("InvalidCurrentScope", "scope",
            "Select an exact maintained device, user, space or Agent enablement scope."));
        return new(Copy(request.ToolIds, "tools"), Copy(request.SkillIds, "skills"),
            Copy(request.PluginIds, "plugins"), Copy(request.McpCapabilityIds, "mcp"), request.Scope);
    }

    private static bool ValidScope(string? scope)
    {
        if (scope is "device" or "user") return true;
        if (scope is null) return false;
        foreach (var prefix in new[] { "space:", "agent:" })
            if (scope.StartsWith(prefix, StringComparison.Ordinal) &&
                Guid.TryParseExact(scope[prefix.Length..], "N", out var id) && id != Guid.Empty &&
                scope == prefix + id.ToString("N")) return true;
        return false;
    }

    private static void ResolveCapability(string id, bool requireMcp,
        IReadOnlyList<CapabilityDefinition> current, Dictionary<Guid, CapabilityDefinition> selected,
        List<AgentDependencyDiagnostic> diagnostics)
    {
        var matches = current.Where(value => value.Id.ToString("D") == id || value.Key == id || value.ImplementationKey == id).ToArray();
        if (matches.Length != 1)
        { diagnostics.Add(new(matches.Length == 0 ? "CapabilityNotFound" : "AmbiguousCapabilityIdentity", id,
            "The declaration must identify exactly one current canonical capability.")); return; }
        var match = matches[0];
        if (!match.IsAgentUsable || !match.IsEnabled ||
            match.Availability is not (CapabilityAvailability.Available or CapabilityAvailability.PermissionRequired))
        { diagnostics.Add(new("CapabilityUnavailable", id, "The selected capability is unavailable to the current Agent runtime.")); return; }
        if (requireMcp && (match.ImplementationKey != "connection.mcp" ||
            match.Key != ExternalConnectionNaming.CapabilityKey(match.Id)))
        { diagnostics.Add(new("McpCapabilityRequired", id, "This declaration does not identify a configured canonical MCP connection capability.")); return; }
        selected.TryAdd(match.Id, match);
    }

    private static InstalledExtensionPackage? FindPackage(string id, string scope,
        IReadOnlyList<InstalledExtensionPackage> installed, List<AgentDependencyDiagnostic> diagnostics)
    {
        var matches = installed.Where(value => value.Manifest.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Manifest.PackageId != id)
        { diagnostics.Add(new("PackageIdentityUnavailable", id, "An exact unique canonical installed PackageID is required.")); return null; }
        var package = matches[0];
        var scopes = package.EnabledScopes ?? (package.IsEnabled ? [package.EnablementScope] : []);
        if (!package.IsEnabled || package.State is not (ExtensionInstallState.Installed or ExtensionInstallState.Enabled or ExtensionInstallState.UpdateAvailable) ||
            !scopes.Contains(scope, StringComparer.Ordinal) ||
            (package.GrantedPermissions & ~package.Manifest.RequestedPermissions) != ExtensionPermission.None)
        { diagnostics.Add(new("PackageUnavailableInScope", id, "The installed package is not currently enabled with valid grants in this exact scope.")); return null; }
        return package;
    }

    private static string PackageFingerprint(InstalledExtensionPackage package) => JsonSerializer.Serialize(package);

    private static AgentDependencyLookup Result(AgentDependencyRequest request,
        IReadOnlyList<CapabilityDefinition> capabilities, IReadOnlyList<ResolvedSkill> skills,
        IReadOnlyList<AgentPackageObservation> packages, IReadOnlyList<AgentDependencyDiagnostic> diagnostics) =>
        new(request, Array.AsReadOnly(capabilities.ToArray()), Array.AsReadOnly(skills.ToArray()),
            Array.AsReadOnly(packages.ToArray()), Array.AsReadOnly(diagnostics.ToArray()));
}
