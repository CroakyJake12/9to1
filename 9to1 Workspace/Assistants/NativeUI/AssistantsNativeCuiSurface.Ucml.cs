using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private readonly IAssistantUcmlFilePicker? _ucmlFilePicker;
    public IAssistantUcmlFilePicker? OriginalUcmlFilePicker => _ucmlFilePicker;

    private async Task DispatchUcmlAsync(string command, object? parameter,
        AssistantConversationBinding target, long generation, CancellationToken token)
    {
        var owner = _memoryManagement as IAssistantUcmlMemoryManagementController
            ?? throw new InvalidOperationException("The canonical UCML memory owner is unavailable.");
        var picker = _ucmlFilePicker ?? throw new InvalidOperationException("The native UCML file picker is unavailable.");
        if (command == "assistants.memory.ucml.inspect")
        {
            var selected = parameter as AssistantsMemoryCuiBindings.UcmlImportRow
                ?? throw new InvalidOperationException("Select an actual reviewed file.");
            PublishSynchronous(() => MemoryBindings.SelectUcmlRow(selected)); return;
        }
        if (command == "assistants.memory.ucml.clear")
        { PublishSynchronous(MemoryBindings.ClearUcmlReview); return; }
        var view = MemoryBindings.OriginalView ?? throw new InvalidOperationException("Read current permitted memory first.");
        bool Current() => IsMemoryCurrent(target, generation) && ReferenceEquals(view, MemoryBindings.OriginalView);
        PublishSynchronous(() => MemoryBindings.SetBusy(true));
        AssistantsMemoryCuiBindings.UcmlImportRow? pending = null;
        try
        {
            if (command == "assistants.memory.ucml.pick")
            {
                var files = await SourceAsync(() => picker.PickImportAsync(token));
                if (files.Count != 0 && Current()) PublishSynchronous(() => MemoryBindings.SetUcmlReview(view, files));
            }
            else if (command == "assistants.memory.ucml.export")
            {
                var row = parameter as AssistantsMemoryCuiBindings.MemoryRow
                    ?? throw new InvalidOperationException("Select the current canonical memory row to export.");
                if (!view.Records.Any(record => ReferenceEquals(record, row.Original)))
                    throw new InvalidOperationException("Refresh and select the current memory before export.");
                var saved = await SourceAsync(() => picker.SaveExportAsync(row.Id + ".ucml", exportToken =>
                {
                    if (!Current()) throw new UnauthorizedAccessException("The original memory selection changed while choosing its destination.");
                    return SourceAsync(() => owner.ExportUcmlAsync(view, row.Original, exportToken));
                }, token));
                if (Current()) PublishSynchronous(() => MemoryBindings.SetUcmlExportStatus(saved));
            }
            else if (command == "assistants.memory.ucml.confirm")
            {
                // Each exact file obtains its own original Home decision. Import never
                // changes the Assistant's model, enabled categories or disclosure policy.
                var ready = MemoryBindings.OriginalUcmlRows.Where(row => row.State == AssistantsMemoryCuiBindings.UcmlImportState.Ready).ToArray();
                foreach (var row in ready)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Current()) throw new UnauthorizedAccessException("The current Assistant memory scope changed during import.");
                    pending = row;
                    PublishSynchronous(() => MemoryBindings.BeginUcmlImport(row));
                    var preview = await SourceAsync(() => owner.PrepareUcmlImportAsync(view, row.Serialized, row.OperationId, token));
                    if (!Current()) throw new UnauthorizedAccessException("The current memory review changed before submission to Home.");
                    PublishSynchronous(() => MemoryBindings.SetUcmlPreview(row, preview));
                    var actual = SourceAsync(() => _memoryManagement!.CommitAsync(preview, token));
                    Exception? publicationFailure = null;
                    try
                    {
                        while (!actual.IsCompleted && Current())
                        {
                            if (_memoryManagement is IAssistantMemoryWriteObservationSource observer)
                                PublishSynchronous(() => MemoryBindings.ObserveUcml(row, observer.ObserveOriginalWrite(preview)));
                            await SourceAsync(() => Task.Delay(250));
                        }
                    }
                    catch (Exception failure) { publicationFailure = failure; Add(failure); }
                    var result = await actual; // Independently join the SAME original, including after presentation failure.
                    if (publicationFailure is not null)
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(publicationFailure).Throw();
                    if (Current()) PublishSynchronous(() => MemoryBindings.AcknowledgeUcml(row, result));
                    pending = null;
                }
                if (Current()) await ReadOriginalMemoryAsync(target, generation, token);
            }
        }
        catch (Exception failure)
        {
            if (IsMemoryCurrent(target, generation)) PublishSynchronous(() =>
            {
                if (pending is not null) MemoryBindings.MarkUcmlUnconfirmed(pending, failure.Message);
                else MemoryBindings.SetError(failure.Message);
            });
            throw;
        }
        finally { if (IsMemoryCurrent(target, generation)) PublishSynchronous(() => MemoryBindings.SetBusy(false)); }
    }
}
