using System.Collections;

namespace HavenOS.Apps.Assistants.NativeUI;

// Declared display members of SAME currently projected rows only. No reflection,
// provider/store access, restoration or permission issuance occurs here.
public sealed partial class AssistantsCuiBindings
{
    private bool ContainsOriginalRow(object item, params string[] sources)
    {
        if (!Current) return false;
        foreach (var source in sources)
            if (_values.TryGetValue(source, out var rows) && rows is IEnumerable values &&
                values.Cast<object>().Any(value => ReferenceEquals(value, item))) return true;
        return false;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!Current) return false;
        if (TryGetAttachmentItemValue(item, path, out value)) return true;
        switch (item)
        {
            case AssistantRow row when ContainsOriginalRow(row, "Assistants"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Name" => row.Name,
                    "Description" => row.Description,
                    "AvatarKey" => row.AvatarKey,
                    "AvatarLabel" => row.AvatarLabel,
                    "ActivityLabel" => row.ActivityLabel,
                    "CanOpen" => row.CanOpen,
                    _ => null
                };
                return path is "Id" or "Name" or "Description" or "AvatarKey" or "AvatarLabel" or "ActivityLabel" or "CanOpen";
            case ConversationRow row when ContainsOriginalRow(row, "RecentConversations", "SelectedConversations"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Title" => row.Title,
                    "AssistantName" => row.AssistantName,
                    "UpdatedLabel" => row.UpdatedLabel,
                    "CanOpen" => row.CanOpen,
                    _ => null
                };
                return path is "Id" or "Title" or "AssistantName" or "UpdatedLabel" or "CanOpen";
            case WorkRow row when ContainsOriginalRow(row, "ActiveWork", "SelectedWork"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Title" => row.Title,
                    "StatusLabel" => row.StatusLabel,
                    "CanOpen" => row.CanOpen,
                    _ => null
                };
                return path is "Id" or "Title" or "StatusLabel" or "CanOpen";
            case SettingRow row when ContainsOriginalRow(row, "ConfigurationSettings"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Category" => row.Category,
                    "ChoiceLabel" => row.ChoiceLabel,
                    "AvailabilityLabel" => row.AvailabilityLabel,
                    "CanChoose" => row.CanChoose,
                    _ => null
                };
                return path is "Id" or "Category" or "ChoiceLabel" or "AvailabilityLabel" or "CanChoose";
            case ResourceRow row when ContainsOriginalRow(row, "Resources"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Name" => row.Name,
                    "AvailabilityLabel" => row.AvailabilityLabel,
                    "CanReview" => row.CanReview,
                    _ => null
                };
                return path is "Id" or "Name" or "AvailabilityLabel" or "CanReview";
            case ConfigurationModelRow row when ContainsOriginalRow(row, "ConfigurationModels") && CurrentConfigurationModelRow(row) is not null:
                value = path switch { "Id" => row.Id, "Label" => row.Label, _ => null };
                return path is "Id" or "Label";
            case ModelRow row when ContainsOriginalRow(row, "Models"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Label" => row.Label,
                    _ => null
                };
                return path is "Id" or "Label";
            case EffortRow row when ContainsOriginalRow(row, "EffortChoices"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Label" => row.Label,
                    "CanChoose" => row.CanChoose,
                    _ => null
                };
                return path is "Id" or "Label" or "CanChoose";
            case OriginalProjectRow row when ContainsOriginalRow(row, "ProjectChoices"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Title" => row.Title,
                    _ => null
                };
                return path is "Id" or "Title";
            case CompatiblePendingRow row when ContainsOriginalRow(row, "CompatiblePendingCreations"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Title" => row.Title,
                    "Reason" => row.Reason,
                    _ => null
                };
                return path is "Id" or "Title" or "Reason";
            case ConfiguredProjectPreferenceRow row when ContainsOriginalRow(row, "ConfiguredDevelopmentProjects"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Name" => row.Name,
                    "AvailabilityLabel" => row.AvailabilityLabel,
                    _ => null
                };
                return path is "Id" or "Name" or "AvailabilityLabel";
            case AssistantConfigurationCapabilityPreferences.Row row when ContainsOriginalRow(row, "ConfigurationCapabilityRows"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Name" => row.Name,
                    "Description" => row.Description,
                    "AvailabilityLabel" => row.AvailabilityLabel,
                    "CanChoose" => row.CanChoose,
                    _ => null
                };
                return path is "Id" or "Name" or "Description" or "AvailabilityLabel" or "CanChoose";
            case HomeCapabilityReadinessRow row when ContainsOriginalRow(row, "HomeCapabilityReadinessRows"):
                value = path switch
                {
                    "Id" => row.Id,
                    "Name" => row.Name,
                    "StatusLabel" => row.StatusLabel,
                    _ => null
                };
                return path is "Id" or "Name" or "StatusLabel";
            case ConversationSearchRow row when ContainsOriginalRow(row, "ConversationSearchResults"):
                value = path switch { "Id" => row.Id, "Label" => row.Label, "Snippet" => row.Snippet, _ => null };
                return path is "Id" or "Label" or "Snippet";
            case ConversationBranchRow row when ContainsOriginalRow(row, "ConversationBranches"):
                value = path switch { "Id" => row.Id, "Label" => row.Label, "CanOpen" => CurrentConversationBranchRow(row) is not null, _ => null };
                return path is "Id" or "Label" or "CanOpen";
            case AssistantMessagePresentation row when ContainsOriginalRow(row, "Messages"):
                value = path switch
                {
                    "Id" => row.Id,
                    "RoleLabel" => row.RoleLabel,
                    "Content" => row.Content,
                    "Thinking" => row.Thinking,
                    "Activity" => row.Activity,
                    "HasThinking" => row.HasThinking,
                    "HasActivity" => row.HasActivity,
                    "CanContinue" => CurrentBranchMessage(row) is not null,
                    _ => null
                };
                return path is "Id" or "RoleLabel" or "Content" or "Thinking" or "Activity" or "HasThinking" or "HasActivity" or "CanContinue";
            default: return TryGetTaskProgressItemValue(item, path, out value) || TryGetAvatarItemValue(item, path, out value);
        }
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    private bool IsCurrentOriginalRepeatAction(string command, object? item) => Current && (command switch
    {
        "assistants.conversation.find.show" => CurrentConversationSearchRow(item) is not null,
        "assistants.branch.create" => CurrentBranchMessage(item) is not null,
        "assistants.branch.switch" => CurrentConversationBranchRow(item) is not null,
        "assistants.attachment.detach" => CurrentAttachmentRow(item) is not null,
        "assistants.avatar.select" => IsCurrentAvatarRow(item),
        "assistants.open" => item is AssistantRow assistant && ContainsOriginalRow(assistant, "Assistants"),
        "assistants.conversation.open" => item is ConversationRow conversation && ContainsOriginalRow(conversation, "RecentConversations", "SelectedConversations"),
        "assistants.work.open" => item is WorkRow work && ContainsOriginalRow(work, "ActiveWork", "SelectedWork"),
        "assistants.configuration.model.choose" => CurrentConfigurationModelRow(item) is not null,
        "assistants.model.choose" => item is ModelRow model && ContainsOriginalRow(model, "Models"),
        "assistants.configuration.effort.choose" => item is EffortRow effort && ContainsOriginalRow(effort, "EffortChoices"),
        "assistants.project.choose" => item is OriginalProjectRow project && ContainsOriginalRow(project, "ProjectChoices"),
        "assistants.project.pending.resume" => item is CompatiblePendingRow pending && ContainsOriginalRow(pending, "CompatiblePendingCreations"),
        "assistants.configuration.project.review" => item is ConfiguredProjectPreferenceRow preference && ContainsOriginalRow(preference, "ConfiguredDevelopmentProjects"),
        "assistants.configuration.capability.choose" => item is AssistantConfigurationCapabilityPreferences.Row capability && ContainsOriginalRow(capability, "ConfigurationCapabilityRows"),
        "assistants.resource.review" => item is ResourceRow resource && ContainsOriginalRow(resource, "Resources"),
        "assistants.setting.choose" => item is SettingRow setting && ContainsOriginalRow(setting, "ConfigurationSettings"),
        _ => true
    });
}
