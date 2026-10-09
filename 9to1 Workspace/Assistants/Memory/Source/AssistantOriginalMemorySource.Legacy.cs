using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    private async Task<AssistantOriginalLegacyMemoryReceipt?> PrepareLegacyMemoryAsync(
        AssistantConversationBinding binding, ResourceStoreIdentity store,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        if (_legacyMemory is null) return null;
        var receipt = await source.Read(() => _legacyMemory.PrepareOriginalLegacyMemoryWithinSourceAsync(
            binding, _home, _ownership, store, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (receipt is not null && !ObserveLegacyReceipt(receipt, source))
            throw new UnauthorizedAccessException("The configured original migration source did not issue this live memory lineage.");
        return receipt;
    }

    private bool ObserveLegacyReceipt(AssistantOriginalLegacyMemoryReceipt receipt, AssistantMemoryOriginals.Scope source)
    {
        var issued = false; source.Run(() => issued = _legacyMemory!.IsIssuedOriginalLegacyMemoryReceipt(receipt)); return issued;
    }

    private async Task DemandLegacyMemoryAsync(Input input, AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        if (input.Legacy is null)
        {
            if (input.ReadScope.AllowVerifiedLegacy)
                throw new UnauthorizedAccessException("Legacy recall requires its actual current original source receipt.");
            return;
        }
        if (_legacyMemory is null || !ObserveLegacyReceipt(input.Legacy, source) ||
            !await source.Read(() => _legacyMemory.ValidateOriginalLegacyMemoryWithinSourceAsync(
                input.Legacy, source.Run, source.Retain, token)).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual migrated identity, original saved Agent source or current Home import changed. Reopen memory.");
    }
}
