using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private Guid? _displayedBranchConversation, _displayedBranch;
    private void ApplyOriginalConversationBranchPresentation(AssistantsWorkspaceSnapshot snapshot)
    {
        var data = snapshot.Conversation;
        var branch = AssistantDraftAttachmentProjection.CurrentBranch(data);
        if (_displayedBranchConversation == data?.Conversation.Id && _displayedBranch == branch) return;
        _displayedBranchConversation = data?.Conversation.Id; _displayedBranch = branch;
        // A different branch revokes the old rendered message actions even though
        // its canonical conversation/Assistant identity intentionally stays the same.
        Interlocked.Increment(ref _presentationGeneration);
        _conversationTarget = data is null ? null : _conversation.Bind(data);
        if (data is null) _conversation.Clear();
        _savedPromptConversation = data?.Conversation.Id; _savedPrompt = data?.Draft?.Content ?? "";
        Bindings.ApplyOriginalConversationBranchDraft(data);
    }

    private async Task ChangeOriginalConversationBranchAsync(object? item, bool create, CancellationToken token)
    {
        var row = create ? null : Bindings.CurrentConversationBranchRow(item);
        var message = create ? Bindings.CurrentBranchMessage(item) : null;
        if (create ? message is null : row is null) return;
        var binding = _binding;
        if (binding is null) return;
        var generation = PresentationGeneration;
        var originalMessage = create ? _controller.Snapshot.Conversation?.Messages.SingleOrDefault(actual =>
            actual.Id == message!.Id && actual.Content == message.Content) : null;
        if (create && originalMessage is null) return;
        if (!await PrepareNavigationAsync(token) || !IsPresentationCurrent(binding, generation)) return;
        PublishSynchronous(() =>
        {
            var actual = _controller.Snapshot;
            if (actual.Revision >= _appliedSnapshot) ApplySnapshot(actual);
        });
        if (!IsPresentationCurrent(binding, generation) ||
            (create ? !ReferenceEquals(message, Bindings.CurrentBranchMessage(item)) : !ReferenceEquals(row, Bindings.CurrentConversationBranchRow(item)))) return;
        if (create && !_conversation.IsCurrentMessage(_conversationTarget, message!)) return;
        PublishSynchronous(() => Bindings.SetConversationBranchBusy(true));
        try
        {
            // Re-read actual current work after saving the draft. A Task may have
            // started independently since this rendered row was published.
            await SourceAsync(() => _controller.RefreshWorkAsync(token));
            if (!IsPresentationCurrent(binding, generation)) return;
            PublishSynchronous(() =>
            {
                var current = _controller.Snapshot;
                if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
            });
            if (!IsPresentationCurrent(binding, generation) || !Bindings.IsOriginalConversationBranchContextAvailable) return;
            var currentData = _controller.Snapshot.Conversation;
            // The original admitted row is private custody, not a new row chosen
            // by ID. Compare its complete original source value with fresh data.
            if (create ? currentData?.Messages.Any(actual => actual == originalMessage) != true :
                currentData?.Branches.Any(actual => actual == row!.Original && !actual.IsCurrent) != true) return;
            if (create && !_conversation.IsCurrentMessage(_conversationTarget, message!)) return;
            if (create)
                await SourceAsync(() => _controller.CreateBranchAsync(message!.Id,
                    "Branch " + ((_controller.Snapshot.Conversation?.Branches.Count ?? 0) + 1), token));
            else await SourceAsync(() => _controller.SwitchBranchAsync(row!.Id, token));
            if (!IsPresentationCurrent(binding, generation)) return;
            var saved = await SourceAsync(() => _controller.RefreshWorkAsync(token));
            if (IsRetiring || !ReferenceEquals(_controller.Snapshot.ConversationBinding, binding)) return;
            PublishSynchronous(() =>
            {
                var current = _controller.Snapshot;
                var actual = current.Revision >= saved.Revision ? current : saved;
                if (actual.Revision >= _appliedSnapshot) ApplySnapshot(actual);
                Bindings.SetConversationStatus(create ? "New branch opened. Earlier history and its draft are saved." : "Branch opened with its saved draft.");
            });
        }
        finally { if (!IsRetiring) PublishSynchronous(() => Bindings.SetConversationBranchBusy(false)); }
    }
}
