using System.Text.Json;

namespace Haven.Core;

/// <summary>Rejects arbitrary visual/code payloads and bounds the trusted HavenUI document contract.</summary>
public static class GenerativeUiContractValidator
{
    public const int CurrentContractVersion = 1;
    public const int MaximumDepth = 16;
    public const int MaximumComponents = 500;
    public const int MaximumJsonBytes = 512 * 1024;

    public static IReadOnlySet<string> TrustedComponentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HavenWorkspace", "HavenStack", "HavenGrid", "HavenSplitView", "HavenToolbar",
        "HavenText", "HavenMarkdown", "HavenButton", "HavenTextInput", "HavenSelect",
        "HavenToggle", "HavenSlider", "HavenProgress", "HavenCard", "HavenList",
        "HavenTable", "HavenTabs", "HavenForm", "HavenWizard", "HavenChart",
        "HavenGraph", "HavenCanvas", "HavenImage", "HavenStatus"
    };

    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "css", "xaml", "html", "javascript", "script", "code", "executable", "commandLine"
    };

    public static bool ValidatePropertyName(string name) => !ForbiddenPropertyNames.Contains(name);

    public static IReadOnlyList<string> Validate(GenUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (document.ContractVersion != CurrentContractVersion)
            errors.Add($"Contract version {document.ContractVersion} is unsupported.");
        if (document.DocumentId == Guid.Empty) errors.Add("Document ID must be stable and non-empty.");
        if (document.Origin is null)
            errors.Add("Document origin is required.");
        else
        {
            if (document.Origin.ThreadId == Guid.Empty) errors.Add("Thread ID must be non-empty.");
            if (document.Origin.InstanceId == Guid.Empty) errors.Add("Instance ID must be non-empty.");
            if (string.IsNullOrWhiteSpace(document.Origin.AppKey)) errors.Add("Owning App key is required.");
        }
        if (document.State is null) errors.Add("Document state is required.");
        if (document.Root is null) errors.Add("Document component root is required.");

        if (document.Root is not null)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            Visit(document.Root, 1, ids, ref count, errors);
            if (count > MaximumComponents) errors.Add($"Document contains {count} components; maximum is {MaximumComponents}.");
        }

        // Walk and bound the component graph before serializing it. Generated or
        // imported object graphs can be cyclic even though the wire format cannot.
        if (errors.Count == 0)
        {
            try
            {
                if (JsonSerializer.SerializeToUtf8Bytes(document).Length > MaximumJsonBytes)
                    errors.Add($"Document exceeds the {MaximumJsonBytes}-byte contract limit.");
            }
            catch (JsonException)
            {
                errors.Add("Document cannot be represented as valid bounded JSON.");
            }
        }
        return errors;
    }

    public static IReadOnlyList<string> Validate(GenUiActionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(binding.ActionId)) errors.Add("Action ID is required.");
        if (!Enum.IsDefined(binding.Route)) errors.Add($"Action '{binding.ActionId}' has an unsupported route.");
        if (!Enum.IsDefined(binding.RiskClass)) errors.Add($"Action '{binding.ActionId}' has an unsupported risk class.");
        if (string.IsNullOrWhiteSpace(binding.TargetKey)) errors.Add($"Action '{binding.ActionId}' has no route target.");
        if (binding.Route == GenUiRouteKind.Local
            && (binding.RequiresPermission || binding.RiskClass >= CapabilityRiskClass.Consequential))
            errors.Add($"Local action '{binding.ActionId}' cannot own consequential or permissioned work.");
        if (binding.Route != GenUiRouteKind.Local
            && binding.RiskClass >= CapabilityRiskClass.Consequential
            && !binding.RequiresPermission)
            errors.Add($"Consequential action '{binding.ActionId}' must preserve the permission boundary.");
        return errors;
    }

    public static void ValidateAndThrow(GenUiDocument document)
    {
        var errors = Validate(document);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }

    public static IReadOnlyList<string> Validate(GenUiEvent semanticEvent)
    {
        ArgumentNullException.ThrowIfNull(semanticEvent);
        var errors = new List<string>();
        if (semanticEvent.EventId == Guid.Empty) errors.Add("Event ID must be non-empty.");
        if (semanticEvent.Origin is null)
            errors.Add("Event origin is required.");
        else if (semanticEvent.Origin.InstanceId == Guid.Empty)
            errors.Add("Event instance ID must be non-empty.");
        if (string.IsNullOrWhiteSpace(semanticEvent.ComponentId)) errors.Add("Component ID is required.");
        if (string.IsNullOrWhiteSpace(semanticEvent.ActionId)) errors.Add("Action ID is required.");
        if (semanticEvent.StructuredPayload.ValueKind is JsonValueKind.Undefined)
            errors.Add("Structured payload must be explicit JSON.");
        try
        {
            if (JsonSerializer.SerializeToUtf8Bytes(semanticEvent).Length > MaximumJsonBytes)
                errors.Add($"Event exceeds the {MaximumJsonBytes}-byte contract limit.");
        }
        catch (JsonException)
        {
            errors.Add("Event cannot be represented as valid JSON.");
        }
        return errors;
    }

    private static void Visit(
        GenUiComponent component,
        int depth,
        HashSet<string> ids,
        ref int count,
        List<string> errors)
    {
        count++;
        if (depth > MaximumDepth)
        {
            errors.Add($"Component tree exceeds maximum depth {MaximumDepth}.");
            return;
        }
        if (component is null)
        {
            errors.Add("Component tree contains a null node.");
            return;
        }
        if (string.IsNullOrWhiteSpace(component.ComponentId)) errors.Add("Every component requires a stable ID.");
        else if (!ids.Add(component.ComponentId)) errors.Add($"Duplicate component ID '{component.ComponentId}'.");
        if (!TrustedComponentTypes.Contains(component.ComponentType))
            errors.Add($"Component '{component.ComponentType}' is not in the trusted HavenUI vocabulary.");
        if (component.Properties is null || component.Actions is null || component.Children is null)
        {
            errors.Add($"Component '{component.ComponentId}' has a missing properties, actions, or children collection.");
            return;
        }
        foreach (var key in component.Properties.Keys)
            if (ForbiddenPropertyNames.Contains(key)) errors.Add($"Property '{key}' is forbidden in generated UI.");
        if (component.Actions.Any(action => action is null))
        {
            errors.Add($"Component '{component.ComponentId}' contains a null action.");
            return;
        }
        if (component.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != component.Actions.Count)
            errors.Add($"Component '{component.ComponentId}' has duplicate action IDs.");
        foreach (var action in component.Actions)
        {
            foreach (var error in Validate(action)) errors.Add(error);
        }
        if (component.Properties.TryGetValue("actionId", out var actionIdValue))
        {
            if (actionIdValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(actionIdValue.GetString())
                || !component.Actions.Any(action => action.ActionId.Equals(actionIdValue.GetString(), StringComparison.Ordinal)))
                errors.Add($"Component '{component.ComponentId}' actionId must select one declared action.");
        }
        else if (component.Actions.Count > 1)
        {
            errors.Add($"Component '{component.ComponentId}' has multiple actions and requires an actionId selector.");
        }
        foreach (var child in component.Children) Visit(child, depth + 1, ids, ref count, errors);
    }

    public static GenUiActionBinding? SelectActionBinding(GenUiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.Actions is null || component.Actions.Count == 0) return null;
        if (component.Properties is null) return null;
        if (component.Properties.TryGetValue("actionId", out var selected))
        {
            if (selected.ValueKind != JsonValueKind.String) return null;
            var actionId = selected.GetString();
            return component.Actions.FirstOrDefault(action => action is not null && action.ActionId.Equals(actionId, StringComparison.Ordinal));
        }
        return component.Actions.Count == 1 ? component.Actions[0] : null;
    }
}
