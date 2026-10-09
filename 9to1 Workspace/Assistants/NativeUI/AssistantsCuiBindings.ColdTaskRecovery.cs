using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private AssistantConversationBinding? _coldTaskBinding;
    private AssistantColdTaskRecoveryPreview? _coldTaskPreview;
    private bool _coldTaskBusy;
    private bool HasOriginalColdTask => Current && _snapshot.Work is { RequiresOwnerRenewal: true, CanonicalTask: { } task } &&
        _snapshot.ConversationBinding is { } binding && task.ContextId == binding.Conversation.Id;
    private bool CanReviewOriginalColdTask => HasOriginalColdTask && !_coldTaskBusy && !Busy && !_streaming;
    private bool CanRequestOriginalColdTask => CanReviewOriginalColdTask && _coldTaskPreview?.CanRequestResume == true;
    internal void SetColdTaskRecoveryBusy(bool value) { _coldTaskBusy = value; Refresh(); }
    internal void PublishOriginalColdTaskPreview(AssistantConversationBinding binding, AssistantColdTaskRecoveryPreview actual)
    {
        if (!Current || !ReferenceEquals(binding, _snapshot.ConversationBinding) || !IsOriginalColdTaskPreviewCurrent(actual)) return;
        _coldTaskBinding = binding; _coldTaskPreview = actual; Refresh();
    }
    internal AssistantColdTaskRecoveryPreview CaptureOriginalColdTaskPreview() =>
        CanRequestOriginalColdTask && _coldTaskPreview is { } actual ? actual
            : throw new InvalidOperationException("Review this Task's recovery input first.");
    internal void ConsumeOriginalColdTaskPreview(AssistantColdTaskRecoveryPreview samePreview)
    { if (ReferenceEquals(_coldTaskPreview, samePreview)) { _coldTaskPreview = null; Refresh(); } }
    private bool IsOriginalColdTaskPreviewCurrent(AssistantColdTaskRecoveryPreview actual) =>
        _snapshot.Work is { RequiresOwnerRenewal: true, CanonicalTask: { } task } &&
        task.TaskId == actual.TaskId && task.ExecutionId == actual.RunId && task.ContextId == actual.ConversationId &&
        task.PersistenceRevision == actual.Revision;
    private void RefreshColdTaskRecovery()
    {
        if (!ReferenceEquals(_coldTaskBinding, _snapshot.ConversationBinding))
        { _coldTaskBinding = _snapshot.ConversationBinding; _coldTaskPreview = null; _coldTaskBusy = false; }
        if (_coldTaskPreview is { } preview && !IsOriginalColdTaskPreviewCurrent(preview)) _coldTaskPreview = null;
        Set("HasColdTaskRecovery", HasOriginalColdTask);
        Set("CanReviewColdTaskRecovery", CanReviewOriginalColdTask);
        Set("CanResumeColdTaskRecovery", CanRequestOriginalColdTask);
        Set("ColdTaskRecoveryReason", _coldTaskPreview?.Reason ?? "This Task was saved in an earlier session. Review recovery before continuing.");
    }
}
