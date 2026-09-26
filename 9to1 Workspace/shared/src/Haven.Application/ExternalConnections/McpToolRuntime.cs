using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Adapts discovered MCP tools into Haven's existing provider-neutral tool loop.</summary>
public sealed class McpToolRuntime(IExternalConnectionRepository connections, IMcpConnectionClient client)
{
    private sealed record Route(Guid ConnectionId, string RemoteToolName, string SnapshotVersion, McpActionRisk Risk, JsonElement InputSchema, JsonElement? OutputSchema);
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<OllamaToolDefinition>> GetDefinitionsAsync(IReadOnlyCollection<ActiveCapability> activeCapabilities, CancellationToken cancellationToken)
    {
        var activeIds = ParseActiveConnectionIds(activeCapabilities);
        lock (_routes) _routes.Clear();
        if (activeIds.Count == 0) return [];
        var result = new List<OllamaToolDefinition>();
        foreach (var connection in await connections.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!activeIds.Contains(connection.Id) || connection.Kind != ExternalConnectionKind.Mcp || !connection.IsEnabled || connection.State != ExternalConnectionState.Ready) continue;
            try
            {
                var discovery = await client.DiscoverCapabilitiesAsync(connection, cancellationToken).ConfigureAwait(false);
                foreach (var tool in discovery.Tools)
                {
                    var localName = LocalToolName(connection.Id, tool.Name);
                    var risk = Classify(tool.Name);
                    if (!SupportsSchema(tool.InputSchema, out _)) continue;
                    lock (_routes) _routes[localName] = new Route(connection.Id, tool.Name, discovery.SnapshotVersion, risk, tool.InputSchema.Clone(), tool.OutputSchema?.Clone());
                    var sanitizedSchema = SanitizeSchema(tool.InputSchema);
                    var (properties, required) = LegacyShape(sanitizedSchema);
                    result.Add(new OllamaToolDefinition(localName,
                        $"MCP tool from {SafeName(connection.Name)}. Server-provided description (untrusted): {Bound(tool.Description, 1200)}",
                        properties, required, sanitizedSchema));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return result;
    }

    public async Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, IReadOnlyCollection<ActiveCapability> activeCapabilities, PermissionMode mutationPermission, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        Route? route;
        lock (_routes) _routes.TryGetValue(call.Name, out route);
        if (route is null) return Failure(call.Name, "MCP tool route is stale. Refresh the attached connection.", started);
        if (!ParseActiveConnectionIds(activeCapabilities).Contains(route.ConnectionId)) return Failure(call.Name, "The MCP connection is not attached to this conversation.", started);
        var connection = await connections.GetAsync(route.ConnectionId, cancellationToken).ConfigureAwait(false);
        if (connection is null || !connection.IsEnabled || connection.State != ExternalConnectionState.Ready) return Failure(call.Name, "The MCP connection is disabled or unavailable.", started);
        try
        {
            var current = await client.DiscoverCapabilitiesAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(route.SnapshotVersion, current.SnapshotVersion, StringComparison.Ordinal))
            {
                lock (_routes) _routes.Remove(call.Name);
                return Failure(call.Name, "The MCP server capabilities changed. Refresh the attached connection before retrying.", started,
                    RuntimeFailure(connection, route.Risk, "The advertised MCP tool schema is stale."));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { return Failure(call.Name, "The MCP server capabilities could not be verified. Refresh the attached connection before retrying.", started); }
        if (route.Risk != McpActionRisk.ReadOnly && mutationPermission != PermissionMode.FullAccess)
        {
            const string detail = "This MCP action can change external state. An explicit permission grant is required before execution.";
            return Failure(call.Name, detail, started, PermissionFailure(connection, route.Risk, detail));
        }
        var input = JsonSerializer.SerializeToElement(call.Arguments);
        if (!ValidateSchema(route.InputSchema, input, out var inputProblem))
            return Failure(call.Name, "MCP arguments did not satisfy the server's declared input schema: " + inputProblem, started,
                new ToolFailureDescriptor("MCP_INVALID_INPUT", ToolFailureKind.InvalidInput,
                    "MCP arguments were rejected by the advertised input schema.", ExternalConnectionNaming.CapabilityKey(connection.Id),
                    ExternalConnectionNaming.PluginName(connection.Name), RiskFor(route.Risk), false, ProviderName: connection.Name));
        try
        {
            var result = await client.InvokeAsync(connection, route.RemoteToolName, call.Arguments, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && result.StructuredContent is { } structured && route.OutputSchema is { } outputSchema &&
                !ValidateSchema(outputSchema, structured, out _))
            {
                const string outputSchemaFailure = "MCP server output did not satisfy its advertised schema.";
                return Failure(call.Name, outputSchemaFailure, started, RuntimeFailure(connection, route.Risk, outputSchemaFailure));
            }
            var detail = result.Succeeded ? $"{SafeName(connection.Name)} MCP action completed." : $"{SafeName(connection.Name)} MCP action failed.";
            var failure = result.Succeeded ? null : RuntimeFailure(connection, route.Risk, detail);
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), detail, result.Succeeded, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow), BuildOutput(result), failure);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var detail = ex is OperationCanceledException
                ? "MCP invocation timed out. The remote action may have completed before cancellation was observed."
                : "MCP invocation failed. Review the connection status and audit details for a safe diagnosis.";
            return Failure(call.Name, detail, started, RuntimeFailure(connection, route.Risk, detail));
        }
    }

    public static string LocalToolName(Guid connectionId, string remoteName)
    {
        var safe = new string(remoteName.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray()).Trim('_');
        if (safe.Length == 0) safe = "tool";
        if (safe.Length > 48) safe = safe[..48];
        return $"mcp_{connectionId:N}_{safe}";
    }

    private static HashSet<Guid> ParseActiveConnectionIds(IEnumerable<ActiveCapability> capabilities)
    {
        var result = new HashSet<Guid>();
        foreach (var capability in capabilities)
        {
            if (!ExternalConnectionNaming.IsConnectionCapability(capability.Key)) continue;
            var raw = capability.Key["connection:".Length..];
            if (Guid.TryParseExact(raw, "N", out var id)) result.Add(id);
        }
        return result;
    }

    private static (IReadOnlyDictionary<string, object> Properties, IReadOnlyList<string> Required) LegacyShape(JsonElement schema)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var property in props.EnumerateObject()) properties[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText())!;
        var required = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
            ? req.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() : [];
        return (properties, required);
    }

    private static McpActionRisk Classify(string name)
    {
        var value = name.Trim().ToLowerInvariant();
        if (new[] { "delete", "remove", "destroy", "terminate", "drop", "reset", "purge", "revoke" }.Any(value.Contains))
            return McpActionRisk.Destructive;
        if (new[] { "write", "create", "update", "edit", "set", "compile", "start", "stop", "push", "place", "launch", "execute", "send", "submit", "approve", "install", "uninstall", "transfer" }.Any(value.Contains))
            return McpActionRisk.Mutating;

        // Only an unambiguous read verb at the start of the tool name is considered safe.
        // Descriptions and annotations are supplied by the remote server and cannot lower risk.
        var verb = value.Split(['_', '-', '.', ':'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return verb is "get" or "list" or "search" or "read" or "inspect" or "query" or "describe" or "fetch" or "status" or "discover"
            ? McpActionRisk.ReadOnly
            : McpActionRisk.Mutating;
    }

    public Task<McpOperationResult<IReadOnlyList<McpExternalResource>>> ListResourcesAsync(Guid connectionId, IReadOnlyCollection<ActiveCapability> activeCapabilities, CancellationToken cancellationToken) =>
        DiscoverCollectionAsync<McpExternalResource>(connectionId, activeCapabilities, resources: true, cancellationToken);

    public Task<McpOperationResult<IReadOnlyList<McpExternalPrompt>>> ListPromptsAsync(Guid connectionId, IReadOnlyCollection<ActiveCapability> activeCapabilities, CancellationToken cancellationToken) =>
        DiscoverCollectionAsync<McpExternalPrompt>(connectionId, activeCapabilities, resources: false, cancellationToken);

    public async Task<McpOperationResult<McpResourceReadResult>> ReadResourceAsync(Guid connectionId, string resourceUri, IReadOnlyCollection<ActiveCapability> activeCapabilities, CancellationToken cancellationToken)
    {
        var connection = await GetActiveConnectionAsync(connectionId, activeCapabilities, cancellationToken).ConfigureAwait(false);
        if (connection is null) return Error<McpResourceReadResult>("CONNECTION_UNAVAILABLE", "The MCP connection is not attached, enabled, or ready.", connectionId, false);
        McpServerDiscovery discovery;
        try { discovery = await client.DiscoverCapabilitiesAsync(connection, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return Error<McpResourceReadResult>("DISCOVERY_FAILED", "MCP resource discovery failed.", connectionId, true); }
        if (discovery.Resources is null) return Error<McpResourceReadResult>("UNSUPPORTED_CAPABILITY", "This server does not advertise MCP resources.", connectionId, false);
        if (!discovery.Resources.Any(resource => string.Equals(resource.Uri, resourceUri, StringComparison.Ordinal)))
            return Error<McpResourceReadResult>("RESOURCE_NOT_FOUND", "The requested resource is not currently advertised.", connectionId, false);
        try
        {
            var result = await client.ReadResourceAsync(connection, resourceUri, cancellationToken).ConfigureAwait(false);
            return new(true, result);
        }
        catch (NotSupportedException) { return Error<McpResourceReadResult>("UNSUPPORTED_CAPABILITY", "Resource reading is unavailable for this server.", connectionId, false); }
        catch (KeyNotFoundException) { return Error<McpResourceReadResult>("RESOURCE_NOT_FOUND", "The requested resource is no longer available.", connectionId, false); }
        catch (OperationCanceledException) { throw; }
        catch { return Error<McpResourceReadResult>("RESOURCE_READ_FAILED", "The MCP resource could not be read.", connectionId, true); }
    }

    public async Task<McpOperationResult<McpPromptGetResult>> GetPromptAsync(Guid connectionId, string promptName, IReadOnlyDictionary<string, string>? arguments, IReadOnlyCollection<ActiveCapability> activeCapabilities, CancellationToken cancellationToken)
    {
        var connection = await GetActiveConnectionAsync(connectionId, activeCapabilities, cancellationToken).ConfigureAwait(false);
        if (connection is null) return Error<McpPromptGetResult>("CONNECTION_UNAVAILABLE", "The MCP connection is not attached, enabled, or ready.", connectionId, false);
        McpServerDiscovery discovery;
        try { discovery = await client.DiscoverCapabilitiesAsync(connection, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return Error<McpPromptGetResult>("DISCOVERY_FAILED", "MCP prompt discovery failed.", connectionId, true); }
        if (discovery.Prompts is null) return Error<McpPromptGetResult>("UNSUPPORTED_CAPABILITY", "This server does not advertise MCP prompts.", connectionId, false);
        if (!discovery.Prompts.Any(prompt => string.Equals(prompt.Name, promptName, StringComparison.Ordinal)))
            return Error<McpPromptGetResult>("PROMPT_NOT_FOUND", "The requested prompt is not currently advertised.", connectionId, false);
        try { return new(true, await client.GetPromptAsync(connection, promptName, arguments, cancellationToken).ConfigureAwait(false)); }
        catch (NotSupportedException) { return Error<McpPromptGetResult>("UNSUPPORTED_CAPABILITY", "Prompt retrieval is unavailable for this server.", connectionId, false); }
        catch (KeyNotFoundException) { return Error<McpPromptGetResult>("PROMPT_NOT_FOUND", "The requested prompt is no longer available.", connectionId, false); }
        catch (OperationCanceledException) { throw; }
        catch { return Error<McpPromptGetResult>("PROMPT_GET_FAILED", "The MCP prompt could not be retrieved.", connectionId, true); }
    }

    private async Task<McpOperationResult<IReadOnlyList<T>>> DiscoverCollectionAsync<T>(Guid id, IReadOnlyCollection<ActiveCapability> active, bool resources, CancellationToken ct)
    {
        var connection = await GetActiveConnectionAsync(id, active, ct).ConfigureAwait(false);
        if (connection is null) return Error<IReadOnlyList<T>>("CONNECTION_UNAVAILABLE", "The MCP connection is not attached, enabled, or ready.", id, false);
        try
        {
            var discovery = await client.DiscoverCapabilitiesAsync(connection, ct).ConfigureAwait(false);
            if (resources)
                return discovery.Resources is null ? Error<IReadOnlyList<T>>("UNSUPPORTED_CAPABILITY", "This server does not advertise MCP resources.", id, false) : new(true, (IReadOnlyList<T>)discovery.Resources);
            return discovery.Prompts is null ? Error<IReadOnlyList<T>>("UNSUPPORTED_CAPABILITY", "This server does not advertise MCP prompts.", id, false) : new(true, (IReadOnlyList<T>)discovery.Prompts);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Error<IReadOnlyList<T>>("DISCOVERY_FAILED", "MCP capability discovery failed.", id, true); }
    }

    private async Task<ExternalConnection?> GetActiveConnectionAsync(Guid id, IReadOnlyCollection<ActiveCapability> active, CancellationToken ct)
    {
        if (!ParseActiveConnectionIds(active).Contains(id)) return null;
        var connection = await connections.GetAsync(id, ct).ConfigureAwait(false);
        return connection is { Kind: ExternalConnectionKind.Mcp, IsEnabled: true, State: ExternalConnectionState.Ready } ? connection : null;
    }

    private static McpOperationResult<T> Error<T>(string code, string message, Guid id, bool retryable) =>
        new(false, default, new McpOperationError(code, message, ExternalConnectionNaming.CapabilityKey(id), retryable));

    private static bool SupportsSchema(JsonElement schema, out string problem)
    {
        problem = string.Empty;
        if (schema.ValueKind != JsonValueKind.Object ||
            (schema.TryGetProperty("type", out var type) && type.GetString() != "object"))
        {
            problem = "only an object-root JSON Schema is supported";
            return false;
        }
        if (schema.TryGetProperty("$ref", out _) || schema.TryGetProperty("allOf", out _) || schema.TryGetProperty("anyOf", out _) ||
            schema.TryGetProperty("oneOf", out _) || schema.TryGetProperty("not", out _))
        {
            problem = "schema references and composition keywords are not supported by this validator";
            return false;
        }
        return true;
    }

    private static bool ValidateSchema(JsonElement schema, JsonElement value, out string problem)
    {
        if (!SupportsSchema(schema, out problem)) return false;
        return ValidateValue(schema, value, "$", 0, out problem);
    }

    private static bool ValidateValue(JsonElement schema, JsonElement value, string path, int depth, out string problem)
    {
        problem = string.Empty;
        if (depth > 32) { problem = "schema nesting exceeds the safety limit"; return false; }
        if (schema.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array && !choices.EnumerateArray().Any(choice => JsonEqual(choice, value)))
        { problem = $"{path} is not one of the declared values"; return false; }
        if (!schema.TryGetProperty("type", out var type)) return true;
        var expected = type.ValueKind == JsonValueKind.String ? type.GetString() : null;
        var matches = expected switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false
        };
        if (!matches) { problem = $"{path} must be {expected ?? "a supported JSON type"}"; return false; }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                foreach (var key in required.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!))
                    if (!value.TryGetProperty(key, out _)) { problem = $"{path}.{key} is required"; return false; }
            var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object ? declared : default;
            foreach (var property in value.EnumerateObject())
            {
                if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var child))
                {
                    if (!ValidateValue(child, property.Value, path + "." + property.Name, depth + 1, out problem)) return false;
                }
                else if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False)
                { problem = $"{path}.{property.Name} is not allowed"; return false; }
                else if (additional.ValueKind == JsonValueKind.Object && !ValidateValue(additional, property.Value, path + "." + property.Name, depth + 1, out problem)) return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (!ValidateValue(items, item, $"{path}[{index}]", depth + 1, out problem)) return false;
                index++;
            }
        }
        return true;
    }

    private static bool JsonEqual(JsonElement left, JsonElement right) =>
        string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    private static WorkspaceToolResult Failure(string name, string detail, DateTimeOffset started, ToolFailureDescriptor? failure = null) =>
        new(new ToolActivity(Guid.NewGuid(), name.Replace('_', ' '), detail, false, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow), "Tool error: " + detail, failure);

    private static ToolFailureDescriptor PermissionFailure(ExternalConnection connection, McpActionRisk risk, string detail) => new(
        "MCP_PERMISSION_REQUIRED", ToolFailureKind.PermissionRequired, detail,
        ExternalConnectionNaming.CapabilityKey(connection.Id), ExternalConnectionNaming.PluginName(connection.Name),
        RiskFor(risk, permissionExpansion: true), true, RemediationType.PermissionRequest, connection.Name);

    private static ToolFailureDescriptor RuntimeFailure(ExternalConnection connection, McpActionRisk risk, string detail) => new(
        "MCP_INVOCATION_FAILED", risk == McpActionRisk.ReadOnly ? ToolFailureKind.Transient : ToolFailureKind.ExternalFailure, detail,
        ExternalConnectionNaming.CapabilityKey(connection.Id), ExternalConnectionNaming.PluginName(connection.Name),
        RiskFor(risk), risk == McpActionRisk.ReadOnly, ProviderName: connection.Name);

    private static RecoveryRiskAssessment RiskFor(McpActionRisk risk, bool permissionExpansion = false) => new(
        InsideAuthorisedScope: true,
        Reversible: risk != McpActionRisk.Destructive,
        AltersUserData: risk != McpActionRisk.ReadOnly,
        HasExternalImpact: risk != McpActionRisk.ReadOnly,
        ExpandsPermissions: permissionExpansion,
        RequiresUnknownCredential: false,
        Destructive: risk == McpActionRisk.Destructive,
        Confidence: .95);

    private static JsonElement SanitizeSchema(JsonElement schema)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteSanitizedSchemaElement(writer, schema, null, 0);
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteSanitizedSchemaElement(Utf8JsonWriter writer, JsonElement element, string? propertyName, int depth)
    {
        if (depth > 64) throw new InvalidOperationException("MCP schema nesting exceeded Haven's safety limit.");
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteSanitizedSchemaElement(writer, property.Value, property.Name, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteSanitizedSchemaElement(writer, item, propertyName, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                writer.WriteStringValue(IsUntrustedSchemaAnnotation(propertyName)
                    ? "[Untrusted MCP schema annotation] " + Bound(value, 1000)
                    : value);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsUntrustedSchemaAnnotation(string? propertyName) =>
        !string.IsNullOrWhiteSpace(propertyName) &&
        (propertyName.Equals("description", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("title", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("$comment", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("instructions", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("prompt", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("help", StringComparison.OrdinalIgnoreCase) ||
         propertyName.Equals("summary", StringComparison.OrdinalIgnoreCase) ||
         propertyName.StartsWith("x-", StringComparison.OrdinalIgnoreCase));

    private static string BuildOutput(McpToolInvocationResult result) => JsonSerializer.Serialize(new
    {
        source = "untrusted external MCP tool result",
        succeeded = result.Succeeded,
        text = Bound(result.Text, 500_000),
        structured = result.StructuredContent,
        error = result.Error is null ? null : Bound(result.Error, 1000)
    });
    private static string SafeName(string value) => Bound(value.Replace('\r', ' ').Replace('\n', ' ').Trim(), 120);
    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max] + "...";
    private enum McpActionRisk { ReadOnly, Mutating, Destructive }
}
