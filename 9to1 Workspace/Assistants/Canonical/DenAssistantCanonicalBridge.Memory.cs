using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    private sealed record OriginalAssistantMemoryRequest(AssistantDefinitionSnapshot Definition,
        IChatOriginalPersistentMemoryInput? Input);

    private async Task<OriginalAssistantMemoryRequest> PrepareOriginalAssistantMemoryRequestAsync(
        AssistantConversationBinding binding, AssistantDefinitionSnapshot actualDefinition,
        CancellationToken token)
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        DemandSameMemoryDefinition(actualDefinition, current.Definition);
        if (!actualDefinition.Configuration.Memory.Enabled) return new(actualDefinition, null);
        if (_memory is null || !_originals.Invoke(() => _chat.HasOriginalPersistentMemorySource(_memory) &&
            _memory.HasOriginalComposition(_home, _conversations)))
            throw new AssistantCommandRefusedException("Persistent-memory opt-in requires the SAME composed scoped source; the saved input remains available.");
        var prepared = await _originals.Source(() => _memory.PrepareOriginalAssistantMemoryInputWithinSourceAsync(
            binding, actualDefinition, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
        var input = _originals.Invoke(() => prepared.IsPrepared ? prepared.Input : null);
        if (input is null)
            throw new AssistantCommandRefusedException(_originals.Invoke(() => prepared.Reason));
        if (!_originals.Invoke(() => _memory.IsIssuedOriginalInput(input)))
            throw new InvalidOperationException("The actual Assistant memory source did not issue this preparation input.");
        var request = new OriginalAssistantMemoryRequest(actualDefinition, input);
        await DemandCurrentOriginalAssistantMemoryRequestAsync(binding, request, token).ConfigureAwait(false);
        return request;
    }

    private async Task DemandCurrentOriginalAssistantMemoryRequestAsync(AssistantConversationBinding binding,
        OriginalAssistantMemoryRequest request, CancellationToken token)
    {
        var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        DemandSameMemoryDefinition(request.Definition, current.Definition);
        if (!current.Definition.Configuration.Memory.Enabled)
        {
            if (request.Input is not null)
                throw new InvalidOperationException("Disabled memory cannot carry a positive live input.");
            return;
        }
        if (_memory is null || request.Input is not { } input ||
            !_originals.Invoke(() => _chat.HasOriginalPersistentMemorySource(_memory) &&
                _memory.HasOriginalComposition(_home, _conversations) && _memory.IsIssuedOriginalInput(input)))
            throw new AssistantCommandRefusedException("The SAME live memory preparation is unavailable before model dispatch.");
        // Preparation already authenticates its current Home/store resource receipt.
        // The canonical Task has not been admitted here: do not invent a null Task
        // context or a copied snapshot. SAME Chat validates the source with its actual
        // issued Task/Run/attempt before reading content and again before dispatch.
    }

    private static void DemandSameMemoryDefinition(AssistantDefinitionSnapshot expected,
        AssistantDefinitionSnapshot current)
    {
        if (expected.Identity != current.Identity || expected.Kind != current.Kind || expected.Revision != current.Revision ||
            JsonSerializer.Serialize(expected.Configuration, DenJson.Options) != JsonSerializer.Serialize(current.Configuration, DenJson.Options))
            throw new AssistantCommandRefusedException("The configured identity or memory request changed; refresh the current observation.");
    }

    private static GenerationOptions OriginalMemoryRoutingOptions(OriginalAssistantMemoryRequest request) => new()
    {
        RequestedRoutingConstraints = new(request.Definition.Configuration.Model.AllowCloud,
            request.Definition.Configuration.Model.AllowFallback),
        RequestedContextConstraints = new(request.Definition.Configuration.Memory.Enabled)
        {
            RequireOriginalPersistentMemoryInput = request.Definition.Configuration.Memory.Enabled
        },
        OriginalPersistentMemoryInput = request.Input
    };
}
