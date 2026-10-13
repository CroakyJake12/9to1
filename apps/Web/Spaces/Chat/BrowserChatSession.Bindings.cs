using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;

namespace NineToOne.Web.Spaces.Chat;

public sealed partial class BrowserChatSession
{
    private sealed record RecentSection(IReadOnlyList<ChatSpaceRecentChat> Chats);
    private sealed record RecoveryChoice(string Id, string Label);
    private static readonly IReadOnlyList<SpaceDefinition> FirstParty = SpaceRegistry.GetFirstPartyDefinitions();
    public bool TryGetValue(string path, out object? value)
    {
        var state = _disposed ? ChatSpaceViewState.Empty : _state;
        value = path switch
        {
            "shell.columns" => _compact ? "70 1fr" : (_sidebar ? "350 1fr" : "70 1fr"),
            "shell.rows" => _compact ? "172 1fr" : "80 1fr",
            "shell.navigationWidth" => _compact && _sidebar ? 350d : _sidebar ? 350d : 70d,
            "shell.navigationSpan" => _compact && _sidebar ? 2 : 1,
            "shell.headerColumns" => _compact ? "1fr" : "1fr Auto Auto",
            "shell.modelColumn" => _compact ? 0 : 1,
            "shell.modelRow" => _compact ? 1 : 0,
            "shell.headerRows" => _compact ? "Auto Auto Auto" : "Auto",
            "shell.headerMinHeight" => _compact ? 172d : 80d,
            "shell.utilityColumn" => _compact ? 0 : 2,
            "shell.utilityRow" => _compact ? 2 : 0,
            "shell.utilitySpan" => 1,
            "shell.hasUsage" or "shell.canOpenUsage" or "shell.canSearch" or "shell.canNotify" => false,
            "shell.usageLabel" or "shell.usageDescription" => "",
            "shell.canAccount" => CanAct,
            "layout.columns" => _sidebar ? "70 280" : "70 0",
            "layout.railVisible" => true,
            "layout.sidebarVisible" => _sidebar,
            "layout.collapsed" => !_sidebar,
            "layout.canChange" => CanAct,
            "navigation.interactive" => CanAct,
            "navigation.pins" => FirstParty,
            "navigation.canConfigure" => false,
            "navigation.canManageSpaces" => CanAct,
            "sidebar.name" => "Chat",
            "sidebar.iconSource" => "icon:" + FirstParty.Single(space => space.Id == SpaceRegistry.ChatSpaceId).IconKey,
            "sidebar.isChat" => true,
            "sidebar.query" => _query,
            "sidebar.newChatLabel" => "New Chat",
            "sidebar.canStart" => CanEdit && _controller?.RequiresTurnReconciliation != true,
            "sidebar.groupsVisible" => true,
            "sidebar.groupsLabel" => "Chat Groups",
            "sidebar.newGroupLabel" => "New Chat Group",
            "sidebar.specialistsAvailable" => false,
            "sidebar.specialistsDescription" => "Specialists are not yet connected in this browser.",
            "sidebar.sections" => new[] { new RecentSection(state.RecentChats.Where(chat => string.IsNullOrEmpty(_query) || chat.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)).ToArray()) },
            "sidebar.status" => _available ? "Local chats · saved for this account on this device" : _status,
            "chat.mainInteractive" => !_models && !_recovery,
            "chat.workspacePadding" => _compact ? "16,20,16,24" : "24,20,24,53",
            "chat.transcriptMargin" => _compact ? "0" : "31,0,13,0",
            "chat.title" => state.Conversation?.Title ?? "Chat",
            "chat.typeLabel" => "Normal",
            "chat.locationLabel" => "Local · this account on this device",
            "chat.modelLabel" => ModelLabel(state.Composer.SelectedModelName),
            "chat.canChooseType" => false,
            "chat.canChooseModel" => CanEdit && state.Models.Count > 0,
            "chat.empty" => state.Conversation?.Messages.Count is null or 0,
            "chat.hasMessages" => state.Conversation?.Messages.Count > 0,
            "chat.hasIssues" => HasIssues,
            "chat.emptyDescription" => _available ? "Start a conversation with your selected model." : _status,
            "chat.messages" => state.Conversation?.Messages ?? [],
            "chat.status" => _saveError ?? (_dirty && !state.Composer.IsSending ? "Draft not yet saved" : _status),
            "chat.canChooseApproval" => false,
            "chat.approvalLabel" => "Ask for Approval",
            "composer.text" => state.Composer.Draft,
            "composer.controlsRows" => _compact ? "Auto Auto" : "Auto",
            "composer.approvalRow" => _compact ? 1 : 0,
            "composer.approvalColumn" => _compact ? 0 : 2,
            "composer.approvalSpan" => _compact ? 5 : 1,
            "composer.placeholder" => _available ? "Ask Anything" : "Sign in to start chatting",
            "composer.canEdit" => CanEdit,
            "composer.canSend" => CanEdit && state.Composer.CanSend && _controller?.RequiresTurnReconciliation != true,
            "composer.sending" => state.Composer.IsSending,
            "composer.sendLabel" => "Send message",
            "composer.sendIcon" => "icon:arrow-up",
            "composer.sendGlyph" => "↑",
            "composer.hasAttachments" => state.Composer.Attachments.Count > 0,
            "composer.attachments" => state.Composer.Attachments,
            "context.visible" or "context.expanded" or "dual.available" or "dual.active" => false,
            "context.entries" => Array.Empty<object>(),
            "catalog.visible" => _catalog,
            "catalog.query" => "",
            "catalog.items" => Array.Empty<object>(),
            "catalog.status" => "Choose Files to attach a local file. Skills, Plugins & Apps, Specialists and Assistants require their connected catalogues.",
            "dialog.visible" => _models || _recovery,
            "dialog.title" => _recovery ? "Resolve Issues" : "Choose model",
            "dialog.description" => _recovery ? (_saveError ?? _status) + "\n\nRefresh checks the saved chat and connected models, and reconciles any completed turn awaiting acknowledgement. It does not repeat the model response or tools." : "Models available from your connected runtime.",
            "dialog.isText" or "dialog.canConfirm" => false,
            "dialog.canInteract" => _recovery ? CanAct && !_busy && !state.Composer.IsSending : CanEdit,
            "dialog.choices" => _recovery ? (object)new[] { new RecoveryChoice("refresh", "Refresh Chat state") } : state.Models,
            "dialog.error" or "dialog.text" or "dialog.confirmLabel" => "",
            "navDialog.visible" or "navDialog.isText" or "navDialog.canConfirm" => false,
            "navDialog.choices" => Array.Empty<object>(),
            _ => null
        };
        return value is not null;
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (_disposed) { value = null; return false; }
        _demand();
        value = item switch
        {
            SpaceDefinition space => path switch
            {
                "id" => space.Id.ToString("D"), "name" => space.Name, "iconSource" => "icon:" + space.IconKey,
                "description" => space.Id == SpaceRegistry.ChatSpaceId ? space.Description : space.Description + " · not connected in this browser",
                "selected" => space.Id == SpaceRegistry.ChatSpaceId, "available" => space.Id == SpaceRegistry.ChatSpaceId,
                _ => null
            },
            RecentSection section => path switch { "key" => "recent", "title" => "Recent Chats", "visible" => true, "rows" => section.Chats, _ => null },
            ChatSpaceRecentChat chat => path switch
            {
                "key" or "id" => chat.Id.ToString("D"), "title" or "accessibleName" => chat.Title,
                "selected" => chat.IsCurrent, "isConversation" => true, "isGroup" or "unread" or "hasActions" => false,
                "margin" => "0", _ => null
            },
            ChatSpaceModelChoice model => path switch { "id" => model.Name, "label" => ModelLabel(model.Name), _ => null },
            RecoveryChoice choice => path switch { "id" => choice.Id, "label" => choice.Label, _ => null },
            ChatSpaceMessage message => MessageValue(message, path),
            MessageAttachment attachment => path switch
            {
                "id" => attachment.Id.ToString("D"), "label" => attachment.OriginalName,
                "removeLabel" => "Remove " + attachment.OriginalName, _ => null
            },
            ToolActivity tool => path switch { "id" => tool.Id.ToString("D"), "title" => tool.Title, "detail" => tool.Detail, _ => null },
            _ => null
        };
        return value is not null;
    }
    private static string ModelLabel(string? name) => name is null ? "Choose model"
        : name.StartsWith("hosted-ollama:", StringComparison.Ordinal) ? "Hosted · " + name[14..] : name;
    private object? MessageValue(ChatSpaceMessage message, string path) => path switch
    {
        "id" => message.Id.ToString("D"), "content" => message.Content,
        "author" => message.Role == MessageRole.User ? "You" : message.SpeakerLabel,
        "accessibleName" => message.SpeakerLabel + " message",
        "isAssistant" => message.Role == MessageRole.Assistant,
        "canModify" => CanEdit,
        "alignment" => message.Role == MessageRole.User ? "Right" : "Left",
        "padding" => "0", "background" => Brushes.Transparent,
        "tools" => message.ToolActivities,
        "hasWorkStatus" => message.Thinking.Length > 0,
        "workStatus" => message.Thinking,
        "hasGenerated" => false,
        _ => null
    };
}
