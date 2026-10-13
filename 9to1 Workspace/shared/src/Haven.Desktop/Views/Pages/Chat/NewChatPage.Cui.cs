using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private ChatCuiActions CreateCuiChatActions() => new(
        SubmitCurrentInstructionAsync,
        () => RunChatOriginalAsync(() => { TryStopResponse(); return Task.CompletedTask; }),
        () => RunChatOriginalAsync(() => { _bus.Fire("Chat.Problems.Resolve.Click"); ShowResolveProblems(); return Task.CompletedTask; }),
        action => RunChatOriginalAsync(async () =>
        {
            if (_activeMention is not null) ConsumeActiveMention();
            if (action == AddMenu.AddMenuAction.File) await AwaitChatOriginalAsync(AddFileAsync());
            DemandOriginalChatPublication();
            AddActionSelected?.Invoke(this, action);
        }),
        selection => RunChatOriginalAsync(() =>
        {
            if (_activeMention is not null) ConsumeActiveMention();
            ApplyAddSelection(selection);
            DemandOriginalChatPublication();
            AddCatalogItemSelected?.Invoke(this, selection);
            FocusComposer();
            return Task.CompletedTask;
        }),
        request => RunChatOriginalAsync(() => OnMessageActionRequestedOriginalBodyAsync(this, request)),
        request => RunChatOriginalAsync(() => OnMarkdownCodeActionRequestedOriginalBodyAsync(this, request)),
        RemoveContextEntryAsync,
        id => RunChatOriginalAsync(() => OnAttachmentRemoveRequestedOriginalBodyAsync(this, id)),
        () => RunChatOriginalAsync(() => OnDualToggleRequestedOriginalBodyAsync(this, EventArgs.Empty)),
        () => RunChatOriginalAsync(() => OnDualModelPickerRequestedOriginalBodyAsync(this, EventArgs.Empty)),
        key => RunChatOriginalAsync(() => { OnDualSecondModelChosenOriginalBody(this, key); return Task.CompletedTask; }))
    {
        ChooseTypeAndLocation = () => RunChatOriginalAsync(ShowCuiChatTypeAsync),
        ChooseModelAndReasoning = () => RunChatOriginalAsync(ShowCuiModelChoicesAsync),
        ChooseToolApproval = () => RunChatOriginalAsync(ShowCuiToolApprovalModesAsync)
    };

    private Task ShowCuiChatTypeAsync()
    {
        DemandOriginalChatPublication();
        var original = CapturePresentationGuard();
        var choices = new List<(string Label, Func<Task> Action)>();
        if (_conversation.IsTemporary)
            choices.Add(("Save as Normal · Local", () => RunChatOriginalAsync(async () =>
            {
                if (!original()) throw new InvalidOperationException("This chat choice belongs to an earlier conversation.");
                await AwaitChatOriginalAsync(SetTemporaryAsync(false));
                RefreshCuiConversationHeader();
            })));
        else if (!HasStarted)
            choices.Add(("Temporary · Local", () => RunChatOriginalAsync(async () =>
            {
                if (!original()) throw new InvalidOperationException("This chat choice belongs to an earlier conversation.");
                await AwaitChatOriginalAsync(SetTemporaryAsync(true));
                RefreshCuiConversationHeader();
            })));
        _scene.ShowChoicePrompt("Chat type and location",
            _conversation.IsTemporary
                ? "Temporary chats remain on this device until you explicitly save them. Model execution has its own permissions."
                : "This conversation is stored on this device. Multi-Agent, Mesh and Cloud are not yet available in this native workspace.", choices);
        return Task.CompletedTask;
    }

    private async Task ShowCuiModelChoicesAsync()
    {
        var original = CapturePresentationGuard();
        var models = await AwaitChatOriginalAsync(_ollama.GetModelsAsync(RequireChatOriginal().Token));
        if (!original()) throw new InvalidOperationException("The model picker belongs to an earlier conversation.");
        _scene.ShowChoicePrompt("Choose a model", "Available models reported by your configured runtime.",
            models.Select(model => (model.Name, (Func<Task>)(() => RunChatOriginalAsync(() =>
            {
                if (!original()) throw new InvalidOperationException("This model choice belongs to an earlier conversation.");
                SelectModel(model);
                RefreshCuiConversationHeader();
                return Task.CompletedTask;
            })))).ToArray());
    }

    private void RefreshCuiConversationHeader()
    {
        _scene.SetConversationHeader(_conversation.Title, _conversation.IsTemporary ? "Temporary" : "Normal", "Local", _selectedModel?.Name);
        _scene.SetApprovalLabel(ToolApprovalLabel(CurrentToolApprovalMode));
    }
}
