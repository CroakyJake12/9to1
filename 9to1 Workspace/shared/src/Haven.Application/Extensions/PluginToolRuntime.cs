/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Extensions/PluginToolRuntime.cs, in the Application layer.
 * What: Owns PluginToolBinding and PluginToolRuntime — the tool-loop adapter that lets registered
 *       native-plugin capabilities execute through the SAME planning/permission/Action Graph path as
 *       every other runtime.
 * How: Capability ImplementationKeys of form "native-plugin:{package}:{capability}" become tool
 *      definitions; invocation delegates to NativePluginRuntime, which enforces granted permissions.
 * Why: Plugin capabilities must not be a parallel execution system — they join the shared one.
 * Maintenance: Keep tool-name sanitisation stable so conversations survive restarts; permission
 *              enforcement stays inside NativePluginRuntime (single authority).
 */

using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

/// <summary>One executable plugin capability bound to its generated tool definition.</summary>
public sealed record PluginToolBinding(
    OllamaToolDefinition Definition,
    string PackageId,
    string CapabilityId,
    ExtensionPermission RequiredPermissions,
    string RegistryCapabilityKey,
    System.Text.Json.JsonElement InputSchema,
    System.Text.Json.JsonElement? OutputSchema,
    string RiskClassification);

public sealed partial class PluginToolRuntime(NativePluginRuntime runtime)
{
    private sealed record ResolvedPluginToolDescriptor(ExtensionPermission RequiredPermissions, System.Text.Json.JsonElement InputSchema, System.Text.Json.JsonElement? OutputSchema, string RiskClassification);

    public const string ImplementationKeyPrefix = "native-plugin:";

    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex UnsafeCharacters();

    /// <summary>Builds tool bindings for the turn's selected plugin-backed capabilities.</summary>
    public IReadOnlyList<PluginToolBinding> GetBindings(IReadOnlyCollection<ActiveCapability> selectedCapabilities)
    {
        var bindings = new List<PluginToolBinding>();
        foreach (var capability in selectedCapabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.ImplementationKey) ||
                !capability.ImplementationKey.StartsWith(ImplementationKeyPrefix, StringComparison.Ordinal)) continue;

            var parts = capability.ImplementationKey[ImplementationKeyPrefix.Length..].Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) continue;

            if (!TryReadDescriptor(capability.Instructions, out var descriptor)) continue;
            var toolName = SanitiseToolName(parts[0] + "_" + parts[1]);
            bindings.Add(new PluginToolBinding(
                new OllamaToolDefinition(
                    toolName,
                    $"Plugin capability {capability.Name}. Use only the declared typed schema; the host checks permissions before execution.",
                    new Dictionary<string, object>(StringComparer.Ordinal),
                    [],
                    InputSchema: descriptor.InputSchema),
                parts[0],
                parts[1],
                descriptor.RequiredPermissions,
                capability.Key,
                descriptor.InputSchema,
                descriptor.OutputSchema,
                descriptor.RiskClassification));
        }
        return bindings;
    }

    public async Task<WorkspaceToolResult> ExecuteAsync(
        PluginToolBinding binding,
        OllamaToolCall call,
        Guid executionId,
        Guid? parentActionId,
        PermissionMode permissionMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        try
        {
            if (permissionMode != PermissionMode.FullAccess)
            {
                const string detail = "This Plugin action requires an explicit permission grant before execution.";
                return new WorkspaceToolResult(
                    new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), detail, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                    "Tool error: " + detail,
                    new ToolFailureDescriptor("PLUGIN_PERMISSION_REQUIRED", ToolFailureKind.PermissionRequired, detail,
                        binding.RegistryCapabilityKey, "Plugin capability",
                        new RecoveryRiskAssessment(true, false, true, false, true, false, false, .98), true,
                        RemediationType.PermissionRequest));
            }
            var result = await runtime.InvokeAsync(
                binding.PackageId,
                binding.CapabilityId,
                SerializeArguments(call.Arguments),
                binding.RequiredPermissions,
                executionId,
                parentActionId,
                cancellationToken).ConfigureAwait(false);
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), "Plugin action completed.", true, TimeSpan.Zero, DateTimeOffset.UtcNow),
                SensitiveTextRedactor.Redact(result, 8_000));
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), ex.Message, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                "Tool error: Plugin permission is not currently granted.",
                new ToolFailureDescriptor(
                    "PLUGIN_PERMISSION_DENIED", ToolFailureKind.PermissionRequired, "Plugin permission is not currently granted.",
                    binding.PackageId, "Plugin permissions",
                    new RecoveryRiskAssessment(true, false, false, false, true, false, false, 0.9),
                    Retryable: false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException or InvalidDataException)
        {
            var detail = ex is ArgumentException ? "Plugin arguments failed schema validation." :
                ex is InvalidDataException ? "Plugin output failed schema validation." : "Plugin action failed safely.";
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), detail, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                "Tool error: " + detail);
        }
    }

    public Task<WorkspaceToolResult> ExecuteAsync(
        PluginToolBinding binding,
        OllamaToolCall call,
        Guid executionId,
        Guid? parentActionId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(binding, call, executionId, parentActionId, PermissionMode.Ask, cancellationToken);

    private static string SerializeArguments(IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments)
    {
        if (arguments.Count == 0) return "{}";
        return System.Text.Json.JsonSerializer.Serialize(arguments);
    }

    private static string SanitiseToolName(string capabilityId)
    {
        var cleaned = UnsafeCharacters().Replace(capabilityId.Trim(), "_").Trim('_');
        if (cleaned.Length == 0) cleaned = "action";
        var name = $"plugin_{cleaned}";
        return name.Length <= 48 ? name : name[..48];
    }

    private static bool TryReadDescriptor(string json, out ResolvedPluginToolDescriptor descriptor)
    {
        descriptor = default!;
        try
        {
            var declaration = System.Text.Json.JsonSerializer.Deserialize<PluginCapabilityToolDescriptor>(json);
            if (declaration is null || !ExtensionJsonSchemaValidator.IsSupported(declaration.InputSchemaJson)) return false;
            using var input = System.Text.Json.JsonDocument.Parse(declaration.InputSchemaJson);
            System.Text.Json.JsonElement? output = null;
            if (declaration.OutputSchemaJson is not null)
            {
                if (!ExtensionJsonSchemaValidator.IsSupported(declaration.OutputSchemaJson)) return false;
                using var outputDocument = System.Text.Json.JsonDocument.Parse(declaration.OutputSchemaJson);
                output = outputDocument.RootElement.Clone();
            }
            descriptor = new ResolvedPluginToolDescriptor(declaration.RequiredPermissions, input.RootElement.Clone(), output, declaration.RiskClassification);
            return true;
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

}
