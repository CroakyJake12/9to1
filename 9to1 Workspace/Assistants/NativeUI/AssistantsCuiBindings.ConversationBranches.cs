using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private bool _branchBusy;
    private IReadOnlyList<ConversationBranch>? _branchSource;
    private ConversationBranchRow[] _branchRows = [];
    private bool CanChangeConversationBranch => !Busy && IsOriginalConversationBranchContextAvailable;
    internal bool IsOriginalConversationBranchContextAvailable => !_unavailable && Current && !_snapshot.IsRetiring &&
        IsIdentityEnabled && _snapshot.ConversationBinding is not null && !_streaming &&
        !_snapshot.IsLoading && !_snapshot.IsSaving && !_attachmentBusy &&
        !_configurationInitializationBusy && !_configurationModelsBusy &&
        !_projectBusy && !_hasPendingProjectSubmission && _draft?.IsDirty != true &&
        !HasUnsettledBranchTask(_snapshot.Conversation?.CanonicalTask) && !HasUnsettledBranchTask(_snapshot.Work?.CanonicalTask);
    private static bool HasUnsettledBranchTask(TaskExecutionSnapshot? task) => task is not null &&
        task.State is not (TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Failed or TaskExecutionLifecycle.Cancelled);

    internal void SetConversationBranchBusy(bool value) { _branchBusy = value; Refresh(); }
    internal void ApplyOriginalConversationBranchDraft(AssistantConversationData? data)
    {
        _promptConversation = data?.Conversation.Id;
        _prompt = data?.Draft?.Content ?? "";
    }
    internal ConversationBranchRow? CurrentConversationBranchRow(object? item) =>
        CanChangeConversationBranch && item is ConversationBranchRow row && !row.Original.IsCurrent &&
        _branchRows.Any(actual => ReferenceEquals(actual, row)) &&
        _snapshot.Conversation?.Branches.Any(actual => ReferenceEquals(actual, row.Original)) == true ? row : null;
    internal AssistantMessagePresentation? CurrentBranchMessage(object? item) =>
        CanChangeConversationBranch && item is AssistantMessagePresentation row && ContainsOriginalRow(row, "Messages") &&
        _snapshot.Conversation?.Messages.Any(actual => actual.Id == row.Id && actual.Content == row.Content &&
            !actual.IsCompacted && actual.Role is MessageRole.User or MessageRole.Assistant) == true ? row : null;

    private void RefreshConversationBranches()
    {
        var data = _snapshot.Conversation;
        if (!ReferenceEquals(_branchSource, data?.Branches))
        {
            _branchSource = data?.Branches;
            _branchRows = (data?.Branches ?? []).Where(branch => branch.ConversationId == data!.Conversation.Id)
                .Select(branch => new ConversationBranchRow(branch)).ToArray();
        }
        Set("ConversationBranches", _branchRows); Set("HasConversationBranches", _branchRows.Length != 0);
        Set("CanContinueConversationBranch", CanChangeConversationBranch);
        Set("ConversationBranchHelp", _branchBusy ? "Opening this branch…"
            : _streaming || HasUnsettledBranchTask(data?.CanonicalTask) || HasUnsettledBranchTask(_snapshot.Work?.CanonicalTask)
                ? "Finish the current response or task before changing branches."
            : _draft?.IsDirty == true ? "Save or discard configuration changes before changing branches."
            : "Continue from a message to try another direction. Each branch keeps its own draft; earlier history stays saved.");
        _publish(() =>
        {
            if (!Current) return;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("CanOpen"));
            if (Current) PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("CanContinue"));
        });
    }
    public sealed class ConversationBranchRow
    {
        internal ConversationBranchRow(ConversationBranch original) => Original = original;
        internal ConversationBranch Original { get; }
        public Guid Id => Original.Id;
        public string Label => Original.Name + (Original.IsCurrent ? " · current" : "");
    }
}
