using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class ChatPage
{
    private readonly ContentControl _legacyChoiceOverlay = new();
    private CuiChoicePrompt? _legacyChoicePrompt;
    private Func<bool>? _legacyClosePrepared;

    // The legacy host has no owned retirement operation. A consent receipt is not disposal proof.
    internal Task? OriginalClose => null;
    internal bool IsOriginalChatClosePrepared => _legacyClosePrepared?.Invoke() == true;

    private async Task<bool?> ChooseTemporaryMemoryReadAsync(CancellationToken token)
    {
        bool? answer = null;
        var original = CapturePresentationGuard();
        Task Choose(bool read)
        {
            token.ThrowIfCancellationRequested();
            if (!original()) throw new InvalidOperationException("The Temporary chat changed while waiting for Memory choice.");
            answer = read;
            return Task.CompletedTask;
        }
        var prompt = new CuiChoicePrompt(new CuiChoicePromptOptions("Memory for this message",
            "May this Temporary chat read your existing Memory for this message? It will not create or reinforce persistent Memory. Your existing permissions still apply.",
            "Read Memory", "Don't read Memory", "Cancel", () => Choose(true), () => Choose(false), () => Task.CompletedTask), "Spaces");
        await RunLegacyChoiceAsync(prompt, _legacyChoiceOverlay, token);
        return answer;
    }

    private Func<bool> CaptureLegacyHistoryGuard()
    {
        var generation = _presentationGeneration;
        var conversation = _conversation;
        var messages = Messages.Select(message => message.ToMessage(conversation.Id)).ToArray();
        var context = _temporaryContextEntries.ToArray();
        var selectedModel = SelectedModel;
        var agent = (SelectedAgent?.Name, SelectedAgent?.Instructions);
        var effort = SelectedEffort;
        var duo = SelectedDuo;
        var options = _preferences.GenerationOptions;
        var plugins = Plugins.Select(item => (item.Name, item.IsActive)).ToArray();
        var prompts = Prompts.Select(item => (item.Name, item.IsActive, item.Instructions)).ToArray();
        var draft = Composer;
        var summary = _contextSummary;
        var pendingAttachmentIds = _pendingAttachmentIds.ToArray();
        var attachments = Attachments.Select(item => item.Path).ToArray();
        return () => !IsSending && !_changingLegacyType && _legacyAttachmentOperations == 0 && _pendingLegacySave is null &&
            _presentationGeneration == generation && _conversation == conversation && Composer == draft && _contextSummary == summary && pendingAttachmentIds.SequenceEqual(_pendingAttachmentIds) &&
            Equals(SelectedModel, selectedModel) && (SelectedAgent?.Name, SelectedAgent?.Instructions) == agent &&
            SelectedEffort == effort && SelectedDuo == duo && Equals(_preferences.GenerationOptions, options) &&
            plugins.SequenceEqual(Plugins.Select(item => (item.Name, item.IsActive))) &&
            prompts.SequenceEqual(Prompts.Select(item => (item.Name, item.IsActive, item.Instructions))) &&
            messages.SequenceEqual(Messages.Select(message => message.ToMessage(conversation.Id))) &&
            context.SequenceEqual(_temporaryContextEntries) && attachments.SequenceEqual(Attachments.Select(item => item.Path));
    }

    public async Task<bool> PrepareToCloseAsync(ContentControl? choiceHost, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        if (IsOriginalChatClosePrepared) return true;
        if (IsSending || _changingLegacyType || _legacyAttachmentOperations != 0 || _pendingLegacySave is not null || _legacyChoicePrompt is not null) return false;
        if (!IsTemporary)
        {
            var current = CaptureLegacyHistoryGuard();
            if (!string.IsNullOrWhiteSpace(Composer) || _pendingAttachmentIds.Count != 0)
            {
                if (_production is null) return false;
                await SaveCurrentLegacyDraftAsync(caller);
            }
            if (!current()) return false;
            _legacyClosePrepared = current;
            return true;
        }
        var original = CaptureLegacyHistoryGuard();
        Task Discard()
        {
            caller.ThrowIfCancellationRequested();
            if (!original()) throw new InvalidOperationException("The Temporary chat changed. Review it before closing.");
            _legacyClosePrepared = original;
            return Task.CompletedTask;
        }
        async Task Save()
        {
            caller.ThrowIfCancellationRequested();
            if (!original()) throw new InvalidOperationException("The Temporary chat changed. Review it before saving.");
            await SetTemporaryAsync(false);
            // Draft persistence is an existing owner, separate from atomic transcript conversion.
            if (!string.IsNullOrWhiteSpace(Composer) || _pendingAttachmentIds.Count != 0)
            {
                if (_production is null) throw new NotSupportedException("The current host cannot save this draft. Keep the chat open.");
                await SaveCurrentLegacyDraftAsync(caller);
            }
            caller.ThrowIfCancellationRequested();
            _legacyClosePrepared = CaptureLegacyHistoryGuard();
        }
        var prompt = new CuiChoicePrompt(new CuiChoicePromptOptions("Close Temporary chat?",
            "Save this conversation locally, discard this session's history, or keep the chat open.",
            "Save locally", "Discard", "Cancel", Save, Discard, () => Task.CompletedTask), "Spaces");
        await RunLegacyChoiceAsync(prompt, choiceHost ?? _legacyChoiceOverlay, caller);
        return IsOriginalChatClosePrepared;
    }

    private async Task RunLegacyChoiceAsync(CuiChoicePrompt prompt, ContentControl host, CancellationToken token)
    {
        if (_legacyChoicePrompt is not null) throw new InvalidOperationException("Another choice already owns this chat.");
        _legacyChoicePrompt = prompt;
        Exception? failure = null;
        try { await prompt.ShowAsync(host, token); }
        catch (Exception error) { failure = error; }
        try { await prompt.CloseAsync(); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        finally { _legacyChoicePrompt = null; }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
