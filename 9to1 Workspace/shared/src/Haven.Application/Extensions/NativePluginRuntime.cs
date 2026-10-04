using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Host-owned runtime that registers plugin capabilities into Haven's authoritative registry
/// and executes them only through a permission-checked, cancellable process boundary.
/// </summary>
public sealed class NativePluginRuntime(
    ICapabilityRepository capabilities,
    ICatalogRepository catalog,
    INativePluginProcessFactory processFactory,
    IExecutionEventSink events,
    IExtensionRepository? extensions = null) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, (InstalledExtensionPackage Package, INativePluginProcess Process)> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task LoadAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        EnsurePermissionGrantValid(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded.ContainsKey(package.Manifest.PackageId)) return;
            var process = processFactory.Create(package);
            try
            {
                await process.StartAsync(cancellationToken).ConfigureAwait(false);
                await RegisterPackageAsync(package, cancellationToken).ConfigureAwait(false);
                if (!_loaded.TryAdd(package.Manifest.PackageId, (package, process)))
                    throw new InvalidOperationException("Plugin was loaded concurrently.");
            }
            catch
            {
                try { await DisablePackageRegistrationsAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
                await process.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Resolves only caller-selected Skills enabled for the requested scope; Skills never gain code execution.</summary>
    public async Task<SkillResolutionResult> ResolveSkillsForAsync(SkillResolutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (extensions is null) return new(false, [], [], "EXTENSION_STORE_UNAVAILABLE", "Skill registry is unavailable.");
        if (!TryNormalizeScope(request.Scope, out var scope)) return new(false, [], [], "INVALID_SCOPE", "Skill scope is invalid.");
        var requested = request.RequestedSkillIds ?? [];
        if (requested.Count == 0) return new(true, [], []);
        if (requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count)
            return new(false, [], [], "DUPLICATE_SKILL", "A Skill was selected more than once.");

        var installed = await extensions.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var availableCapabilities = new HashSet<string>(request.AvailableCapabilityIds ?? [], StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ResolvedSkill>(requested.Count);
        var conflicts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in requested)
        {
            var separator = identity.IndexOf('/');
            if (separator <= 0 || separator == identity.Length - 1)
                return new(false, [], [], "INVALID_SKILL_ID", "Skill identity must be PackageID/SkillID.");
            var packageId = identity[..separator];
            var skillKey = identity[(separator + 1)..];
            var package = installed.FirstOrDefault(item => item.Manifest.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
            var skill = package?.Manifest.Skills.FirstOrDefault(item => item.Id.Equals(skillKey, StringComparison.OrdinalIgnoreCase));
            if (package is null || skill is null) return new(false, [], [], "SKILL_NOT_FOUND", "A selected Skill is not installed.");
            var packageScopes = package.EnabledScopes ?? (package.IsEnabled ? [package.EnablementScope] : []);
            if (!package.IsEnabled || !packageScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                return new(false, [], [], "SKILL_DISABLED", "A selected Skill's package is not enabled for this scope.");
            var skillScopes = package.SkillEnablementScopes?.GetValueOrDefault(skill.Id) ?? [];
            if (!skillScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                return new(false, [], [], "SKILL_DISABLED", "A selected Skill is not enabled for this scope.");
            var missingCapability = (skill.RequiredCapabilityIds ?? []).FirstOrDefault(item => !availableCapabilities.Contains(item));
            if (missingCapability is not null)
                return new(false, [], [], "SKILL_CAPABILITY_UNAVAILABLE", $"Skill '{identity}' requires an unavailable capability.");
            try { await ExtensionPackageIntegrity.VerifyInstalledAsync(package, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException) { return new(false, [], [], "SKILL_PACKAGE_INTEGRITY_FAILED", "A selected Skill package failed integrity validation."); }

            foreach (var conflictKey in skill.ConflictKeys ?? [])
                if (conflicts.TryGetValue(conflictKey, out var conflictingSkill))
                    return new(false, [], [], "CONFLICT", $"Skills '{conflictingSkill}' and '{identity}' conflict on a declared policy key.");
                else conflicts[conflictKey] = identity;

            var instructionsPath = ResolvePackageFile(package.InstallPath, skill.InstructionPath);
            EnsureNoLinkedPath(package.InstallPath, instructionsPath);
            var info = new FileInfo(instructionsPath);
            if (!info.Exists || info.Length > 1_000_000) return new(false, [], [], "SKILL_RESOURCE_INVALID", "Skill instructions are missing or exceed the supported size.");
            var instructions = await File.ReadAllTextAsync(instructionsPath, cancellationToken).ConfigureAwait(false);
            var resources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var resourcePath in skill.ResourcePaths ?? [])
            {
                var path = ResolvePackageFile(package.InstallPath, resourcePath);
                EnsureNoLinkedPath(package.InstallPath, path);
                var resource = new FileInfo(path);
                if (!resource.Exists || resource.Length > 1_000_000) return new(false, [], [], "SKILL_RESOURCE_INVALID", "A Skill resource is missing or exceeds the supported size.");
                resources[resourcePath] = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            resolved.Add(new ResolvedSkill(identity, package.Manifest.PackageId, package.Manifest.Version,
                skill.DisplayName, instructions, skill.WorkflowJson, skill.ContextRulesJson, resources,
                (skill.RequiredCapabilityIds ?? []).ToArray()));
        }
        var ordered = resolved.OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.SkillId, StringComparer.Ordinal).ToArray();
        var provenance = ordered.Select(item => $"{item.PackageId}@{item.PackageVersion}/{item.SkillId[(item.PackageId.Length + 1)..]}").ToArray();
        return new SkillResolutionResult(true, ordered, provenance);
    }

    public async Task<string> InvokeAsync(
        string packageId,
        string capabilityId,
        string argumentsJson,
        ExtensionPermission authorisedPermissions,
        Guid executionId,
        Guid? parentActionId,
        CancellationToken cancellationToken)
    {
        (InstalledExtensionPackage Package, INativePluginProcess Process) loaded;
        if (!_loaded.TryGetValue(packageId, out loaded)) throw new InvalidOperationException("Plugin is not loaded.");
        var manifest = loaded.Package.Manifest.Capabilities.FirstOrDefault(value => value.Id.Equals(capabilityId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Plugin capability was not declared.");
        var actionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
            ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Running,
            manifest.DisplayName, "The selected plugin capability matches the requested action.", null,
            packageId, now, now));
        try
        {
            if ((manifest.RequiredPermissions & ~authorisedPermissions) != 0 ||
                (manifest.RequiredPermissions & ~loaded.Package.GrantedPermissions) != 0)
                throw new UnauthorizedAccessException("The capability requires permissions that are not currently granted.");
            using var inputDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (!ExtensionJsonSchemaValidator.Validate(manifest.InputSchemaJson, inputDocument.RootElement, out _))
                throw new ArgumentException("Plugin arguments do not match the declared input schema.", nameof(argumentsJson));
            var result = await loaded.Process.InvokeAsync(capabilityId, SensitiveTextRedactor.Redact(argumentsJson, 16_000), cancellationToken).ConfigureAwait(false);
            if (manifest.OutputSchemaJson is not null)
            {
                using var outputDocument = JsonDocument.Parse(result);
                if (!ExtensionJsonSchemaValidator.Validate(manifest.OutputSchemaJson, outputDocument.RootElement, out _))
                    throw new InvalidDataException("Plugin output did not match the declared output schema.");
            }
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
                ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Completed,
                manifest.DisplayName, null, SensitiveTextRedactor.Redact(result, 8_000), packageId,
                DateTimeOffset.UtcNow, now, DateTimeOffset.UtcNow));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
                ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Cancelled,
                manifest.DisplayName, null, "Plugin action was cancelled.", packageId, DateTimeOffset.UtcNow, now, DateTimeOffset.UtcNow));
            throw;
        }
        catch (Exception ex)
        {
            var failure = new ExecutionFailure("PLUGIN_CALL_FAILED", "Plugin call failed", $"Plugin runtime reported {ex.GetType().Name}; details were withheld from the execution log.");
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
                ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Failed,
                manifest.DisplayName, null, failure.Message, packageId, DateTimeOffset.UtcNow, now, DateTimeOffset.UtcNow, Failure: failure));
            throw;
        }
    }

    public async Task UnloadAsync(string packageId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded.TryRemove(packageId, out var loaded)) return;
            Exception? stopFailure = null;
            try { await loaded.Process.StopAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { stopFailure = ex; }
            try { await loaded.Process.DisposeAsync().ConfigureAwait(false); }
            finally { await DisablePackageRegistrationsAsync(loaded.Package, CancellationToken.None).ConfigureAwait(false); }
            if (stopFailure is not null) throw stopFailure;
        }
        finally { _gate.Release(); }
    }

    private async Task RegisterPackageAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        foreach (var item in package.Manifest.Capabilities)
        {
            var risk = item.RiskClassification switch
            {
                "read-only" => CapabilityRiskClass.ReadOnly,
                "low" => CapabilityRiskClass.Low,
                "restricted" => CapabilityRiskClass.Restricted,
                _ => CapabilityRiskClass.Consequential
            };
            var availability = package.IsEnabled
                ? (item.RequiredPermissions & ~package.GrantedPermissions) == ExtensionPermission.None ? CapabilityAvailability.Available : CapabilityAvailability.PermissionRequired
                : CapabilityAvailability.DependencyRequired;
            await capabilities.UpsertCapabilityAsync(new CapabilityDefinition(
                StableId(package.Manifest.PackageId, "capability:" + item.Id), $"extension.{package.Manifest.PackageId}.{item.Id}",
                item.DisplayName, item.Description, "plugins", "plugin", JsonSerializer.Serialize(new PluginCapabilityToolDescriptor(
                    item.InputSchemaJson, item.OutputSchemaJson, item.RequiredPermissions, item.RiskClassification)),
                $"native-plugin:{package.Manifest.PackageId}:{item.Id}", JsonSerializer.Serialize(item.SemanticActions),
                CapabilityPlatform.All, risk, availability, JsonSerializer.Serialize(ExtensionDependencyResolver.EffectiveDependencies(package.Manifest)),
                package.Manifest.PackageId, true, true, false, package.IsEnabled, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        }
        foreach (var item in package.Manifest.Skills)
        {
            var path = Path.GetFullPath(Path.Combine(package.InstallPath, item.InstructionPath));
            var root = Path.GetFullPath(package.InstallPath) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Skill path escaped the installed package.");
            EnsureNoLinkedPath(package.InstallPath, path);
            if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("Skill instructions exceed Haven's one-megabyte package limit.");
            var instructions = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            await catalog.UpsertPromptAsync(new PromptDefinition(
                StableId(package.Manifest.PackageId, "skill:" + item.Id), item.DisplayName, item.Description,
                "sparkles", instructions, true, false, false,
                DateTimeOffset.UtcNow, IsAgentic: true), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DisablePackageRegistrationsAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        foreach (var manifest in package.Manifest.Capabilities)
            await capabilities.SetCapabilityEnabledAsync(StableId(package.Manifest.PackageId, "capability:" + manifest.Id), false, cancellationToken).ConfigureAwait(false);
        foreach (var skill in package.Manifest.Skills)
            await catalog.SetPromptEnabledAsync(StableId(package.Manifest.PackageId, "skill:" + skill.Id), false, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsurePermissionGrantValid(InstalledExtensionPackage package)
    {
        if ((package.GrantedPermissions & ~package.Manifest.RequestedPermissions) != 0)
            throw new InvalidOperationException("Granted permissions exceed the package request.");
    }

    private static Guid StableId(string packageId, string childId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(packageId + "\n" + childId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void EnsureNoLinkedPath(string root, string path)
    {
        var current = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Installed extension roots cannot be linked directories.");
        foreach (var segment in Path.GetRelativePath(current, path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Installed extensions cannot execute or load linked content.");
        }
    }

    private static string ResolvePackageFile(string root, string relativePath)
    {
        var resolvedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Skill resource path escaped its package.");
        return resolved;
    }

    private static bool TryNormalizeScope(string scope, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(scope)) return false;
        var value = scope.Trim();
        if (value.Equals("device", StringComparison.OrdinalIgnoreCase) || value.Equals("user", StringComparison.OrdinalIgnoreCase))
        { normalized = value.ToLowerInvariant(); return true; }
        foreach (var prefix in new[] { "space:", "agent:" })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value[prefix.Length..], out var id))
            { normalized = prefix + id.ToString("N"); return true; }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _loaded.Keys.ToArray())
            try { await UnloadAsync(key, CancellationToken.None).ConfigureAwait(false); } catch { }
        _gate.Dispose();
    }
}

/// <summary>Metadata carried by the capability registry so the model tool schema matches the installed manifest.</summary>
public sealed record PluginCapabilityToolDescriptor(
    string InputSchemaJson,
    string? OutputSchemaJson,
    ExtensionPermission RequiredPermissions,
    string RiskClassification);
