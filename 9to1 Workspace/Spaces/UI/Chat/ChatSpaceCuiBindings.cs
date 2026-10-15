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
        "chat.send", "chat.clear-search"
    };

    private readonly CuiViewModel _values = new();
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<ChatSpaceViewState> _state;
    private readonly Func<ChatSpaceViewState, ChatSpaceViewState, bool> _isCurrent;
    private ChatSpaceViewState _snapshot = ChatSpaceViewState.Empty;
    private string _recentSearchQuery = string.Empty;
    private long _revision;

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
        // Reads go through TryGetValue below rather than through this view model. CuiViewModel now
        // resolves an exact key before falling back to the final path segment, so both approaches
        // agree; resolving here anyway keeps every authored path listed in one place and means an
        // unrecognised path is visibly absent instead of silently matching some other property.
        // The view model exists to raise the change notification the repeat reconciler listens for.
        _values.Set("Revision", _revision++);
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
        var snapshot = _snapshot;
        switch (path)
        {
            case "RecentChats": value = snapshot.RecentChats; return true;
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
            case "Composer.CanSend": value = snapshot.Composer.CanSend; return true;
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
        switch (path)
        {
            case "RecentSearchQuery":
                _recentSearchQuery = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                break;
            case "Composer.Draft":
            case "Composer.SelectedModelName":
                PublishWrite(path, value);
                break;
            default:
                return false;
        }

        Refresh();
        return true;
    }

    public bool? IsActionAvailable(string command)
    {
        if (!KnownCommands.Contains(command))
        {
            return null;
        }

        var snapshot = Snapshot;
        return command switch
        {
            "chat.send" => snapshot.Composer.CanSend,
            "chat.branch" or "chat.regenerate" => true,
            _ => true
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!KnownCommands.Contains(command))
        {
            // An unknown command is a document defect, not a user error. Refusing it here keeps
            // markup from naming anything the host has not explicitly admitted.
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
                "Attachments" => message.Attachments,
                "Citations" => message.Citations,
                "ToolActivities" => message.ToolActivities,
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
    private void PublishWrite(string path, object? value) =>
        _ = _dispatch(path == "Composer.Draft" ? "chat.update-draft" : "chat.select-model", value, CancellationToken.None);
}