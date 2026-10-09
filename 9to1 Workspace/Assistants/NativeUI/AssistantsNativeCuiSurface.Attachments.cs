using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private IAssistantOriginalAttachmentPicker? _attachmentPicker;
    public void BindOriginalAttachmentPicker(IAssistantOriginalAttachmentPicker actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        using var physical = EnterPhysical();
        lock (_gate)
        {
            DemandCurrent();
            if (_initialization is not null) throw new InvalidOperationException("Bind the original file picker before this presentation is initialized.");
            if (_attachmentPicker is not null && !ReferenceEquals(_attachmentPicker, actual))
                throw new InvalidOperationException("This presentation already has its original file picker.");
            _attachmentPicker = actual;
        }
        PublishSynchronous(Bindings.ConfigureOriginalAttachmentPicker);
    }
    private void AttachmentPickerScope(Action callback)
    {
        using var physical = EnterPhysical(); DemandCurrent(); callback(); DemandCurrent();
    }
    private void RetainOriginalAttachmentPickerTask(Task actual)
    { ArgumentNullException.ThrowIfNull(actual); lock (_gate) _originals.Add(actual); }
    private async Task PickOriginalAttachmentAsync(CancellationToken token)
    {
        var picker = _attachmentPicker ?? throw new InvalidOperationException("File selection is unavailable in this window.");
        var binding = _controller.Snapshot.ConversationBinding ?? throw new InvalidOperationException("Open a conversation first.");
        var generation = PresentationGeneration;
        PublishSynchronous(() => { Bindings.SetAttachmentBusy(true); Bindings.SetAttachmentStatus("Choose a registered text, DOCX, PPTX or XLSX file from Files."); });
        try
        {
            await SaveDraftAsync(token);
            if (!IsPresentationCurrent(binding, generation)) return;
            var path = await SourceAsync(() => picker.PickOriginalAttachmentWithinSourceAsync(
                AttachmentPickerScope, RetainOriginalAttachmentPickerTask, token));
            if (!IsPresentationCurrent(binding, generation) || path is null) return;
            PublishSynchronous(() => Bindings.SetAttachmentStatus("Review access and attachment import in Home. Your draft is saved."));
            await SourceAsync(() => _controller.ImportAttachmentAsync(path,
                AssistantDraftAttachmentProjection.CurrentBranch(_controller.Snapshot.Conversation), token));
            if (!IsPresentationCurrent(binding, generation)) return;
            await RefreshOriginalAttachmentsAsync(binding, generation, "File attached to the saved draft.", token);
        }
        finally
        {
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() => Bindings.SetAttachmentBusy(false));
        }
    }
    private async Task DetachOriginalAttachmentAsync(object? item, CancellationToken token)
    {
        var row = Bindings.CurrentAttachmentRow(item);
        if (row is null) return;
        var binding = _controller.Snapshot.ConversationBinding ?? throw new InvalidOperationException("Open a conversation first.");
        var generation = PresentationGeneration;
        PublishSynchronous(() => { Bindings.SetAttachmentBusy(true); Bindings.SetAttachmentStatus("Review removal from this draft in Home. The original file will be kept."); });
        try
        {
            await SaveDraftAsync(token);
            if (!IsPresentationCurrent(binding, generation)) return;
            // The actual source independently proves the saved draft still references
            // this original record. A UI row or attachment ID grants no write.
            await SourceAsync(() => _controller.RemoveAttachmentAsync(row.Original.Id, token));
            if (!IsPresentationCurrent(binding, generation)) return;
            await RefreshOriginalAttachmentsAsync(binding, generation, "Attachment removed from the saved draft. Original file kept.", token);
        }
        finally
        {
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() => Bindings.SetAttachmentBusy(false));
        }
    }
    private async Task RefreshOriginalAttachmentsAsync(AssistantConversationBinding binding, long generation,
        string message, CancellationToken token)
    {
        var saved = await SourceAsync(() => _controller.RefreshWorkAsync(token));
        if (!IsPresentationCurrent(binding, generation)) return;
        PublishSynchronous(() =>
        {
            if (!IsPresentationCurrent(binding, generation)) return;
            var current = _controller.Snapshot;
            var actual = current.Revision >= saved.Revision ? current : saved;
            if (actual.Revision >= _appliedSnapshot) ApplySnapshot(actual);
            Bindings.SetAttachmentStatus(message);
        });
    }
}
