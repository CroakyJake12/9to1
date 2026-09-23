using Haven.Application;

namespace Haven.Desktop.ViewModels;

public sealed partial class MailPageViewModel
{
    private async Task RestoreLatestDraftAsync()
    {
        var account = SelectedAccount;
        if (account is null || IsComposeOpen) return;

        var draft = SelectLatestDraftForAccount(
            await _mail.GetDraftsAsync(CancellationToken.None), account.AccountId);
        if (draft is null) return;

        CancelDraftAutosave();
        _composeLocalDraftId = draft.LocalId == Guid.Empty ? Guid.NewGuid() : draft.LocalId;
        _composeDraftId = draft.ProviderDraftId;
        _composeResponseKind = draft.ResponseKind;
        _composeSourceMessageId = draft.SourceMessageId;
        _composeThreadId = draft.ThreadId;
        ComposeTo = string.Join("; ", draft.To.Select(item => item.Address));
        ComposeCc = string.Join("; ", draft.Cc.Select(item => item.Address));
        ComposeBcc = string.Join("; ", draft.Bcc.Select(item => item.Address));
        ComposeSubject = draft.Subject;
        ComposeHtmlBody = draft.IsHtml ? draft.Body : string.Empty;
        ComposeBody = draft.IsHtml ? ToPlainText(draft.Body) : draft.Body;

        ComposeAttachments.Clear();
        foreach (var attachment in draft.Attachments)
            ComposeAttachments.Add(new MailComposeAttachmentItem(
                attachment.FileName, attachment.ContentType, attachment.Content));

        IsComposeOpen = true;
        ComposeStatus = draft.PersistenceState is MailDraftPersistenceState.SaveFailed or MailDraftPersistenceState.SendFailed
            ? "Recovered draft - previous provider action did not complete"
            : "Recovered saved draft";
    }

    internal static MailDraft? SelectLatestDraftForAccount(IEnumerable<MailDraft> drafts, Guid accountId) => drafts
        .Where(item => item.AccountId == accountId)
        .OrderByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
        .ThenBy(item => item.LocalId)
        .FirstOrDefault();

    private static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var withoutTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
