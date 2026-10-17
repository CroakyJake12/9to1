using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>
/// The Chat Space projection into CUI.
///
/// Merge stage M3: this is the CUI binding owner for <c>ChatSpace.cui</c>. It mirrors the proven
/// Assistants arrangement - markup declares structure, this type supplies values and owns
/// dispatch - so the markup can never reach the domain directly and no action in the document is
/// an unmediated method call.
///
/// Two things this deliberately does not do:
///
/// It does not resolve repeat items by reflection. CUI's repeat item path resolves against
/// whatever the host supplies, and the typed records here (<see cref="ChatSpaceMessage"/> and
/// friends) are not shapes the resolver can walk. Each supported item type is mapped explicitly,
/// which also means an item that is no longer part of the published collection is refused rather
/// than rendered from a stale reference.
///
/// It does not execute anything. Every command becomes a call to the injected dispatch delegate;
/// this type holds no store, actor or service.
/// </summary>
public sealed class ChatSpaceCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    /// <summary>Every command the authored Chat Space document may raise.</summary>
    private static readonly IReadOnlySet<string> KnownCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "chat.new", "chat.open", "chat.select-model", "chat.switch-branch",
        "chat.branch", "chat.regenerate", "chat.attach", "chat.remove-attachment",
        "chat.send", "chat.clear-search", "chat.update-draft",
        "chat.copy", "chat.delete", "chat.forget", "chat.edit", "chat.cancel-edit",
        "chat.set-action-mode", "chat.set-response-mode",
        "chat.toggle-dual", "chat.select-second-model", "chat.select-dual-mode"
    };

    private readonly CuiViewModel _values = new();
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<ChatSpaceViewState> _state;
    private readonly Func<ChatSpaceViewState, ChatSpaceViewState, bool> _isCurrent;
    private ChatSpaceViewState _snapshot = ChatSpaceViewState.Empty;
    private string _recentSearchQuery = string.Empty;
    private long _revision;
    private bool _narrow;

    public ChatSpaceCuiBindings(
        Func<ChatSpaceViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch,
        Func<ChatSpaceViewState, ChatSpaceViewState, bool>? isCurrent = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isCurrent = isCurrent ?? ((expected, actual) => ReferenceEquals(expected, actual));
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The most recently published projection. Never null.</summary>
    public ChatSpaceViewState Snapshot => Current ? _snapshot : ChatSpaceViewState.Empty;

    private bool Current => _isCurrent(_state(), _snapshot);

    /// <summary>Republishes the current domain state into the CUI value tree.</summary>
    public void Refresh()
    {
        _snapshot = _state();
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void UpdateViewportWidth(double width)
    {
        var narrow = width > 0 && width < 720;
        if (_narrow == narrow) return;
        _narrow = narrow;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// Clears the recent-chat search box.
    ///
    /// The document's clear control carries only an action, so the host has no value to write.
    /// The projection owns the query - it is what the two-way input writes through
    /// <see cref="TrySetValue"/> - so clearing it belongs here and republishes like any other
    /// state change. Previously this command was a documented no-op.
    /// </summary>
    public void ClearRecentSearch()
    {
        _recentSearchQuery = string.Empty;
        Refresh();
    }

    /// <summary>
    /// Resolves the exact authored paths against the published snapshot.
    ///
    /// Nothing is read reflectively and nothing falls back to a partial match: an unrecognised
    /// path is refused, so a binding that names a property the view state does not have renders
    /// as absent instead of silently picking up some other field.
    /// </summary>
    public bool TryGetValue(string path, out object? value)
    {
        var snapshot = Snapshot;
        switch (path)
        {
            case "RecentChats": value = snapshot.RecentChats.Where(item => string.IsNullOrWhiteSpace(_recentSearchQuery) || item.Title.Contains(_recentSearchQuery, StringComparison.OrdinalIgnoreCase)).ToArray(); return true;
            case "Layout.Columns": value = _narrow ? "1fr" : "220 1fr"; return true;
            case "Layout.Rows": value = _narrow ? "Auto 1fr" : "1fr"; return true;
            case "Layout.ConversationColumn": value = _narrow ? 0 : 1; return true;
            case "Layout.ConversationRow": value = _narrow ? 1 : 0; return true;
            case "Layout.RecentHeight": value = _narrow ? 160d : 100000d; return true;
            case "RecentSearchQuery": value = _recentSearchQuery; return true;
            case "Models": value = snapshot.Models; return true;
            case "Status.Message": value = snapshot.Status.Message; return true;
            case "Conversation.Title": value = snapshot.Conversation?.Title ?? string.Empty; return true;
            case "Conversation.Messages": value = snapshot.Conversation?.Messages ?? []; return true;
            case "Conversation.Branches": value = snapshot.Conversation?.Branches ?? []; return true;
            case "Conversation.CurrentBranchId": value = snapshot.Conversation?.CurrentBranchId; return true;
            case "Composer.Draft": value = snapshot.Composer.Draft; return true;
            case "Composer.Attachments": value = snapshot.Composer.Attachments; return true;
            case "Composer.SelectedModelName": value = snapshot.Composer.SelectedModelName; return true;
            case "Composer.SelectedModelIndex": value = snapshot.Composer.SelectedModelIndex; return true;
            case "Composer.CanSend": value = !snapshot.IsLoading && !snapshot.IsSafetyLocked && snapshot.Composer.CanSend; return true;
            case "Composer.IsSending": value = snapshot.Composer.IsSending; return true;
            case "Composer.IsEditing": value = snapshot.Composer.IsEditing; return true;
            case "IsSafetyLocked": value = snapshot.IsSafetyLocked; return true;
            case "SelectedBranchIndex": value = snapshot.SelectedBranchIndex; return true;
            case "ThreadSettings.ActionMode": value = snapshot.ThreadSettings?.ActionMode ?? ChatActionMode.AllowBasicActions; return true;
            case "ThreadSettings.ResponseMode": value = snapshot.ThreadSettings?.ResponseMode ?? GenerativeUiResponseMode.Auto; return true;
            case "DualModel.IsActive": value = snapshot.DualModel?.IsActive ?? false; return true;
            case "DualModel.SecondModelName": value = snapshot.DualModel?.SecondModelName; return true;
            case "DualModel.SecondModelIndex": value = Array.FindIndex(snapshot.Models.ToArray(), model => model.Name.Equals(snapshot.DualModel?.SecondModelName, StringComparison.OrdinalIgnoreCase)); return true;
            case "DualModel.IsRunning": value = snapshot.DualModel?.IsRunning ?? false; return true;
            case "DualModel.Mode": value = snapshot.DualModel?.Mode ?? DuoMode.Solo; return true;
            default: value = null; return false;
        }
    }

    /// <summary>
    /// Two-way write path for authored inputs. Only the paths the document actually edits are
    /// accepted; anything else is refused rather than silently absorbed, so a typo in the markup
    /// surfaces instead of writing somewhere the host never reads.
    /// </summary>
    public bool TrySetValue(string path, object? value)
    {
        if (!Current) return false;
        var snapshot = Snapshot;
        switch (path)
        {
            case "RecentSearchQuery":
                _recentSearchQuery = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return true;
            case "Composer.Draft":
                PublishWrite("chat.update-draft", value);
                return true;
            case "Composer.SelectedModelName" when value is string name && snapshot.Models.Any(model => model.Name == name):
                PublishWrite("chat.select-model", name);
                return true;
            case "Composer.SelectedModelIndex" when value is int index && index >= 0 && index < snapshot.Models.Count:
                // Resolve the identity from the exact collection which populated the selector.
                PublishWrite("chat.select-model", snapshot.Models[index].Name);
                return true;
            case "DualModel.SecondModelIndex" when value is int index && index >= 0 && index < snapshot.Models.Count:
                PublishWrite("chat.select-second-model", snapshot.Models[index].Name);
                return true;
            case "SelectedBranchIndex" when value is int index && snapshot.Conversation is { } conversation && index >= 0 && index < conversation.Branches.Count:
                PublishWrite("chat.switch-branch", conversation.Branches[index].Id);
                return true;
            default: return false;
        }
    }

    public bool? IsActionAvailable(string command)
    {
        if (!KnownCommands.Contains(command))
        {
            return null;
        }

        if (!Current) return false;
        var snapshot = Snapshot;
        if (snapshot.IsLoading) return false;
        return command switch
        {
            "chat.send" => !snapshot.IsSafetyLocked && snapshot.Composer.CanSend,
            "chat.copy" => snapshot.Conversation is not null,
            "chat.delete" => snapshot.Conversation is not null && !snapshot.Composer.IsSending,
            "chat.forget" => snapshot.Conversation is not null && !snapshot.IsSafetyLocked && !snapshot.Composer.IsSending,
            "chat.edit" => snapshot.Conversation is not null && !snapshot.IsSafetyLocked && !snapshot.Composer.IsSending,
            "chat.branch" or "chat.regenerate" or "chat.switch-branch" => snapshot.Conversation is not null && !snapshot.IsSafetyLocked && !snapshot.Composer.IsSending,
            "chat.attach" or "chat.remove-attachment" => !snapshot.IsSafetyLocked,
            "chat.cancel-edit" => snapshot.Composer.IsEditing,
            "chat.toggle-dual" => !snapshot.Composer.IsSending && snapshot.Models.Count >= 2,
            "chat.select-second-model" => snapshot.DualModel?.IsActive == true,
            _ => true
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!KnownCommands.Contains(command))
        {
            throw new InvalidOperationException($"Chat Space CUI command '{command}' is not owned by this binding.");
        }

        await _dispatch(command, parameter, cancellationToken);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        var snapshot = Snapshot;

        if (item is ChatSpaceRecentChat recent && snapshot.RecentChats.Any(actual => ReferenceEquals(actual, recent)))
        {
            value = path switch
            {
                "Id" => recent.Id,
                "Title" => recent.Title,
                "UpdatedLabel" => recent.UpdatedLabel,
                "IsCurrent" => recent.IsCurrent,
                _ => null
            };
            return value is not null;
        }

        if (item is ChatSpaceMessage message && snapshot.Conversation?.Messages.Any(actual => ReferenceEquals(actual, message)) == true)
        {
            value = path switch
            {
                "Id" => message.Id,
                "Role" => message.Role.ToString(),
                "SpeakerLabel" => message.SpeakerLabel,
                "CreatedLabel" => message.CreatedLabel,
                "Content" => message.Content,
                "CanRegenerate" => message.CanRegenerate,
                "CanEdit" => message.CanEdit,
                "HasThinking" => message.HasThinking,
                "AvatarVisible" => message.AvatarVisible,
                "AvatarSource" => message.AvatarSource,
                "Attachments" => message.Attachments,
                "Citations" => message.Citations,
                "ToolActivities" => message.ToolActivities,
                "Thinking" => message.Thinking,
                "IsStreaming" => message.IsStreaming,
                "ProgressLabel" => message.ProgressLabel,
                _ => null
            };
            return value is not null;
        }

        if (item is MessageAttachment attachment && OwnsAttachment(attachment))
        {
            value = path switch
            {
                "Id" => attachment.Id,
                "OriginalName" => attachment.OriginalName,
                "MediaType" => attachment.MediaType,
                _ => null
            };
            return value is not null;
        }

        if (item is ChatSpaceCitation citation && OwnsCitation(citation))
        {
            value = path switch
            {
                "Number" => citation.Number,
                "Title" => citation.Title,
                "Source" => citation.Source,
                _ => null
            };
            return value is not null;
        }

        if (item is ToolActivity activity && OwnsActivity(activity))
        {
            value = path switch
            {
                "Id" => activity.Id,
                "Title" => activity.Title,
                "Detail" => activity.Detail,
                _ => null
            };
            return value is not null;
        }

        return false;

        bool OwnsAttachment(MessageAttachment candidate) =>
            snapshot.Conversation?.Messages.Any(message => message.Attachments.Contains(candidate)) == true
            || snapshot.Composer.Attachments.Contains(candidate);
        bool OwnsCitation(ChatSpaceCitation candidate) =>
            snapshot.Conversation?.Messages.Any(message => message.Citations.Contains(candidate)) == true;
        bool OwnsActivity(ToolActivity candidate) =>
            snapshot.Conversation?.Messages.Any(message => message.ToolActivities.Contains(candidate)) == true;
    }

    /// <summary>
    /// Repeat items are published records, not editable drafts. A write is refused rather than
    /// faked; the only mutable state on this surface is the composer, which the write path above
    /// already owns.
    /// </summary>
    public bool TrySetItemValue(object item, string path, object? value) => false;

    /// <summary>
    /// A live input edit is fire-and-forget: the host republishes the authoritative state, which
    /// comes back through <see cref="Refresh"/>. This must not block the CUI write path on an
    /// async round trip, so the task is deliberately not awaited here.
    /// </summary>
    private void PublishWrite(string command, object? value)
    {
        if (IsActionAvailable(command) != true) return;
        // The owning host retains and reports command failures through its original-work lifetime.
        _ = _dispatch(command, value, CancellationToken.None);
    }
}
