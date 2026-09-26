using HavenOS.Mail.Storage;

namespace HavenOS.Mail.Services;

/// <summary>Evaluates a Mail query against the canonical AccountID-scoped cache.</summary>
public static class MailSearchEngine
{
    public static MailSearchResult Search(MailState state, MailSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(state);
        query = (query ?? throw new ArgumentNullException(nameof(query))).Validate();
        var attachmentNames = state.Attachments
            .GroupBy(item => item.MessageId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.FileName).ToArray());

        var matches = state.Messages
            .Where(message => Matches(message, query, attachmentNames))
            .OrderByDescending(message => message.ReceivedAt)
            .ThenBy(message => message.MessageId)
            .ToArray();
        var offset = checked(query.Page * query.PageSize);
        return new MailSearchResult(matches.Skip(offset).Take(query.PageSize).ToArray(), matches.Length, query.Page, query.PageSize);
    }

    private static bool Matches(MailMessage message, MailSearchQuery query, IReadOnlyDictionary<Guid, string[]> attachmentNames)
    {
        if (message.IsDeleted) return false;
        if (query.AccountIds is { Count: > 0 } accounts && !accounts.Contains(message.AccountId)) return false;
        if (query.FolderKeys is { Count: > 0 } folders && !folders.Contains(message.FolderKey, StringComparer.OrdinalIgnoreCase)) return false;
        if (query.Labels is { Count: > 0 } labels && !labels.Any(label => message.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))) return false;
        if (query.IsRead is { } isRead && message.IsRead != isRead) return false;
        if (query.IsStarred is { } isStarred && message.IsStarred != isStarred) return false;
        if (query.HasAttachments is { } hasAttachments && (message.AttachmentIds.Count > 0) != hasAttachments) return false;
        if (query.From is { } from && message.ReceivedAt < from) return false;
        if (query.To is { } to && message.ReceivedAt > to) return false;
        if (query.Sender is { Count: > 0 } senders && !senders.Any(value => Contains(message.Sender?.Address, value))) return false;
        var recipients = message.To.Concat(message.Cc).Select(item => item.Address).ToArray();
        if (query.Recipient is { Count: > 0 } requestedRecipients && !requestedRecipients.Any(value => recipients.Any(item => Contains(item, value)))) return false;
        if (query.Subject is { Length: > 0 } subject && !Contains(message.Subject, subject)) return false;
        if (query.Body is { Length: > 0 } body && !Contains(message.PlainBody, body) && !Contains(message.HtmlBody, body)) return false;
        if (query.AttachmentName is { Length: > 0 } fileName &&
            (!attachmentNames.TryGetValue(message.MessageId, out var names) || !names.Any(name => Contains(name, fileName)))) return false;
        if (query.Categories is { Count: > 0 } categories && !categories.Any(category => message.Labels.Contains(category, StringComparer.OrdinalIgnoreCase))) return false;
        if (query.Text is { Length: > 0 } text)
        {
            var inAttachment = attachmentNames.TryGetValue(message.MessageId, out var textAttachmentNames) && textAttachmentNames.Any(name => Contains(name, text));
            if (!Contains(message.Subject, text) && !Contains(message.Preview, text) &&
                !Contains(message.Sender?.Address, text) && !Contains(message.Sender?.DisplayName, text) &&
                !recipients.Any(item => Contains(item, text)) &&
                !Contains(message.PlainBody, text) && !Contains(message.HtmlBody, text) && !inAttachment) return false;
        }
        return true;
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}
