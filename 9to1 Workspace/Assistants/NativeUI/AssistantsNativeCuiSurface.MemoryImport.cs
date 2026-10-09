using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task DispatchOriginalMemoryImportAsync(string command, AssistantConversationBinding target,
        long generation, CancellationToken token)
    {
        var owner = _memoryManagement as IAssistantMemoryImportManagementController
            ?? throw new InvalidOperationException("The configured memory import owner is unavailable.");
        var preview = MemoryBindings.OriginalImportPreview
            ?? throw new InvalidOperationException("Inspect this exact configured memory store first.");
        var request = Interlocked.Increment(ref _memoryReadRequest);
        var searchRevision = MemoryBindings.SearchRevision;
        bool CurrentImport() => IsMemoryCurrent(target, generation) && request == Interlocked.Read(ref _memoryReadRequest) &&
            searchRevision == MemoryBindings.SearchRevision;
        PublishSynchronous(() => MemoryBindings.SetBusy(true));
        try
        {
            var actual = await SourceAsync(() => command switch
            {
                "assistants.memory.import.request" => owner.RequestImportAsync(preview, token),
                "assistants.memory.import.refresh" => owner.RefreshImportAsync(preview, token),
                "assistants.memory.import.complete" => owner.CompleteImportAsync(preview, token),
                "assistants.memory.import.audit" => owner.RetryImportAuditAsync(preview, token),
                _ => throw new InvalidOperationException("Unsupported original memory import operation.")
            });
            if (!CurrentImport()) return;
            PublishSynchronous(() => { if (CurrentImport()) MemoryBindings.SetImportPreview(actual); });
            if (actual.CanRead && CurrentImport() && MemoryBindings.IsImportReadAvailable) await ReadOriginalMemoryAsync(target, generation, token);
        }
        catch (Exception failure)
        {
            if (CurrentImport()) PublishSynchronous(() => { if (CurrentImport()) MemoryBindings.MarkImportUnconfirmed(failure.Message); });
            throw; // Every unknown original source remains owned through native close.
        }
        finally
        {
            if (CurrentImport()) PublishSynchronous(() => { if (CurrentImport()) MemoryBindings.SetBusy(false); });
        }
    }
}
