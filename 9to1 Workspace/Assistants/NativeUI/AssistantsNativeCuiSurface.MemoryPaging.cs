using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private long _memoryReadRequest;
    private async Task ReadOriginalMemoryAsync(AssistantConversationBinding target, long generation, CancellationToken token,
        AssistantMemoryPageContinuation? continuation = null)
    {
        if (_memoryManagement is null || !IsMemoryCurrent(target, generation)) return;
        if (target.Conversation.Mode != HavenMode.Chat || target.Conversation.Kind != ConversationKind.Chat)
        { PublishSynchronous(() => MemoryBindings.SetUnavailable("Open an ordinary conversation with this Assistant to review or add its private preferences.")); return; }
        var request = Interlocked.Increment(ref _memoryReadRequest);
        var searchRevision = MemoryBindings.SearchRevision; var search = MemoryBindings.OriginalSearch;
        bool CurrentRead() => IsMemoryCurrent(target, generation) && request == Interlocked.Read(ref _memoryReadRequest) &&
            searchRevision == MemoryBindings.SearchRevision;
        PublishSynchronous(() => MemoryBindings.SetBusy(true));
        try
        {
            var imports = _memoryManagement as IAssistantMemoryImportManagementController;
            var useImports = imports?.HasOriginalImportSession == true && target.Definition.Configuration.Memory.Enabled;
            if (CurrentRead()) PublishSynchronous(() => { if (CurrentRead()) MemoryBindings.SetImportSupport(useImports); });
            if (useImports)
            {
                // Import observations never grant content access. A completed actual
                // Home review is followed by the unchanged protected memory reader.
                var observed = await SourceAsync(() => imports!.InspectImportAsync(target, token));
                if (CurrentRead()) PublishSynchronous(() => { if (CurrentRead()) MemoryBindings.SetImportPreview(observed); });
                if (!observed.CanRead || !CurrentRead() || !MemoryBindings.IsImportReadAvailable) return;
            }
            var actual = _memoryManagement is IAssistantMemoryManagementPagingController paging
                ? await SourceAsync(() => paging.ReadPageAsync(target, 32, search, continuation, token))
                : await SourceAsync(() => _memoryManagement.ReadAsync(target, token));
            if (CurrentRead()) PublishSynchronous(() => { if (CurrentRead()) MemoryBindings.SetView(actual); });
        }
        catch (Exception failure)
        {
            if (CurrentRead()) PublishSynchronous(() => { if (CurrentRead()) MemoryBindings.SetError(failure.Message); });
            throw;
        }
        finally
        {
            if (CurrentRead()) PublishSynchronous(() => { if (CurrentRead()) MemoryBindings.SetBusy(false); });
        }
    }
}
