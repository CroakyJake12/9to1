using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatCuiScene
{
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return ValueTask.FromException(new ObjectDisposedException(nameof(ChatCuiScene)));
        return new(_originalWork.RunAsync(async original =>
        {
            original.DemandPublication();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                switch (command)
                {
                    case "chat.send":
                        if (_safetyLocked || (!_modelAvailable && !_sending)) throw new InvalidOperationException("This chat cannot send with its current model and safety state.");
                        if (_sending)
                        {
                            var draft = Instruction.Text.Trim();
                            if (draft.Length == 0) { await original.AwaitAsync(_actions.Stop()); return; }
                            SetStatus("A response is still in progress. Your draft is retained; send it when the response finishes."); return;
                        }
                        await original.AwaitAsync(_actions.Send()); return;
                    case "chat.stop":
                        if (!_sending) throw new InvalidOperationException("There is no response to stop.");
                        await original.AwaitAsync(_actions.Stop()); return;
                    case "chat.resolve": await original.AwaitAsync(_actions.ResolveProblems()); return;
                    case "chat.choose-type": await original.AwaitAsync((_actions.ChooseTypeAndLocation ?? throw new InvalidOperationException("The current chat type owner is unavailable."))()); return;
                    case "chat.choose-model": await original.AwaitAsync((_actions.ChooseModelAndReasoning ?? throw new InvalidOperationException("The current model selection owner is unavailable."))()); return;
                    case "chat.choose-approval": await original.AwaitAsync((_actions.ChooseToolApproval ?? throw new InvalidOperationException("The current tool approval owner is unavailable."))()); return;
                    case "chat.add": ShowAddMenu(); return;
                    case "chat.close-catalog": HideAddMenu(); return;
                    case "chat.attach-files": HideAddMenu(); await original.AwaitAsync(_actions.AddAction(AddMenu.AddMenuAction.File)); return;
                    case "chat.catalog-agents": ShowCatalog(AddMenu.AddMenuAction.Agent); return;
                    case "chat.catalog-skills": ShowCatalog(AddMenu.AddMenuAction.Instruction); return;
                    case "chat.catalog-capabilities": ShowCatalog(AddMenu.AddMenuAction.Capability); return;
                    case "chat.catalog-apps": ShowCatalog(AddMenu.AddMenuAction.App); return;
                    case "chat.choose-catalog":
                        if (parameter is not string key || !_catalogItems.TryGetValue(key, out var item)) throw new InvalidOperationException("That catalog choice is no longer available.");
                        HideAddMenu(); await original.AwaitAsync(_actions.SelectCatalog(item)); return;
                    case "chat.response-settings": ShowResponseSettings(); return;
                    case "chat.toggle-context": _isContextExpanded = !_isContextExpanded; _values["context.expanded"] = _isContextExpanded; Changed(); return;
                    case "chat.hide-context": _contextDismissed = true; _values["context.visible"] = false; Changed(); return;
                    case "chat.remove-context":
                        var contextId = ParseId(parameter);
                        if (!_contextEntries.Any(entry => entry.EntryId == contextId && entry.IsRemovable)) throw new InvalidOperationException("That context entry is no longer removable.");
                        await original.AwaitAsync(_actions.RemoveContext(contextId)); return;
                    case "chat.remove-attachment":
                        if (parameter is not string chipId || !_attachmentChips.Any(chip => chip.Id == chipId)) throw new InvalidOperationException("That attachment is no longer present.");
                        await original.AwaitAsync(_actions.RemoveAttachment(chipId)); return;
                    case "chat.toggle-dual": await original.AwaitAsync(_actions.ToggleDual()); return;
                    case "chat.choose-dual-model": await original.AwaitAsync(_actions.ChooseDualModel()); return;
                    case "chat.cancel-dialog":
                        if (_dialogBusy) throw new InvalidOperationException("The selected dialog action is still completing.");
                        CloseDialog(); return;
                    case "chat.confirm-dialog":
                    case "chat.choose-dialog":
                        await original.AwaitAsync(ConfirmDialogAsync(command, parameter)); return;
                }
                if (!command.StartsWith("chat.message-", StringComparison.Ordinal)) throw new InvalidOperationException("Unknown Chat action.");
                var id = ParseId(parameter);
                if (!_messages.TryGetValue(id, out var message)) throw new InvalidOperationException("The message is no longer in this conversation.");
                var action = command switch
                {
                    "chat.message-copy" => ChatMessageAction.Copy, "chat.message-edit" => ChatMessageAction.Edit,
                    "chat.message-branch" => ChatMessageAction.Branch, "chat.message-regenerate" => ChatMessageAction.Regenerate,
                    "chat.message-forget" => ChatMessageAction.Forget, "chat.message-delete" => ChatMessageAction.Delete,
                    _ => throw new InvalidOperationException("Unknown message action.")
                };
                if ((_safetyLocked || _sending) && action is not (ChatMessageAction.Copy or ChatMessageAction.Delete))
                    throw new InvalidOperationException("This message cannot be changed while sending or safety-locked.");
                await original.AwaitAsync(_actions.MessageAction(new(id, action)));
            }
            catch (Exception error)
            {
                if (!_disposed) SetStatus(error.Message);
                throw;
            }
        }));
    }
    private static Guid ParseId(object? parameter) => parameter is string text && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty
        ? id : throw new InvalidOperationException("This action requires a canonical identity.");
    private void DispatchCodeAction(CuiMarkdownView view, CuiMarkdownCodeActionRequest request)
    {
        if (_disposed || !view.IsCurrentOriginalCodeAction(request) || view.DataContext is not ICuiBindingContext context ||
            !context.TryGetValue("message.id", out var idValue) || idValue is not string text || !Guid.TryParse(text, out var id) ||
            !_messages.TryGetValue(id, out var message) || view.Text != message.Content) return;
        var mapped = request.Action switch
        {
            CuiMarkdownCodeAction.Copy => Haven.UI.Components.MarkdownCodeAction.Copy,
            CuiMarkdownCodeAction.AskToRun => Haven.UI.Components.MarkdownCodeAction.AskToRun,
            CuiMarkdownCodeAction.AskToApply => Haven.UI.Components.MarkdownCodeAction.AskToApply,
            _ => throw new InvalidOperationException("Unknown code action.")
        };
        _ = _originalWork.RunAsync(async original =>
        {
            original.DemandPublication();
            if (!view.IsCurrentOriginalCodeAction(request) || !_messages.ContainsKey(id)) throw new InvalidOperationException("That code selection expired.");
            await original.AwaitAsync(_actions.CodeAction(new(id, new(mapped, request.Language, request.Code))));
        });
    }
    public void ShowResolveProblemsMenu(string title, string description, IReadOnlyList<(string Label, Func<Task> Action)> choices) => ShowChoices(title, description, choices);
    public void ShowMessageChoiceMenu(Guid messageId, string title, IReadOnlyList<(string Label, Func<Task> Action)> choices)
    { if (_messages.ContainsKey(messageId)) ShowChoices(title, "", choices); }
    public void ShowChoicePrompt(string title, string description, IReadOnlyList<(string Label, Func<Task> Action)> choices) => ShowChoices(title, description, choices);
    public void ShowTextPrompt(string title, string description, string initialValue, string confirmLabel, Func<string, Task> confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        OpenDialog(title, description); _dialogConfirm = confirm;
        _values["dialog.isText"] = true; _values["dialog.text"] = initialValue; _values["dialog.confirmLabel"] = confirmLabel;
        Changed(); FocusDialog();
    }
    private void ShowChoices(string title, string description, IReadOnlyList<(string Label, Func<Task> Action)> choices)
    {
        OpenDialog(title, description);
        _values["dialog.choices"] = choices.Select(choice =>
        {
            var id = Guid.NewGuid().ToString("D"); _dialogChoices[id] = choice.Action;
            return new Dictionary<string, object?> { ["id"] = id, ["label"] = choice.Label };
        }).ToArray();
        Changed(); FocusDialog();
    }
    private void OpenDialog(string title, string description)
    {
        ++_dialogRevision; _dialogChoices.Clear(); _dialogConfirm = null;
        _dialogReturnFocus = TopLevel.GetTopLevel(Root)?.FocusManager?.GetFocusedElement() as Control;
        _values["dialog.title"] = title; _values["dialog.description"] = description; _values["dialog.error"] = "";
        _values["dialog.visible"] = true; _values["dialog.isText"] = false; _values["dialog.choices"] = Array.Empty<object>();
    }
    private void CloseDialog()
    {
        var top = TopLevel.GetTopLevel(Root);
        var current = top?.FocusManager?.GetFocusedElement() as Control;
        var dialog = Find<Control>("chat-dialog");
        var restore = current is null || dialog.IsVisualAncestorOf(current);
        ++_dialogRevision; _values["dialog.visible"] = false; _dialogChoices.Clear(); _dialogConfirm = null; Changed();
        if (restore && _dialogReturnFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous && TopLevel.GetTopLevel(previous) == top) previous.Focus();
        _dialogReturnFocus = null;
    }
    private async Task ConfirmDialogAsync(string command, object? parameter)
    {
        if (_dialogBusy) throw new InvalidOperationException("The current dialog action is still completing.");
        var revision = _dialogRevision;
        Func<Task> invoke;
        if (command == "chat.confirm-dialog")
        {
            var text = (_values["dialog.text"] as string)?.Trim() ?? "";
            if (_dialogConfirm is not { } confirm || text.Length == 0) throw new InvalidOperationException("Enter a value before confirming.");
            invoke = () => confirm(text);
        }
        else if (parameter is string id && _dialogChoices.TryGetValue(id, out var choice)) invoke = choice;
        else throw new InvalidOperationException("That dialog choice expired.");
        _dialogBusy = true; Changed();
        try { await invoke(); if (!_disposed && revision == _dialogRevision) CloseDialog(); }
        catch (Exception error) { if (!_disposed && revision == _dialogRevision) _values["dialog.error"] = error.Message; throw; }
        finally { _dialogBusy = false; if (!_disposed) Changed(); }
    }
    private void FocusDialog()
    {
        var revision = _dialogRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || revision != _dialogRevision || _values["dialog.visible"] is not true) return;
            var dialog = Find<Control>("chat-dialog");
            KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
            if (_values["dialog.isText"] is true) Find<TextBox>("chat-dialog-editor").Focus();
            else Find<Button>("chat-cancel-dialog").Focus();
        }, DispatcherPriority.Loaded);
    }
    private void OnDialogKey(object? sender, KeyEventArgs args)
    {
        if (_disposed || args.Handled || _values["dialog.visible"] is not true || _dialogBusy) return;
        var focused = TopLevel.GetTopLevel(Root)?.FocusManager?.GetFocusedElement() as Control;
        var dialog = Find<Control>("chat-dialog");
        if (focused is null || !dialog.IsVisualAncestorOf(focused)) return;
        var action = args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None ? "chat-cancel-dialog"
            : args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.Control ? "chat-confirm-dialog" : null;
        if (action is null) return;
        var button = Find<Button>(action);
        if (button.IsEffectivelyEnabled && button.IsEffectivelyVisible) { args.Handled = true; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    }
}
