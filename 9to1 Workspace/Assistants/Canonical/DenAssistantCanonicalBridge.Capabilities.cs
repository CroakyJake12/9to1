using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    // PRIVATE source proposal. Requires Files' genuine configured catalogue owner
    // and Spaces' SAME membership/project dispatch owner; no permission is created here.
    private sealed record OriginalTaskCapabilities(AssistantDefinitionSnapshot Definition,
        AssistantOriginalCapabilitySelection? Selection, ITaskRunColdOriginalProjectInput? OriginalProjectInput, IReadOnlyList<ActiveCapability> Active,
        IReadOnlyList<ToolCapability> RequiredModel, PermissionMode FilePermission,
        PermissionMode CommandPermission, PermissionMode BrowserPermission,
        ModelRequestToolSelectionConstraints ToolSelection);

    private void CapabilityScope(Action body) => _originals.Invoke(() => { body(); return true; });

    private async Task<OriginalTaskCapabilities> ReadOriginalTaskCapabilitiesAsync(
        AssistantConversationBinding binding, AssistantDefinitionSnapshot definition,
        ModelDescriptor actualModel, ITaskRunColdOriginalProjectInput? actualProjectInput,
        CancellationToken token)
    {
        if (_capabilities is null)
        {
            if (definition.Configuration.ToolIds.Count != 0 || definition.Configuration.ConnectedAppIds.Count != 0 ||
                definition.Configuration.ComputerUseRequested)
                throw new AssistantCommandRefusedException("The configured canonical capability owner is not composed; saved selections grant no tools.");
            return new(definition, null, actualProjectInput, [], [], PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, new([]));
        }

        var selected = await _originals.Source(() => _capabilities.ReadOriginalConfiguredCapabilitiesWithinSourceAsync(
            binding, definition, actualModel, CapabilityScope, _originals.Retain, token, actualProjectInput)).ConfigureAwait(false);
        if (!_originals.Invoke(() => _capabilities.IsIssuedOriginalSelection(selected)) ||
            !ReferenceEquals(selected.Binding, binding) || !SameOriginalConfiguredDefinition(selected.Definition, definition) ||
            selected.Actor != _membership.OriginalActor || !ReferenceEquals(selected.OriginalProjectInput, actualProjectInput))
            throw new AssistantCommandRefusedException("The actual configured capability source returned a foreign or stale selection.");

        // Returned collection callbacks remain inside the actual presentation source.
        // Detachment cannot erase an admitted query or manufacture model/resource rights.
        var snapshot = _originals.Invoke(() => (Active: selected.ActiveCapabilities.ToArray(),
            Required: selected.RequiredModelCapabilities.ToArray(), Support: selected.Observations.ToArray()));
        if (snapshot.Support.Any(observation => observation.State is AssistantSupportState.Unsupported or
                AssistantSupportState.NotConfigured or AssistantSupportState.RequiresInspection))
            throw new AssistantCommandRefusedException("Some requested canonical capabilities are unavailable; inspect the current source observations before starting work.");
        if (snapshot.Required.Any(capability => !Enum.IsDefined(capability) || !actualModel.Supports(capability)))
            throw new AssistantCommandRefusedException("The fresh selected model does not support the actual narrowed capability requirement.");
        if (!Enum.IsDefined(selected.FilePermission) || !Enum.IsDefined(selected.CommandPermission) ||
            !Enum.IsDefined(selected.BrowserPermission))
            throw new AssistantCommandRefusedException("The actual source policy modes are unavailable.");
        if (actualProjectInput is null)
        {
            if (selected.OriginalWorkspaceRoot is not null || selected.FilePermission != PermissionMode.Ask ||
                selected.CommandPermission != PermissionMode.Ask || selected.BrowserPermission != PermissionMode.Ask)
                throw new AssistantCommandRefusedException("A resource-free Task selection cannot add workspace or broader policy authority.");
        }
        else if (selected.OriginalWorkspaceRoot != actualProjectInput.OriginalIdentity.CanonicalRoot)
            throw new AssistantCommandRefusedException("The configured capability plan does not match the SAME source-prepared current project input.");

        var constraints = _originals.Invoke(() => _capabilities.GetOriginalDispatchToolConstraints(selected));
        return new(definition, selected, actualProjectInput, Array.AsReadOnly(snapshot.Active), Array.AsReadOnly(snapshot.Required),
            selected.FilePermission, selected.CommandPermission, selected.BrowserPermission, constraints);
    }

    private async Task DemandCurrentOriginalTaskCapabilitiesAsync(AssistantConversationBinding binding,
        OriginalTaskCapabilities selected, CancellationToken token)
    {
        if (selected.Selection is { } actual)
        {
            var owner = _capabilities ?? throw new AssistantCommandRefusedException("The actual capability owner changed.");
            await _originals.Source(() => owner.RevalidateOriginalSelectionWithinSourceAsync(
                actual, CapabilityScope, _originals.Retain, token, selected.OriginalProjectInput)).ConfigureAwait(false);
            if (!_originals.Invoke(() => owner.IsIssuedOriginalSelection(actual) &&
                    selected.ToolSelection.Equals(owner.GetOriginalDispatchToolConstraints(actual))) ||
                !ReferenceEquals(actual.OriginalProjectInput, selected.OriginalProjectInput))
                throw new AssistantCommandRefusedException("The SAME configured source selection is no longer current.");
        }
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        if (!SameOriginalConfiguredDefinition(current.Definition, selected.Definition))
            throw new AssistantCommandRefusedException("The configured identity changed before original Task dispatch.");
        token.ThrowIfCancellationRequested();
    }

    private static bool SameOriginalConfiguredDefinition(AssistantDefinitionSnapshot first,
        AssistantDefinitionSnapshot second) => first.Identity == second.Identity && first.Revision == second.Revision &&
        first.Kind == second.Kind && JsonSerializer.Serialize(first.Configuration, DenJson.Options) ==
            JsonSerializer.Serialize(second.Configuration, DenJson.Options);
}
