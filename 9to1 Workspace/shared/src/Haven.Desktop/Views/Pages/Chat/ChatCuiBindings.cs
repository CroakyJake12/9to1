using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Typed CUI binding/action adapter over the existing <see cref="NewChatPage"/> owner.
/// It projects the owner's actual state and forwards commands to existing page methods.
/// No state is duplicated or cloned; the owner remains the single authority.
/// </summary>
public sealed class ChatCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly NewChatPage _owner;

    public ChatCuiBindings(NewChatPage owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public NewChatPage Owner => _owner;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        var projection = _owner.ProjectionState;
        switch (path)
        {
            case "Conversation.Title":
                value = projection.ConversationTitle;
                return true;
            case "Status.Message":
                value = projection.StatusText ?? string.Empty;
                return true;
            case "Composer.Draft":
                value = _owner.ComposerInput.Text;
                return true;
            case "Composer.Attachments":
                value = _owner.PendingComposerAttachments;
                return true;
            case "Composer.CanSend":
                value = !_owner.IsSafetyLocked && !_owner.IsSending &&
                    _owner.ModelAvailability == ChatModelAvailability.Available &&
                    !string.IsNullOrWhiteSpace(_owner.ComposerInput.Text);
                return true;
            case "Composer.IsSending":
                value = _owner.IsSending;
                return true;
            case "Composer.IsEditing":
                value = false;
                return true;
            case "IsSafetyLocked":
                value = _owner.IsSafetyLocked;
                return true;
            case "Models":
                value = _owner.AdmittedModels;
                return true;
            default:
                return false;
        }
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path == "Composer.Draft")
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            // Distinguish a real user edit from a programmatic restore by using the input port's
            // text setter, which is the same mutation path the owner already uses.
            _owner.ComposerInput.Text = text;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return true;
        }
        return false;
    }

    public bool? IsActionAvailable(string command)
    {
        var draft = _owner.ComposerInput.Text;
        return command switch
        {
            "chat.send" => !_owner.IsSafetyLocked &&
                (_owner.IsSending ||
                 (_owner.ModelAvailability == ChatModelAvailability.Available &&
                  !string.IsNullOrWhiteSpace(draft))),
            _ => true
        };
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        return command switch
        {
            "chat.send" => DispatchSend(),
            _ => ValueTask.FromException(new InvalidOperationException($"Chat CUI command '{command}' is not migrated yet."))
        };
    }

    private ValueTask DispatchSend()
    {
        // Use the same queue-aware owner operation as the legacy Send button. This preserves
        // FIFO queueing and empty-composer Stop instead of a direct send from an arbitrary
        // parameter. Stop is handled by that same operation when the composer is empty.
        _owner.RequestComposerSend();
        return ValueTask.CompletedTask;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is MessageAttachment attachment &&
            _owner.PendingComposerAttachments.Contains(attachment))
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
        return false;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}