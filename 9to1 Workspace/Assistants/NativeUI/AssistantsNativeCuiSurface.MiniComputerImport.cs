using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.MiniComputer;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private long _miniComputerImportRequest;
    private Task ReadOriginalMiniComputerImportAsync(AssistantConversationBinding binding, long generation, CancellationToken token) =>
        DispatchOriginalMiniComputerImportAsync("assistants.mini.import.inspect", binding, generation, token);
    private async Task DispatchOriginalMiniComputerImportAsync(string command, AssistantConversationBinding binding,
        long generation, CancellationToken token)
    {
        if (_miniComputerManagement is not IAssistantMiniComputerImportController imports) return;
        var request = Interlocked.Increment(ref _miniComputerImportRequest);
        PublishSynchronous(() => MiniComputerBindings.SetBusy(true));
        try
        {
            AssistantMiniComputerImportPreview actual;
            if (command == "assistants.mini.import.inspect")
                actual = await SourceAsync(() => imports.InspectImportAsync(binding, token));
            else
            {
                var preview = MiniComputerBindings.OriginalImport ?? throw new InvalidOperationException("Review the actual catalogue import first.");
                var action = command switch
                {
                    "assistants.mini.import.request" => AssistantMiniComputerImportAction.Request,
                    "assistants.mini.import.refresh" => AssistantMiniComputerImportAction.Refresh,
                    "assistants.mini.import.complete" => AssistantMiniComputerImportAction.Complete,
                    "assistants.mini.import.retry" => AssistantMiniComputerImportAction.RetryAudit,
                    _ => throw new InvalidOperationException("No original catalogue import action was selected.")
                };
                actual = await SourceAsync(() => imports.ImportAsync(preview, action, token));
            }
            if (!Current()) return;
            PublishSynchronous(() => { if (Current()) MiniComputerBindings.SetImport(actual); });
            if (actual.CanBrowse && Current()) await ReadOriginalMiniComputerAsync(binding, generation, token);
        }
        catch (Exception cause)
        { if (Current()) PublishSynchronous(() => MiniComputerBindings.SetError(cause.Message)); throw; }
        finally { if (Current()) PublishSynchronous(() => { if (Current()) MiniComputerBindings.SetBusy(false); }); }
        bool Current() => IsMiniComputerCurrent(binding, generation) && request == Volatile.Read(ref _miniComputerImportRequest);
    }
}
