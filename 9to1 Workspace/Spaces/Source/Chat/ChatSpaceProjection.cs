using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

public static class ChatSpaceProjection
{
    public static ChatSpaceConversation Project(ChatSpaceConversationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var attachments = data.Attachments
            .Where(item => item.MessageId is not null)
            .GroupBy(item => item.MessageId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MessageAttachment>)group.ToArray());

        var messages = data.Messages.Select(message => new ChatSpaceMessage(
            message.Id,
            message.Role,
            message.Content,
            SpeakerFor(message),
            message.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
            message.ModelName,
            attachments.GetValueOrDefault(message.Id, []),
            ReadCitations(message),
            ReadToolActivities(message),
            message.Role == MessageRole.Assistant)).ToArray();
        var branches = data.Branches
            .OrderBy(item => item.CreatedAt)
            .Select(item => new ChatSpaceBranch(item.Id, item.Name, item.Reason, item.IsCurrent))
            .ToArray();
        return new(
            data.Conversation.Id,
            string.IsNullOrWhiteSpace(data.Conversation.Title) ? "Untitled chat" : data.Conversation.Title,
            messages,
            branches,
            branches.FirstOrDefault(item => item.IsCurrent)?.Id);
    }

    public static IReadOnlyList<ToolActivity> ReadToolActivities(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.Metadata.TryGetValue("toolActivities", out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        try
        {
            return value.Deserialize<ToolActivity[]>() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<ChatSpaceCitation> ReadCitations(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.Metadata.TryGetValue("citations", out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<ChatSpaceCitation>();
        var fallbackNumber = 1;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var number = ReadInt(item, "number") ?? fallbackNumber;
            var title = ReadString(item, "title") ?? ReadString(item, "source") ?? $"Source {number}";
            var source = ReadString(item, "sourceType") ?? ReadString(item, "source") ?? "Conversation context";
            var uri = ReadString(item, "uri") ?? ReadString(item, "url");
            var excerpt = ReadString(item, "excerpt") ?? string.Empty;
            result.Add(new ChatSpaceCitation(number, title, source, uri, excerpt));
            fallbackNumber++;
        }
        return result;
    }

    private static string SpeakerFor(ChatMessage message) => message.Role switch
    {
        MessageRole.User => "You",
        MessageRole.Assistant => string.IsNullOrWhiteSpace(message.AgentName) ? "Haven" : message.AgentName,
        MessageRole.Tool => "Tool",
        _ => "System"
    };

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
}
