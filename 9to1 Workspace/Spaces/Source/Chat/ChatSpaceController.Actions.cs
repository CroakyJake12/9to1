using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

public sealed partial class ChatSpaceController
{
    private readonly HashSet<Guid> _mutations = [];
    private bool CanMutate(View view)
    {
        lock (_gate) return Matches(view) && !_state.IsLoading && !_state.IsSafetyLocked
            && view.ConversationId is { } id && !_turns.ContainsKey(id) && !_mutations.Contains(id);
    }
    private bool TryBeginMutation(View view, bool allowSafetyLocked = false)
    {
        lock (_gate)
        {
            if (!Matches(view) || _state.IsLoading || (!allowSafetyLocked && _state.IsSafetyLocked)
                || view.ConversationId is not { } id || _turns.ContainsKey(id) || !_mutations.Add(id)) return false;
            return true;
        }
    }
    private void EndMutation(View view)
    {
        lock (_gate) if (view.ConversationId is { } id) _mutations.Remove(id);
    }
    private bool OwnsMessage(View view, Guid messageId, MessageRole? role = null)
    {
        lock (_gate) return Matches(view) && _state.Conversation?.Messages.Any(message =>
            message.Id == messageId && (role is null || message.Role == role)) == true;
    }
    public async Task BranchAsync(Guid messageId, string? name = null, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        if (!OwnsMessage(view, messageId) || !TryBeginMutation(view)) return;
        try
        {
            await SaveCurrentDraftAsync(view, cancellationToken).ConfigureAwait(false);
            await _backend.CreateBranchAsync(view.ConversationId!.Value, messageId, name, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(view, "Branch created", cancellationToken, false).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
        finally { EndMutation(view); }
    }
    public async Task SwitchBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        if (view.BranchId == branchId || State.Conversation?.Branches.Any(branch => branch.Id == branchId) != true || !TryBeginMutation(view)) return;
        try
        {
            await SaveCurrentDraftAsync(view, cancellationToken).ConfigureAwait(false);
            await _backend.SwitchBranchAsync(view.ConversationId!.Value, branchId, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(view, "Branch selected", cancellationToken, false).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
        finally { EndMutation(view); }
    }
    public async Task RegenerateAsync(Guid assistantMessageId, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        ModelDescriptor? model;
        lock (_gate)
        {
            if (!CanMutate(view) || !OwnsMessage(view, assistantMessageId, MessageRole.Assistant)) return;
            model = _modelDescriptors.FirstOrDefault(item => item.Name.Equals(_state.Composer.SelectedModelName, StringComparison.OrdinalIgnoreCase));
            if (model is not null) _mutations.Add(view.ConversationId!.Value);
        }
        if (model is null) throw new InvalidOperationException("Choose an available model before regenerating.");
        try
        {
            Change(state => state with { Composer = state.Composer with { IsSending = true }, Status = new("Regenerating response", ChatSpaceStatusTone.Progress, true) }, view);
            await _backend.RegenerateAsync(view.ConversationId!.Value, assistantMessageId, model, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(view, "Response regenerated", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
        finally
        {
            lock (_gate) _mutations.Remove(view.ConversationId!.Value);
            Change(state => state with { Composer = state.Composer with { IsSending = _turns.ContainsKey(view.ConversationId!.Value) } }, view);
        }
    }
    public async Task CopyMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        if (!OwnsMessage(view, messageId)) return;
        try
        {
            if (_clipboard is null) throw new NotSupportedException("Clipboard access is unavailable in this host.");
            var content = await _backend.GetMessageContentAsync(view.ConversationId!.Value, messageId, cancellationToken).ConfigureAwait(false);
            await _clipboard.SetClipboardTextAsync(content, cancellationToken).ConfigureAwait(false);
            Change(state => state with { Status = new("Message copied", ChatSpaceStatusTone.Success, true) }, view);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
    }
    public async Task DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        if (!OwnsMessage(view, messageId) || !TryBeginMutation(view, allowSafetyLocked: true)) return;
        try
        {
            await _backend.DeleteMessageAsync(view.ConversationId!.Value, messageId, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(view, "Message deleted", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
        finally { EndMutation(view); }
    }
    public async Task ForgetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        if (!OwnsMessage(view, messageId) || !TryBeginMutation(view)) return;
        try
        {
            await _backend.ForgetMessageAsync(view.ConversationId!.Value, messageId, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(view, "Message removed from active context", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
        finally { EndMutation(view); }
    }
    public void StartEditMessage(Guid messageId)
    {
        var view = CurrentView();
        if (!CanMutate(view) || !OwnsMessage(view, messageId, MessageRole.User)) return;
        Change(state =>
        {
            _composerVersion++;
            return state with { Composer = state.Composer with { Draft = state.Conversation!.Messages.Single(message => message.Id == messageId).Content,
                IsEditing = true, EditingMessageId = messageId }, Status = new("Edit the message. Updating creates a branch and preserves the original history.", ChatSpaceStatusTone.Neutral, true) };
        }, view);
    }
    public void CancelEdit() => Change(state =>
    { _composerVersion++; return state with { Composer = state.Composer with { Draft = string.Empty, IsEditing = false, EditingMessageId = null } }; });
    private async Task ConfirmEditAsync(Guid messageId, CancellationToken token)
    {
        var view = CurrentView();
        if (!CanMutate(view) || !OwnsMessage(view, messageId, MessageRole.User)) return;
        string content; long composerVersion;
        lock (_gate) { content = _state.Composer.Draft; composerVersion = _composerVersion; }
        if (string.IsNullOrWhiteSpace(content) || !TryBeginMutation(view)) return;
        try
        {
            await _backend.EditUserMessageAsync(view.ConversationId!.Value, messageId, content, MessageEditMode.NewBranch, token).ConfigureAwait(false);
            Change(state =>
            {
                if (_composerVersion != composerVersion) return state;
                _composerVersion++;
                return state with { Composer = state.Composer with { Draft = string.Empty, IsEditing = false, EditingMessageId = null } };
            }, view);
            await RefreshAsync(view, "Edited message saved in a new branch", token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
        finally { EndMutation(view); }
    }
    public void SetThreadActionMode(ChatActionMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Change(state => { _threadSettings = _threadSettings with { ActionMode = mode }; return state with { ThreadSettings = _threadSettings }; });
    }
    public void SetThreadResponseMode(GenerativeUiResponseMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Change(state => { _threadSettings = _threadSettings with { ResponseMode = mode }; return state with { ThreadSettings = _threadSettings }; });
    }
    public void ToggleDualModel() => Change(state =>
    { _dualModel = _dualModel with { IsActive = !_dualModel.IsActive }; return state with { DualModel = _dualModel }; });
    public void SelectDualSecondModel(string modelName) => Change(state =>
    {
        var model = _modelDescriptors.FirstOrDefault(item => item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose a model in the current governed inventory.", nameof(modelName));
        _dualModel = _dualModel with { SecondModelName = model.Name }; return state with { DualModel = _dualModel };
    });
    public void SelectDualMode(DuoMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Change(state => { _dualModel = _dualModel with { Mode = mode }; return state with { DualModel = _dualModel }; });
    }
}
