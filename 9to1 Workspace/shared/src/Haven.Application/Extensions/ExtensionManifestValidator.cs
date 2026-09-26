using System.Text.RegularExpressions;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed record ExtensionManifestValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>Validates multi-package repository manifests before any executable content is installed.</summary>
public sealed partial class ExtensionManifestValidator
{
    public ExtensionManifestValidationResult Validate(ExtensionManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (document.SchemaVersion != 1) errors.Add($"Unsupported manifest schema version {document.SchemaVersion}.");
        ValidateUnknownRequiredFields(document.ExtensionData, "Repository", errors);
        if (document.Packages is null) return new ExtensionManifestValidationResult(false, ["The repository package list is missing."]);
        if (document.Packages.Count == 0) errors.Add("The repository manifest contains no packages.");
        if (document.Packages.Count > 100) errors.Add("A repository may expose at most 100 packages.");
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in document.Packages)
        {
            ValidatePackage(package, errors);
            if (!packageIds.Add(package.PackageId)) errors.Add($"Duplicate package ID '{package.PackageId}'.");
        }
        return new ExtensionManifestValidationResult(errors.Count == 0, errors);
    }

    private static void ValidatePackage(ExtensionPackageManifest package, ICollection<string> errors)
    {
        if (package is null) { errors.Add("The repository contains a null package entry."); return; }
        var prefix = string.IsNullOrWhiteSpace(package.PackageId) ? "Package" : package.PackageId;
        ValidateUnknownRequiredFields(package.ExtensionData, prefix, errors);
        if (!PackageIdPattern().IsMatch(package.PackageId ?? string.Empty)) errors.Add($"{prefix}: package ID is invalid.");
        if (!IsSafeRelativePath(package.PackagePath)) errors.Add($"{prefix}: package path must be a safe relative path.");
        if (string.IsNullOrWhiteSpace(package.DisplayName) || package.DisplayName.Length > 120) errors.Add($"{prefix}: display name is required and must be at most 120 characters.");
        if (!Version.TryParse(package.Version, out _)) errors.Add($"{prefix}: version must be a valid dotted numeric version.");
        if (string.IsNullOrWhiteSpace(package.HavenVersionRange)) errors.Add($"{prefix}: Haven compatibility range is required.");
        if (!string.IsNullOrWhiteSpace(package.MinimumHavenVersion) && !Version.TryParse(package.MinimumHavenVersion, out _))
            errors.Add($"{prefix}: minimum Haven version must be a dotted numeric version.");
        if (!Enum.IsDefined(package.PackageType)) errors.Add($"{prefix}: package type is unsupported.");
        if (package.DeclaredContentHash is not null && !Regex.IsMatch(package.DeclaredContentHash, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            errors.Add($"{prefix}: declared SHA-256 integrity hash is invalid.");
        if ((package.Signature is null) != (package.SignatureKeyId is null)) errors.Add($"{prefix}: signature and signature key ID must be declared together.");
        if (package.Signature is not null && (package.IntegrityAlgorithm is null || package.DeclaredContentHash is null))
            errors.Add($"{prefix}: signatures require an integrity algorithm and declared content hash.");
        if (package.UpdateMetadataJson is not null) ValidateJson(package.UpdateMetadataJson, $"{prefix}: update metadata", errors);
        if (package.RequestedPermissions.HasFlag(ExtensionPermission.ProcessExecution) && package.Capabilities.Count == 0)
            errors.Add($"{prefix}: process execution cannot be requested without a declared capability.");
        if (package.PackageType == ExtensionPackageType.Skill && package.Capabilities.Count > 0)
            errors.Add($"{prefix}: a Skill-only package cannot declare executable capabilities.");
        if (package.PackageType == ExtensionPackageType.Plugin && package.Skills.Count > 0)
            errors.Add($"{prefix}: use PluginAndSkills when Skills are bundled.");
        ValidateUnique(package.Capabilities.Select(value => value.Id), $"{prefix}: capability", errors);
        ValidateUnique(package.Skills.Select(value => value.Id), $"{prefix}: Skill", errors);
        ValidateUnique(package.DependencyDefinitions?.Select(value => value.PackageId) ?? [], $"{prefix}: dependency", errors);
        ValidateUnique(package.Surfaces?.Select(value => value.Id) ?? [], $"{prefix}: surface", errors);
        foreach (var capability in package.Capabilities)
        {
            if (capability is null) { errors.Add($"{prefix}: capability entries cannot be null."); continue; }
            ValidateUnknownRequiredFields(capability.ExtensionData, $"{prefix} capability '{capability.Id}'", errors);
            if (!PackageIdPattern().IsMatch(capability.Id)) errors.Add($"{prefix}: capability ID '{capability.Id}' is invalid.");
            if (!IsSafeRelativePath(capability.EntryPoint)) errors.Add($"{prefix}: capability entry point must be a safe relative path.");
            if (!capability.RequiredPermissions.HasFlag(ExtensionPermission.ProcessExecution))
                errors.Add($"{prefix}: capability '{capability.Id}' must declare the process execution permission.");
            if ((capability.RequiredPermissions & ~package.RequestedPermissions) != 0)
                errors.Add($"{prefix}: capability '{capability.Id}' requests permissions not declared by the package.");
            if (string.IsNullOrWhiteSpace(capability.InputSchemaJson)) errors.Add($"{prefix}: capability '{capability.Id}' must declare an input schema.");
            else ValidateObjectSchema(capability.InputSchemaJson, $"{prefix}: capability '{capability.Id}' input schema", errors);
            if (capability.OutputSchemaJson is not null) ValidateObjectSchema(capability.OutputSchemaJson, $"{prefix}: capability '{capability.Id}' output schema", errors);
            if (capability.RiskClassification is not ("read-only" or "low" or "consequential" or "restricted"))
                errors.Add($"{prefix}: capability '{capability.Id}' has an unsupported risk classification.");
            if (capability.RequiredPermissions != ExtensionPermission.None && string.IsNullOrWhiteSpace(capability.CancellationSemantics))
                errors.Add($"{prefix}: capability '{capability.Id}' must document its cancellation/timeout semantics.");
            if (capability.RequiredPermissions.HasFlag(ExtensionPermission.ConnectorAccess) &&
                (string.IsNullOrWhiteSpace(capability.CredentialReferenceKey) || string.IsNullOrWhiteSpace(capability.ConnectionScope)))
                errors.Add($"{prefix}: external connector capability '{capability.Id}' must use a credential reference and explicit connection scope.");
        }
        foreach (var skill in package.Skills)
        {
            if (skill is null) { errors.Add($"{prefix}: Skill entries cannot be null."); continue; }
            ValidateUnknownRequiredFields(skill.ExtensionData, $"{prefix} Skill '{skill.Id}'", errors);
            if (!PackageIdPattern().IsMatch(skill.Id)) errors.Add($"{prefix}: Skill ID '{skill.Id}' is invalid.");
            if (!IsSafeRelativePath(skill.InstructionPath)) errors.Add($"{prefix}: Skill instruction path must be a safe relative path.");
            if (skill.WorkflowJson is not null) ValidateJson(skill.WorkflowJson, $"{prefix}: Skill '{skill.Id}' workflow", errors);
            if (skill.ContextRulesJson is not null) ValidateJson(skill.ContextRulesJson, $"{prefix}: Skill '{skill.Id}' context rules", errors);
            ValidateUnique(skill.ConflictKeys ?? [], $"{prefix}: Skill conflict key", errors);
        }
        foreach (var dependency in package.DependencyDefinitions ?? [])
        {
            if (!PackageIdPattern().IsMatch(dependency.PackageId)) errors.Add($"{prefix}: dependency package ID '{dependency.PackageId}' is invalid.");
            if (!ExtensionDependencyResolver.IsValidVersionRange(dependency.VersionRange)) errors.Add($"{prefix}: dependency '{dependency.PackageId}' has an invalid version range.");
            if (!Enum.IsDefined(dependency.Type)) errors.Add($"{prefix}: dependency '{dependency.PackageId}' has an unsupported dependency type.");
            if (dependency.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase)) errors.Add($"{prefix}: package cannot depend on itself.");
        }
        foreach (var resource in package.ResourcePaths ?? [])
            if (!IsSafeRelativePath(resource)) errors.Add($"{prefix}: resource path '{resource}' must be a safe relative path.");
        foreach (var surface in package.Surfaces ?? [])
        {
            if (!PackageIdPattern().IsMatch(surface.Id)) errors.Add($"{prefix}: Plugin surface ID '{surface.Id}' is invalid.");
            if (!IsSafeRelativePath(surface.CUiDefinitionPath)) errors.Add($"{prefix}: Plugin surface path must be a safe relative path.");
            if (string.IsNullOrWhiteSpace(surface.HostContractVersion)) errors.Add($"{prefix}: Plugin surface host contract version is required.");
        }
    }

    private static void ValidateUnknownRequiredFields(IReadOnlyDictionary<string, JsonElement>? extensionData, string prefix, ICollection<string> errors)
    {
        if (extensionData is null) return;
        foreach (var property in extensionData)
        {
            if (property.Key.StartsWith("required", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{prefix}: unknown required manifest field '{property.Key}' blocks installation.");
            else if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True)
                errors.Add($"{prefix}: unknown required extension field '{property.Key}' blocks installation.");
        }
    }

    private static void ValidateObjectSchema(string json, string label, ICollection<string> errors)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                (document.RootElement.TryGetProperty("type", out var type) && type.GetString() != "object"))
                errors.Add($"{label} must be a JSON object schema with object root type.");
            if (document.RootElement.TryGetProperty("required", out var required) && required.ValueKind != JsonValueKind.Array)
                errors.Add($"{label} has an invalid required list.");
        }
        catch (JsonException) { errors.Add($"{label} is not valid JSON."); }
    }

    private static void ValidateJson(string json, string label, ICollection<string> errors)
    {
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException) { errors.Add($"{label} is not valid JSON."); }
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split('/', '\\').Any(part => part == "..");

    private static void ValidateUnique(IEnumerable<string> values, string label, ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
            if (!seen.Add(value)) errors.Add($"{label} ID '{value}' is duplicated.");
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{1,126}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();
}
